using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VideoPlatform.Infrastructure;

/// <summary>
/// 统一的 TLS 信任策略。
/// 默认走操作系统信任链；仅当运维通过 <c>PLATFORM_TRUSTED_CERT_SHA256</c> 显式登记
/// 证书指纹时，才对匹配指纹的自签名证书放行。禁止"无条件返回 true"。
/// </summary>
public static class TlsTrust
{
    /// <summary>规范化指纹：去除分隔符并转大写，便于比较。</summary>
    public static string Normalize(string thumbprint)
        => thumbprint.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    /// 构造证书校验回调。未配置任何指纹时返回 null，让调用方沿用系统默认校验，
    /// 避免在 SslOptions 中注入无意义的回调。
    /// </summary>
    public static RemoteCertificateValidationCallback? PinnedCallback(IReadOnlyList<string> allowedThumbprints)
    {
        if (allowedThumbprints.Count == 0) return null;
        var allowed = new HashSet<string>(allowedThumbprints, StringComparer.OrdinalIgnoreCase);
        return (_, certificate, _, errors) =>
        {
            // 系统信任链通过时直接放行，行为与默认校验一致。
            if (errors == SslPolicyErrors.None) return true;
            if (certificate is null) return false;
            try
            {
                // 仅接受由已登记指纹精确匹配的证书，其余一律拒绝（含链错误与名称不匹配）。
                var actual = Normalize(X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()).Thumbprint);
                return allowed.Contains(actual);
            }
            catch (CryptographicException) { return false; }
        };
    }
}
