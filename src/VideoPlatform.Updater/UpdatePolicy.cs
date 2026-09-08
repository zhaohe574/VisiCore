using System.Security.Cryptography;

namespace VideoPlatform.Updater;

public static class UpdatePolicy
{
    public static Uri Validate(string url, string fileName, string sha256, string restart)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidOperationException("安装包下载必须使用 HTTPS，仅本机测试允许 HTTP。");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("下载地址不得包含账号密码。");
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new InvalidOperationException("SHA-256 必须为 64 位十六进制字符串。");
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("自动更新仅支持无路径的 MSI 安装包文件名。");
        if (!Path.IsPathFullyQualified(restart) || !File.Exists(restart) || !restart.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("桌面端重启路径无效。");
        return uri;
    }
    public static async Task<string> VerifyAsync(string path, string expectedHash, long? expectedSize = null, CancellationToken cancellationToken = default)
    {
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)) throw new InvalidDataException("安装包哈希格式无效。");
        if (expectedSize is <= 0) throw new InvalidDataException("安装包长度无效。");
        await using var stream = File.OpenRead(path);
        if (expectedSize is { } length && stream.Length != length) throw new InvalidDataException("安装包大小与版本记录不一致。");
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedHash))) throw new InvalidDataException("安装包 SHA-256 校验失败。");
        return Convert.ToHexString(actual);
    }
    public static bool InstallSucceeded(int code) => code is 0 or 3010 or 1641;
}
