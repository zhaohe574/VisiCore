using System.Runtime.InteropServices;
using Native = HCNetSDKInterop;

internal sealed partial class HikvisionDevice : IDevice
{
    private const int GetIpParaConfigV40 = 1062, IpParaConfigSize = 50792, StreamModeSize = 496, StreamModeOffset = 19028;
    private readonly object _sdkGate = new();
    private readonly object _searchGate = new();
    private readonly object _ptzGate = new();
    private readonly DeviceOptions _options;
    private readonly SdkRuntime _runtime;
    private readonly AlarmJournal _journal;
    private readonly Timer _watchdog;
    private readonly Dictionary<int, (uint Command, uint Speed, DateTimeOffset Deadline)> _ptz = new();
    private int _controlUserId = -1, _alarmHandle = -1;
    private byte[] _deviceInfo = [];
    private bool _disposed;
    public long Id { get; }
    public DeviceOptions Options => _options;
    public bool Simulated => false;

    public HikvisionDevice(long id, DeviceOptions options, SdkRuntime runtime, AlarmJournal journal)
    {
        Id = id; _options = options; _runtime = runtime; _journal = journal;
        _watchdog = new Timer(_ => Watchdog(), null, 1000, 1000);
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.EnsureInitialized();
        if (_controlUserId >= 0) return;
        _controlUserId = Login(out _deviceInfo);
        _runtime.Register(_controlUserId, _journal);
    }

    public DeviceSnapshot Sync()
    {
        lock (_sdkGate)
        {
            EnsureConnected();
            var summary = Summarize(_deviceInfo);
            var channels = TryReadIsapiChannels() ?? ReadIpChannels(_controlUserId);
            if (TryReadIsapiDeviceInfo() is { } info)
                summary = summary with { Model = info.Model ?? summary.Model, SerialNumber = info.SerialNumber ?? summary.SerialNumber };
            if (_alarmHandle < 0)
            {
                var parameters = new Native.SetupAlarmParam { Size = (uint)Marshal.SizeOf<Native.SetupAlarmParam>(), AlarmInfoType = 1, DeployType = 1, Reserved1 = new byte[3] };
                _alarmHandle = Native.NET_DVR_SetupAlarmChan_V41(_controlUserId, ref parameters);
                if (_alarmHandle < 0) _journal.SignalFault("设备报警布防失败，将在下次同步重试。");
            }
            return new(summary, channels.Select(c => new ChannelInfo(c.ChannelNumber, c.Name ?? $"通道 {c.ChannelNumber}", c.Model, c.Online ?? c.Enabled, c.PtzCapable)).ToArray());
        }
    }

    internal static uint Command(string command) => command switch
    {
        "up" => 21, "down" => 22, "left" => 23, "right" => 24, "auto" => 29,
        "zoomIn" => 11, "zoomOut" => 12, "focusNear" => 13, "focusFar" => 14, "irisOpen" => 15, "irisClose" => 16,
        _ => throw new ArgumentException("云台命令无效。")
    };
    public void Ptz(PtzRequest request)
    {
        Validate.Channel(request.Channel);
        if (request.Speed is < 1 or > 7) throw new ArgumentException("云台速度必须在 1～7 之间。");
        lock (_sdkGate) EnsureConnected();
        lock (_ptzGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var command = request.Stop ? 21u : Command(request.Command);
            var speed = request.Speed;
            if (_ptz.TryGetValue(request.Channel, out var previous))
            {
                if (request.Stop) { command = previous.Command; speed = previous.Speed; }
                else if (previous.Command != command)
                    SdkRuntime.Check(Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, request.Channel, previous.Command, 1, previous.Speed), "停止原云台命令失败");
            }
            else if (request.Stop) return;
            SdkRuntime.Check(Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, request.Channel, command, request.Stop ? 1u : 0u, speed), "云台控制失败");
            if (request.Stop) _ptz.Remove(request.Channel);
            else _ptz[request.Channel] = (command, speed, DateTimeOffset.UtcNow.AddSeconds(10));
        }
    }
    public void Preset(PresetRequest request)
    {
        Validate.Channel(request.Channel);
        if (request.Preset is < 1 or > 300) throw new ArgumentException("预置位编号无效。");
        lock (_sdkGate) { EnsureConnected(); SdkRuntime.Check(Native.NET_DVR_PTZPreset_Other(_controlUserId, request.Channel, 39, request.Preset), "调用预置位失败"); }
    }
    private void Watchdog()
    {
        // 独立时钟不随媒体消费推进，平台掉线后仍会停止机械运动。
        if (!Monitor.TryEnter(_ptzGate)) return;
        try
        {
            if (_disposed) return;
            foreach (var item in _ptz.Where(p => p.Value.Deadline <= DateTimeOffset.UtcNow).ToArray())
            {
                if (Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, item.Key, item.Value.Command, 1, item.Value.Speed) != 0) _ptz.Remove(item.Key);
                else _journal.SignalFault("云台保护停止失败，将持续重试。");
            }
        }
        catch { _journal.SignalFault("云台保护线程发生 SDK 异常。"); }
        finally { Monitor.Exit(_ptzGate); }
    }
    public void Dispose()
    {
        _watchdog.Dispose();
        lock (_sdkGate)
        {
            if (_disposed) return;
            _disposed = true;
            lock (_ptzGate)
            {
                foreach (var item in _ptz) Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, item.Key, item.Value.Command, 1, item.Value.Speed);
                _ptz.Clear();
            }
            if (_alarmHandle >= 0) Native.NET_DVR_CloseAlarmChan_V30(_alarmHandle);
            if (_controlUserId >= 0) { _runtime.Unregister(_controlUserId); Native.NET_DVR_Logout(_controlUserId); }
            _controlUserId = _alarmHandle = -1;
        }
    }
}
