using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class AdapterReviewChecks
{
    public static async Task RunAsync()
    {
        NativeAbi();
        await PrefixMemoryAsync();
        await RefreshDoesNotStopMediaAsync();
        await MediaAuthorizationAsync();
        await ExportPersistenceAsync();
        await ExportCancellationAsync();
        var media = Environment.GetEnvironmentVariable("HIK_ADAPTER_REVIEW_MEDIA") == "1";
        if (media) await MediaTimingAsync();
        Console.WriteLine($"独立源码复核自检完成：{(media ? "7／7" : "6／6")} 项通过。");
    }
    private static void NativeAbi()
    {
        Expect(Marshal.SizeOf<HCNetSDKInterop.VodPara>() == 160, "Linux 回放结构大小");
        Expect(Marshal.OffsetOf<HCNetSDKInterop.VodPara>(nameof(HCNetSDKInterop.VodPara.FileIndex)).ToInt32() == 132, "回放文件索引偏移");
        Expect(Marshal.OffsetOf<HCNetSDKInterop.VodPara>(nameof(HCNetSDKInterop.VodPara.Async)).ToInt32() == 140, "回放异步标记偏移");
        Expect(Marshal.SizeOf<HCNetSDKInterop.UserLoginInfo>() == 416, "Linux 登录结构大小");
        Expect(Marshal.OffsetOf<HCNetSDKInterop.UserLoginInfo>(nameof(HCNetSDKInterop.UserLoginInfo.LoginCallback)).ToInt32() == 264, "登录回调指针偏移");
        Expect(Marshal.OffsetOf<HCNetSDKInterop.UserLoginInfo>(nameof(HCNetSDKInterop.UserLoginInfo.UseAsyncLogin)).ToInt32() == 280, "登录异步标记偏移");
        Expect(Marshal.SizeOf<HCNetSDKInterop.DownloadCondition>() == 116, "按时间下载结构大小");
        AlarmParser.SelfTest();
        Console.WriteLine("通过：厂商 Linux ABI 对应布局、报警指针复制。");
    }
    private static async Task PrefixMemoryAsync()
    {
        const int megabyte = 1024 * 1024;
        var queue = new BoundedMediaBuffer();
        var first = new byte[PlaybackPrefix.TargetBytes - 1];
        var largest = new byte[16 * megabyte];
        RandomNumberGenerator.Fill(first); RandomNumberGenerator.Fill(largest);
        Expect(queue.TryWrite(first), "探测前缀首块入队");
        var reading = PlaybackPrefix.ReadAsync(queue, default);
        Expect(queue.TryWrite(largest), "最大回调块入队");
        var prefix = await reading;
        Expect(queue.TryWrite(new byte[16 * megabyte]), "探测期间上游继续填满队列");
        var sample = prefix.Sample();
        Expect(sample.Length == PlaybackPrefix.SampleLimit, "探测样本独立字节上限");
        Expect(prefix.RetainedBytes + queue.Bytes + sample.Length < 40L * megabyte, "单路压缩数据暂存低于 40 MiB");
        using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        expected.AppendData(first); expected.AppendData(largest);
        using var output = new HashStream();
        await prefix.ReplayAsync(output, default);
        Expect(expected.GetHashAndReset().SequenceEqual(output.Finish()), "探测后原始压缩块完整原序重放");
        Expect(prefix.RetainedBytes == 0, "重放完成后释放前缀块引用");
        Console.WriteLine("通过：最大回调块下的 40 MiB 托管缓冲预算与无损重放。");
    }
    private static async Task RefreshDoesNotStopMediaAsync()
    {
        var path = TemporaryDirectory();
        try
        {
            using var runtime = new SdkRuntime(); using var zlm = new ZlmClient();
            await using var exports = new ExportService(Path.Combine(path, "exports"));
            await using var registry = new DeviceRegistry(runtime, zlm, new(), exports, Path.Combine(path, "alarms"), true);
            var registration = new DeviceRegistration("127.0.0.1", 8000, "review", "review");
            await registry.RegisterAsync(1, registration);
            await registry.SyncAsync(1);
            var device = registry.Find(1).Device;
            var liveId = Guid.NewGuid();
            await registry.UseAsync(1, e => e.Live!.StartAsync(new(liveId, 1, 1), default));
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => registry.RegisterAsync(1, registration)));
            Expect(ReferenceEquals(device, registry.Find(1).Device), "相同参数刷新复用设备实例");
            var snapshot = JsonSerializer.SerializeToElement(await registry.SessionsAsync());
            Expect(snapshot.GetProperty("live").GetArrayLength() == 1, "并发刷新保留播放会话");
            await registry.RegisterAsync(1, registration with { Password = "changed" });
            Expect(!ReferenceEquals(device, registry.Find(1).Device), "真实凭据变更重建设备");
            snapshot = JsonSerializer.SerializeToElement(await registry.SessionsAsync());
            Expect(snapshot.GetProperty("live").GetArrayLength() == 0, "凭据变更清理旧媒体");
        }
        finally { Directory.Delete(path, true); }
        Console.WriteLine("通过：并发注册刷新、凭据更新及媒体生命周期。");
    }
    private static async Task MediaAuthorizationAsync()
    {
        var oldKey = Environment.GetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY");
        var oldRtsp = Environment.GetEnvironmentVariable("HIK_ZLM_RTSP_URL");
        try
        {
            Environment.SetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY", "review +/&key");
            Environment.SetEnvironmentVariable("HIK_ZLM_RTSP_URL", "rtsp://127.0.0.1:18555");
            var url = new Uri(MediaTools.InternalRtsp("live", "vp2_review"));
            Expect(url.AbsolutePath == "/live/vp2_review", "内部播放 app 与流名");
            Expect(url.Query == "?token=review%20%2B%2F%26key", "内部令牌编码且参数名为 token");
            using var zlm = new ZlmClient(new MissingMediaHandler(), "http://127.0.0.1:18082", "test", TimeSpan.FromMilliseconds(30));
            try { await zlm.WaitReadyAsync("live", "missing", default); throw new InvalidOperationException("缺少预期媒体错误。"); }
            catch (AdapterException ex) { Expect(ex.Status == 502 && ex.Code == "MEDIA_NOT_READY", "子码流超时归类为允许回退的媒体错误"); }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await zlm.WaitReadyAsync("live", "missing", cancelled.Token); throw new InvalidOperationException("调用方取消未传播。"); }
            catch (OperationCanceledException) { }
        }
        finally
        {
            Environment.SetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY", oldKey);
            Environment.SetEnvironmentVariable("HIK_ZLM_RTSP_URL", oldRtsp);
        }
        Console.WriteLine("通过：内部播放令牌、子码流超时回退分类及调用取消。");
    }
    private static async Task ExportPersistenceAsync()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var exports = new ExportService(root);
            var device = new ReviewExportDevice();
            var end = DateTimeOffset.UtcNow;
            var request = new ExportRequest(Guid.NewGuid(), 1, end.AddMinutes(-1), end, root);
            var pending = Path.Combine(root, ".tasks", $"{request.JobId:N}.json.pending");
            Directory.CreateDirectory(pending);
            try { exports.Start(device, request); throw new InvalidOperationException("未触发首次写盘故障。"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Directory.Delete(pending);
            exports.Start(device, request);
            await device.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.CreateDirectory(pending);
            device.Finish.SetResult();
            await WaitAsync(() => exports.Get(device.Id, request.JobId).State == "failed");
            await exports.CancelDeviceAsync(device.Id);
            Directory.Delete(pending);
            Expect(exports.Get(device.Id, request.JobId).State == "failed", "终态写盘失败不遗留后台任务或占用并发槽");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("通过：首次持久化失败可重试、终态写盘失败仍完成资源回收。");
    }
    private static async Task ExportCancellationAsync()
    {
        var root = TemporaryDirectory();
        var devices = Enumerable.Range(1, 3).Select(id => new DelayedExportDevice(id)).ToArray();
        try
        {
            await using var exports = new ExportService(root, () => { });
            var end = DateTimeOffset.UtcNow;
            var requests = devices.Select(_ => new ExportRequest(Guid.NewGuid(), 1, end.AddMinutes(-1), end, root)).ToArray();
            try
            {
                exports.Start(devices[0], requests[0]);
                exports.Start(devices[1], requests[1]);
                await Task.WhenAll(devices.Take(2).Select(d => d.Started.Task)).WaitAsync(TimeSpan.FromSeconds(5));
                exports.Start(devices[2], requests[2]);
                var queued = requests[0] with { JobId = Guid.NewGuid() };
                exports.Start(devices[0], queued);
                Expect((await exports.CancelAsync(1, queued.JobId)).State == "cancelled", "排队任务取消无需占用下载句柄");
                var cancellation = exports.CancelAsync(1, requests[0].JobId);
                var duplicate = exports.CancelAsync(1, requests[0].JobId);
                await devices[0].CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Expect(!cancellation.IsCompleted && !duplicate.IsCompleted, "重复取消均等待实际下载退出");
                Expect(exports.Get(1, requests[0].JobId).State == "running", "取消确认前 GET 不发布终态");
                Expect(exports.Start(devices[0], requests[0]).State == "running", "取消未完成时相同任务不能重启");
                var snapshot = JsonSerializer.SerializeToElement(exports.Snapshot);
                Expect(snapshot.EnumerateArray().Single(j => j.GetProperty("jobId").GetGuid() == requests[0].JobId).GetProperty("state").GetString() == "running", "资源核对清单不提前发布终态");
                Expect(!devices[2].Started.Task.IsCompleted, "下载未退出时继续占用全局并发配额");
                Expect(File.Exists(devices[0].DownloadPath), "取消确认前保留仍在写入的临时文件");
                devices[0].ExitAllowed.TrySetResult();
                Expect((await cancellation.WaitAsync(TimeSpan.FromSeconds(5))).State == "cancelled", "下载结束后返回取消终态");
                Expect((await duplicate).State == "cancelled", "重复取消返回一致终态");
                Expect(!File.Exists(devices[0].DownloadPath), "取消返回前完成文件句柄关闭与临时清理");
                await devices[2].Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                foreach (var device in devices) device.ExitAllowed.TrySetResult();
                await Task.WhenAll(devices.Select(d => exports.CancelDeviceAsync(d.Id)));
            }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("通过：排队与重复取消、延迟退出确认、配额持有、重试隔离及取消前文件清理。");
    }
    private static async Task MediaTimingAsync()
    {
        var root = TemporaryDirectory();
        try
        {
            var input = Path.Combine(root, "source.mkv");
            await MediaTools.RunAsync(MediaTools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "5", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", input], default);
            var target = Path.Combine(root, "double.mkv");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var report = await MediaTools.RunAsync(MediaTools.Ffmpeg, PlaybackPublisher.Arguments(input, target, new("h264", "aac"), false, "native", 2, "matroska"), default);
            var probe = await MediaTools.RunAsync(MediaTools.Ffprobe, ["-v", "error", "-show_entries", "format=duration:stream=codec_name,codec_type", "-of", "json", target], default);
            using var result = JsonDocument.Parse(probe);
            var duration = double.Parse(result.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Expect(duration is > 2 and < 3, "两倍速输出真实媒体时长减半");
            Expect(clock.Elapsed.TotalSeconds >= 2, "回放发布按媒体时间进行节流");
            Expect(result.RootElement.GetProperty("streams").EnumerateArray().Any(s => s.GetProperty("codec_name").GetString() == "h264"), "倍速不重编码 H.264 视频");
            var last = TimeSpan.Zero;
            using var progress = new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(report)));
            await PlaybackPublisher.ReadProgressAsync(progress, value => last = value, default);
            Expect(last.TotalSeconds is > 2 and < 3, "进度来自发布进程真实媒体时间");
            Console.WriteLine($"通过：真实 FFmpeg 两倍速发布、音频时长、原视频编码与媒体进度，输出 {duration:F3} 秒。");
            var hevc = Path.Combine(root, "hevc.mkv");
            await MediaTools.RunAsync(MediaTools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-t", "2", "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "pools=1:frame-threads=1", hevc], default);
            foreach (var profile in new[] { "native", "browser" })
            {
                var output = Path.Combine(root, $"hevc-{profile}.mkv");
                await MediaTools.RunAsync(MediaTools.Ffmpeg, PlaybackPublisher.Arguments(hevc, output, new("hevc", null), profile == "browser", profile, 1, "matroska", paced: false), default);
                var outputCodecs = await MediaTools.ProbeAsync(output, default);
                Expect(outputCodecs.Video == (profile == "native" ? "hevc" : "h264"), $"H.265 {profile} 输出编码");
            }
            Console.WriteLine("通过：H.265 原生保留与浏览器 H.264 兼容转码。");
        }
        finally { Directory.Delete(root, true); }
    }
    private static string TemporaryDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "review-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static void Expect(bool value, string message) { if (!value) throw new InvalidOperationException($"复核断言失败：{message}。"); }
    private static async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
    private sealed class MissingMediaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":-1}") });
    }
    private sealed class ReviewExportDevice : IDevice
    {
        public long Id => 1;
        public DeviceOptions Options => new("127.0.0.1", 8000, "review", "review");
        public bool Simulated => true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DeviceSnapshot Sync() => throw new NotSupportedException();
        public IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end) => [new("review", start, end, 1, 0, 0, 0)];
        public async Task DownloadAsync(int channel, DateTimeOffset start, DateTimeOffset end, string path, Action<int> progress, CancellationToken token)
        {
            Started.TrySetResult(); await Finish.Task.WaitAsync(token); throw new InvalidOperationException("复核用下载失败。");
        }
        public IPlaybackSource OpenPlayback(int channel, DateTimeOffset start, DateTimeOffset end, uint fileIndex, Action<uint, IntPtr, uint> receive) => throw new NotSupportedException();
        public void Ptz(PtzRequest request) => throw new NotSupportedException();
        public void Preset(PresetRequest request) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private sealed class DelayedExportDevice(long id) : IDevice
    {
        public long Id => id;
        public DeviceOptions Options => new("127.0.0.1", 8000, "review", "review");
        public bool Simulated => true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExitAllowed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? DownloadPath { get; private set; }
        public DeviceSnapshot Sync() => throw new NotSupportedException();
        public IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end) => [new("review", start, end, 1, 0, 0, 0)];
        public async Task DownloadAsync(int channel, DateTimeOffset start, DateTimeOffset end, string path, Action<int> progress, CancellationToken token)
        {
            DownloadPath = path;
            await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally
            {
                CancellationObserved.TrySetResult();
                // 模拟原生下载响应取消后仍需等待回调及最后一次写入完成。
                await ExitAllowed.Task;
                await file.WriteAsync(new byte[] { 1 }, CancellationToken.None);
            }
        }
        public IPlaybackSource OpenPlayback(int channel, DateTimeOffset start, DateTimeOffset end, uint fileIndex, Action<uint, IntPtr, uint> receive) => throw new NotSupportedException();
        public void Ptz(PtzRequest request) => throw new NotSupportedException();
        public void Preset(PresetRequest request) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private sealed class HashStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public byte[] Finish() => _hash.GetHashAndReset();
        public override void Write(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { _hash.AppendData(buffer.Span); return ValueTask.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
