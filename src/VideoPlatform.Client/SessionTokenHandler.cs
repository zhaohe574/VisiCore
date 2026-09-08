using System.Net.Http.Headers;

namespace VideoPlatform.Client;

// 桌面端负责用 DPAPI 安全存储令牌；每个请求读取最新令牌，避免写入共享 DefaultRequestHeaders。
public sealed class SessionTokenHandler(Func<CancellationToken, ValueTask<string?>> readToken) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await readToken(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
