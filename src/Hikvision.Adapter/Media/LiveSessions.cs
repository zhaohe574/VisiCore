using System.Diagnostics;

internal readonly record struct LiveKey(long DeviceId, int Channel, int StreamType, string Profile)
{
    public string Stream => $"vp2_d{DeviceId}_c{Channel}_s{StreamType}_{Profile}";
}

internal sealed class LiveSessions(IDevice device, ZlmClient zlm, TranscodeBudget budget) : IAsyncDisposable
{
    public TranscodeBudget Budget => budget;
    private sealed class Shared(LiveKey key, string codec, string? proxy, Process? process, IDisposable? slot, LiveKey? dependency)
    {
        public LiveKey Key { get; } = key;
        public string Codec { get; } = codec;
        public string? Proxy { get; } = proxy;
        public Process? Process { get; } = process;
        public IDisposable? Slot { get; } = slot;
        public LiveKey? Dependency { get; } = dependency;
        public int References { get; set; }
        public Task? Drain { get; set; }
    }
    private readonly Dictionary<LiveKey, Shared> _streams = new();
    private readonly Dictionary<Guid, (LiveStartRequest Request, LiveKey Key)> _sessions = new();
    public object[] Snapshot => _sessions.Select(s => (object)new { id = s.Key, sessionId = s.Key, deviceId = device.Id, channel = s.Value.Key.Channel, streamType = s.Value.Key.StreamType, profile = s.Value.Key.Profile, stream = s.Value.Key.Stream, state = IsHealthy(_streams[s.Value.Key]) ? "playing" : "failed" }).ToArray();
    private static bool IsHealthy(Shared stream) => stream.Process is null || !stream.Process.HasExited;

    public async Task<LiveSummary> StartAsync(LiveStartRequest request, CancellationToken token)
    {
        Validate.Channel(request.Channel); Validate.Session(request.SessionId, request.Profile);
        if (request.StreamType is not (1 or 2)) throw new ArgumentException("码流类型无效。");
        if (_sessions.TryGetValue(request.SessionId, out var previous))
        {
            if (previous.Request != request) throw new AdapterException(409, "SESSION_CONFLICT", "会话标识已绑定其他实时参数。");
            var existing = _streams[previous.Key];
            if (!IsHealthy(existing)) throw new AdapterException(502, "LIVE_FAILED", "实时转码进程已退出，请停止并重新创建会话。");
            return Summary(existing);
        }
        foreach (var type in request.StreamType == 2 ? new[] { 2, 1 } : new[] { 1 })
        {
            try
            {
                var key = new LiveKey(device.Id, request.Channel, type, request.Profile);
                var stream = await AcquireAsync(key, token);
                _sessions.Add(request.SessionId, (request, key));
                return Summary(stream);
            }
            catch (AdapterException ex) when (request.StreamType == 2 && type == 2 && ex.Status == 502) { }
        }
        throw new AdapterException(502, "LIVE_UNAVAILABLE", "设备主码流与子码流均不可用。");
    }
    private static LiveSummary Summary(Shared value) => new(value.Key.Stream, value.Key.StreamType, value.Codec, value.Slot is not null);
    private async Task<Shared> AcquireAsync(LiveKey key, CancellationToken token)
    {
        if (_streams.TryGetValue(key, out var found))
        {
            if (!IsHealthy(found)) throw new AdapterException(502, "LIVE_FAILED", "共享实时转码已失败，请先清理原会话。");
            found.References++; return found;
        }
        if (device.Simulated)
        {
            var sourceCodec = key.StreamType == 1 ? "H265" : "H264";
            var simulated = new Shared(key, sourceCodec, null, null, null, null) { References = 1 };
            _streams.Add(key, simulated); return simulated;
        }
        var host = device.Options.DeviceIp.Contains(':') ? $"[{device.Options.DeviceIp}]" : device.Options.DeviceIp;
        var origin = $"rtsp://{Uri.EscapeDataString(device.Options.Username)}:{Uri.EscapeDataString(device.Options.Password)}@{host}:554/Streaming/Channels/{key.Channel}{key.StreamType:D2}";
        var proxy = await zlm.AddProxyAsync(key.Stream, origin, token);
        try
        {
            await zlm.WaitReadyAsync("live", key.Stream, token, "rtsp");
            var codecs = await MediaTools.ProbeAsync(MediaTools.InternalRtsp("live", key.Stream), token);
            var shared = new Shared(key, codecs.DisplayVideo, proxy, null, null, null) { References = 1 };
            _streams.Add(key, shared); return shared;
        }
        catch { await zlm.DeleteProxyAsync(proxy); throw; }
    }
    public async Task StopAsync(Guid id)
    {
        if (!_sessions.TryGetValue(id, out var session)) return;
        await ReleaseAsync(session.Key); _sessions.Remove(id);
    }
    private async Task ReleaseAsync(LiveKey key)
    {
        if (!_streams.TryGetValue(key, out var stream)) return;
        if (stream.References > 1) { stream.References--; return; }
        if (stream.Process is not null)
        {
            MediaTools.Kill(stream.Process);
            if (stream.Drain is not null) await stream.Drain;
        }
        if (!device.Simulated)
        {
            if (stream.Proxy is not null) await zlm.DeleteProxyAsync(stream.Proxy);
            else await zlm.CloseAsync("live", key.Stream);
        }
        if (stream.Dependency is { } dependency) await ReleaseAsync(dependency);
        stream.Process?.Dispose(); stream.Slot?.Dispose(); _streams.Remove(key);
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var id in _sessions.Keys.ToArray()) await StopAsync(id);
    }
}
