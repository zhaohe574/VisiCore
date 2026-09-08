using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Native = HCNetSDKInterop;

// SDK 生命周期及进程级回调只有一个所有者，设备释放不能清理其他设备使用的 SDK。
internal sealed class SdkRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<int, AlarmJournal> _users = new();
    private readonly Native.MessageCallback _alarm;
    private readonly Native.ExceptionCallback _exception;
    private bool _initialized;
    public SdkRuntime() { _alarm = OnAlarm; _exception = OnException; }

    public void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_initialized) return;
            if (!OperatingSystem.IsLinux()) throw new AdapterException(503, "SDK_PLATFORM", "真实海康适配器需要 Linux x64 运行环境。");
            var root = Path.GetFullPath(Environment.GetEnvironmentVariable("HIK_SDK_DIR") ?? "/opt/video-platform/sdk/hikvision");
            var path = new Native.SdkPath { Path = new byte[256], Reserved = new byte[128] };
            var bytes = Encoding.UTF8.GetBytes(root);
            if (bytes.Length >= 256) throw new ArgumentException("SDK 路径过长。");
            bytes.CopyTo(path.Path, 0);
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Native.SdkPath>());
            try
            {
                Marshal.StructureToPtr(path, pointer, false);
                Check(Native.NET_DVR_SetSDKInitCfg(2, pointer), "设置 SDK 目录失败");
            }
            finally { Marshal.FreeHGlobal(pointer); }
            foreach (var (type, file) in new[] { (3, "libcrypto.so.3"), (4, "libssl.so.3") })
            {
                var dependency = Path.Combine(root, file);
                if (!File.Exists(dependency)) throw new InvalidOperationException($"缺少 SDK 依赖：{file}");
                pointer = Marshal.StringToCoTaskMemUTF8(dependency);
                try { Check(Native.NET_DVR_SetSDKInitCfg(type, pointer), "设置 SDK 依赖失败"); }
                finally { Marshal.FreeCoTaskMem(pointer); }
            }
            Check(Native.NET_DVR_Init(), "SDK 初始化失败");
            try
            {
                Check(Native.NET_DVR_SetDVRMessageCallBack_V31(_alarm, IntPtr.Zero), "注册报警回调失败");
                Check(Native.NET_DVR_SetExceptionCallBack_V30(0, IntPtr.Zero, _exception, IntPtr.Zero), "注册设备异常回调失败");
                Check(Native.NET_DVR_SetReconnect(5000, 1), "启用设备断线重连失败");
                _initialized = true;
            }
            catch { Native.NET_DVR_Cleanup(); throw; }
        }
    }

    public void Register(int userId, AlarmJournal journal) => _users[userId] = journal;
    public void Unregister(int userId) => _users.TryRemove(userId, out _);
    internal bool Route(int userId, AdapterAlarmEvent item) => _users.TryGetValue(userId, out var journal) && journal.TryEnqueue(item);

    private int OnAlarm(int command, IntPtr alarmer, IntPtr info, uint size, IntPtr user)
    {
        try
        {
            if (alarmer == IntPtr.Zero || Marshal.ReadByte(alarmer) == 0) return 0;
            var userId = Marshal.ReadInt32(alarmer, 8);
            if (!_users.TryGetValue(userId, out var journal)) return 0;
            if (size > 4 * 1024 * 1024) { journal.SignalFault("报警报文超过内存限制。"); return 0; }
            var payload = new byte[size];
            if (size > 0 && info != IntPtr.Zero) Marshal.Copy(info, payload, 0, payload.Length);
            AlarmEventDetails? details = null;
            try { details = AlarmParser.Parse(command, info, size); }
            catch { journal.SignalFault("报警指针数据解析失败，原始报文已保留。"); }
            // 回调返回前复制全部间接指针；回调内不做磁盘或网络操作。
            return journal.TryEnqueue(new(DateTimeOffset.UtcNow, command, payload, details)) ? 1 : 0;
        }
        catch { return 0; }
    }

    private void OnException(uint type, int userId, int handle, IntPtr user)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, type);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), userId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), handle);
        Route(userId, new(DateTimeOffset.UtcNow, 0x2001, payload, new("device.exception", type, [], null, null, null, null)));
    }

    internal static void Check(int result, string message)
    {
        if (result == 0) throw new AdapterException(502, "SDK_ERROR", $"{message}，SDK 错误码：{Native.NET_DVR_GetLastError()}。");
    }
    public void Dispose()
    {
        lock (_gate) { if (_initialized) Native.NET_DVR_Cleanup(); _initialized = false; }
        GC.KeepAlive(_alarm); GC.KeepAlive(_exception);
    }
}
