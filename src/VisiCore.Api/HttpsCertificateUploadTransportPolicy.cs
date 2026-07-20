using System.Net;
using Microsoft.AspNetCore.Http;

namespace VisiCore.Api;

/// <summary>
/// 私钥上传只允许 TLS，或经本机回环访问的首次部署 HTTP。
/// </summary>
public static class HttpsCertificateUploadTransportPolicy
{
    public static bool Allows(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            return true;
        }

        // Nginx 是容器内唯一允许的反向代理。它会覆盖转发头；直接请求 API 的场景只允许回环地址。
        if (context.Connection.RemoteIpAddress is not { } remoteAddress || !IPAddress.IsLoopback(remoteAddress))
        {
            return false;
        }

        var forwardedProtocol = context.Request.Headers["X-Forwarded-Proto"].ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.Equals(forwardedProtocol, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Docker Desktop 会把宿主机的 127.0.0.1 连接 NAT 为容器网关地址；此时 X-Forwarded-For
        // 已不是回环地址。Host 仍由 Nginx 原样转发，且浏览器不能跨源伪造它，因此允许明确的回环访问。
        if (context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(context.Request.Host.Host, out var requestHostAddress) && IPAddress.IsLoopback(requestHostAddress))
        {
            return true;
        }

        var forwardedAddress = context.Request.Headers["X-Forwarded-For"].ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(forwardedAddress) ||
               IPAddress.TryParse(forwardedAddress, out var address) && IPAddress.IsLoopback(address);
    }
}
