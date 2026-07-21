using System.Text.Json;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 未部署 Linux Host Agent 时，配置页仅能写入同一无特权容器已挂载的 Agent 状态目录。
/// 它不接触 Docker Socket、宿主机配置或升级设置。
/// </summary>
public sealed class LocalEdgeNodeConfigurationStore(string configurationPath, string bootstrapPath)
{
    private readonly string configurationPath = configurationPath;
    private readonly string bootstrapPath = bootstrapPath;

    public bool TryApply(string controlPlaneBaseUri, string enrollmentCode, out string? failureKind)
    {
        failureKind = null;
        if (string.IsNullOrWhiteSpace(controlPlaneBaseUri) ||
            string.IsNullOrWhiteSpace(enrollmentCode) ||
            !Path.IsPathFullyQualified(configurationPath) ||
            !Path.IsPathFullyQualified(bootstrapPath))
        {
            failureKind = "configuration_paths_invalid";
            return false;
        }

        try
        {
            WriteJsonAtomically(configurationPath, new
            {
                EdgeAgent = new
                {
                    ControlPlaneBaseUri = controlPlaneBaseUri,
                    BootstrapFilePath = bootstrapPath,
                    HostUpgradeEnabled = false
                }
            });
            WriteJsonAtomically(bootstrapPath, new { enrollmentCode = enrollmentCode.Trim() });
            RestrictToCurrentUser(configurationPath);
            RestrictToCurrentUser(bootstrapPath);
            return true;
        }
        catch (IOException)
        {
            failureKind = "configuration_storage_failed";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            failureKind = "configuration_storage_failed";
            return false;
        }
    }

    private static void WriteJsonAtomically(string path, object value)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new IOException("配置文件目录无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
