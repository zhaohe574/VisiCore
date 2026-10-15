using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace VideoPlatform.Desktop.Services;

public static class TransportPolicy
{
    public static SocketsHttpHandler CreateHandler(string? sourceAddress = null, string? hostPrefix = null, string? trustedThumbprints = null)
    {
        var settings = ClientFiles.LoadSettings();
        sourceAddress ??= settings.SourceAddress;
        hostPrefix ??= settings.SourceHostPrefix;
        trustedThumbprints ??= settings.TrustedCertificateThumbprints
            ?? Environment.GetEnvironmentVariable("PLATFORM_TRUSTED_CERT_SHA256");

        // 默认使用系统信任链（证书需已导入"受信任的根证书颁发机构"，见
        // tools/install-platform-certificate.ps1）。仅当登记了证书指纹时才对自签名证书放行，
        // 不再无条件跳过 TLS 校验。
        var ssl = new System.Net.Security.SslClientAuthenticationOptions();
        var pinned = BuildPinnedCallback(trustedThumbprints);
        if (pinned is not null) ssl.RemoteCertificateValidationCallback = pinned;

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 100,
            SslOptions = ssl
        };

        IPAddress? address = null;
        if (!string.IsNullOrWhiteSpace(sourceAddress))
        {
            if (!IPAddress.TryParse(sourceAddress, out address))
                throw new InvalidOperationException("配置的本地源地址无效。");
        }
        else
        {
            address = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && a.ToString().StartsWith("10.37."));
        }

        if (address is null) return handler;

        handler.ConnectCallback = async (context, token) =>
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                var isInternal = context.DnsEndPoint.Host.StartsWith(hostPrefix ?? "10.37.", StringComparison.OrdinalIgnoreCase);
                if (!isInternal)
                {
                    try
                    {
                        var ips = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                        isInternal = ips.Any(a => a.ToString().StartsWith("10.37."));
                    }
                    catch { }
                }

                if (isInternal) socket.Bind(new IPEndPoint(address, 0));
                await socket.ConnectAsync(context.DnsEndPoint, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        };
        return handler;
    }

    /// <summary>
    /// 依据已登记的证书指纹构造校验回调；未登记指纹时返回 null，沿用系统默认校验。
    /// 仅接受指纹精确匹配的证书，其余（链错误、名称不匹配、未知证书）一律拒绝。
    /// </summary>
    private static System.Net.Security.RemoteCertificateValidationCallback? BuildPinnedCallback(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var allowed = new HashSet<string>(
            configured.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant())
                .Where(value => value.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0) return null;
        return (_, certificate, _, errors) =>
        {
            if (errors == System.Net.Security.SslPolicyErrors.None) return true;
            if (certificate is null) return false;
            try
            {
                // 必须按 SHA-256 计算：X509Certificate2.Thumbprint 返回的是 SHA-1，
                // 与设置名（SHA256）及运维用 openssl -sha256 取得的指纹不一致，会导致永不匹配。
                var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.GetRawCertData()));
                return allowed.Contains(actual);
            }
            catch (System.Security.Cryptography.CryptographicException) { return false; }
        };
    }
}
