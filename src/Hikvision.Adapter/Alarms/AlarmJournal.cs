using System.Text;
using System.Text.Json;
using System.Threading.Channels;

internal sealed class AlarmJournal : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Channel<AdapterAlarmEvent> _queue;
    private readonly string _root;
    private readonly long _deviceId;
    private readonly long _maxBytes;
    private readonly long _segmentBytes;
    private readonly object _diskGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _writer;
    private long _queuedBytes, _dropped;
    private long _segment;
    private string _ack = "0:0";
    private string? _fault;
    private string? _pendingFault;
    private int _disposed;

    public AlarmJournal(long deviceId, string root, int capacity = 256, long maxBytes = 64 * 1024 * 1024, long segmentBytes = 8 * 1024 * 1024)
    {
        _deviceId = deviceId; _root = Path.Combine(root, deviceId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _maxBytes = maxBytes; _segmentBytes = segmentBytes;
        Directory.CreateDirectory(_root);
        _segment = Math.Max(DateTimeOffset.UtcNow.UtcTicks, Segments().Select(s => s.Number).DefaultIfEmpty(0).Max() + 1);
        var ackPath = Path.Combine(_root, "ack.json");
        if (File.Exists(ackPath))
        {
            try { _ack = JsonSerializer.Deserialize<string>(File.ReadAllText(ackPath)) ?? "0:0"; ParseCursor(_ack); }
            catch { _ack = "0:0"; SignalFault("报警确认游标损坏，已从保留日志重新读取。"); }
        }
        _queue = Channel.CreateBounded<AdapterAlarmEvent>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        _writer = Task.Run(WriteLoopAsync);
    }

    public object Health => new { deviceId = _deviceId, queuedBytes = Interlocked.Read(ref _queuedBytes), dropped = Interlocked.Read(ref _dropped), error = Volatile.Read(ref _fault), acknowledgedCursor = _ack };
    private static long Size(AdapterAlarmEvent item) => item.Payload.LongLength + (item.Details?.Image?.LongLength ?? 0) + 2048;
    public bool TryEnqueue(AdapterAlarmEvent item)
    {
        var size = Size(item);
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (Interlocked.Add(ref _queuedBytes, size) > _maxBytes)
        {
            Interlocked.Add(ref _queuedBytes, -size); Overflow(); return false;
        }
        if (_queue.Writer.TryWrite(item)) return true;
        Interlocked.Add(ref _queuedBytes, -size); Overflow(); return false;
    }
    private void Overflow()
    {
        Interlocked.Increment(ref _dropped);
        SignalFault("报警缓冲已满，存在未持久化事件，请检查磁盘与报警洪峰。");
    }
    public void SignalFault(string message)
    {
        Volatile.Write(ref _fault, message);
        Interlocked.Exchange(ref _pendingFault, message);
    }

    private async Task WriteLoopAsync()
    {
        AdapterAlarmEvent? pending = null;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (pending is null) _queue.Reader.TryRead(out pending);
                if (pending is null && Volatile.Read(ref _pendingFault) is null)
                {
                    if (_queue.Reader.Completion.IsCompleted) break;
                    await Task.Delay(100, _stop.Token); continue;
                }
                try
                {
                    lock (_diskGate)
                    {
                        if (Volatile.Read(ref _pendingFault) is { } fault)
                        {
                            Append(new("", _deviceId, null, "system.adapter_alarm", DateTimeOffset.UtcNow, false, new { message = fault, dropped = Interlocked.Read(ref _dropped) }));
                            Interlocked.CompareExchange(ref _pendingFault, null, fault);
                        }
                        if (pending is not null)
                        {
                            var details = pending.Details;
                            var channels = details?.Channels.Count > 0 ? details.Channels.Select(c => (int?)c) : new int?[] { null };
                            foreach (var channel in channels)
                                Append(new($"{_deviceId}:{pending.EventId}:{channel ?? 0}", _deviceId, channel, details?.EventType ?? $"alarm.sdk_{pending.Command}", details?.AlarmTime ?? pending.ReceivedAt,
                                    details?.IsRecovery == true,
                                    new { command = pending.Command, payloadBase64 = Convert.ToBase64String(pending.Payload), alarmType = details?.AlarmType, channels = details?.Channels, isRecovery = details?.IsRecovery, imageUrl = details?.ImageUrl },
                                    details?.Image is { Length: > 0 } image ? Convert.ToBase64String(image) : null));
                            Interlocked.Add(ref _queuedBytes, -Size(pending)); pending = null;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    SignalFault("报警日志持久化失败，请检查目录权限和磁盘空间。");
                    await Task.Delay(1000, _stop.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void Append(AlarmEvent item)
    {
        var path = SegmentPath(_segment);
        if (File.Exists(path) && new FileInfo(path).Length >= _segmentBytes) path = SegmentPath(++_segment);
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var offset = file.Position;
        if (string.IsNullOrEmpty(item.Id)) item = item with { Id = $"{_deviceId}:{_segment}:{offset}" };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, Json);
        file.Write(bytes); file.WriteByte((byte)'\n'); file.Flush(true);
    }

    public AlarmPage Read(string? after, int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentException("报警批量读取数量必须在 1～1000 之间。");
        lock (_diskGate)
        {
            var cursor = ParseCursor(string.IsNullOrWhiteSpace(after) ? _ack : after);
            var items = new List<AlarmEvent>();
            var next = Format(cursor);
            foreach (var segment in Segments().Where(s => s.Number >= cursor.Segment))
            {
                using var file = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                file.Position = segment.Number == cursor.Segment ? Math.Min(cursor.Offset, file.Length) : 0;
                while (file.Position < file.Length && items.Count < limit)
                {
                    var offset = file.Position;
                    var line = ReadLine(file);
                    next = Format((segment.Number, file.Position));
                    try
                    {
                        var item = JsonSerializer.Deserialize<AlarmEvent>(line, Json) ?? throw new JsonException();
                        if (item.DeviceId != _deviceId) throw new JsonException();
                        items.Add(item);
                    }
                    catch (JsonException)
                    {
                        var quarantine = Path.Combine(_root, $"damaged-{segment.Number}-{offset}.bin");
                        if (!File.Exists(quarantine)) File.WriteAllBytes(quarantine, line);
                        SignalFault("报警日志存在损坏记录，已隔离并继续读取。");
                    }
                }
                if (items.Count >= limit) break;
            }
            return new(items, next);
        }
    }

    public void Ack(string cursor)
    {
        lock (_diskGate)
        {
            var value = ParseCursor(cursor);
            if (value.CompareTo(ParseCursor(_ack)) < 0) throw new ArgumentException("确认游标不能倒退。");
            if (value == (0L, 0L)) return;
            var path = SegmentPath(value.Segment);
            if (!File.Exists(path) || value.Offset > new FileInfo(path).Length) throw new ArgumentException("确认游标超出已持久化日志。");
            using (var file = File.OpenRead(path))
            {
                if (value.Offset > 0) { file.Position = value.Offset - 1; if (file.ReadByte() != '\n') throw new ArgumentException("确认游标不在记录边界。"); }
            }
            var temporary = Path.Combine(_root, "ack.pending");
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            {
                file.Write(JsonSerializer.SerializeToUtf8Bytes(Format(value))); file.Flush(true);
            }
            File.Move(temporary, Path.Combine(_root, "ack.json"), true);
            _ack = Format(value);
            foreach (var segment in Segments().Where(s => s.Number < value.Segment)) File.Delete(segment.Path);
        }
    }
    private IEnumerable<(long Number, string Path)> Segments() => Directory.EnumerateFiles(_root, "*.ndjson")
        .Select(p => (Number: long.TryParse(Path.GetFileNameWithoutExtension(p), out var n) ? n : -1, Path: p)).Where(s => s.Number >= 0).OrderBy(s => s.Number);
    private string SegmentPath(long number) => Path.Combine(_root, $"{number:D20}.ndjson");
    private static (long Segment, long Offset) ParseCursor(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var segment) || !long.TryParse(parts[1], out var offset) || segment < 0 || offset < 0)
            throw new ArgumentException("报警游标格式无效。");
        return (segment, offset);
    }
    private static string Format((long Segment, long Offset) value) => $"{value.Segment}:{value.Offset}";
    private static byte[] ReadLine(FileStream file)
    {
        using var buffer = new MemoryStream();
        var oversized = false;
        for (var value = file.ReadByte(); value >= 0 && value != '\n'; value = file.ReadByte())
        {
            if (buffer.Length >= 20 * 1024 * 1024) oversized = true;
            else buffer.WriteByte((byte)value);
        }
        // 异常巨行跳到下一条记录边界，保留前缀供隔离，不能阻塞后续有效报警。
        return oversized ? [] : buffer.ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        _stop.CancelAfter(TimeSpan.FromSeconds(10));
        await _writer;
        _stop.Dispose();
    }
}
