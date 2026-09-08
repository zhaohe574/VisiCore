using System.Collections.Concurrent;

internal sealed class DeviceEntry(long id, AlarmJournal journal)
{
    public long Id { get; } = id;
    public AlarmJournal Journal { get; } = journal;
    public SemaphoreSlim Gate { get; } = new(1);
    public DeviceRegistration? Registration;
    public IDevice? Device;
    public LiveSessions? Live;
    public Dictionary<Guid, PlaybackSession> Playback { get; } = new();
    public string State = "registered";
    public string? Error;
    public DateTimeOffset? LastSeenAt;
    public IDevice Required => Device ?? throw new AdapterException(409, "DEVICE_DISABLED", "设备已禁用或尚未注册。");
}

internal sealed class DeviceRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, DeviceEntry> _devices = new();
    private readonly SdkRuntime _runtime;
    private readonly ZlmClient _zlm;
    private readonly TranscodeBudget _budget;
    private readonly ExportService _exports;
    private readonly string _alarmRoot;
    public bool Simulated { get; }
    public Guid BootId { get; } = Guid.NewGuid();
    public DeviceRegistry(SdkRuntime runtime, ZlmClient zlm, TranscodeBudget budget, ExportService exports, string alarmRoot, bool simulated)
    {
        _runtime = runtime; _zlm = zlm; _budget = budget; _exports = exports; _alarmRoot = alarmRoot; Simulated = simulated;
    }
    public async Task<object> RegisterAsync(long id, DeviceRegistration registration)
    {
        if (id <= 0 || registration.Port == 0 || Uri.CheckHostName(registration.Host) == UriHostNameType.Unknown || string.IsNullOrWhiteSpace(registration.Username) || string.IsNullOrEmpty(registration.Password))
            throw new ArgumentException("设备编号、地址、端口或登录凭据无效。");
        // Lazy 创建由独立短锁保护，避免 ConcurrentDictionary 工厂重复启动日志写线程。
        DeviceEntry entry;
        lock (_devices)
        {
            if (!_devices.TryGetValue(id, out entry!))
            {
                if (_devices.Count >= 256) throw new AdapterException(429, "DEVICE_LIMIT", "适配器设备数量已达上限。");
                entry = new(id, new AlarmJournal(id, _alarmRoot)); _devices[id] = entry;
            }
        }
        await entry.Gate.WaitAsync();
        try
        {
            if (entry.Registration == registration) return Describe(entry);
            await StopResourcesAsync(entry);
            entry.Registration = registration;
            if (registration.Enabled)
            {
                var options = new DeviceOptions(registration.Host, registration.Port, registration.Username, registration.Password);
                entry.Device = Simulated ? new SimulatedDevice(id, options, entry.Journal) : new HikvisionDevice(id, options, _runtime, entry.Journal);
                entry.Live = new(entry.Device, _zlm, _budget); entry.State = "registered";
            }
            else entry.State = "disabled";
            entry.Error = null;
            return Describe(entry);
        }
        finally { entry.Gate.Release(); }
    }
    public async Task DeleteAsync(long id)
    {
        if (!_devices.TryGetValue(id, out var entry)) return;
        await entry.Gate.WaitAsync();
        try { await StopResourcesAsync(entry); entry.Registration = null; entry.State = "disabled"; }
        finally { entry.Gate.Release(); }
    }
    public DeviceEntry Find(long id) => _devices.TryGetValue(id, out var entry) ? entry : throw new AdapterException(404, "DEVICE_NOT_FOUND", "设备尚未在适配器注册。");
    public async Task<T> UseAsync<T>(long id, Func<DeviceEntry, Task<T>> action)
    {
        var entry = Find(id);
        await entry.Gate.WaitAsync();
        try { return await action(entry); }
        finally { entry.Gate.Release(); }
    }
    public async Task<DeviceSnapshot> SyncAsync(long id)
    {
        return await UseAsync(id, entry =>
        {
            try { var result = entry.Required.Sync(); entry.State = "online"; entry.LastSeenAt = DateTimeOffset.UtcNow; entry.Error = null; return Task.FromResult(result); }
            catch { entry.State = "offline"; entry.Error = "设备同步失败，请检查连接和凭据。"; throw; }
        });
    }
    public async Task<PlaybackSummary> StartPlaybackAsync(long id, PlaybackStartRequest request)
    {
        Validate.Range(request.Channel, request.Start, request.End); Validate.Session(request.SessionId, request.Profile);
        return await UseAsync(id, async entry =>
        {
            if (entry.Playback.TryGetValue(request.SessionId, out var existing))
            {
                if (existing.Request != request) throw new AdapterException(409, "SESSION_CONFLICT", "回放会话标识已绑定其他参数。");
                return existing.Summary();
            }
            if (entry.Playback.Count >= 20) throw new AdapterException(429, "PLAYBACK_LIMIT", "单设备回放会话达到上限。");
            var device = entry.Required;
            var segments = PlaybackTimeline.Normalize(device.SearchRecordings(request.Channel, request.Start, request.End), request.Start, request.End);
            var session = new PlaybackSession(device, request, segments, _zlm, _budget);
            try { await session.StartAsync(); entry.Playback.Add(request.SessionId, session); return session.Summary(); }
            catch { await session.DisposeAsync(); throw; }
        });
    }
    public async Task StopPlaybackAsync(long id, Guid sessionId)
    {
        await UseAsync(id, async entry =>
        {
            if (entry.Playback.TryGetValue(sessionId, out var session)) { await session.DisposeAsync(); entry.Playback.Remove(sessionId); }
            return true;
        });
    }
    public static PlaybackSession Playback(DeviceEntry entry, Guid id) => entry.Playback.TryGetValue(id, out var session) ? session : throw new AdapterException(404, "PLAYBACK_NOT_FOUND", "回放会话不存在。");
    public async Task<object> SessionsAsync()
    {
        var devices = new List<object>(); var live = new List<object>(); var playback = new List<object>();
        foreach (var entry in _devices.Values)
        {
            await entry.Gate.WaitAsync();
            try
            {
                devices.Add(Describe(entry));
                if (entry.Live is not null) live.AddRange(entry.Live.Snapshot);
                playback.AddRange(entry.Playback.Values.Select(s => (object)new { id = s.Request.SessionId, sessionId = s.Request.SessionId, deviceId = entry.Id, userId = s.Request.UserId, channel = s.Request.Channel, profile = s.Request.Profile, state = s.Summary().State, stream = s.Summary().Stream }));
            }
            finally { entry.Gate.Release(); }
        }
        return new { bootId = BootId, devices, live, playback, exports = _exports.Snapshot };
    }
    private static object Describe(DeviceEntry entry) => new { id = entry.Id, deviceId = entry.Id, enabled = entry.Registration?.Enabled == true, state = entry.State, lastSeenAt = entry.LastSeenAt, error = entry.Error };
    public object Health => new { bootId = BootId, simulated = Simulated, registeredDevices = _devices.Count, transcodes = _budget.Count, alarms = _devices.Values.Select(e => e.Journal.Health).ToArray() };
    private async Task StopResourcesAsync(DeviceEntry entry)
    {
        await _exports.CancelDeviceAsync(entry.Id);
        foreach (var playback in entry.Playback.Values) await playback.DisposeAsync();
        entry.Playback.Clear();
        if (entry.Live is not null) await entry.Live.DisposeAsync();
        entry.Live = null; entry.Device?.Dispose(); entry.Device = null;
    }
    public async Task PollAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(token))
            {
                await Parallel.ForEachAsync(_devices.Values.Where(e => e.Registration?.Enabled == true), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, async (entry, _) =>
                {
                    try { await SyncAsync(entry.Id); }
                    catch { entry.Journal.SignalFault("设备周期同步失败，将自动重试。"); }
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _devices.Values)
        {
            await entry.Gate.WaitAsync();
            try { await StopResourcesAsync(entry); await entry.Journal.DisposeAsync(); }
            finally { entry.Gate.Release(); }
        }
    }
}
