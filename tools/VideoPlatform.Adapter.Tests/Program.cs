using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

var checks = new List<(string Name, Func<Task> Run)>
{
    ("已验证 SDK 结构布局和报警间接指针复制", () =>
    {
        AlarmParser.SelfTest();
        Check.Equal(72, Marshal.SizeOf<HCNetSDKInterop.StreamInfo>());
        Check.Equal(160, Marshal.SizeOf<HCNetSDKInterop.VodPara>());
        Check.Equal(132, Marshal.OffsetOf<HCNetSDKInterop.VodPara>(nameof(HCNetSDKInterop.VodPara.FileIndex)).ToInt32());
        Check.Equal(140, Marshal.OffsetOf<HCNetSDKInterop.VodPara>(nameof(HCNetSDKInterop.VodPara.Async)).ToInt32());
        Check.Equal(416, Marshal.SizeOf<HCNetSDKInterop.UserLoginInfo>());
        Check.Equal(116, Marshal.SizeOf<HCNetSDKInterop.DownloadCondition>());
        return Task.CompletedTask;
    }),
    ("录像片段裁剪、重叠与缺口", () => { PlaybackTimeline.SelfTest(); return Task.CompletedTask; }),
    ("媒体错误输出有界且隐藏设备密码和令牌", async () =>
    {
        using var input = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 65536))));
        Check.Equal(16384, (await MediaTools.CaptureAsync(input, 16384, default)).Length);
        Check.Equal(0, (await input.ReadToEndAsync()).Length);
        var detail = MediaTools.RedactDiagnostic("rtsp://user:device-password@host/live?token=play-secret https://host/live?key=internal-secret token=another-secret; code=234");
        Check.True(!detail.Contains("device-password") && !detail.Contains("play-secret") && !detail.Contains("internal-secret") && !detail.Contains("another-secret"));
        Check.True(detail.Contains("code=234"));
    }),
    ("海康私有头剥离后保留标准 MPEG-PS", async () =>
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "download");
        await File.WriteAllBytesAsync(path, [.. System.Text.Encoding.ASCII.GetBytes("IMKH\x02\x01"), 0, 0, 1, 0xba, 1, 2, 3]);
        var normalized = await MediaTools.NormalizeDownloadAsync(path, default);
        Check.True(normalized != path);
        Check.True((await File.ReadAllBytesAsync(normalized)).SequenceEqual(new byte[] { 0, 0, 1, 0xba, 1, 2, 3 }));
        File.Delete(normalized);
        var standard = Path.Combine(directory.Path, "standard");
        await File.WriteAllBytesAsync(standard, [0, 0, 1, 0xba, 1, 2, 3]);
        Check.Equal(standard, await MediaTools.NormalizeDownloadAsync(standard, default));
        await File.WriteAllBytesAsync(standard, [.. "other-container"u8.ToArray(), 0, 0, 1, 0xba, 1, 2, 3]);
        Check.Equal(standard, await MediaTools.NormalizeDownloadAsync(standard, default));
        await File.WriteAllBytesAsync(standard, "IMKH-invalid"u8.ToArray());
        await Check.ThrowsAsync<AdapterException>(() => MediaTools.NormalizeDownloadAsync(standard, default));
    }),
    ("有界缓冲按字节限制失败且不继续消费", BufferBytes),
    ("有界缓冲按条目限制失败和并发写入边界", BufferConcurrency),
    ("报警按用户 ID 分流、持久化确认与重启恢复", AlarmPersistence),
    ("报警损坏行隔离后继续读取", AlarmCorruption),
    ("报警缓冲溢出进入健康故障", AlarmOverflow),
    ("两台设备同号通道共享隔离和幂等停止", MultipleDevices),
    ("真实控制调用顺序、SDK 时间和溢出会话失败", PlaybackBehavior),
    ("模拟云台保护时限", PtzWatchdog),
    ("导出路径穿越拒绝、取消与重启恢复", ExportBoundaries),
    ("下载期间磁盘不足主动终止任务", ExportDiskFailure),
    ("HTTP 内部密钥与全部契约端点", HttpContract)
};
if (args.Contains("--media")) checks.Add(("真实 FFmpeg 视频原编码封装与分段 ZIP 导出", ExportMedia));
var failures = 0;
foreach (var test in checks)
{
    try { await test.Run(); Console.WriteLine($"通过：{test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"失败：{test.Name}\n{ex}"); }
}
Console.WriteLine($"验证完成：{checks.Count - failures}/{checks.Count} 项通过。");
Environment.ExitCode = failures == 0 ? 0 : 1;

static async Task BufferBytes()
{
    var buffer = new BoundedMediaBuffer(8, 4);
    Check.True(buffer.TryWrite([1, 2, 3, 4]));
    Check.Equal(4L, buffer.Bytes);
    Check.Equal(4, (await buffer.ReadAsync(CancellationToken.None)).Length);
    Check.Equal(0L, buffer.Bytes);
    Check.True(!buffer.TryWrite(new byte[9]));
    Check.True(buffer.Failed);
    await Check.ThrowsAsync<IOException>(async () => await buffer.ReadAsync(CancellationToken.None));
}
static async Task BufferConcurrency()
{
    var watermark = new BoundedMediaBuffer(1024 * 1024, 16);
    for (var i = 0; i < 4; i++) Check.True(watermark.TryWrite([1]));
    Check.True(watermark.HighWatermark);
    for (var i = 0; i < 3; i++) await watermark.ReadAsync(default);
    Check.True(watermark.LowWatermark);
    var count = new BoundedMediaBuffer(1024, 1);
    Check.True(count.TryWrite([1])); Check.True(!count.TryWrite([2])); Check.True(count.Failed);
    var concurrent = new BoundedMediaBuffer(8192, 8);
    await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => concurrent.TryWrite(new byte[1024]))));
    Check.True(concurrent.Failed); Check.True(concurrent.Bytes <= 8192);
    var pointer = Marshal.AllocHGlobal(16);
    try { var native = new BoundedMediaBuffer(8, 1); Check.True(!native.TryCopy(pointer, 16)); Check.Equal(0L, native.Bytes); }
    finally { Marshal.FreeHGlobal(pointer); }
}
static async Task AlarmPersistence()
{
    using var directory = new TestDirectory();
    string cursor;
    await using (var first = new AlarmJournal(11, directory.Path, segmentBytes: 100))
    await using (var second = new AlarmJournal(22, directory.Path))
    using (var runtime = new SdkRuntime())
    {
        runtime.Register(7, first); runtime.Register(9, second);
        Check.True(runtime.Route(7, Alarm(1))); Check.True(runtime.Route(9, Alarm(1)));
        Check.True(!runtime.Route(8, Alarm(1)));
        await Check.Eventually(() => first.Read(null, 100).Items.Count == 1 && second.Read(null, 100).Items.Count == 1);
        var page = first.Read(null, 100); Check.Equal(11L, page.Items[0].DeviceId); Check.Equal(22L, second.Read(null, 100).Items[0].DeviceId);
        cursor = page.NextCursor; first.Ack(cursor);
        runtime.Unregister(7); Check.True(!runtime.Route(7, Alarm(1)));
    }
    await using (var resumed = new AlarmJournal(11, directory.Path))
    {
        Check.Equal(0, resumed.Read(null, 100).Items.Count);
        resumed.TryEnqueue(Alarm(2));
        await Check.Eventually(() => resumed.Read(null, 100).Items.Count == 1);
        var next = resumed.Read(null, 100); Check.Equal(2, next.Items[0].Channel!.Value);
        Check.True(next.NextCursor != cursor); resumed.Ack(next.NextCursor);
        Check.Throws<ArgumentException>(() => resumed.Ack(cursor));
        Check.Throws<ArgumentException>(() => resumed.Ack("999999999999999999:100"));
    }
}
static async Task AlarmCorruption()
{
    using var directory = new TestDirectory();
    await using (var journal = new AlarmJournal(1, directory.Path))
    {
        journal.TryEnqueue(Alarm(1)); await Check.Eventually(() => journal.Read(null, 100).Items.Count == 1);
    }
    var file = Directory.GetFiles(System.IO.Path.Combine(directory.Path, "1"), "*.ndjson").Single();
    var valid = await File.ReadAllTextAsync(file);
    await File.AppendAllTextAsync(file, "损坏记录\n" + valid);
    await using var restored = new AlarmJournal(1, directory.Path);
    Check.Equal(2, restored.Read(null, 100).Items.Count(i => i.EventType == "alarm.motion"));
    Check.Equal(1, Directory.GetFiles(System.IO.Path.Combine(directory.Path, "1"), "damaged-*.bin").Length);
}
static async Task AlarmOverflow()
{
    using var directory = new TestDirectory();
    await using var journal = new AlarmJournal(1, directory.Path, maxBytes: 2);
    Check.True(!journal.TryEnqueue(Alarm(1)));
    await Check.Eventually(() => journal.Read(null, 100).Items.Any(i => i.EventType == "system.adapter_alarm"));
    Check.True(JsonSerializer.Serialize(journal.Health).Contains("dropped"));
}
static async Task MultipleDevices()
{
    await using var fixture = new RegistryFixture();
    await fixture.Registry.RegisterAsync(1, Registration("127.0.0.1"));
    await fixture.Registry.RegisterAsync(2, Registration("127.0.0.2"));
    Check.Equal(81, (await fixture.Registry.SyncAsync(1)).Channels.Count);
    Check.Equal(81, (await fixture.Registry.SyncAsync(2)).Channels.Count);
    var first = new LiveStartRequest(Guid.NewGuid(), 1, 1, "native");
    var same = first with { SessionId = Guid.NewGuid() };
    var a = await fixture.Registry.UseAsync(1, entry => entry.Live!.StartAsync(first, default));
    var b = await fixture.Registry.UseAsync(2, entry => entry.Live!.StartAsync(first, default));
    Check.True(a.Stream != b.Stream);
    var shared = await fixture.Registry.UseAsync(1, entry => entry.Live!.StartAsync(same, default));
    Check.Equal(a.Stream, shared.Stream);
    Check.Equal(a, await fixture.Registry.UseAsync(1, entry => entry.Live!.StartAsync(first, default)));
    await Check.ThrowsAsync<AdapterException>(() => fixture.Registry.UseAsync(1, entry => entry.Live!.StartAsync(first with { Channel = 2 }, default)));
    await fixture.Registry.UseAsync(1, async entry => { await entry.Live!.StopAsync(first.SessionId); await entry.Live.StopAsync(first.SessionId); return true; });
    var state = JsonSerializer.SerializeToElement(await fixture.Registry.SessionsAsync()); Check.Equal(2, state.GetProperty("live").GetArrayLength());
    var browser = await fixture.Registry.UseAsync(1, entry => entry.Live!.StartAsync(new(Guid.NewGuid(), 1, 1, "browser"), default));
    Check.True(browser.Transcoded); Check.Equal("H264", browser.Codec);
    Check.Equal(1, fixture.Budget.Count);
    await fixture.Registry.RegisterAsync(1, Registration("127.0.0.1") with { Enabled = false });
    Check.Equal(0, fixture.Budget.Count);
    await Check.ThrowsAsync<AdapterException>(() => fixture.Registry.SyncAsync(1));
    var remains = JsonSerializer.SerializeToElement(await fixture.Registry.SessionsAsync()); Check.Equal(1, remains.GetProperty("live").GetArrayLength());
    Check.Equal(2L, remains.GetProperty("live")[0].GetProperty("deviceId").GetInt64());
}
static async Task PlaybackBehavior()
{
    using var zlm = new ZlmClient();
    var device = new ControlledDevice();
    var start = DateTimeOffset.Parse("2026-09-07T00:00:00+08:00");
    var request = new PlaybackStartRequest(Guid.NewGuid(), 7, 1, start, start.AddMinutes(3));
    var segments = PlaybackTimeline.Normalize(device.SearchRecordings(1, request.Start, request.End), request.Start, request.End);
    await using var session = new PlaybackSession(device, request, segments, zlm, new());
    await session.StartAsync(); Check.Equal(request.SessionId, session.Summary().Id);
    device.Source!.Position = start.AddSeconds(7);
    await Check.Eventually(() => session.Summary().CurrentTime == start.AddSeconds(7));
    await session.ControlAsync(new("pause")); Check.True(device.Source.Paused); Check.Equal("paused", session.Summary().State);
    await session.ControlAsync(new("resume")); Check.True(!device.Source.Paused);
    await session.ControlAsync(new("speed", Speed: 2)); Check.Equal(2d, device.Source.Speed); Check.Equal(2d, session.Summary().Speed);
    await Check.ThrowsAsync<ArgumentException>(() => session.ControlAsync(new("speed", Speed: 3)));
    await Check.ThrowsAsync<ArgumentException>(() => session.ControlAsync(new("seek", request.Start.AddSeconds(-1))));
    Check.True(session.Summary().State != "failed");
    await session.ControlAsync(new("seek", start.AddSeconds(90))); Check.Equal("gap", session.Summary().State);
    await session.ControlAsync(new("resume")); Check.Equal(start.AddMinutes(2), device.OpenedAt);
    var pointer = Marshal.AllocHGlobal(1);
    try { device.Receive!(2, pointer, 17 * 1024 * 1024); }
    finally { Marshal.FreeHGlobal(pointer); }
    await Check.Eventually(() => session.Summary().State == "failed");
    Check.True(session.Summary().Error!.Contains("溢出"));
}
static async Task PtzWatchdog()
{
    using var directory = new TestDirectory();
    var clock = new TestClock();
    var journal = new AlarmJournal(1, directory.Path);
    using var device = new SimulatedDevice(1, new("127.0.0.1", 8000, "test", "test"), journal, clock);
    device.Ptz(new(1, "up")); Check.True(device.IsMoving(1));
    clock.Advance(TimeSpan.FromSeconds(9)); Check.True(device.IsMoving(1));
    device.Ptz(new(1, "up")); clock.Advance(TimeSpan.FromSeconds(9)); Check.True(device.IsMoving(1));
    clock.Advance(TimeSpan.FromSeconds(2)); Check.True(!device.IsMoving(1));
    await journal.DisposeAsync();
}
static async Task ExportBoundaries()
{
    using var directory = new TestDirectory();
    var exportRoot = System.IO.Path.Combine(directory.Path, "exports");
    var device = new ControlledDevice { BlockDownload = true };
    var now = DateTimeOffset.UtcNow;
    var request = new ExportRequest(Guid.NewGuid(), 1, now.AddHours(-1), now, exportRoot);
    await using (var exports = new ExportService(exportRoot))
    {
        Check.Throws<ArgumentException>(() => exports.EnsureDirectory(System.IO.Path.Combine(exportRoot, "..", "escape")));
        Check.Throws<ArgumentException>(() => exports.EnsureDirectory(exportRoot + "-other"));
        var status = exports.Start(device, request); Check.True(status.State is "queued" or "running");
        await Check.Eventually(() => device.DownloadStarted);
        Check.Equal("cancelled", exports.Cancel(1, request.JobId).State);
        await exports.CancelDeviceAsync(1);
    }
    await using (var restored = new ExportService(exportRoot)) Check.Equal("cancelled", restored.Get(1, request.JobId).State);
}
static async Task HttpContract()
{
    await using var server = await TestServer.StartAsync();
    using var unauthorized = new HttpClient { BaseAddress = server.Client.BaseAddress };
    Check.Equal(HttpStatusCode.Unauthorized, (await unauthorized.GetAsync("/health")).StatusCode);
    Check.Equal("2.0.0", (await server.Get("/health")).GetProperty("version").GetString()!);
    Check.Equal(0, (await server.Get("/internal/sessions")).GetProperty("devices").GetArrayLength());
    foreach (var device in new[] { 1, 2 })
    {
        await server.Send(HttpMethod.Put, $"/internal/devices/{device}", Registration($"127.0.0.{device}"));
        Check.Equal(81, (await server.Send(HttpMethod.Post, $"/internal/devices/{device}/sync", new { })).GetProperty("channels").GetArrayLength());
    }
    var id = Guid.NewGuid();
    var live = await server.Send(HttpMethod.Post, "/internal/devices/1/live", new { sessionId = id, channel = 1, streamType = 1, profile = "browser" });
    Check.True(live.GetProperty("transcoded").GetBoolean());
    Check.True(!live.GetRawText().Contains("adapter-test-key"));
    var second = await server.Send(HttpMethod.Post, "/internal/devices/2/live", new { sessionId = id, channel = 1, streamType = 1, profile = "native" });
    Check.True(live.GetProperty("stream").GetString() != second.GetProperty("stream").GetString());
    var start = DateTimeOffset.UtcNow.AddHours(-1); var end = start.AddMinutes(3);
    Check.Equal(2, (await server.Send(HttpMethod.Post, "/internal/devices/1/recordings/search", new { channel = 1, start, end })).GetArrayLength());
    var playId = Guid.NewGuid();
    var playback = await server.Send(HttpMethod.Post, "/internal/devices/1/playback", new { sessionId = playId, userId = 1, channel = 1, start, end, profile = "native" });
    Check.Equal(playId, playback.GetProperty("id").GetGuid());
    await server.Get($"/internal/devices/1/playback/{playId}");
    await server.Send(HttpMethod.Post, $"/internal/devices/1/playback/{playId}/control", new { action = "pause" });
    Check.Equal("paused", (await server.Get($"/internal/devices/1/playback/{playId}")).GetProperty("state").GetString()!);
    await server.Send(HttpMethod.Post, "/internal/devices/1/ptz", new { channel = 1, command = "up", speed = 4, stop = false });
    await server.Send(HttpMethod.Post, "/internal/devices/1/ptz", new { channel = 1, command = "up", speed = 4, stop = true });
    await server.Send(HttpMethod.Post, "/internal/devices/1/ptz/preset", new { channel = 1, preset = 1 });
    await Task.Delay(200);
    var events = await server.Get("/internal/devices/1/events?limit=100"); Check.True(events.GetProperty("items").GetArrayLength() > 0);
    await server.Send(HttpMethod.Post, "/internal/devices/1/events/ack", new { cursor = events.GetProperty("nextCursor").GetString() });
    var exportId = Guid.NewGuid();
    await server.Send(HttpMethod.Post, "/internal/devices/1/exports", new { jobId = exportId, channel = 81, start, end, outputDirectory = server.ExportRoot });
    await server.Get($"/internal/devices/1/exports/{exportId}");
    await server.Send(HttpMethod.Delete, $"/internal/devices/1/exports/{exportId}", null);
    await server.Send(HttpMethod.Delete, $"/internal/devices/1/playback/{playId}", null);
    await server.Send(HttpMethod.Delete, $"/internal/devices/1/playback/{playId}", null);
    await server.Send(HttpMethod.Delete, $"/internal/devices/1/live/{id}", null);
    await server.Send(HttpMethod.Delete, "/internal/devices/1", null);
    await server.Send(HttpMethod.Delete, "/internal/devices/1", null);
    Check.Equal(1, (await server.Get("/internal/sessions")).GetProperty("live").GetArrayLength());
}
static async Task ExportDiskFailure()
{
    using var directory = new TestDirectory();
    var device = new ControlledDevice { BlockDownload = true };
    var root = System.IO.Path.Combine(directory.Path, "exports");
    var checks = 0;
    await using var exports = new ExportService(root, () =>
    {
        if (Interlocked.Increment(ref checks) >= 3) throw new AdapterException(507, "EXPORT_DISK_LOW", "测试磁盘剩余不足 10%。");
    });
    var now = DateTimeOffset.UtcNow;
    var request = new ExportRequest(Guid.NewGuid(), 1, now.AddMinutes(-3), now, root);
    exports.Start(device, request);
    await Check.Eventually(() => exports.Get(1, request.JobId).State == "failed");
    Check.True(device.DownloadStarted);
    Check.True(exports.Get(1, request.JobId).Error!.Contains("10%"));
}
static async Task ExportMedia()
{
    using var directory = new TestDirectory();
    await using var journal = new AlarmJournal(1, directory.Path);
    using var device = new SimulatedDevice(1, new("127.0.0.1", 8000, "test", "test"), journal);
    var exportRoot = System.IO.Path.Combine(directory.Path, "exports");
    await using var exports = new ExportService(exportRoot);
    var now = DateTimeOffset.UtcNow;
    var request = new ExportRequest(Guid.NewGuid(), 1, now.AddMinutes(-3), now, exportRoot);
    exports.Start(device, request);
    await Check.Eventually(() => exports.Get(1, request.JobId).State is "completed" or "failed", 60000);
    var result = exports.Get(1, request.JobId);
    Check.Equal("completed", result.State, result.Error);
    Check.True(result.Path!.EndsWith(".zip"));
    using var archive = System.IO.Compression.ZipFile.OpenRead(result.Path);
    Check.Equal(2, archive.Entries.Count(e => e.Name.EndsWith(".mp4")));
    Check.True(archive.Entries.Any(e => e.Name == "时间清单.json"));
    Check.Equal(0, Directory.GetFiles(exportRoot, "*.download", SearchOption.AllDirectories).Length);
}
static AdapterAlarmEvent Alarm(int channel) => new(DateTimeOffset.UtcNow, 0x1100, [1, 2, 3], new("alarm.motion", 3, [channel], false, null, null, null));
static DeviceRegistration Registration(string host) => new(host, 8000, "test", "test");

internal static class Check
{
    public static void True(bool value) { if (!value) throw new InvalidOperationException("断言结果为假。"); }
    public static void Equal<T>(T expected, T actual, string? detail = null) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"预期：{expected}，实际：{actual}。{detail}"); }
    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; } throw new InvalidOperationException($"预期异常：{typeof(T).Name}");
    }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; } throw new InvalidOperationException($"预期异常：{typeof(T).Name}");
    }
    public static async Task Eventually(Func<bool> predicate, int milliseconds = 5000)
    {
        var timeout = DateTimeOffset.UtcNow.AddMilliseconds(milliseconds);
        while (!predicate()) { if (DateTimeOffset.UtcNow > timeout) throw new TimeoutException("等待测试条件超时。"); await Task.Delay(50); }
    }
}
internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan time) => _now += time;
}
internal sealed class RegistryFixture : IAsyncDisposable
{
    private readonly TestDirectory _directory = new();
    private readonly SdkRuntime _runtime = new();
    private readonly ZlmClient _zlm = new();
    private readonly ExportService _exports;
    public TranscodeBudget Budget { get; } = new();
    public DeviceRegistry Registry { get; }
    public RegistryFixture()
    {
        _exports = new(System.IO.Path.Combine(_directory.Path, "exports"));
        Registry = new(_runtime, _zlm, Budget, _exports, System.IO.Path.Combine(_directory.Path, "alarms"), true);
    }
    public async ValueTask DisposeAsync() { await Registry.DisposeAsync(); await _exports.DisposeAsync(); _runtime.Dispose(); _zlm.Dispose(); _directory.Dispose(); }
}
internal sealed class ControlledDevice : IDevice
{
    public long Id => 1;
    public DeviceOptions Options => new("127.0.0.1", 8000, "test", "test");
    public bool Simulated => true;
    public ControlledSource? Source;
    public Action<uint, IntPtr, uint>? Receive;
    public DateTimeOffset OpenedAt;
    public bool BlockDownload;
    public volatile bool DownloadStarted;
    public DeviceSnapshot Sync() => throw new NotSupportedException();
    public IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end) =>
        [new("first", start, start + (end - start) / 3, 1, 0, 0, 1), new("last", start + (end - start) * 2 / 3, end, 1, 0, 0, 2)];
    public IPlaybackSource OpenPlayback(int channel, DateTimeOffset start, DateTimeOffset end, uint fileIndex, Action<uint, IntPtr, uint> receive)
    {
        Receive = receive; OpenedAt = start; return Source = new() { Position = start };
    }
    public async Task DownloadAsync(int channel, DateTimeOffset start, DateTimeOffset end, string path, Action<int> progress, CancellationToken token)
    {
        DownloadStarted = true; if (BlockDownload) await Task.Delay(Timeout.Infinite, token); else throw new InvalidOperationException("模拟下载失败。");
    }
    public void Ptz(PtzRequest request) { }
    public void Preset(PresetRequest request) { }
    public void Dispose() { }
}
internal sealed class ControlledSource : IPlaybackSource
{
    public DateTimeOffset Position;
    public bool Paused;
    public double Speed = 1;
    public DateTimeOffset? CurrentTime => Position;
    public bool Completed => false;
    public void Start() { }
    public void Pause(bool pause) => Paused = pause;
    public void SetSpeed(double speed) => Speed = speed;
    public void Dispose() { }
}
internal sealed class TestServer : IAsyncDisposable
{
    private readonly TestDirectory _directory = new();
    private readonly System.Diagnostics.Process _process;
    private readonly Task _drains;
    public HttpClient Client { get; }
    public string ExportRoot => System.IO.Path.Combine(_directory.Path, "exports");
    private TestServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var dotnet = Environment.ProcessPath!;
        if (!System.IO.Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe";
        var info = new System.Diagnostics.ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(typeof(DeviceRegistry).Assembly.Location);
        info.Environment["HIK_ADAPTER_INTERNAL_KEY"] = "adapter-test-key";
        info.Environment["HIK_ADAPTER_SIMULATOR"] = "1";
        info.Environment["HIK_ADAPTER_API_URL"] = $"http://127.0.0.1:{port}";
        info.Environment["HIK_ADAPTER_DATA_ROOT"] = _directory.Path;
        info.Environment["HIK_EXPORT_ROOT"] = ExportRoot;
        foreach (var name in new[] { "HIK_DEVICE_IP", "HIK_DEVICE_USER", "HIK_DEVICE_PASSWORD", "HIK_ALARM_SELF_TEST", "HIK_PLAYBACK_TIMELINE_SELF_TEST" }) info.Environment.Remove(name);
        _process = System.Diagnostics.Process.Start(info)!;
        _drains = Task.WhenAll(MediaTools.DrainAsync(_process.StandardOutput, default), MediaTools.DrainAsync(_process.StandardError, default));
        Client = new() { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(20) };
        Client.DefaultRequestHeaders.Add("X-Adapter-Key", "adapter-test-key");
    }
    public static async Task<TestServer> StartAsync()
    {
        var server = new TestServer();
        try
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (server._process.HasExited) throw new InvalidOperationException("测试适配器启动失败。");
                try { await server.Get("/health"); return server; } catch (HttpRequestException) { await Task.Delay(100); }
            }
            throw new TimeoutException("测试适配器启动超时。");
        }
        catch { await server.DisposeAsync(); throw; }
    }
    public async Task<JsonElement> Get(string path)
    {
        using var response = await Client.GetAsync(path); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public async Task<JsonElement> Send(HttpMethod method, string path, object? payload)
    {
        using var request = new HttpRequestMessage(method, path); if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"内部接口 {method} {path} 返回 {(int)response.StatusCode}：{await response.Content.ReadAsStringAsync()}");
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public async ValueTask DisposeAsync()
    {
        MediaTools.Kill(_process); await _process.WaitForExitAsync(); await _drains; _process.Dispose(); Client.Dispose(); _directory.Dispose();
    }
}
