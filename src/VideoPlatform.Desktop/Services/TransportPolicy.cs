using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace VideoPlatform.Desktop.Services;

public static class TransportPolicy
{
    public static SocketsHttpHandler CreateHandler(string? sourceAddress = null, string? hostPrefix = null)
    {
        var settings = ClientFiles.LoadSettings();
        sourceAddress ??= settings.SourceAddress;
        hostPrefix ??= settings.SourceHostPrefix;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 100,
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
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
}
