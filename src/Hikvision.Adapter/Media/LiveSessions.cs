using System.Collections.Concurrent;
using System.Diagnostics;

internal readonly record struct LiveKey(long DeviceId, int Channel, int StreamType, string Profile)
{
    public string Stream => $"vp2_d{DeviceId}_c{Channel}_s{StreamType}_{Profile}";
}

internal sealed class LiveSessions(IDevice device, ZlmClient zlm, TranscodeBudget budget) : IAsyncDisposable
{
    public TranscodeBudget Budget => budget;
    private sealed class Shared(LiveKey key, string codec, string? proxy, Process? process, IDisposable? slot, LiveKey? dependency, int? width = null, int? height = null, int? bitrateKbps = null)
    {
        public LiveKey Key { get; } = key;
        public string Codec { get; } = codec;
        public int? Width { get; } = width;
        public int? Height { get; } = height;
        public int? BitrateKbps { get; } = bitrateKbps;
        public string? Proxy { get; } = proxy;
        public Process? Process { get; } = process;
        public IDisposable? Slot { get; } = slot;
        public LiveKey? Dependency { get; } = dependency;
        public int References { get; set; }
        public Task? Drain { get; set; }
    }
    private readonly Dictionary<LiveKey, Shared> _streams = new();
    private readonly Dictionary<Guid, (LiveStartRequest Request, LiveKey Key)> _sessions = new();
    private readonly ConcurrentDictionary<LiveKey, SemaphoreSlim> _streamGates = new();

    public object[] Snapshot
    {
        get
        {
            lock (_streams)
            {
                return _sessions.Select(s => (object)new
                {
                    id = s.Key,
                    sessionId = s.Key,
                    deviceId = device.Id,
                    channel = s.Value.Key.Channel,
                    streamType = s.Value.Key.StreamType,
                    profile = s.Value.Key.Profile,
                    stream = s.Value.Key.Stream,
                    state = _streams.TryGetValue(s.Value.Key, out var val) && IsHealthy(val) ? "playing" : "failed"
                }).ToArray();
            }
        }
    }

    private static bool IsHealthy(Shared stream) => stream.Process is null || !stream.Process.HasExited;

    public async Task<LiveSummary> StartAsync(LiveStartRequest request, CancellationToken token)
    {
        Validate.Channel(request.Channel); Validate.Session(request.SessionId, request.Profile);
        if (request.StreamType is not (1 or 2)) throw new ArgumentException("码流类型无效。");

        lock (_streams)
        {
            if (_sessions.TryGetValue(request.SessionId, out var previous))
            {
                if (previous.Request != request) throw new AdapterException(409, "SESSION_CONFLICT", "会话标识已绑定其他实时参数。");
                var existing = _streams[previous.Key];
                if (!IsHealthy(existing)) throw new AdapterException(502, "LIVE_FAILED", "实时转码进程已退出，请停止并重新创建会话。");
                return Summary(existing);
            }
        }

        foreach (var type in request.StreamType == 2 ? new[] { 2, 1 } : new[] { 1 })
        {
            try
            {
                var key = new LiveKey(device.Id, request.Channel, type, request.Profile);
                var streamGate = _streamGates.GetOrAdd(key, _ => new SemaphoreSlim(1));
                await streamGate.WaitAsync(token);
                try
                {
                    lock (_streams)
                    {
                        if (_sessions.TryGetValue(request.SessionId, out var previous))
                        {
                            return Summary(_streams[previous.Key]);
                        }
                    }

                    var stream = await AcquireAsync(key, token);

                    lock (_streams)
                    {
                        _sessions[request.SessionId] = (request, key);
                    }
                    return Summary(stream);
                }
                finally { streamGate.Release(); }
            }
            catch (AdapterException ex) when (request.StreamType == 2 && type == 2 && ex.Status == 502) { }
        }
        throw new AdapterException(502, "LIVE_UNAVAILABLE", "设备主码流与子码流均不可用。");
    }

    private static LiveSummary Summary(Shared value) => new(value.Key.Stream, value.Key.StreamType, value.Codec, value.Slot is not null, value.Width, value.Height, value.BitrateKbps);

    private async Task<Shared> AcquireAsync(LiveKey key, CancellationToken token)
    {
        lock (_streams)
        {
            if (_streams.TryGetValue(key, out var found))
            {
                if (!IsHealthy(found)) throw new AdapterException(502, "LIVE_FAILED", "共享实时转码已失败，请先清理原会话。");
                found.References++; return found;
            }
        }

        if (device.Simulated)
        {
            var sourceCodec = key.StreamType == 1 ? "H265" : "H264";
            var simulated = new Shared(key, sourceCodec, null, null, null, null) { References = 1 };
            lock (_streams) { _streams[key] = simulated; }
            return simulated;
        }

        var host = device.Options.DeviceIp.Contains(':') ? $"[{device.Options.DeviceIp}]" : device.Options.DeviceIp;
        var origin = $"rtsp://{Uri.EscapeDataString(device.Options.Username)}:{Uri.EscapeDataString(device.Options.Password)}@{host}:554/Streaming/Channels/{key.Channel}{key.StreamType:D2}";
        var proxy = await zlm.AddProxyAsync(key.Stream, origin, token);
        try
        {
            await zlm.WaitReadyAsync("live", key.Stream, token, "rtsp");
            var codecs = await MediaTools.ProbeAsync(MediaTools.InternalRtsp("live", key.Stream), token);
            var shared = new Shared(key, codecs.DisplayVideo, proxy, null, null, null, codecs.Width, codecs.Height, codecs.BitrateKbps) { References = 1 };
            lock (_streams) { _streams[key] = shared; }
            return shared;
        }
        catch { await zlm.DeleteProxyAsync(proxy); throw; }
    }

    public async Task StopAsync(Guid id)
    {
        LiveKey key;
        lock (_streams)
        {
            if (!_sessions.TryGetValue(id, out var session)) return;
            key = session.Key;
            _sessions.Remove(id);
        }
        var streamGate = _streamGates.GetOrAdd(key, _ => new SemaphoreSlim(1));
        await streamGate.WaitAsync();
        try
        {
            await ReleaseAsync(key);
        }
        finally { streamGate.Release(); }
    }

    private async Task ReleaseAsync(LiveKey key)
    {
        Shared? stream;
        lock (_streams)
        {
            if (!_streams.TryGetValue(key, out stream)) return;
            if (stream.References > 1) { stream.References--; return; }
            _streams.Remove(key);
        }
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
        stream.Process?.Dispose(); stream.Slot?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Guid[] ids;
        lock (_streams) { ids = _sessions.Keys.ToArray(); }
        foreach (var id in ids) await StopAsync(id);
    }
}

