using System.Collections.Concurrent;

// 仅显式 HIK_ADAPTER_SIMULATOR=1 启用，模拟结果不能作为实机验收。
internal sealed class SimulatedDevice(long id, DeviceOptions options, AlarmJournal journal, TimeProvider? clock = null) : IDevice
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<int, DateTimeOffset> _ptz = new();
    private bool _disposed;
    private int _synced;
    public long Id => id;
    public DeviceOptions Options => options;
    public bool Simulated => true;
    public DeviceSnapshot Sync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _synced, 1) == 0)
            journal.TryEnqueue(new(_clock.GetUtcNow(), 0x1100, [], new("alarm.motion", 3, [1], false, _clock.GetUtcNow(), null, null)));
        return new(new($"SIM-{id:D6}", 0, 81, 1, 8, 0, 0, true, "模拟海康录像机", options.DeviceIp, options.DevicePort),
            Enumerable.Range(1, 81).Select(c => new ChannelInfo(c, $"设备 {id} 通道 {c:D2}", c == 1 ? "模拟云台" : "模拟摄像机", c != 81, c == 1, "H265")).ToArray());
    }
    public IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Validate.Range(channel, start, end);
        if (channel == 81) return [];
        var duration = end - start;
        return [new($"sim-{id}-{channel}-1", start, start + duration / 3, 1024, 0, 0, 1), new($"sim-{id}-{channel}-2", start + duration * 2 / 3, end, 1024, 0, 0, 2)];
    }
    public IPlaybackSource OpenPlayback(int channel, DateTimeOffset start, DateTimeOffset end, uint fileIndex, Action<uint, IntPtr, uint> receive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SimulatedPlaybackSource(start, end, _clock);
    }
    public async Task DownloadAsync(int channel, DateTimeOffset start, DateTimeOffset end, string path, Action<int> progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        progress(0);
        var seconds = Math.Min(3, (end - start).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await MediaTools.RunAsync(MediaTools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-t", seconds, "-c:v", "libx264", "-preset", "ultrafast", "-f", "mpegts", path], cancellationToken);
        progress(100);
    }
    public void Ptz(PtzRequest request)
    {
        Validate.Channel(request.Channel);
        if (request.Stop) { _ptz.TryRemove(request.Channel, out _); return; }
        HikvisionDevice.Command(request.Command);
        if (request.Speed is < 1 or > 7) throw new ArgumentException("云台速度无效。");
        _ptz[request.Channel] = _clock.GetUtcNow().AddSeconds(10);
    }
    internal bool IsMoving(int channel) => _ptz.TryGetValue(channel, out var expiry) && expiry > _clock.GetUtcNow();
    public void Preset(PresetRequest request)
    {
        Validate.Channel(request.Channel);
        if (request.Preset is < 1 or > 300) throw new ArgumentException("预置位无效。");
    }
    public void Dispose() { _disposed = true; _ptz.Clear(); }
}

internal sealed class SimulatedPlaybackSource(DateTimeOffset start, DateTimeOffset end, TimeProvider clock) : IPlaybackSource
{
    private readonly object _gate = new();
    private DateTimeOffset _position = start;
    private DateTimeOffset _at = clock.GetUtcNow();
    private double _speed = 1;
    private bool _playing;
    public void Start() { lock (_gate) { _at = clock.GetUtcNow(); _playing = true; } }
    public void Pause(bool pause) { lock (_gate) { Advance(); _playing = !pause; } }
    public void SetSpeed(double speed)
    {
        if (speed is not (0.25 or 0.5 or 1 or 2 or 4)) throw new ArgumentException("模拟倍速无效。");
        lock (_gate) { Advance(); _speed = speed; }
    }
    private void Advance()
    {
        var now = clock.GetUtcNow();
        if (_playing) _position += (now - _at) * _speed;
        if (_position > end) _position = end;
        _at = now;
    }
    public DateTimeOffset? CurrentTime { get { lock (_gate) { Advance(); return _position; } } }
    public bool Completed => CurrentTime >= end;
    public void Dispose() { lock (_gate) _playing = false; }
}
