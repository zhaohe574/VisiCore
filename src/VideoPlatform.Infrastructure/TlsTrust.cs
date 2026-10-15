using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VideoPlatform.Infrastructure;

/// <summary>
/// 统一的 TLS 信任策略。
/// 默认走操作系统信任链；仅当运维通过 <c>PLATFORM_TRUSTED_CERT_SHA256</c> 显式登记
/// 证书 SHA-256 指纹时，才对匹配指纹的自签名证书放行。禁止"无条件返回 true"。
///
/// ⚠️ 必须用 <see cref="SHA256"/> 计算指纹，**不能用 <c>X509Certificate2.Thumbprint</c>**：
/// 后者返回的是 SHA-1，与设置名（SHA256）及运维用
/// <c>openssl x509 -fingerprint -sha256</c> 取得的指纹格式不一致，会导致登记后永不匹配。
/// 该缺陷曾在生产环境表现为"已登记指纹但自签名证书仍被拒绝"。
/// </summary>
public static class TlsTrust
{
    /// <summary>规范化指纹：去除分隔符并转大写，便于比较。</summary>
    public static string Normalize(string thumbprint)
        => thumbprint.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>按 SHA-256 计算证书指纹（大写、无分隔符），与 <see cref="Normalize"/> 输出格式一致。</summary>
    public static string Sha256Fingerprint(byte[] rawCertificateData)
        => Convert.ToHexString(SHA256.HashData(rawCertificateData));

    /// <summary>
    /// 构造证书校验回调。未配置任何指纹时返回 null，让调用方沿用系统默认校验，
    /// 避免在 SslOptions 中注入无意义的回调。
    /// </summary>
    public static RemoteCertificateValidationCallback? PinnedCallback(IReadOnlyList<string> allowedThumbprints)
    {
        if (allowedThumbprints.Count == 0) return null;
        // 防御性规范化：调用方可能直接传入 openssl 的带冒号形式（PlatformOptions 会规范化，
        // 但本方法不应依赖这一点），否则会出现"配置看似正确却永不匹配"的故障。
        var allowed = allowedThumbprints
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) return null;
        return (_, certificate, _, errors) =>
        {
            // 系统信任链通过时直接放行，行为与默认校验一致。
            if (errors == SslPolicyErrors.None) return true;
            if (certificate is null) return false;
            try
            {
                // 仅接受由已登记 SHA-256 指纹精确匹配的证书，其余一律拒绝（含链错误与名称不匹配）。
                return allowed.Contains(Sha256Fingerprint(certificate.GetRawCertData()));
            }
            catch (CryptographicException) { return false; }
        };
    }
}
