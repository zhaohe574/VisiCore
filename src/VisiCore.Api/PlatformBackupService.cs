using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Persistence;
using VisiCore.Setup;

namespace VisiCore.Api;

public sealed class PlatformBackupOptions
{
    public string RootDirectory { get; init; } = Environment.GetEnvironmentVariable("VISICORE_BACKUP_DIRECTORY") ?? "/var/lib/visicore/backups";
    public string ConfigurationDirectory { get; init; } = "/var/lib/visicore/config";
    public string MaintenanceDirectory { get; init; } = Environment.GetEnvironmentVariable("VISICORE_MAINTENANCE_DIRECTORY") ?? "/var/lib/visicore/maintenance";
    public int AutomaticRetentionCount { get; init; } = 30;

    public PlatformBackupSettings GetValidated()
    {
        if (!Path.IsPathFullyQualified(RootDirectory) || !Path.IsPathFullyQualified(ConfigurationDirectory) || !Path.IsPathFullyQualified(MaintenanceDirectory) || AutomaticRetentionCount is < 1 or > 365)
        {
            throw new InvalidOperationException("平台备份运行配置无效。 ");
        }
        return new PlatformBackupSettings(Path.GetFullPath(RootDirectory), Path.GetFullPath(ConfigurationDirectory), Path.GetFullPath(MaintenanceDirectory), AutomaticRetentionCount);
    }
}

public sealed record PlatformBackupSettings(string RootDirectory, string ConfigurationDirectory, string MaintenanceDirectory, int AutomaticRetentionCount);

public sealed class PlatformBackupService(
    PlatformDbContext dbContext,
    EmbeddedRuntimeSettings runtime,
    IOptions<PlatformBackupOptions> options,
    ILogger<PlatformBackupService> logger)
{
    private static readonly SemaphoreSlim OperationLock = new(1, 1);

    public async Task<PlatformBackupEntity> CreateAsync(string kind, CancellationToken cancellationToken)
    {
        if (kind is not ("automatic" or "manual" or "protection")) throw new ArgumentOutOfRangeException(nameof(kind));
        var settings = options.Value.GetValidated();
        await OperationLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(settings.RootDirectory);
            Directory.CreateDirectory(Path.Combine(settings.RootDirectory, "items"));
            var id = Guid.NewGuid();
            var fileName = $"visicore-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{id:N}.vcbackup";
            var storageKey = Path.Combine("items", $"{id:N}.vcbackup");
            var finalPath = ResolveStoragePath(settings, storageKey);
            var dumpPath = Path.Combine(settings.RootDirectory, $".{id:N}.dump");
            try
            {
                await DumpDatabaseAsync(dumpPath, cancellationToken);
                await EncryptedBackupArchive.CreateAsync(finalPath, runtime.RecoveryKey, dumpPath, settings.ConfigurationDirectory, cancellationToken);
                var info = new FileInfo(finalPath);
                var entity = new PlatformBackupEntity
                {
                    Id = id,
                    Kind = kind,
                    Status = "available",
                    StorageKey = storageKey.Replace('\\', '/'),
                    FileName = fileName,
                    SizeBytes = info.Length,
                    Sha256 = await GetSha256Async(finalPath, cancellationToken),
                    CreatedAt = DateTimeOffset.UtcNow,
                    RetainUntil = kind == "automatic" ? DateTimeOffset.UtcNow.AddDays(30) : null
                };
                dbContext.PlatformBackups.Add(entity);
                await dbContext.SaveChangesAsync(cancellationToken);
                if (kind == "automatic") await ApplyAutomaticRetentionAsync(settings, cancellationToken);
                return entity;
            }
            catch
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                throw;
            }
            finally
            {
                if (File.Exists(dumpPath)) File.Delete(dumpPath);
            }
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<PlatformBackupEntity> StoreUploadedAsync(IFormFile upload, CancellationToken cancellationToken)
    {
        if (upload.Length <= 0 || upload.Length > 20L * 1024 * 1024 * 1024) throw new InvalidDataException("上传的备份文件大小无效。 ");
        var settings = options.Value.GetValidated();
        await OperationLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.Combine(settings.RootDirectory, "items"));
            var id = Guid.NewGuid();
            var storageKey = Path.Combine("items", $"{id:N}.vcbackup");
            var path = ResolveStoragePath(settings, storageKey);
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous))
            await using (var source = upload.OpenReadStream())
            {
                await source.CopyToAsync(destination, cancellationToken);
            }
            if (!await EncryptedBackupArchive.IsSupportedAsync(path, cancellationToken))
            {
                File.Delete(path);
                throw new InvalidDataException("上传文件不是受支持的视枢加密备份。 ");
            }
            var entity = new PlatformBackupEntity
            {
                Id = id,
                Kind = "uploaded",
                Status = "available",
                StorageKey = storageKey.Replace('\\', '/'),
                FileName = NormalizeUploadName(upload.FileName, id),
                SizeBytes = new FileInfo(path).Length,
                Sha256 = await GetSha256Async(path, cancellationToken),
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.PlatformBackups.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return entity;
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<string> GetDownloadPathAsync(Guid id, CancellationToken cancellationToken)
    {
        var backup = await FindAvailableAsync(id, cancellationToken);
        var path = ResolveStoragePath(options.Value.GetValidated(), backup.StorageKey);
        if (!File.Exists(path)) throw new FileNotFoundException("备份文件已不在备份卷中。", path);
        return path;
    }

    public async Task RequestRestoreAsync(Guid id, string recoveryKey, string requestedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey) || recoveryKey.Trim().Length < 32) throw new InvalidDataException("恢复密钥无效。 ");
        var settings = options.Value.GetValidated();
        await OperationLock.WaitAsync(cancellationToken);
        try
        {
            var backup = await FindAvailableAsync(id, cancellationToken);
            var path = ResolveStoragePath(settings, backup.StorageKey);
            if (!File.Exists(path)) throw new FileNotFoundException("备份文件已不在备份卷中。", path);
            await CreateAsyncWithinLockAsync("protection", settings, cancellationToken);
            Directory.CreateDirectory(settings.MaintenanceDirectory);
            var requestPath = Path.Combine(settings.MaintenanceDirectory, "restore-request.json");
            if (File.Exists(requestPath)) throw new InvalidOperationException("已有恢复任务等待执行，请勿重复提交。 ");
            var temporary = Path.Combine(settings.MaintenanceDirectory, $".{Guid.NewGuid():N}.restore.json");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new MaintenanceRestoreRequest(path, recoveryKey.Trim(), requestedBy)), cancellationToken);
            File.Move(temporary, requestPath, overwrite: false);
            backup.LastRestoredAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var backup = await dbContext.PlatformBackups.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("备份不存在。 ");
        var path = ResolveStoragePath(options.Value.GetValidated(), backup.StorageKey);
        if (File.Exists(path)) File.Delete(path);
        dbContext.PlatformBackups.Remove(backup);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateAsyncWithinLockAsync(string kind, PlatformBackupSettings settings, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var storageKey = Path.Combine("items", $"{id:N}.vcbackup");
        var finalPath = ResolveStoragePath(settings, storageKey);
        var dumpPath = Path.Combine(settings.RootDirectory, $".{id:N}.dump");
        try
        {
            await DumpDatabaseAsync(dumpPath, cancellationToken);
            await EncryptedBackupArchive.CreateAsync(finalPath, runtime.RecoveryKey, dumpPath, settings.ConfigurationDirectory, cancellationToken);
            dbContext.PlatformBackups.Add(new PlatformBackupEntity
            {
                Id = id,
                Kind = kind,
                Status = "available",
                StorageKey = storageKey.Replace('\\', '/'),
                FileName = $"visicore-protection-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{id:N}.vcbackup",
                SizeBytes = new FileInfo(finalPath).Length,
                Sha256 = await GetSha256Async(finalPath, cancellationToken),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            if (File.Exists(dumpPath)) File.Delete(dumpPath);
        }
    }

    private async Task<PlatformBackupEntity> FindAvailableAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.PlatformBackups.SingleOrDefaultAsync(item => item.Id == id && item.Status == "available", cancellationToken)
            ?? throw new KeyNotFoundException("可用备份不存在。 ");

    private async Task ApplyAutomaticRetentionAsync(PlatformBackupSettings settings, CancellationToken cancellationToken)
    {
        var expired = await dbContext.PlatformBackups
            .Where(item => item.Kind == "automatic")
            .OrderByDescending(item => item.CreatedAt)
            .Skip(settings.AutomaticRetentionCount)
            .ToListAsync(cancellationToken);
        foreach (var backup in expired)
        {
            var path = ResolveStoragePath(settings, backup.StorageKey);
            if (File.Exists(path)) File.Delete(path);
        }
        if (expired.Count > 0)
        {
            dbContext.PlatformBackups.RemoveRange(expired);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DumpDatabaseAsync(string dumpPath, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("pg_dump")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.Environment["PGPASSWORD"] = runtime.PostgreSqlPassword;
        foreach (var argument in new[] { "--format=custom", "--no-owner", "--no-privileges", "--host=127.0.0.1", "--port=5432", "--username=visicore", "--file", dumpPath, "visicore" })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cancellationToken));
        if (process.ExitCode != 0)
        {
            logger.LogWarning("PostgreSQL 备份失败，退出码为 {ExitCode}。", process.ExitCode);
            throw new InvalidOperationException("无法创建 PostgreSQL 备份。请检查核心容器状态和备份卷空间。 ");
        }
    }

    private static string ResolveStoragePath(PlatformBackupSettings settings, string storageKey)
    {
        var root = Path.GetFullPath(settings.RootDirectory) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(settings.RootDirectory, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("备份存储路径无效。 ");
        return result;
    }

    private static async Task<string> GetSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072, FileOptions.Asynchronous);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string NormalizeUploadName(string? fileName, Guid id)
    {
        var candidate = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 256) return $"uploaded-{id:N}.vcbackup";
        return candidate.EndsWith(".vcbackup", StringComparison.OrdinalIgnoreCase) ? candidate : $"{candidate}.vcbackup";
    }
}

public sealed class PlatformBackupScheduler(IServiceScopeFactory scopeFactory, ILogger<PlatformBackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun();
            await Task.Delay(delay, stoppingToken);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PlatformBackupService>();
                await service.CreateAsync("automatic", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "平台自动备份失败。 ");
            }
        }
    }

    private static TimeSpan GetDelayUntilNextRun()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, 3, 0, 0, now.Offset);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
