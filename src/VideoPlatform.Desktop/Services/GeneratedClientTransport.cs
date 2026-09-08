using System.Net.Http;
using System.Net.Http.Headers;

namespace VideoPlatform.Desktop.Services;

// 复用应用注入的 HTTP 连接池；每次调用固定服务器及令牌，不修改共享客户端的默认请求头。
internal sealed class GeneratedClientTransport(HttpClient http, string? token) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
            Content = request.Content
        };
        foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (token is not null) forwarded.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }
}
