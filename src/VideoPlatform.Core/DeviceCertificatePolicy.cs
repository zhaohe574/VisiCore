using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace VideoPlatform.Core;

public static class DeviceCertificatePolicy
{
    public static bool TryNormalizeSha256Thumbprint(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = new string(value.Where(character => !char.IsWhiteSpace(character) && character is not ':' and not '-').ToArray());
        if (compact.Length != 64 || compact.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        normalized = compact.ToUpperInvariant();
        return true;
    }

    public static string? NormalizeSha256ThumbprintOrNull(string? value) =>
        TryNormalizeSha256Thumbprint(value, out var normalized) ? normalized : null;

    public static void EnsureTlsConfiguration(WorkerRecorderEndpoint endpoint, bool allowUntrustedCertificateForLocalDevelopment)
    {
        if (!endpoint.UseTls || TryNormalizeSha256Thumbprint(endpoint.CertificateThumbprint, out _))
        {
            return;
        }

        if (allowUntrustedCertificateForLocalDevelopment && IsLoopbackHost(endpoint.Host))
        {
            return;
        }

        throw new InvalidOperationException("TLS 设备端点必须配置 SHA-256 叶证书指纹。");
    }

    public static bool IsServerCertificateAccepted(
        WorkerRecorderEndpoint endpoint,
        X509Certificate2? certificate,
        SslPolicyErrors sslPolicyErrors,
        bool allowUntrustedCertificateForLocalDevelopment)
    {
        if (!endpoint.UseTls)
        {
            return false;
        }

        if (TryNormalizeSha256Thumbprint(endpoint.CertificateThumbprint, out var expectedThumbprint))
        {
            if (sslPolicyErrors != SslPolicyErrors.None || certificate is null)
            {
                return false;
            }

            var expectedBytes = Convert.FromHexString(expectedThumbprint);
            var actualBytes = SHA256.HashData(certificate.RawData);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }

        return allowUntrustedCertificateForLocalDevelopment && IsLoopbackHost(endpoint.Host);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
