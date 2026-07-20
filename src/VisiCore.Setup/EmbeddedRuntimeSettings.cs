using System.Text;

namespace VisiCore.Setup;

/// <summary>
/// 容器入口生成的内部服务参数。它们不经过浏览器，也不写入镜像层。
/// </summary>
public sealed record EmbeddedRuntimeSettings(
    string PostgreSqlHost,
    int PostgreSqlPort,
    string PostgreSqlUsername,
    string PostgreSqlPassword,
    string DatabaseName,
    string MediaMtxApiBaseUri,
    string MediaMtxHlsBaseUri,
    string RecoveryKey)
{
    public static EmbeddedRuntimeSettings FromEnvironment()
    {
        var passwordPath = Environment.GetEnvironmentVariable("VISICORE_EMBEDDED_POSTGRES_PASSWORD_FILE")
            ?? "/var/lib/visicore/config/internal-postgres-password";
        var recoveryKeyPath = Environment.GetEnvironmentVariable("VISICORE_BACKUP_KEY_FILE")
            ?? "/var/lib/visicore/config/backup-recovery.key";
        return new EmbeddedRuntimeSettings(
            "127.0.0.1",
            5432,
            "visicore",
            ReadRequiredSecret(passwordPath, "内置 PostgreSQL 密码文件"),
            "visicore",
            "http://127.0.0.1:9997/",
            "http://127.0.0.1:8888/",
            ReadRequiredSecret(recoveryKeyPath, "恢复密钥文件"));
    }

    private static string ReadRequiredSecret(string path, string label)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"{label}不可用。请检查核心容器数据卷。 ");
        }

        var value = File.ReadAllText(path, Encoding.UTF8).Trim();
        if (value.Length < 32 || value.Length > 256)
        {
            throw new InvalidOperationException($"{label}无效。请停止容器并检查核心配置卷。 ");
        }
        return value;
    }
}
