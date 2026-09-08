using System.Text.Json;

internal sealed class ZlmClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string? _key;
    private readonly TimeSpan _readyTimeout;
    public ZlmClient(HttpMessageHandler? handler = null, string? url = null, string? key = null, TimeSpan? readyTimeout = null)
    {
        _http = new(handler ?? new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(15) };
        _url = (url ?? Environment.GetEnvironmentVariable("HIK_ZLM_API_URL") ?? "http://127.0.0.1:18080").TrimEnd('/');
        _key = key ?? Environment.GetEnvironmentVariable("HIK_ZLM_API_SECRET");
        _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(15);
    }
    public async Task<JsonElement> CallAsync(string method, Dictionary<string, string> values, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(_key)) throw new AdapterException(503, "ZLM_KEY_MISSING", "未配置 ZLMediaKit 内部管理密钥。");
        values["secret"] = _key;
        using var body = new FormUrlEncodedContent(values);
        using var response = await _http.PostAsync($"{_url}/index/api/{method}", body, token);
        if (!response.IsSuccessStatusCode) throw new AdapterException(502, "ZLM_UNAVAILABLE", "ZLMediaKit 请求失败。");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = document.RootElement;
        if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0) throw new AdapterException(502, "ZLM_ERROR", "ZLMediaKit 无法完成媒体操作。");
        return root.Clone();
    }
    public async Task<string> AddProxyAsync(string stream, string origin, CancellationToken token)
    {
        JsonElement result;
        try
        {
            result = await CallAsync("addStreamProxy", new()
            {
                ["vhost"] = "__defaultVhost__", ["app"] = "live", ["stream"] = stream, ["url"] = origin,
                ["enable_rtsp"] = "1", ["enable_rtmp"] = "1", ["enable_ts"] = "1", ["enable_hls"] = "0", ["rtp_type"] = "0", ["retry_count"] = "-1"
            }, token);
        }
        catch
        {
            // 请求取消可能发生在代理已创建之后，按确定的流标识回收该次资源。
            try { await DeleteProxyAsync($"__defaultVhost__/live/{stream}"); } catch { }
            throw;
        }
        return result.GetProperty("data").GetProperty("key").GetString() ?? throw new InvalidOperationException("媒体代理未返回标识。");
    }
    public async Task DeleteProxyAsync(string key) => await CallAsync("delStreamProxy", new() { ["key"] = key });
    public async Task CloseAsync(string app, string stream) => await CallAsync("close_streams", new() { ["vhost"] = "__defaultVhost__", ["app"] = app, ["stream"] = stream, ["force"] = "1" });
    public async Task WaitReadyAsync(string app, string stream, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_readyTimeout);
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    var state = await CallAsync("getMediaInfo", new() { ["schema"] = "rtsp", ["vhost"] = "__defaultVhost__", ["app"] = app, ["stream"] = stream }, timeout.Token);
                    if (state.TryGetProperty("online", out var online) && online.ValueKind == JsonValueKind.True) return;
                    if (state.TryGetProperty("tracks", out var tracks) && tracks.GetArrayLength() > 0) return;
                }
                catch (AdapterException ex) when (ex.Status == 502) { }
                await Task.Delay(150, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new AdapterException(502, "MEDIA_NOT_READY", "设备媒体流未就绪。"); }
    }
    public async Task CleanupOrphansAsync()
    {
        var root = await CallAsync("getMediaList", new());
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
        var streams = data.EnumerateArray().Select(m => (App: m.GetProperty("app").GetString()!, Stream: m.GetProperty("stream").GetString()!)).Distinct();
        foreach (var item in streams.Where(s => s.Stream.StartsWith("vp2_", StringComparison.Ordinal)))
        {
            if (item.App == "live") await DeleteProxyAsync($"__defaultVhost__/live/{item.Stream}");
            await CloseAsync(item.App, item.Stream);
        }
    }
    public void Dispose() => _http.Dispose();
}
