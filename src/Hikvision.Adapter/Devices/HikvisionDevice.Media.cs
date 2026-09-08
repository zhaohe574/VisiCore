using System.Runtime.InteropServices;
using Native = HCNetSDKInterop;

internal sealed partial class HikvisionDevice
{
    public IPlaybackSource OpenPlayback(int channel, DateTimeOffset start, DateTimeOffset end, uint fileIndex, Action<uint, IntPtr, uint> receive)
    {
        lock (_sdkGate)
        {
            EnsureConnected();
            var vod = new Native.VodPara
            {
                Size = (uint)Marshal.SizeOf<Native.VodPara>(),
                StreamInfo = new() { Size = (uint)Marshal.SizeOf<Native.StreamInfo>(), Id = new byte[32], Channel = (uint)channel, Reserved = new byte[32] },
                BeginTime = Native.Time.From(start), EndTime = Native.Time.From(end), FileIndex = fileIndex,
                Async = 1, Reserved = new byte[19]
            };
            var handle = Native.NET_DVR_PlayBackByTime_V40(_controlUserId, ref vod);
            if (handle < 0) throw SdkError("建立回放失败");
            try { return new SdkPlaybackSource(handle, receive); }
            catch { Native.NET_DVR_StopPlayBack(handle); throw; }
        }
    }

    public async Task DownloadAsync(int channel, DateTimeOffset start, DateTimeOffset end, string path, Action<int> progress, CancellationToken cancellationToken)
    {
        int handle;
        lock (_sdkGate)
        {
            EnsureConnected();
            var condition = new Native.DownloadCondition
            {
                Channel = (uint)channel, Start = Native.Time.From(start), End = Native.Time.From(end),
                Download = 1, StreamId = new byte[32], Reserved = new byte[26]
            };
            handle = Native.NET_DVR_GetFileByTime_V40(_controlUserId, path, ref condition);
            if (handle < 0) throw SdkError("创建按时间下载任务失败");
        }
        try
        {
            SdkRuntime.Check(Native.NET_DVR_PlayBackControl_V40(handle, 1, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), "启动录像下载失败");
            var lastProgress = -1;
            var changedAt = DateTimeOffset.UtcNow;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var value = Native.NET_DVR_GetDownloadPos(handle);
                if (value < 0 || value > 100) throw SdkError("录像下载失败");
                if (value != lastProgress) { progress(value); lastProgress = value; changedAt = DateTimeOffset.UtcNow; }
                if (value == 100) break;
                if (DateTimeOffset.UtcNow - changedAt > TimeSpan.FromMinutes(5)) throw new TimeoutException("录像下载连续五分钟无进度。");
                await Task.Delay(500, cancellationToken);
            }
        }
        finally { Native.NET_DVR_StopGetFile(handle); }
    }
}

internal sealed class SdkPlaybackSource : IPlaybackSource
{
    private readonly object _gate = new();
    private readonly Native.PlayDataCallback _callback;
    private int _handle;
    private int _completed;
    public SdkPlaybackSource(int handle, Action<uint, IntPtr, uint> receive)
    {
        _handle = handle;
        _callback = (_, type, pointer, size, _) =>
        {
            if (type == 12) Interlocked.Exchange(ref _completed, 1);
            try { receive(type, pointer, size); } catch { /* 原生回调边界不得传播托管异常。 */ }
        };
        SdkRuntime.Check(Native.NET_DVR_SetPlayDataCallBack_V40(handle, _callback, IntPtr.Zero), "设置回放数据回调失败");
    }
    public void Start() { lock (_gate) Control(1); }
    public void Pause(bool pause) { lock (_gate) Control(pause ? 3u : 4u); }
    public void SetSpeed(double speed)
    {
        if (speed is not (0.25 or 0.5 or 1 or 2 or 4)) throw new ArgumentException("回放倍速支持 0.25、0.5、1、2、4。");
        lock (_gate)
        {
            Control(7);
            try
            {
                var steps = (int)Math.Abs(Math.Log2(speed));
                for (var i = 0; i < steps; i++) Control(speed < 1 ? 6u : 5u);
            }
            catch { Control(7); throw; }
        }
    }
    private void Control(uint command)
    {
        ObjectDisposedException.ThrowIf(_handle < 0, this);
        SdkRuntime.Check(Native.NET_DVR_PlayBackControl_V40(_handle, command, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), "回放控制失败或设备不支持该操作");
    }
    public bool Completed
    {
        get
        {
            if (Volatile.Read(ref _completed) != 0) return true;
            lock (_gate)
            {
                if (_handle < 0) return false;
                var position = Native.NET_DVR_GetPlayBackPos(_handle);
                if (position == 100) Interlocked.Exchange(ref _completed, 1);
                else if (position < 0 || position > 100) throw new AdapterException(502, "PLAYBACK_TRANSFER", "SDK 回放数据传输失败。");
                return Volatile.Read(ref _completed) != 0;
            }
        }
    }
    public DateTimeOffset? CurrentTime
    {
        get
        {
            lock (_gate)
            {
                if (_handle < 0) return null;
                var time = new Native.Time();
                if (Native.NET_DVR_GetPlayBackOsdTime(_handle, ref time) == 0) return null;
                try { return new DateTimeOffset((int)time.Year, (int)time.Month, (int)time.Day, (int)time.Hour, (int)time.Minute, (int)time.Second, TimeSpan.FromHours(8)); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle >= 0) Native.NET_DVR_StopPlayBack(_handle);
            _handle = -1; GC.KeepAlive(_callback);
        }
    }
}
