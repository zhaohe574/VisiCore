using System.Text.Json;
using Microsoft.Extensions.Options;
using VisiCore.Setup;

namespace VisiCore.Api;

/// <summary>
/// 未初始化核心仅允许暂存一次加密备份，实际恢复由入口脚本中的维护工具完成。
/// </summary>
public sealed class BootstrapRestoreService(IOptions<PlatformBackupOptions> options)
{
    public async Task StageAsync(IFormFile backup, string recoveryKey, CancellationToken cancellationToken)
    {
        if (backup.Length <= 0 || backup.Length > 20L * 1024 * 1024 * 1024 || string.IsNullOrWhiteSpace(recoveryKey) || recoveryKey.Trim().Length < 32)
        {
            throw new InvalidDataException("备份文件或恢复密钥无效。 ");
        }
        var settings = options.Value.GetValidated();
        Directory.CreateDirectory(Path.Combine(settings.RootDirectory, "bootstrap"));
        Directory.CreateDirectory(settings.MaintenanceDirectory);
        var requestPath = Path.Combine(settings.MaintenanceDirectory, "restore-request.json");
        if (File.Exists(requestPath)) throw new InvalidOperationException("已有恢复任务等待执行，请勿重复提交。 ");

        var archivePath = Path.Combine(settings.RootDirectory, "bootstrap", $"{Guid.NewGuid():N}.vcbackup");
        try
        {
            await using (var target = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131_072, FileOptions.Asynchronous))
            await using (var source = backup.OpenReadStream())
            {
                await source.CopyToAsync(target, cancellationToken);
            }
            if (!await EncryptedBackupArchive.IsSupportedAsync(archivePath, cancellationToken))
            {
                throw new InvalidDataException("上传文件不是受支持的视枢加密备份。 ");
            }
            var temporary = Path.Combine(settings.MaintenanceDirectory, $".{Guid.NewGuid():N}.restore.json");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new MaintenanceRestoreRequest(archivePath, recoveryKey.Trim(), "bootstrap")), cancellationToken);
            File.Move(temporary, requestPath, overwrite: false);
        }
        catch
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            throw;
        }
    }
}

public sealed record MaintenanceRestoreRequest(string ArchivePath, string RecoveryKey, string? RequestedBy);
