using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace VisiCore.StreamGateway;

public sealed class HlsProxyService(
    HttpClient mediaMtxHlsClient,
    StreamAuthorizationStore authorizationStore,
    MediaMtxOptions mediaMtxOptions,
    ILiveTranscodeRelayManager? liveTranscodeRelayManager = null)
{
    private const int MaxUpstreamRedirects = 1;

    private static readonly HashSet<string> SuppressedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "Set-Cookie", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    };

    public async Task ProxyAsync(HttpContext context)
    {
        if (!HlsProxyRequest.TryParse(context.Request.Path, out var proxyRequest))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var connectionId = BuildConnectionId(
            proxyRequest.SessionId,
            proxyRequest.Ticket,
            context.Connection.RemoteIpAddress);
        if (!await authorizationStore.AuthorizeProxyAsync(
                proxyRequest.StreamKey,
                proxyRequest.SessionId,
                proxyRequest.Ticket,
                connectionId,
                context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (liveTranscodeRelayManager is not null)
        {
            try
            {
                await liveTranscodeRelayManager.EnsureReadyAsync(
                    proxyRequest.StreamKey,
                    context.RequestAborted);
            }
            catch (LiveTranscodeUnavailableException)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "2";
                return;
            }
        }

        var upstreamPath = proxyRequest.StreamKey + "/" + proxyRequest.ResourcePath + context.Request.QueryString;
        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await SendUpstreamAsync(context, proxyRequest, upstreamPath);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.Headers.RetryAfter = "1";
            return;
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (upstreamResponse)
        {
            if (upstreamResponse.StatusCode == HttpStatusCode.GatewayTimeout)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                context.Response.Headers.RetryAfter = "1";
                return;
            }
            if (upstreamResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            context.Response.StatusCode = (int)upstreamResponse.StatusCode;
            CopyResponseHeaders(upstreamResponse, context.Response);
            if (proxyRequest.ResourcePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            if (!HttpMethods.IsHead(context.Request.Method) && upstreamResponse.Content is not null)
            {
                await upstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
        }
    }

    private async Task<HttpResponseMessage> SendUpstreamAsync(
        HttpContext context,
        HlsProxyRequest proxyRequest,
        string upstreamPath)
    {
        var retryPlaylist = HttpMethods.IsGet(context.Request.Method) &&
            string.Equals(proxyRequest.ResourcePath, "index.m3u8", StringComparison.OrdinalIgnoreCase) &&
            mediaMtxOptions.PlaylistStartupRetrySeconds > 0;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(mediaMtxOptions.PlaylistStartupRetrySeconds);
        var retryDelayMilliseconds = mediaMtxOptions.PlaylistStartupRetryMilliseconds;
        while (true)
        {
            HttpResponseMessage upstreamResponse;
            try
            {
                upstreamResponse = await SendWithSafeRedirectsAsync(context, upstreamPath);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                if (!retryPlaylist || DateTimeOffset.UtcNow >= deadline)
                {
                    return new HttpResponseMessage(HttpStatusCode.GatewayTimeout);
                }

                // 首次 HLS 清单可能等待首个关键帧，内部单次超时后在启动窗口内重试。
                var timeoutDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelayMilliseconds, Math.Max(1, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds)));
                await Task.Delay(timeoutDelay, context.RequestAborted);
                retryDelayMilliseconds = Math.Min(retryDelayMilliseconds * 2, 1000);
                continue;
            }
            if (!retryPlaylist || upstreamResponse.StatusCode != HttpStatusCode.NotFound || DateTimeOffset.UtcNow >= deadline)
            {
                return upstreamResponse;
            }

            upstreamResponse.Dispose();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var delay = TimeSpan.FromMilliseconds(Math.Min(retryDelayMilliseconds, Math.Max(1, remaining.TotalMilliseconds)));
            await Task.Delay(delay, context.RequestAborted);
            retryDelayMilliseconds = Math.Min(retryDelayMilliseconds * 2, 1000);
        }
    }

    private async Task<HttpResponseMessage> SendWithSafeRedirectsAsync(
        HttpContext context,
        string upstreamPath)
    {
        var baseAddress = mediaMtxHlsClient.BaseAddress ??
            throw new InvalidOperationException("MediaMTX HLS 客户端缺少上游基址。");
        var originalUri = new Uri(baseAddress, upstreamPath);
        var requestUri = originalUri;
        var internalAuthorization = mediaMtxHlsClient.DefaultRequestHeaders.Authorization;
        for (var redirectCount = 0; ; redirectCount++)
        {
            using var upstreamRequest = new HttpRequestMessage(
                new HttpMethod(context.Request.Method),
                requestUri);
            CopyRequestHeader(context.Request.Headers, upstreamRequest, "Accept");
            CopyRequestHeader(context.Request.Headers, upstreamRequest, "If-Modified-Since");
            CopyRequestHeader(context.Request.Headers, upstreamRequest, "If-None-Match");
            CopyRequestHeader(context.Request.Headers, upstreamRequest, "Range");
            if (internalAuthorization is not null)
            {
                upstreamRequest.Headers.Authorization = internalAuthorization;
            }

            var upstreamResponse = await mediaMtxHlsClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
            if (!IsRedirect(upstreamResponse.StatusCode))
            {
                return upstreamResponse;
            }

            if (redirectCount >= MaxUpstreamRedirects ||
                !TryResolveSafeRedirect(originalUri, requestUri, upstreamResponse, out var redirectUri))
            {
                upstreamResponse.Dispose();
                return new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent(string.Empty)
                };
            }

            upstreamResponse.Dispose();
            requestUri = redirectUri;
        }
    }

    private static bool TryResolveSafeRedirect(
        Uri originalUri,
        Uri requestUri,
        HttpResponseMessage response,
        out Uri redirectUri)
    {
        redirectUri = null!;
        if (!response.Headers.TryGetValues("Location", out var rawLocations))
        {
            return false;
        }
        var locations = rawLocations.Take(2).ToArray();
        if (locations.Length != 1 ||
            !Uri.TryCreate(locations[0], UriKind.RelativeOrAbsolute, out var location) ||
            !Uri.TryCreate(requestUri, location, out var resolved) ||
            !string.Equals(resolved.Scheme, originalUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resolved.IdnHost, originalUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            resolved.Port != originalUri.Port ||
            !string.IsNullOrEmpty(resolved.UserInfo) ||
            !string.IsNullOrEmpty(resolved.Fragment) ||
            !string.Equals(
                resolved.GetComponents(UriComponents.Path, UriFormat.UriEscaped),
                originalUri.GetComponents(UriComponents.Path, UriFormat.UriEscaped),
                StringComparison.Ordinal))
        {
            return false;
        }

        redirectUri = resolved;
        return true;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static void CopyRequestHeader(IHeaderDictionary headers, HttpRequestMessage request, string name)
    {
        if (headers.TryGetValue(name, out var values))
        {
            request.Headers.TryAddWithoutValidation(name, values.ToArray());
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse target)
    {
        foreach (var header in source.Headers.Concat(source.Content.Headers))
        {
            if (!SuppressedResponseHeaders.Contains(header.Key))
            {
                target.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }

    private static string BuildConnectionId(Guid sessionId, string ticket, IPAddress? remoteIpAddress)
    {
        var source = $"{sessionId:N}\n{ticket}\n{remoteIpAddress}";
        return "proxy-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..32];
    }
}

public sealed record HlsProxyRequest(
    Guid SessionId,
    string Ticket,
    string StreamKey,
    string ResourcePath)
{
    public static bool TryParse(PathString requestPath, out HlsProxyRequest request)
    {
        request = null!;
        var rawPath = requestPath.Value;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var segments = rawPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 6 || !string.Equals(segments[0], "hls", StringComparison.Ordinal) ||
            !Guid.TryParseExact(segments[1], "N", out var sessionId) || !IsTicket(segments[2]))
        {
            return false;
        }

        string streamKey;
        int resourceStart;
        if (string.Equals(segments[3], "live", StringComparison.Ordinal))
        {
            if (segments.Length < 7 || !Guid.TryParseExact(segments[4], "N", out _) ||
                (segments[5] != "main" && segments[5] != "sub"))
            {
                return false;
            }
            streamKey = $"live/{segments[4]}/{segments[5]}";
            resourceStart = 6;
        }
        else if (string.Equals(segments[3], "playback", StringComparison.Ordinal))
        {
            if (segments.Length < 6 || !Guid.TryParseExact(segments[4], "N", out var playbackSessionId) ||
                playbackSessionId != sessionId)
            {
                return false;
            }
            streamKey = $"playback/{segments[4]}";
            resourceStart = 5;
        }
        else
        {
            return false;
        }

        var resourceSegments = segments[resourceStart..];
        if (resourceSegments.Any(item =>
                item is "." or ".." ||
                item.Contains('%', StringComparison.Ordinal) ||
                item.Contains('\\', StringComparison.Ordinal)))
        {
            return false;
        }

        request = new HlsProxyRequest(sessionId, segments[2], streamKey, string.Join('/', resourceSegments));
        return true;
    }

    private static bool IsTicket(string ticket) =>
        ticket.Length is >= 32 and <= 256 &&
        ticket.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
