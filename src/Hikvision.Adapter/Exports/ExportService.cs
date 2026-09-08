using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

internal sealed class ExportService : IAsyncDisposable
{
    private sealed record Saved(Guid JobId, long DeviceId, ExportRequest Request, ExportSummary Status);
    private sealed class Job(long deviceId, ExportRequest request, ExportSummary status)
    {
        public long DeviceId { get; } = deviceId;
        public ExportRequest Request { get; } = request;
        public ExportSummary Status = status;
        public ExportSummary ActiveStatus = status;
        public CancellationTokenSource Stop = new();
        public Task Work = Task.CompletedTask;
    }
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _devices = new();
    private readonly SemaphoreSlim _global = new(2);
    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _states;
    private readonly Action? _capacityCheck;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public ExportService(string root, Action? capacityCheck = null)
    {
        _capacityCheck = capacityCheck;
        _root = Path.GetFullPath(root); _states = Path.Combine(_root, ".tasks");
        Directory.CreateDirectory(_states);
        foreach (var file in Directory.EnumerateFiles(_states, "*.json"))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file), Json);
                if (saved is null) continue;
                var status = saved.Status.State is "queued" or "running" ? new ExportSummary("failed", saved.Status.Progress, Error: "适配器已重启，未完成任务需要重试。") : saved.Status;
                var job = new Job(saved.DeviceId, saved.Request, status);
                _jobs[saved.JobId] = job; Persist(job);
            }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException) { Console.Error.WriteLine("导出任务状态文件损坏，已跳过，需由平台核对。"); }
        }
    }
    public object[] Snapshot
    {
        get
        {
            lock (_gate)
                return _jobs.Select(p =>
                {
                    var status = ReadStatus(p.Value);
                    return (object)new { id = p.Key, jobId = p.Key, deviceId = p.Value.DeviceId, state = status.State, progress = status.Progress };
                }).ToArray();
        }
    }
    public ExportSummary Start(IDevice device, ExportRequest request)
    {
        Validate.Range(request.Channel, request.Start, request.End);
        if (request.JobId == Guid.Empty) throw new ArgumentException("必须提供平台导出任务标识。");
        EnsureDirectory(request.OutputDirectory);
        lock (_gate)
        {
            if (_jobs.TryGetValue(request.JobId, out var existing))
            {
                if (existing.DeviceId != device.Id || existing.Request != request) throw new AdapterException(409, "EXPORT_CONFLICT", "任务标识已绑定其他导出参数。");
                if (!existing.Work.IsCompleted || ReadStatus(existing).State is "completed" or "running" or "queued") return ReadStatus(existing);
                existing.Stop.Dispose();
            }
            if (_jobs.Values.Count(j => !j.Work.IsCompleted) >= 1000) throw new AdapterException(429, "EXPORT_QUEUE_FULL", "导出等待队列已满。");
            CheckDisk();
            var job = new Job(device.Id, request, new("queued", 0));
            Persist(job); _jobs[request.JobId] = job;
            job.Work = Task.Run(() => RunAsync(device, job));
            return ReadStatus(job);
        }
    }
    public ExportSummary Get(long deviceId, Guid id)
    {
        lock (_gate) return ReadStatus(Find(deviceId, id));
    }
    public ExportSummary Cancel(long deviceId, Guid id) => CancelAsync(deviceId, id).GetAwaiter().GetResult();
    public async Task<ExportSummary> CancelAsync(long deviceId, Guid id)
    {
        Job job;
        Task cancellation;
        Task work;
        lock (_gate)
        {
            job = Find(deviceId, id);
            lock (job)
            {
                cancellation = job.Status.State is "completed" or "failed" or "cancelled" ? Task.CompletedTask : job.Stop.CancelAsync();
                work = job.Work;
            }
        }
        // 即使请求连接断开，也要等待下载、媒体进程和临时文件清理全部退出。
        await cancellation;
        await work;
        return ReadStatus(job);
    }
    private static ExportSummary ReadStatus(Job job)
    {
        lock (job)
        {
            // 终态不能早于 finally 资源回收；平台据此释放配额和允许重试。
            return !job.Work.IsCompleted && job.Status.State is "completed" or "failed" or "cancelled"
                ? job.ActiveStatus : job.Status;
        }
    }
    private Job Find(long deviceId, Guid id)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.DeviceId != deviceId) throw new AdapterException(404, "EXPORT_NOT_FOUND", "导出任务不存在。");
        return job;
    }
    private async Task RunAsync(IDevice device, Job job)
    {
        var token = job.Stop.Token;
        var local = _devices.GetOrAdd(device.Id, _ => new SemaphoreSlim(1));
        var globalAcquired = false; var localAcquired = false;
        string? workDirectory = null;
        try
        {
            await local.WaitAsync(token); localAcquired = true;
            await _global.WaitAsync(token); globalAcquired = true;
            Update(job, new("running", 0));
            var recordings = device.SearchRecordings(job.Request.Channel, job.Request.Start, job.Request.End);
            var segments = PlaybackTimeline.Normalize(recordings, job.Request.Start, job.Request.End);
            if (segments.Count == 0) throw new InvalidOperationException("指定时间范围没有录像。");
            workDirectory = Path.Combine(EnsureDirectory(job.Request.OutputDirectory), $"d{device.Id}-{job.Request.JobId:N}");
            EnsureDirectory(workDirectory);
            Directory.CreateDirectory(workDirectory);
            var manifest = new List<object>();
            var completed = new List<string>();
            for (var i = 0; i < segments.Count; i++)
            {
                token.ThrowIfCancellationRequested(); CheckDisk();
                var segment = segments[i];
                var ps = Path.Combine(workDirectory, $"{i + 1:D4}.download");
                var mp4 = Path.Combine(workDirectory, $"{i + 1:D4}.mp4");
                var part = i;
                await WithCapacityAsync(cancellation => device.DownloadAsync(job.Request.Channel, segment.Start, segment.End, ps,
                    percent => Update(job, new("running", (part * 90 + percent * 80 / 100) / segments.Count)), cancellation), token);
                var normalized = ps;
                await WithCapacityAsync(async cancellation => normalized = await MediaTools.NormalizeDownloadAsync(ps, cancellation), token);
                MediaCodecs actual;
                try
                {
                    var codecs = await MediaTools.ProbeAsync(normalized, token);
                    // 海康文件可能从不完整的 PS 数据开始，以首个完整包头作为封装入口。
                    await WithCapacityAsync(cancellation => MediaTools.RunAsync(MediaTools.Ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", "-i", normalized, "-map", "0:v:0", "-map", "0:a:0?", "-c:v", "copy", "-c:a", codecs.Mp4AudioCopy ? "copy" : "aac", "-movflags", "+faststart", mp4], cancellation), token);
                    actual = await MediaTools.ProbeAsync(mp4, token);
                    if (actual.Video != codecs.Video) throw new InvalidOperationException("导出视频编码校验失败。");
                }
                finally
                {
                    if (!string.Equals(normalized, ps, StringComparison.Ordinal)) File.Delete(normalized);
                }
                File.Delete(ps);
                completed.Add(mp4);
                manifest.Add(new { fileName = Path.GetFileName(mp4), start = segment.Start, end = segment.End, videoCodec = actual.DisplayVideo, audioCodec = actual.Audio });
            }
            var manifestPath = Path.Combine(workDirectory, "时间清单.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new { deviceId = device.Id, channel = job.Request.Channel, start = job.Request.Start, end = job.Request.End, segments = manifest }, Json), token);
            var gaps = segments.Count != 1 || segments[0].Start > job.Request.Start || segments[0].End < job.Request.End;
            string output;
            if (!gaps) output = completed[0];
            else
            {
                output = Path.Combine(workDirectory, "录像及时间清单.zip");
                using (var file = new FileStream(output, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    foreach (var path in completed.Append(manifestPath))
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = zip.CreateEntry(Path.GetFileName(path), CompressionLevel.NoCompression);
                        await using var input = File.OpenRead(path);
                        await using var destination = entry.Open();
                        await WithCapacityAsync(cancellation => input.CopyToAsync(destination, cancellation), token);
                    }
                }
                foreach (var path in completed) File.Delete(path);
            }
            token.ThrowIfCancellationRequested();
            lock (job)
            {
                token.ThrowIfCancellationRequested();
                Update(job, new("completed", 100, output));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Update(job, new("cancelled", job.Status.Progress)); }
        catch (Exception ex)
        {
            MediaTools.ReportFailure($"导出 {job.Request.JobId}，设备 {device.Id}，通道 {job.Request.Channel}", $"{ex.GetType().Name}：{ex.Message}");
            Update(job, new("failed", job.Status.Progress, Error: ex is AdapterException or InvalidOperationException or TimeoutException ? ex.Message : "导出失败，请检查设备、媒体工具和磁盘空间。"));
        }
        finally
        {
            try
            {
                if (workDirectory is not null && job.Status.State != "completed")
                {
                    try { foreach (var file in Directory.EnumerateFiles(EnsureDirectory(workDirectory))) File.Delete(file); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { Console.Error.WriteLine("导出临时文件清理失败，将由平台维护任务回收。"); }
                }
            }
            finally
            {
                if (globalAcquired) _global.Release();
                if (localAcquired) local.Release();
            }
        }
    }
    internal string EnsureDirectory(string requested)
    {
        var path = Path.GetFullPath(requested);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Equals(_root, comparison) && !path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("导出文件只能写入平台导出目录。");
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("导出目录不能经过符号链接或目录联接。");
            if (current.FullName.Equals(_root, comparison)) break;
        }
        return path;
    }
    private void CheckDisk()
    {
        if (_capacityCheck is not null) { _capacityCheck(); return; }
        var drive = new DriveInfo(Path.GetPathRoot(_root)!);
        if (drive.IsReady && drive.AvailableFreeSpace < drive.TotalSize / 10) throw new AdapterException(507, "EXPORT_DISK_LOW", "磁盘剩余不足 10%，暂不接收录像导出。");
        var size = Directory.EnumerateFiles(_root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Sum(p => new FileInfo(p).Length);
        var quota = long.TryParse(Environment.GetEnvironmentVariable("HIK_EXPORT_QUOTA_GB"), out var gb) ? Math.Max(1, gb) : 100;
        if (size >= quota * 1024L * 1024 * 1024) throw new AdapterException(507, "EXPORT_QUOTA", "导出目录已达到空间配额。");
    }
    private async Task WithCapacityAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var work = operation(lifetime.Token);
        try
        {
            while (!work.IsCompleted)
            {
                CheckDisk();
                await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(1), lifetime.Token));
                token.ThrowIfCancellationRequested();
            }
            await work;
        }
        catch
        {
            await lifetime.CancelAsync();
            try { await work; } catch { }
            throw;
        }
    }
    private void Update(Job job, ExportSummary status)
    {
        lock (job)
        {
            if (job.Stop.IsCancellationRequested && status.State is "queued" or "running") return;
            job.Status = status;
            if (status.State is "queued" or "running") job.ActiveStatus = status;
            try { Persist(job); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && status.State is "failed" or "cancelled")
            {
                // 磁盘故障时仍必须结束任务并释放资源；重启会将旧运行状态恢复为失败。
                Console.Error.WriteLine("导出终态持久化失败，内存状态已更新，请检查磁盘并由平台核对。");
            }
        }
    }
    private void Persist(Job job)
    {
        var path = Path.Combine(_states, $"{job.Request.JobId:N}.json");
        var temporary = path + ".pending";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write))
        {
            file.Write(JsonSerializer.SerializeToUtf8Bytes(new Saved(job.Request.JobId, job.DeviceId, job.Request, job.Status), Json)); file.Flush(true);
        }
        File.Move(temporary, path, true);
    }
    public async Task CancelDeviceAsync(long id)
    {
        var jobs = _jobs.Where(j => j.Value.DeviceId == id).ToArray();
        await Task.WhenAll(jobs.Select(j => CancelAsync(id, j.Key)));
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var job in _jobs.Values) job.Stop.Cancel();
        await Task.WhenAll(_jobs.Values.Select(j => j.Work));
        foreach (var job in _jobs.Values) job.Stop.Dispose();
    }
}
