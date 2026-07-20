using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace VisiCore.Api;

public sealed class ExportArtifactStorageOptions
{
    public string? RootDirectory { get; init; }
    public long MaxUploadBytes { get; init; } = 256L * 1024 * 1024 * 1024;
    public int RetentionDays { get; init; } = 14;
    public int CleanupIntervalMinutes { get; init; } = 30;
    public int TemporaryFileRetentionMinutes { get; init; } = 1_440;

    public ExportArtifactStorageSettings GetValidated()
    {
        if (string.IsNullOrWhiteSpace(RootDirectory) ||
            !Path.IsPathFullyQualified(RootDirectory) ||
            MaxUploadBytes is < 1_048_576 or > 1_099_511_627_776 ||
            RetentionDays is < 1 or > 365 ||
            CleanupIntervalMinutes is < 5 or > 1_440 ||
            TemporaryFileRetentionMinutes is < 60 or > 10_080)
        {
            throw new InvalidOperationException("导出归档存储配置无效。根目录必须是绝对路径，且应设置合理的容量、保留期和清理周期。");
        }

        return new ExportArtifactStorageSettings(
            Path.GetFullPath(RootDirectory),
            MaxUploadBytes,
            RetentionDays,
            CleanupIntervalMinutes,
            TemporaryFileRetentionMinutes);
    }
}

public sealed record ExportArtifactStorageSettings(
    string RootDirectory,
    long MaxUploadBytes,
    int RetentionDays,
    int CleanupIntervalMinutes,
    int TemporaryFileRetentionMinutes);

public sealed record StoredExportArtifact(string StorageKey, long SizeBytes, string Sha256);
public sealed record StaleExportTemporaryFileCleanupResult(int Deleted, int Failed);

public sealed class LocalExportArtifactStore(IOptions<ExportArtifactStorageOptions> options)
{
    private static readonly Regex Sha256Pattern = new("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ExportArtifactStorageSettings GetSettings() => options.Value.GetValidated();

    public async Task<StoredExportArtifact> StoreAsync(
        Guid playbackExportId,
        Stream content,
        long declaredSizeBytes,
        string declaredSha256,
        CancellationToken cancellationToken)
    {
        if (playbackExportId == Guid.Empty || content is null || declaredSizeBytes < 0 || !Sha256Pattern.IsMatch(declaredSha256))
        {
            throw new ArgumentOutOfRangeException(nameof(playbackExportId), "导出文件上传元数据无效。");
        }

        var settings = GetSettings();
        if (declaredSizeBytes > settings.MaxUploadBytes)
        {
            throw new InvalidOperationException("导出文件超过中心归档允许的最大大小。");
        }

        var storageKey = $"exports/{playbackExportId:N}/{Guid.NewGuid():N}.mp4";
        var destination = ResolvePath(settings, storageKey);
        var temporary = destination + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long written = 0;
            string sha256;
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }
                    checked
                    {
                        written += read;
                    }
                    if (written > declaredSizeBytes || written > settings.MaxUploadBytes)
                    {
                        throw new InvalidOperationException("导出文件长度超过申报值或中心存储限额。");
                    }
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (written != declaredSizeBytes)
                {
                    throw new InvalidOperationException("导出文件长度与申报值不一致。");
                }
                sha256 = Convert.ToHexString(hash.GetHashAndReset());
                if (!string.Equals(sha256, declaredSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("导出文件 SHA-256 校验失败。");
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination);
            return new StoredExportArtifact(storageKey, written, sha256);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(GetSettings(), storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null;
        return Task.FromResult(stream);
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(GetSettings(), storageKey);
        if (!File.Exists(path))
        {
            return Task.FromResult(true);
        }
        File.Delete(path);
        return Task.FromResult(!File.Exists(path));
    }

    public Task<StaleExportTemporaryFileCleanupResult> DeleteStaleTemporaryFilesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var exportsDirectory = Path.Combine(settings.RootDirectory, "exports");
        if (!Directory.Exists(exportsDirectory))
        {
            return Task.FromResult(new StaleExportTemporaryFileCleanupResult(0, 0));
        }

        var cutoff = now.UtcDateTime.AddMinutes(-settings.TemporaryFileRetentionMinutes);
        var deleted = 0;
        var failed = 0;
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var path in Directory.EnumerateFiles(exportsDirectory, "*.partial", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(path) > cutoff)
                {
                    continue;
                }

                File.Delete(path);
                if (File.Exists(path))
                {
                    failed++;
                }
                else
                {
                    deleted++;
                }
            }
            catch (IOException)
            {
                failed++;
            }
            catch (UnauthorizedAccessException)
            {
                failed++;
            }
        }

        return Task.FromResult(new StaleExportTemporaryFileCleanupResult(deleted, failed));
    }

    private static string ResolvePath(ExportArtifactStorageSettings settings, string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith("exports/", StringComparison.Ordinal) ||
            storageKey.Contains("..", StringComparison.Ordinal) || storageKey.Contains('\\'))
        {
            throw new InvalidOperationException("归档存储键无效。");
        }
        var root = Path.GetFullPath(settings.RootDirectory);
        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("归档存储路径越界。");
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 清理失败由保留期清理任务再次处理。
        }
        catch (UnauthorizedAccessException)
        {
            // 清理失败由保留期清理任务再次处理。
        }
    }
}
