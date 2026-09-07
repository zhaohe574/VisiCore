using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

if (Environment.GetEnvironmentVariable("HIK_ALARM_SELF_TEST") == "1")
{
    AlarmParser.SelfTest();
    Console.WriteLine("报警解析自检通过");
    return;
}

if (Environment.GetEnvironmentVariable("HIK_PLAYBACK_TIMELINE_SELF_TEST") == "1")
{
    PlaybackTimeline.SelfTest();
    Console.WriteLine("回放时间线自检通过");
    return;
}

var options = DeviceOptions.FromEnvironment();
var probe = new HikvisionProbe(options, new AlarmEventQueue());
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

if (Environment.GetEnvironmentVariable("HIK_RUN_AS_SERVICE") == "1")
{
    await RunServiceAsync(probe, jsonOptions);
    return;
}

Console.WriteLine(JsonSerializer.Serialize(probe.Run(), jsonOptions));

static async Task RunServiceAsync(HikvisionProbe probe, JsonSerializerOptions jsonOptions)
{
    var snapshotPath = Environment.GetEnvironmentVariable("HIK_SNAPSHOT_PATH")
        ?? "/var/lib/video-platform/adapter/device-snapshot.json";
    var intervalSeconds = Math.Max(5, int.Parse(Environment.GetEnvironmentVariable("HIK_PROBE_INTERVAL_SECONDS") ?? "30"));
    var alarmPath = Environment.GetEnvironmentVariable("HIK_ALARM_PATH")
        ?? "/home/liteware/.local/share/video-platform/alarm-events.ndjson";
    using var stop = new CancellationTokenSource();
    using var signal = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => stop.Cancel());
    var controlTask = RunControlApiAsync(probe, stop.Token);
    var alarmWriterTask = probe.Alarms.RunAsync(alarmPath, stop.Token);

    try
    {
        probe.StartAlarm();
        Console.WriteLine("报警布防已启动");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"报警布防启动失败：{ex.Message}");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
    try
    {
        do
        {
            try
            {
                try
                {
                    probe.StartAlarm();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"报警布防重试失败：{ex.Message}");
                }
                var result = probe.Run();
                WriteSnapshot(snapshotPath, new ServiceSnapshot("ok", DateTimeOffset.UtcNow, result, null), jsonOptions);
                Console.WriteLine($"设备同步成功：{DateTimeOffset.UtcNow:O}");
            }
            catch (Exception ex)
            {
                WriteSnapshot(snapshotPath, new ServiceSnapshot("error", DateTimeOffset.UtcNow, null, ex.Message), jsonOptions);
                Console.Error.WriteLine($"设备同步失败：{ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(stop.Token));
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested)
    {
        // 服务停止时退出轮询。
    }
    finally
    {
        stop.Cancel();
        try { await controlTask; } catch (OperationCanceledException) { }
        try { await alarmWriterTask; } catch (OperationCanceledException) { }
        probe.Dispose();
    }
}

static async Task RunControlApiAsync(HikvisionProbe probe, CancellationToken cancellationToken)
{
    var key = Environment.GetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY");
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("HIK_ADAPTER_API_URL") ?? "http://127.0.0.1:5090");
    var app = builder.Build();
    app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "hikvision-adapter" }));
    app.MapPost("/ptz", (HttpContext context, PtzRequest request) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        if (request.Channel <= 0 || request.Command is < 1 or > 255 || request.Speed is < 1 or > 7)
            return Results.BadRequest(new { error = "PTZ 参数无效" });
        try
        {
            var result = probe.Ptz(request.Channel, request.Command, request.Stop, request.Speed);
            return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: 502);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    });
    app.MapPost("/ptz/preset", (HttpContext context, PresetRequest request) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        if (request.Channel <= 0 || request.Preset is < 1 or > 300)
            return Results.BadRequest(new { error = "预置位参数无效" });
        try
        {
            return probe.Preset(request.Channel, request.Preset)
                ? Results.Ok(new { channel = request.Channel, preset = request.Preset })
                : Results.Problem("预置位调用失败", statusCode: 502);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    });
    app.MapPost("/recordings/search", (HttpContext context, RecordingSearchRequest request) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        if (request.Channel <= 0 || request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(7))
            return Results.BadRequest(new { error = "录像查询参数无效，时间范围不能超过 7 天" });
        try
        {
            return Results.Ok(probe.SearchRecordings(request.Channel, request.Start, request.End));
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    });
    app.MapPost("/playback/start", (HttpContext context, PlaybackStartRequest request) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        if (request.UserId <= 0 || request.Channel <= 0 || request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(1))
            return Results.BadRequest(new { error = "回放参数无效，时间范围不能超过 1 天" });
        try
        {
            return Results.Ok(probe.StartPlayback(request));
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    });
    app.MapGet("/playback/{id:guid}", (HttpContext context, Guid id, long userId) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        return probe.GetPlayback(id, userId) is { } session ? Results.Ok(session) : Results.NotFound();
    });
    app.MapPost("/playback/{id:guid}/control", (HttpContext context, Guid id, long userId, PlaybackControlRequest request) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        try
        {
            return probe.ControlPlayback(id, userId, request.Action, request.Position) is { } session ? Results.Ok(session) : Results.NotFound();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });
    app.MapDelete("/playback/{id:guid}", (HttpContext context, Guid id, long userId) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        return probe.StopPlayback(id, userId) ? Results.NoContent() : Results.NotFound();
    });
        app.MapPost("/live/start", (HttpContext context, LiveStartRequest request) =>
        {
            if (!Authorized(context, key))
                return Results.Unauthorized();
            if (request.Channel <= 0 || request.StreamType is not (1 or 2))
                return Results.BadRequest(new { error = "实时预览参数无效" });
            try
            {
                return Results.Ok(probe.StartLive(request.Channel, request.StreamType, request.SessionId));
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 502);
        }
    });
    app.MapPost("/live/stop", (HttpContext context, LiveStopRequest request) =>
    {
        if (!Authorized(context, key))
            return Results.Unauthorized();
        return probe.StopLive(request.Channel, request.StreamType, request.SessionId) ? Results.NoContent() : Results.NotFound();
    });
    await app.StartAsync(cancellationToken);
    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
    finally
    {
        await app.StopAsync(CancellationToken.None);
    }
}

static bool Authorized(HttpContext context, string? expectedKey)
{
    if (string.IsNullOrEmpty(expectedKey))
        return false;
    var provided = context.Request.Headers["X-Adapter-Key"].ToString();
    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
    return providedBytes.Length == expectedBytes.Length
        && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}

static void WriteSnapshot(string path, ServiceSnapshot snapshot, JsonSerializerOptions jsonOptions)
{
    var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, jsonOptions));
    File.Move(temporaryPath, path, true);
}

internal sealed record ServiceSnapshot(
    string Status,
    DateTimeOffset CheckedAt,
    ProbeResult? Result,
    string? Error);

internal sealed record DeviceOptions(
    string SdkDirectory,
    string DeviceIp,
    ushort DevicePort,
    string Username,
    string Password,
    int? PreviewChannel,
    int PreviewSeconds)
{
    public static DeviceOptions FromEnvironment()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"缺少环境变量：{name}");

        var sdkDirectory = Environment.GetEnvironmentVariable("HIK_SDK_DIR")
            ?? "/opt/video-platform/sdk/hikvision";
        var previewChannel = Environment.GetEnvironmentVariable("HIK_PREVIEW_CHANNEL");
        return new DeviceOptions(
            sdkDirectory,
            Required("HIK_DEVICE_IP"),
            ushort.Parse(Environment.GetEnvironmentVariable("HIK_DEVICE_PORT") ?? "8000"),
            Required("HIK_DEVICE_USER"),
            Required("HIK_DEVICE_PASSWORD"),
            string.IsNullOrWhiteSpace(previewChannel) ? null : int.Parse(previewChannel),
            int.Parse(Environment.GetEnvironmentVariable("HIK_PREVIEW_SECONDS") ?? "5"));
    }
}

internal sealed record PtzRequest(int Channel, uint Command, bool Stop, uint Speed = 4);
internal sealed record PresetRequest(int Channel, uint Preset);
internal sealed record PtzResult(bool Success, int Channel, uint Command, bool Stop, string? Error = null);
internal sealed record RecordingSearchRequest(int Channel, DateTimeOffset Start, DateTimeOffset End);
internal sealed record PlaybackStartRequest(int Channel, DateTimeOffset Start, DateTimeOffset End, long UserId = 0);
internal sealed record PlaybackControlRequest(string Action, int? Position = null);
internal sealed record LiveStartRequest(int Channel, int StreamType = 2, Guid? SessionId = null);
internal sealed record LiveStopRequest(int Channel, int StreamType = 2, Guid? SessionId = null);
internal sealed record RecordingSummary(string FileName, DateTimeOffset Start, DateTimeOffset End, long FileSize, int FileType, int StreamType, uint FileIndex);
internal sealed record PlaybackSegment(DateTimeOffset Start, DateTimeOffset End, string FileName, long FileSize, int FileType, int StreamType, uint FileIndex);
internal sealed record LiveSummary(int Channel, string Stream, int StreamType, int References);
internal sealed record AdapterAlarmEvent(DateTimeOffset ReceivedAt, int Command, byte[] Payload, AlarmEventDetails? Details);
internal sealed record AlarmEventDetails(
    string EventType,
    uint AlarmType,
    IReadOnlyList<int> Channels,
    bool? IsRecovery,
    DateTimeOffset? AlarmTime,
    byte[]? Image,
    string? ImageUrl);

internal static class AlarmParser
{
    private const int CommAlarm = 0x1100;
    private const int V30ChannelOffset = 168;
    private const int V30ChannelCount = 64;
    private const int MaxImageBytes = 8 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<uint, string> EventNames = new Dictionary<uint, string>
    {
        [0] = "alarm.input",
        [1] = "alarm.disk",
        [2] = "alarm.video_loss",
        [3] = "alarm.motion",
        [4] = "alarm.disk_unformatted",
        [5] = "alarm.disk_write_error",
        [6] = "alarm.tamper",
        [7] = "alarm.format_mismatch",
        [8] = "alarm.illegal_access",
        [9] = "alarm.video_exception",
        [10] = "alarm.recording_exception",
        [11] = "alarm.ip_conflict",
        [12] = "alarm.network",
        [13] = "alarm.stream_mismatch",
        [14] = "alarm.network_error",
        [15] = "alarm.smart",
        [16] = "alarm.standby_exception",
        [17] = "alarm.recording_space_low",
        [18] = "alarm.recording_failure",
        [19] = "alarm.video_loss",
        [20] = "alarm.recording",
        [21] = "alarm.recording_stopped",
        [22] = "alarm.algorithm_exception",
        [23] = "alarm.thermal",
        [24] = "alarm.disk_exception",
        [25] = "alarm.network",
        [26] = "alarm.picture_upload",
        [27] = "alarm.poc_exception",
        [28] = "alarm.video_exception",
        [30] = "alarm.sd_missing",
        [31] = "alarm.voltage",
        [32] = "alarm.ptz_exception",
        [33] = "alarm.log_exception",
        [34] = "alarm.abnormal_reboot"
    };

    public static AlarmEventDetails? Parse(int command, IntPtr alarmInfo, uint bufferLength)
    {
        if (command != CommAlarm || alarmInfo == IntPtr.Zero || bufferLength < 4)
            return null;
        var length = (int)Math.Min(bufferLength, 4096u);
        var bytes = new byte[length];
        Marshal.Copy(alarmInfo, bytes, 0, length);
        var alarmType = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (length <= 200 && TryReadTime(bytes, 4, out var v40Time))
            return ParseV40(alarmInfo, bytes, alarmType, v40Time);
        return ParseV30(bytes, alarmType);
    }

    private static AlarmEventDetails ParseV30(byte[] bytes, uint alarmType)
    {
        var channels = new List<int>();
        if (bytes.Length >= V30ChannelOffset + V30ChannelCount)
            for (var i = 0; i < V30ChannelCount; i++)
                if (bytes[V30ChannelOffset + i] != 0)
                    channels.Add(i + 1);
        return new AlarmEventDetails(
            EventNames.TryGetValue(alarmType, out var name) ? name : $"alarm.sdk_{alarmType}",
            alarmType,
            channels,
            IsChannelAlarm(alarmType) ? channels.Count == 0 : null,
            null,
            null,
            null);
    }

    private static AlarmEventDetails ParseV40(IntPtr pointer, byte[] bytes, uint alarmType, DateTimeOffset alarmTime)
    {
        var unionOffset = bytes.Length >= 160 ? 16 : 12;
        var channelCount = bytes.Length >= unionOffset + 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(unionOffset, 4)) : 0;
        var channels = ReadV40Channels(pointer, unionOffset == 16 ? 152 : 144, channelCount);
        byte[]? image = null;
        string? imageUrl = null;
        if (IsImageAlarm(alarmType))
        {
            var imageLength = bytes.Length >= unionOffset + 8 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(unionOffset + 4, 4)) : 0;
            var byUrl = bytes.Length > unionOffset + 8 && bytes[unionOffset + 8] != 0;
            var dataOffset = unionOffset + 16;
            if (imageLength > 0 && imageLength <= MaxImageBytes && bytes.Length >= dataOffset + IntPtr.Size)
            {
                var data = Marshal.ReadIntPtr(pointer, dataOffset);
                if (data != IntPtr.Zero)
                {
                    if (byUrl)
                        imageUrl = ReadAnsi(data, 4096);
                    else
                    {
                        image = new byte[imageLength];
                        Marshal.Copy(data, image, 0, image.Length);
                    }
                }
            }
        }
        return new AlarmEventDetails(
            EventNames.TryGetValue(alarmType, out var name) ? name : $"alarm.sdk_{alarmType}",
            alarmType,
            channels,
            null,
            alarmTime,
            image,
            imageUrl);
    }

    private static IReadOnlyList<int> ReadV40Channels(IntPtr pointer, int dataOffset, uint count)
    {
        if (count == 0 || count > 512 || pointer == IntPtr.Zero)
            return Array.Empty<int>();
        var data = Marshal.ReadIntPtr(pointer, dataOffset);
        if (data == IntPtr.Zero)
            return Array.Empty<int>();
        var channels = new List<int>((int)Math.Min(count, 512));
        for (var i = 0; i < count; i++)
        {
            var channel = Marshal.ReadInt32(data, checked(i * sizeof(int)));
            if (channel > 0 && channel <= 65535)
                channels.Add(channel);
        }
        return channels;
    }

    private static bool TryReadTime(byte[] bytes, int offset, out DateTimeOffset value)
    {
        value = default;
        if (bytes.Length < offset + 8)
            return false;
        var year = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
        var month = bytes[offset + 2];
        var day = bytes[offset + 3];
        var hour = bytes[offset + 4];
        var minute = bytes[offset + 5];
        var second = bytes[offset + 6];
        if (year is < 2000 or > 2200 || month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
            return false;
        try
        {
            value = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(8));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string? ReadAnsi(IntPtr pointer, int maxLength)
    {
        var bytes = new byte[maxLength];
        var length = 0;
        while (length < bytes.Length && (bytes[length] = Marshal.ReadByte(pointer, length)) != 0)
            length++;
        return length == 0 ? null : Encoding.ASCII.GetString(bytes, 0, length);
    }

    private static bool IsChannelAlarm(uint alarmType) => alarmType is 2 or 3 or 6 or 9 or 10 or 14 or 19 or 28;
    private static bool IsImageAlarm(uint alarmType) => alarmType is 2 or 3 or 6 or 9 or 10 or 13 or 28;

    public static void SelfTest()
    {
        var bytes = new byte[268];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 3);
        bytes[V30ChannelOffset + 7] = 1;
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var details = Parse(CommAlarm, pointer, (uint)bytes.Length);
            if (details is null || details.EventType != "alarm.motion" || details.Channels.Count != 1 || details.Channels[0] != 8 || details.IsRecovery != false)
                throw new InvalidOperationException("V30 报警解析结果不符合预期");
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        var image = new byte[] { 0xff, 0xd8, 0xff, 0xd9 };
        var v40 = new byte[160];
        BinaryPrimitives.WriteUInt32LittleEndian(v40, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(v40.AsSpan(4), 2026);
        v40[6] = 9; v40[7] = 4; v40[8] = 12; v40[9] = 30; v40[10] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(v40.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(v40.AsSpan(20), (uint)image.Length);
        var imagePointer = Marshal.AllocHGlobal(image.Length);
        var channelPointer = Marshal.AllocHGlobal(sizeof(int));
        var v40Pointer = Marshal.AllocHGlobal(v40.Length);
        try
        {
            Marshal.Copy(image, 0, imagePointer, image.Length);
            Marshal.WriteInt32(channelPointer, 0, 8);
            Marshal.WriteIntPtr(v40Pointer, 32, imagePointer);
            Marshal.WriteIntPtr(v40Pointer, 152, channelPointer);
            Marshal.Copy(v40, 0, v40Pointer, v40.Length);
            // 指针字段在复制结构后写入，避免按平台字节序手工编码地址。
            Marshal.WriteIntPtr(v40Pointer, 32, imagePointer);
            Marshal.WriteIntPtr(v40Pointer, 152, channelPointer);
            var details = Parse(CommAlarm, v40Pointer, (uint)v40.Length);
            if (details is null || details.AlarmTime?.Year != 2026 || details.Channels.Count != 1 || details.Channels[0] != 8 || details.Image?.Length != image.Length)
                throw new InvalidOperationException("V40 报警解析结果不符合预期");
        }
        finally
        {
            Marshal.FreeHGlobal(v40Pointer);
            Marshal.FreeHGlobal(channelPointer);
            Marshal.FreeHGlobal(imagePointer);
        }
    }
}

internal sealed class AlarmEventQueue
{
    // ponytail: 当前使用无界队列，报警洪峰时升级为有界队列加持久化队列。
    private readonly ConcurrentQueue<AdapterAlarmEvent> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void Enqueue(AdapterAlarmEvent item)
    {
        _queue.Enqueue(item);
        _signal.Release();
    }

    public async Task RunAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            while (await _signal.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken))
            {
                while (_queue.TryDequeue(out var item))
                {
                    var line = JsonSerializer.Serialize(new
                    {
                        receivedAt = item.ReceivedAt,
                        command = item.Command,
                        payloadBase64 = Convert.ToBase64String(item.Payload),
                        eventType = item.Details?.EventType,
                        alarmType = item.Details?.AlarmType,
                        channels = item.Details?.Channels,
                        isRecovery = item.Details?.IsRecovery,
                        alarmTime = item.Details?.AlarmTime,
                        imageBase64 = item.Details?.Image is { Length: > 0 } image ? Convert.ToBase64String(image) : null,
                        imageUrl = item.Details?.ImageUrl
                    });
                    await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            while (_queue.TryDequeue(out var item))
            {
                var line = JsonSerializer.Serialize(new
                {
                    receivedAt = item.ReceivedAt,
                    command = item.Command,
                    payloadBase64 = Convert.ToBase64String(item.Payload),
                    eventType = item.Details?.EventType,
                    alarmType = item.Details?.AlarmType,
                    channels = item.Details?.Channels,
                    isRecovery = item.Details?.IsRecovery,
                    alarmTime = item.Details?.AlarmTime,
                    imageBase64 = item.Details?.Image is { Length: > 0 } image ? Convert.ToBase64String(image) : null,
                    imageUrl = item.Details?.ImageUrl
                });
                await File.AppendAllTextAsync(path, line + Environment.NewLine);
            }
        }
    }
}

internal sealed record PlaybackSummary(
    Guid Id,
    int Channel,
    DateTimeOffset Start,
    DateTimeOffset End,
    string State,
    int Progress,
    long Bytes,
    string FileName,
    string Stream,
    string TimelineState,
    DateTimeOffset CurrentTime);

internal static class PlaybackTimeline
{
    public static IReadOnlyList<PlaybackSegment> Normalize(IEnumerable<RecordingSummary> recordings, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        if (rangeEnd <= rangeStart)
            return Array.Empty<PlaybackSegment>();

        var result = new List<PlaybackSegment>();
        foreach (var recording in recordings
            .Where(item => item.End > item.Start && item.End > rangeStart && item.Start < rangeEnd)
            .Select(item => new PlaybackSegment(
                item.Start < rangeStart ? rangeStart : item.Start,
                item.End > rangeEnd ? rangeEnd : item.End,
                item.FileName,
                item.FileSize,
                item.FileType,
                item.StreamType,
                item.FileIndex))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End))
        {
            if (result.Count > 0
                && recording.Start <= result[^1].End
                && string.Equals(recording.FileName, result[^1].FileName, StringComparison.Ordinal)
                && recording.FileIndex == result[^1].FileIndex)
            {
                var previous = result[^1];
                result[^1] = previous with { End = recording.End > previous.End ? recording.End : previous.End };
            }
            else
            {
                result.Add(recording);
            }
        }
        return result;
    }

    public static void SelfTest()
    {
        var start = new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.FromHours(8));
        var recordings = new[]
        {
            new RecordingSummary("late", start.AddMinutes(40), start.AddMinutes(80), 0, 0, 0, 2),
            new RecordingSummary("early", start.AddMinutes(-10), start.AddMinutes(15), 0, 0, 0, 1),
            new RecordingSummary("early", start.AddMinutes(10), start.AddMinutes(20), 0, 0, 0, 1)
        };
        var segments = Normalize(recordings, start, start.AddHours(1));
        if (segments.Count != 2
            || segments[0].Start != start
            || segments[0].End != start.AddMinutes(20)
            || segments[1].Start != start.AddMinutes(40)
            || segments[1].End != start.AddHours(1))
            throw new InvalidOperationException("回放时间线规范化自检失败");
    }
}

internal sealed class PlaybackSession : IDisposable
{
    // ponytail: 单会话无界队列优先保证 SDK 回调不阻塞；高并发时升级为有界队列和丢帧策略。
    private readonly ConcurrentQueue<byte[]> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly FileStream _file;
    private readonly Task _writer;
    private Process? _publisher;
    private Task? _publisherErrors;
    private int _publisherInputClosed;

    public PlaybackSession(Guid id, long userId, int channel, DateTimeOffset start, DateTimeOffset end, string path, IReadOnlyList<PlaybackSegment> segments)
    {
        Id = id;
        UserId = userId;
        Channel = channel;
        Start = start;
        End = end;
        Path = path;
        Segments = segments;
        CurrentTime = start;
        _timelineTick = DateTimeOffset.UtcNow;
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        _writer = WriteAsync();
        if (Segments.Count == 0)
        {
            SetCurrentTime(Start);
            SetState("gap");
        }
        else if (Segments[0].Start <= Start)
        {
            SegmentIndex = 0;
            SetState("playing");
        }
    }

    public Guid Id { get; }
    public long UserId { get; }
    public int Channel { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public string Path { get; }
    public IReadOnlyList<PlaybackSegment> Segments { get; }
    public string Stream => $"pb_{Id:N}";
    public int Handle { get; set; } = -1;
    public int Progress { get; set; }
    public string State { get; set; } = "gap";
    public string TimelineState { get; set; } = "gap";
    public DateTimeOffset CurrentTime { get; private set; }
    public int SegmentIndex { get; private set; } = -1;
    public PlaybackSegment? CurrentSegment => SegmentIndex >= 0 && SegmentIndex < Segments.Count ? Segments[SegmentIndex] : null;
    public HikvisionProbe.Native.PlayDataCallback? Callback { get; set; }
    private DateTimeOffset _timelineTick;

    public void Enqueue(uint dataType, IntPtr buffer, uint size)
    {
        if (_stop.IsCancellationRequested || buffer == IntPtr.Zero || size == 0)
            return;
        var payload = new byte[(int)Math.Min(size, 4u * 1024u * 1024u)];
        Marshal.Copy(buffer, payload, 0, payload.Length);
        _queue.Enqueue(payload);
        Interlocked.Add(ref _bytes, payload.Length);
        try { _signal.Release(); } catch (ObjectDisposedException) { }
    }

    private long _bytes;

    public PlaybackSummary Summary() => new(Id, Channel, Start, End, State, Progress, Interlocked.Read(ref _bytes), System.IO.Path.GetFileName(Path), Stream, TimelineState, CurrentTime);

    public void StartSegment(int index, DateTimeOffset currentTime)
    {
        SegmentIndex = index;
        SetCurrentTime(currentTime);
        SetState("playing");
        _timelineTick = DateTimeOffset.UtcNow;
    }

    public void EnterGap(DateTimeOffset currentTime)
    {
        SetCurrentTime(currentTime);
        SetState("gap");
        _timelineTick = DateTimeOffset.UtcNow;
    }

    public void AdvanceGap(DateTimeOffset now)
    {
        if (State != "gap" || Segments.Count == 0)
            return;
        var elapsed = now - _timelineTick;
        if (elapsed > TimeSpan.Zero)
            CurrentTime = CurrentTime + elapsed;
        _timelineTick = now;
        var next = SegmentIndex + 1 < Segments.Count ? Segments[SegmentIndex + 1].Start : End;
        if (CurrentTime > next)
            CurrentTime = next;
        UpdateProgress();
    }

    public bool Advance(DateTimeOffset now)
    {
        if (State is "paused" or "completed")
            return false;
        var elapsed = now - _timelineTick;
        if (elapsed <= TimeSpan.Zero)
            return false;
        _timelineTick = now;
        CurrentTime += elapsed;
        if (CurrentTime >= End)
        {
            SetCurrentTime(End);
            SetState("completed");
            return false;
        }
        if (State == "gap")
        {
            var nextIndex = SegmentIndex + 1;
            if (nextIndex < Segments.Count && CurrentTime >= Segments[nextIndex].Start)
            {
                CurrentTime = Segments[nextIndex].Start;
                SegmentIndex = nextIndex;
                SetState("playing");
                UpdateProgress();
                return true;
            }
            UpdateProgress();
            return false;
        }
        while (SegmentIndex + 1 < Segments.Count && CurrentTime >= Segments[SegmentIndex].End)
        {
            var next = Segments[SegmentIndex + 1];
            if (next.Start > Segments[SegmentIndex].End)
            {
                CurrentTime = Segments[SegmentIndex].End;
                SetState("gap");
                UpdateProgress();
                return false;
            }
            SegmentIndex++;
        }
        if (SegmentIndex >= 0 && CurrentTime >= Segments[SegmentIndex].End && SegmentIndex == Segments.Count - 1)
        {
            SetCurrentTime(End);
            SetState("completed");
        }
        else
        {
            UpdateProgress();
        }
        return false;
    }

    public void Pause() => SetState("paused");

    public void Resume()
    {
        var index = Segments.ToList().FindIndex(item => CurrentTime >= item.Start && CurrentTime < item.End);
        SegmentIndex = index >= 0 ? index : Segments.TakeWhile(item => item.Start <= CurrentTime).Count() - 1;
        SetState(index >= 0 && Handle >= 0 ? "playing" : "gap");
        _timelineTick = DateTimeOffset.UtcNow;
    }

    public void SetState(string state) => State = TimelineState = state;

    public void SetCurrentTime(DateTimeOffset value)
    {
        CurrentTime = value < Start ? Start : value > End ? End : value;
        UpdateProgress();
    }

    public void SetGapSegmentIndex(int index) => SegmentIndex = index;

    private void UpdateProgress()
    {
        var duration = End - Start;
        Progress = duration <= TimeSpan.Zero ? 100 : Math.Clamp((int)Math.Round((CurrentTime - Start).TotalMilliseconds / duration.TotalMilliseconds * 100), 0, 100);
    }

    public void StartPublisher(string ffmpegPath, string rtmpBase)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("warning");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("mpeg");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("pipe:0");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("aac");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("flv");
        startInfo.ArgumentList.Add($"{rtmpBase.TrimEnd('/')}/{Stream}");
        try
        {
            _publisher = Process.Start(startInfo);
            if (_publisher is not null)
                _publisherErrors = DrainPublisherErrorsAsync(_publisher);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"回放流发布器启动失败：{ex.Message}");
        }
    }

    private static async Task DrainPublisherErrorsAsync(Process publisher)
    {
        try
        {
            while (await publisher.StandardError.ReadLineAsync() is { } line)
                Console.Error.WriteLine($"回放流发布器：{line}");
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void CompletePublisher()
    {
        if (Interlocked.Exchange(ref _publisherInputClosed, 1) != 0)
            return;
        try { _signal.Release(); } catch (ObjectDisposedException) { }
    }

    private void ClosePublisherInput()
    {
        try { _publisher?.StandardInput.Close(); }
        catch (InvalidOperationException) { }
    }

    private async Task WriteAsync()
    {
        try
        {
            while (await _signal.WaitAsync(Timeout.InfiniteTimeSpan, _stop.Token))
            {
                while (_queue.TryDequeue(out var payload))
                {
                    await _file.WriteAsync(payload, _stop.Token);
                    if (_publisher is { HasExited: false })
                        await _publisher.StandardInput.BaseStream.WriteAsync(payload, _stop.Token);
                }
                await _file.FlushAsync(_stop.Token);
                if (Volatile.Read(ref _publisherInputClosed) != 0)
                    ClosePublisherInput();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            while (_queue.TryDequeue(out var payload))
            {
                await _file.WriteAsync(payload);
                if (_publisher is { HasExited: false })
                    await _publisher.StandardInput.BaseStream.WriteAsync(payload);
            }
            await _file.FlushAsync();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _signal.Release(); } catch (ObjectDisposedException) { }
        try { _writer.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        try
        {
            if (_publisher is { HasExited: false } publisher)
            {
                publisher.StandardInput.Close();
                if (!publisher.WaitForExit(3000)) publisher.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
        _publisher?.Dispose();
        _file.Dispose();
        _stop.Dispose();
        _signal.Dispose();
    }
}

internal sealed class HikvisionProbe : IDisposable
{
    private const int GetIpParaConfigV40 = 1062;
    private const int IpParaConfigSize = 50792;
    private const int StreamModeSize = 496;
    private const int StreamModeOffset = 19028;

    private readonly DeviceOptions _options;
    private readonly string _sdkPath;
    private readonly AlarmEventQueue _alarms;
    private readonly object _sdkGate = new();
    private readonly ConcurrentDictionary<Guid, PlaybackSession> _playbacks = new();
    private readonly Dictionary<(int Channel, int StreamType), int> _liveReferences = new();
    private readonly Dictionary<Guid, (int Channel, int StreamType)> _liveSessionKeys = new();
    private readonly Dictionary<int, (uint Command, uint Speed, DateTimeOffset Deadline)> _ptzCommands = new();
    private readonly Timer _ptzWatchdog;
    private readonly object _liveGate = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _zlmApiUrl = Environment.GetEnvironmentVariable("HIK_ZLM_API_URL") ?? "http://127.0.0.1:18080";
    private readonly string? _zlmApiSecret = Environment.GetEnvironmentVariable("HIK_ZLM_API_SECRET");
    private readonly string _ffmpegPath = Environment.GetEnvironmentVariable("HIK_FFMPEG_PATH") ?? "/usr/bin/ffmpeg";
    private readonly string _zlmRtmpUrl = Environment.GetEnvironmentVariable("HIK_ZLM_RTMP_URL") ?? "rtmp://127.0.0.1:1935/playback";
    private bool _sdkInitialized;
    private int _controlUserId = -1;
    private int _alarmHandle = -1;
    private Native.MessageCallback? _alarmCallback;
    private Native.ExceptionCallback? _exceptionCallback;

    public HikvisionProbe(DeviceOptions options, AlarmEventQueue alarms)
    {
        _options = options;
        _sdkPath = Path.GetFullPath(options.SdkDirectory);
        _alarms = alarms;
        _ptzWatchdog = new Timer(_ => StopExpiredPtz(), null, 1000, 1000);
    }

    public AlarmEventQueue Alarms => _alarms;

    public ProbeResult Run()
    {
        lock (_sdkGate)
        {
            EnsureSdk();
            var userId = -1;
            try
            {
                userId = Login(out var deviceInfo);
                var channels = TryReadIsapiChannels() ?? ReadIpChannels(userId);
                PreviewResult? preview = null;
                if (_options.PreviewChannel is int channel)
                    preview = Preview(userId, channel, _options.PreviewSeconds);
                var summary = Summarize(deviceInfo);
                if (TryReadIsapiDeviceInfo() is { } isapi)
                    summary = summary with
                    {
                        SerialNumber = isapi.SerialNumber ?? summary.SerialNumber,
                        Model = isapi.Model ?? summary.Model
                    };
                return new ProbeResult(summary, channels, preview);
            }
            finally
            {
                if (userId >= 0)
                    Native.NET_DVR_Logout(userId);
                ReleaseSdkIfUnused();
            }
        }
    }

    public PtzResult Ptz(int channel, uint command, bool stop, uint speed)
    {
        lock (_sdkGate)
        {
            EnsureSdk();
            if (_controlUserId < 0)
                _controlUserId = Login(out _);
            if (_ptzCommands.TryGetValue(channel, out var previous))
            {
                if (stop) { command = previous.Command; speed = previous.Speed; }
                else if (previous.Command != command && Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, channel, previous.Command, 1, previous.Speed) == 0)
                    return new PtzResult(false, channel, command, stop, "无法停止该通道原有云台运动");
            }
            var success = Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, channel, command, stop ? 1u : 0u, speed) != 0;
            if (success)
            {
                if (stop) _ptzCommands.Remove(channel);
                else _ptzCommands[channel] = (command, speed, DateTimeOffset.UtcNow.AddSeconds(10));
            }
            return success
                ? new PtzResult(true, channel, command, stop)
                : new PtzResult(false, channel, command, stop, $"SDK 错误码：{Native.NET_DVR_GetLastError()}");
        }
    }

    public bool Preset(int channel, uint preset)
    {
        lock (_sdkGate)
        {
            EnsureSdk();
            if (_controlUserId < 0)
                _controlUserId = Login(out _);
            return Native.NET_DVR_PTZPreset_Other(_controlUserId, channel, 39, preset) != 0;
        }
    }

    private void StopExpiredPtz()
    {
        lock (_sdkGate)
        {
            foreach (var pair in _ptzCommands.Where(item => item.Value.Deadline <= DateTimeOffset.UtcNow).ToArray())
            {
                if (_controlUserId >= 0 && Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, pair.Key, pair.Value.Command, 1, pair.Value.Speed) != 0)
                    _ptzCommands.Remove(pair.Key);
                else Console.Error.WriteLine($"通道 {pair.Key} 云台保护停止失败，下次重试");
            }
        }
    }

    public void StartAlarm()
    {
        lock (_sdkGate)
        {
            EnsureSdk();
            if (_controlUserId < 0)
                _controlUserId = Login(out _);
            if (_alarmHandle >= 0)
                return;
            _alarmCallback = OnAlarm;
            if (Native.NET_DVR_SetDVRMessageCallBack_V31(_alarmCallback, IntPtr.Zero) == 0)
                throw SdkError("注册报警回调失败");
            var parameter = new Native.SetupAlarmParam
            {
                Size = (uint)Marshal.SizeOf<Native.SetupAlarmParam>(),
                AlarmInfoType = 1,
                DeployType = 1,
                Reserved1 = new byte[3]
            };
            _alarmHandle = Native.NET_DVR_SetupAlarmChan_V41(_controlUserId, ref parameter);
            if (_alarmHandle < 0)
                throw SdkError("报警布防失败");
        }
    }

    public IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        lock (_sdkGate)
        {
            EnsureSdk();
            if (_controlUserId < 0)
                _controlUserId = Login(out _);
            // NET_DVR_FILECOND_V50 的固定大小为 424 字节。
            var condition = Marshal.AllocHGlobal(424);
            try
            {
                Zero(condition, 424);
                WriteUInt32(condition, 0, 72);
                WriteUInt32(condition, 36, channel);
                WriteSearchTime(condition, 72, start);
                WriteSearchTime(condition, 84, end);
                Marshal.WriteByte(condition, 96, 0); // 按通道查询
                Marshal.WriteByte(condition, 97, 0); // 不抽帧
                Marshal.WriteByte(condition, 98, 0); // 普通查询
                Marshal.WriteByte(condition, 99, 0); // 主码流
                WriteUInt32(condition, 100, unchecked((int)0xffffffff)); // 全部录像类型
                Marshal.WriteByte(condition, 108, 0xff); // 不限制锁定状态
                WriteUInt32(condition, 168, 15000); // SDK 查询等待上限（毫秒）
                var findHandle = Native.NET_DVR_FindFile_V50(_controlUserId, condition);
                if (findHandle < 0)
                {
                    if (Native.NET_DVR_GetLastError() == 23)
                        return SearchRecordingsV40Locked(channel, start, end);
                    throw SdkError("录像查询建立失败");
                }
                try
                {
                    var results = new List<RecordingSummary>();
                    var data = Marshal.AllocHGlobal(572);
                    try
                    {
                        var searchStarted = Stopwatch.GetTimestamp();
                        while (true)
                        {
                            if (Stopwatch.GetElapsedTime(searchStarted) >= TimeSpan.FromMinutes(2))
                                throw new TimeoutException("录像查询超时");
                            Zero(data, 572);
                            var state = Native.NET_DVR_FindNextFile_V50(findHandle, data);
                            if (state == 1000)
                            {
                                results.Add(ParseRecording(data));
                                continue;
                            }
                            if (state == 1002)
                            {
                                Thread.Sleep(50);
                                continue;
                            }
                            if (state is 1001 or 1003)
                                return results;
                            throw new InvalidOperationException($"录像查询失败，SDK 状态：{state}，错误码：{Native.NET_DVR_GetLastError()}");
                        }
                        throw new TimeoutException("录像查询超时");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(data);
                    }
                }
                finally
                {
                    Native.NET_DVR_FindClose_V30(findHandle);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(condition);
            }
        }
    }

    private IReadOnlyList<RecordingSummary> SearchRecordingsV40Locked(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        var condition = Marshal.AllocHGlobal(160);
        try
        {
            Zero(condition, 160);
            WriteUInt32(condition, 0, channel);
            WriteUInt32(condition, 4, unchecked((int)0xffffffff));
            WriteUInt32(condition, 8, unchecked((int)0xffffffff));
            WriteLegacyTime(condition, 48, start);
            WriteLegacyTime(condition, 72, end);
            Marshal.WriteByte(condition, 97, 0); // 按通道查询
            Marshal.WriteByte(condition, 98, 0); // 普通查询
            Marshal.WriteByte(condition, 128, 0); // 主码流，兼容旧型号设备
            var findHandle = Native.NET_DVR_FindFile_V40(_controlUserId, condition);
            if (findHandle < 0)
            {
                var error = Native.NET_DVR_GetLastError();
                if (error is 11 or 23)
                    return SearchRecordingsV30Locked(channel, start, end);
                throw SdkError("录像查询建立失败");
            }
            try
            {
                var results = new List<RecordingSummary>();
                var data = Marshal.AllocHGlobal(324);
                try
                {
                    var searchStarted = Stopwatch.GetTimestamp();
                    while (true)
                    {
                        if (Stopwatch.GetElapsedTime(searchStarted) >= TimeSpan.FromMinutes(2))
                            throw new TimeoutException("录像查询超时");
                        Zero(data, 324);
                        var state = Native.NET_DVR_FindNextFile_V40(findHandle, data);
                        if (state == 1000)
                        {
                            results.Add(ParseRecordingV40(data));
                            continue;
                        }
                        if (state == 1002)
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                        if (state is 1001 or 1003)
                            return results;
                        throw new InvalidOperationException($"录像查询失败，SDK 状态：{state}，错误码：{Native.NET_DVR_GetLastError()}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
            }
            finally
            {
                Native.NET_DVR_FindClose_V30(findHandle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(condition);
        }
    }

    private IReadOnlyList<RecordingSummary> SearchRecordingsV30Locked(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        var condition = Marshal.AllocHGlobal(96);
        try
        {
            Zero(condition, 96);
            WriteUInt32(condition, 0, channel);
            WriteUInt32(condition, 4, unchecked((int)0xffffffff));
            WriteUInt32(condition, 8, unchecked((int)0xffffffff));
            WriteLegacyTime(condition, 48, start);
            WriteLegacyTime(condition, 72, end);
            var findHandle = Native.NET_DVR_FindFile_V30(_controlUserId, condition);
            if (findHandle < 0)
                throw SdkError("录像查询建立失败");
            try
            {
                var results = new List<RecordingSummary>();
                var data = Marshal.AllocHGlobal(188);
                try
                {
                    var searchStarted = Stopwatch.GetTimestamp();
                    while (true)
                    {
                        if (Stopwatch.GetElapsedTime(searchStarted) >= TimeSpan.FromMinutes(2))
                            throw new TimeoutException("录像查询超时");
                        Zero(data, 188);
                        var state = Native.NET_DVR_FindNextFile_V30(findHandle, data);
                        if (state == 1000)
                        {
                            results.Add(ParseRecordingV30(data));
                            continue;
                        }
                        if (state == 1002)
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                        if (state is 1001 or 1003)
                            return results;
                        throw new InvalidOperationException($"录像查询失败，SDK 状态：{state}，错误码：{Native.NET_DVR_GetLastError()}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
            }
            finally
            {
                Native.NET_DVR_FindClose_V30(findHandle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(condition);
        }
    }

    public PlaybackSummary StartPlayback(PlaybackStartRequest request)
    {
        lock (_sdkGate)
        {
            EnsureSdk();
            if (_controlUserId < 0)
                _controlUserId = Login(out _);

            var id = Guid.NewGuid();
            var recordings = SearchRecordings(request.Channel, request.Start, request.End);
            var segments = PlaybackTimeline.Normalize(recordings, request.Start, request.End);
            var root = Environment.GetEnvironmentVariable("HIK_PLAYBACK_PATH")
                ?? "/home/liteware/.local/share/video-platform/playback";
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"{id:N}.ps");
            var session = new PlaybackSession(id, request.UserId, request.Channel, request.Start, request.End, path, segments);
            _playbacks[id] = session;
            session.Callback = new Native.PlayDataCallback((_, dataType, buffer, size, _) => session.Enqueue(dataType, buffer, size));
            try
            {
                if (segments.Count == 0)
                {
                    session.EnterGap(request.Start);
                }
                else
                {
                    session.StartPublisher(_ffmpegPath, _zlmRtmpUrl);
                    if (segments[0].Start <= request.Start)
                        StartPlaybackSegment(session, 0, segments[0].Start);
                    else
                        session.EnterGap(request.Start);
                }
                return session.Summary();
            }
            catch
            {
                _playbacks.TryRemove(id, out _);
                session.Dispose();
                throw;
            }
        }
    }

    private void StartPlaybackSegment(PlaybackSession session, int index, DateTimeOffset startAt)
    {
        var segment = session.Segments[index];
        var vod = new Native.VodPara
        {
            Size = (uint)Marshal.SizeOf<Native.VodPara>(),
            StreamInfo = new Native.StreamInfo
            {
                Size = (uint)Marshal.SizeOf<Native.StreamInfo>(),
                Id = new byte[32],
                Channel = (uint)session.Channel,
                Reserved = new byte[32]
            },
            BeginTime = Native.Time.From(segment.Start),
            EndTime = Native.Time.From(segment.End),
            Window = IntPtr.Zero,
            DrawFrame = 0,
            VolumeType = 0,
            VolumeNumber = 0,
            StreamType = 0,
            FileIndex = segment.FileIndex,
            AudioFile = 0,
            CourseFile = 0,
            Download = 0,
            OptimalStreamType = 0,
            Async = 1,
            Reserved = new byte[19]
        };
        var callback = session.Callback ?? throw new InvalidOperationException("回放回调未初始化");
        var handle = Native.NET_DVR_PlayBackByTime_V40(_controlUserId, ref vod);
        if (handle < 0)
            throw SdkError("建立回放失败");
        session.Handle = handle;
        try
        {
            if (Native.NET_DVR_SetPlayDataCallBack_V40(handle, callback, IntPtr.Zero) == 0
                || Native.NET_DVR_PlayBackControl_V40(handle, 1, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero) == 0)
                throw SdkError("启动回放数据回调失败");
            session.StartSegment(index, startAt);
            if (startAt > segment.Start)
            {
                var output = 0;
                var position = SegmentPosition(segment, startAt);
                if (Native.NET_DVR_PlayBackControl(handle, 12, (uint)position, ref output) == 0)
                    throw SdkError("回放定位失败");
            }
        }
        catch
        {
            Native.NET_DVR_StopPlayBack(handle);
            session.Handle = -1;
            throw;
        }
    }

    private static int SegmentPosition(PlaybackSegment segment, DateTimeOffset value)
    {
        var duration = segment.End - segment.Start;
        return duration <= TimeSpan.Zero
            ? 0
            : Math.Clamp((int)Math.Round((value - segment.Start).TotalMilliseconds / duration.TotalMilliseconds * 100), 0, 100);
    }

    private static DateTimeOffset SegmentTime(PlaybackSegment segment, int position) =>
        segment.Start + TimeSpan.FromMilliseconds((segment.End - segment.Start).TotalMilliseconds * Math.Clamp(position, 0, 100) / 100d);

    private static void StopPlaybackHandle(PlaybackSession session)
    {
        if (session.Handle < 0)
            return;
        Native.NET_DVR_StopPlayBack(session.Handle);
        session.Handle = -1;
    }

    public PlaybackSummary? GetPlayback(Guid id, long userId)
    {
        if (!_playbacks.TryGetValue(id, out var session) || session.UserId != userId)
            return null;
        UpdatePlaybackTimeline(session);
        return session.Summary();
    }

    public PlaybackSummary? ControlPlayback(Guid id, long userId, string action, int? position = null)
    {
        if (!_playbacks.TryGetValue(id, out var session) || session.UserId != userId)
            return null;
        var normalizedAction = action?.Trim().ToLowerInvariant();
        var code = normalizedAction switch
        {
            "pause" => 3u,
            "resume" => 4u,
            "fast" => 5u,
            "slow" => 6u,
            "normal" => 7u,
            "seek" => 12u,
            _ => 0u
        };
        if (code == 0)
            throw new InvalidOperationException("不支持的回放控制命令：pause、resume、fast、slow、normal、seek");
        if (code == 12 && (position is null or < 0 or > 100))
            throw new InvalidOperationException("回放位置必须是 0 到 100");
        lock (_sdkGate)
        {
            if (code == 12)
            {
                var target = session.Start + TimeSpan.FromMilliseconds((session.End - session.Start).TotalMilliseconds * position!.Value / 100d);
                var segmentIndex = session.Segments.ToList().FindIndex(item => target >= item.Start && target < item.End);
                var targetIndex = segmentIndex >= 0 ? segmentIndex : session.Segments.TakeWhile(item => item.Start <= target).Count() - 1;
                var currentIndex = session.SegmentIndex;
                if (session.Handle >= 0 && segmentIndex >= 0 && currentIndex == segmentIndex)
                {
                    var output = 0;
                    var segmentPosition = SegmentPosition(session.Segments[segmentIndex], target);
                    if (Native.NET_DVR_PlayBackControl(session.Handle, code, (uint)segmentPosition, ref output) == 0)
                        throw SdkError("回放控制失败");
                }
                else if (session.Handle >= 0)
                {
                    StopPlaybackHandle(session);
                }
                session.SetGapSegmentIndex(targetIndex);
                session.SetCurrentTime(target);
                session.SetState(segmentIndex >= 0 ? "playing" : target >= session.End ? "completed" : "gap");
                if (session.State == "playing" && (session.Handle < 0 || currentIndex != targetIndex))
                    StartPlaybackSegment(session, targetIndex, target);
                else if (session.State == "gap" && session.Handle >= 0)
                    Native.NET_DVR_PlayBackControl_V40(session.Handle, 3, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
                else if (session.State == "completed")
                    StopPlaybackHandle(session);
            }
            else if (normalizedAction == "pause")
            {
                if (session.Handle >= 0 && Native.NET_DVR_PlayBackControl_V40(session.Handle, code, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero) == 0)
                    throw SdkError("回放控制失败");
                session.Pause();
            }
            else if (code is 5u or 6u or 7u)
            {
                if (session.Handle < 0 || Native.NET_DVR_PlayBackControl_V40(session.Handle, code, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero) == 0)
                    throw SdkError("回放控制失败");
            }
            else
            {
                var targetIndex = session.Segments.ToList().FindIndex(item => session.CurrentTime >= item.Start && session.CurrentTime < item.End);
                if (targetIndex >= 0 && (session.Handle < 0 || session.SegmentIndex != targetIndex))
                {
                    StopPlaybackHandle(session);
                    StartPlaybackSegment(session, targetIndex, session.CurrentTime);
                }
                else if (targetIndex < 0)
                {
                    StopPlaybackHandle(session);
                }
                else if (session.Handle >= 0 && targetIndex >= 0 && Native.NET_DVR_PlayBackControl_V40(session.Handle, code, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero) == 0)
                {
                    throw SdkError("回放控制失败");
                }
                session.Resume();
                if (session.State == "gap" && session.Handle >= 0)
                    Native.NET_DVR_PlayBackControl_V40(session.Handle, 3, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            }
            if (code is 5u or 6u or 7u && session.Handle < 0)
                throw SdkError("回放控制失败");
            if (session.State == "completed")
                session.CompletePublisher();
        }
        return session.Summary();
    }

    private void UpdatePlaybackTimeline(PlaybackSession session)
    {
        lock (_sdkGate)
        {
            if (session.State == "completed")
                return;
            var previous = session.State;
            var previousSegment = session.SegmentIndex;
            session.Advance(DateTimeOffset.UtcNow);
            if (previous == "playing" && session.State == "gap" && session.Handle >= 0)
                Native.NET_DVR_PlayBackControl_V40(session.Handle, 3, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            else if (session.State == "playing" && (previous != "playing" || session.SegmentIndex != previousSegment || session.Handle < 0))
            {
                if (previous != "playing" || session.SegmentIndex != previousSegment)
                    StopPlaybackHandle(session);
                StartPlaybackSegment(session, session.SegmentIndex, session.CurrentTime);
            }
            if (session.State == "completed")
            {
                if (session.Handle >= 0)
                    Native.NET_DVR_PlayBackControl_V40(session.Handle, 3, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
                StopPlaybackHandle(session);
                session.CompletePublisher();
            }
        }
    }

    public bool StopPlayback(Guid id, long userId)
    {
        if (!_playbacks.TryGetValue(id, out var existing) || existing.UserId != userId || !_playbacks.TryRemove(id, out var session))
            return false;
        lock (_sdkGate)
        {
            if (session.Handle >= 0)
                Native.NET_DVR_StopPlayBack(session.Handle);
            session.Handle = -1;
        }
        session.State = "stopped";
        session.Dispose();
        return true;
    }

    public LiveSummary StartLive(int channel, int requestedStreamType, Guid? sessionId = null)
    {
        if (channel <= 0 || requestedStreamType is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(channel), "实时预览参数无效");
        lock (_liveGate)
        {
            if (sessionId is { } existingId && _liveSessionKeys.TryGetValue(existingId, out var existing))
                return new LiveSummary(existing.Channel, LiveStreamName(existing.Channel, existing.StreamType), existing.StreamType, _liveReferences[existing]);
            foreach (var streamType in requestedStreamType == 2 ? new[] { 2, 1 } : new[] { 1 })
            {
                var key = (channel, streamType);
                if (_liveReferences.TryGetValue(key, out var references))
                {
                    _liveReferences[key] = references + 1;
                    if (sessionId is { } id) _liveSessionKeys[id] = key;
                    return new LiveSummary(channel, LiveStreamName(channel, streamType), streamType, references + 1);
                }
                try
                {
                    AddLiveProxy(channel, streamType);
                    _liveReferences[key] = 1;
                    if (sessionId is { } id) _liveSessionKeys[id] = key;
                    return new LiveSummary(channel, LiveStreamName(channel, streamType), streamType, 1);
                }
                catch when (requestedStreamType == 2 && streamType == 2)
                {
                }
            }
            throw new InvalidOperationException("ZLMediaKit 建立实时代理失败：设备码流不可用");
        }
    }

    public bool StopLive(int channel, int streamType, Guid? sessionId = null)
    {
        lock (_liveGate)
        {
            var key = (channel, streamType);
            if (sessionId is { } id && !_liveSessionKeys.TryGetValue(id, out key)) return true;
            if (!_liveReferences.TryGetValue(key, out var references))
                return false;
            if (references > 1)
            {
                _liveReferences[key] = references - 1;
                if (sessionId is { } removedId) _liveSessionKeys.Remove(removedId);
                return true;
            }
            DeleteLiveProxy(key.channel, key.streamType);
            _liveReferences.Remove(key);
            if (sessionId is { } lastId) _liveSessionKeys.Remove(lastId);
            return true;
        }
    }

    private static string LiveStreamName(int channel, int streamType) => $"ch{channel}_{(streamType == 1 ? "main" : "sub")}";

    private void AddLiveProxy(int channel, int streamType)
    {
        if (string.IsNullOrWhiteSpace(_zlmApiSecret))
            throw new InvalidOperationException("ZLMediaKit 管理密钥未配置");
        var stream = LiveStreamName(channel, streamType);
        var suffix = streamType == 1 ? "01" : "02";
        try
        {
            var sourceChannel = $"{channel}{suffix}";
            var origin = $"rtsp://{Uri.EscapeDataString(_options.Username)}:{Uri.EscapeDataString(_options.Password)}@{_options.DeviceIp}:554/Streaming/Channels/{sourceChannel}";
            var uri = $"{_zlmApiUrl.TrimEnd('/')}/index/api/addStreamProxy?secret={Uri.EscapeDataString(_zlmApiSecret)}&vhost=__defaultVhost__&app=live&stream={stream}&enable_rtsp=1&enable_hls=1&url={Uri.EscapeDataString(origin)}";
            using var document = JsonDocument.Parse(_httpClient.GetStringAsync(uri).GetAwaiter().GetResult());
            var code = document.RootElement.GetProperty("code").GetInt32();
            if (code == 0)
                return;
            var message = document.RootElement.TryGetProperty("msg", out var messageValue) ? messageValue.GetString() : null;
            throw new InvalidOperationException(message ?? "设备码流不可用");
        }
        catch
        {
            DeleteLiveProxy(channel, streamType);
            throw;
        }
    }

    private void DeleteLiveProxy(int channel, int streamType)
    {
        if (string.IsNullOrWhiteSpace(_zlmApiSecret))
            return;
        var key = Uri.EscapeDataString($"__defaultVhost__/live/{LiveStreamName(channel, streamType)}");
        var uri = $"{_zlmApiUrl.TrimEnd('/')}/index/api/delStreamProxy?secret={Uri.EscapeDataString(_zlmApiSecret)}&key={key}";
        try { _httpClient.GetStringAsync(uri).GetAwaiter().GetResult(); } catch { }
    }

    private static void WriteSearchTime(IntPtr pointer, int offset, DateTimeOffset value)
    {
        var local = value.ToOffset(TimeSpan.FromHours(8));
        Marshal.WriteInt16(pointer, offset, (short)local.Year);
        Marshal.WriteByte(pointer, offset + 2, (byte)local.Month);
        Marshal.WriteByte(pointer, offset + 3, (byte)local.Day);
        Marshal.WriteByte(pointer, offset + 4, (byte)local.Hour);
        Marshal.WriteByte(pointer, offset + 5, (byte)local.Minute);
        Marshal.WriteByte(pointer, offset + 6, (byte)local.Second);
        Marshal.WriteByte(pointer, offset + 7, 0); // 使用设备本地时间
        Marshal.WriteInt16(pointer, offset + 8, (short)local.Millisecond);
        Marshal.WriteByte(pointer, offset + 10, 8);
        Marshal.WriteByte(pointer, offset + 11, 0);
    }

    private static void WriteLegacyTime(IntPtr pointer, int offset, DateTimeOffset value)
    {
        var local = value.ToOffset(TimeSpan.FromHours(8));
        Marshal.WriteInt32(pointer, offset, local.Year);
        Marshal.WriteInt32(pointer, offset + 4, local.Month);
        Marshal.WriteInt32(pointer, offset + 8, local.Day);
        Marshal.WriteInt32(pointer, offset + 12, local.Hour);
        Marshal.WriteInt32(pointer, offset + 16, local.Minute);
        Marshal.WriteInt32(pointer, offset + 20, local.Second);
    }

    private static RecordingSummary ParseRecording(IntPtr pointer)
    {
        var fileName = Decode(pointer, 0, 100);
        var start = ReadSearchTime(pointer, 100);
        var end = ReadSearchTime(pointer, 112);
        var fileSize = unchecked((long)(uint)Marshal.ReadInt32(pointer, 272));
        var high = unchecked((uint)Marshal.ReadInt32(pointer, 344));
        var low = unchecked((uint)Marshal.ReadInt32(pointer, 348));
        if (high != 0 || low != 0)
            fileSize = (long)(((ulong)high << 32) | low);
        return new RecordingSummary(fileName, start, end, fileSize, Marshal.ReadByte(pointer, 277), Marshal.ReadByte(pointer, 279), unchecked((uint)Marshal.ReadInt32(pointer, 280)));
    }

    private static DateTimeOffset ReadSearchTime(IntPtr pointer, int offset)
    {
        var year = Marshal.ReadInt16(pointer, offset);
        var month = Marshal.ReadByte(pointer, offset + 2);
        var day = Marshal.ReadByte(pointer, offset + 3);
        var hour = Marshal.ReadByte(pointer, offset + 4);
        var minute = Marshal.ReadByte(pointer, offset + 5);
        var second = Marshal.ReadByte(pointer, offset + 6);
        var localOrUtc = Marshal.ReadByte(pointer, offset + 7) != 0;
        var milliseconds = Marshal.ReadInt16(pointer, offset + 8);
        var hours = localOrUtc ? (sbyte)Marshal.ReadByte(pointer, offset + 10) : (sbyte)8;
        var minutes = localOrUtc ? (sbyte)Marshal.ReadByte(pointer, offset + 11) : (sbyte)0;
        try { return new DateTimeOffset(year, month, day, hour, minute, second, milliseconds, new TimeSpan(hours, minutes, 0)); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MinValue; }
    }

    private static RecordingSummary ParseRecordingV40(IntPtr pointer)
    {
        var fileName = Decode(pointer, 0, 100);
        var start = ReadLegacyTime(pointer, 100);
        var end = ReadLegacyTime(pointer, 124);
        var fileSize = unchecked((long)(uint)Marshal.ReadInt32(pointer, 148));
        return new RecordingSummary(fileName, start, end, fileSize, Marshal.ReadByte(pointer, 185), Marshal.ReadByte(pointer, 192), unchecked((uint)Marshal.ReadInt32(pointer, 188)));
    }

    private static RecordingSummary ParseRecordingV30(IntPtr pointer)
    {
        var fileName = Decode(pointer, 0, 100);
        var start = ReadLegacyTime(pointer, 100);
        var end = ReadLegacyTime(pointer, 124);
        var fileSize = unchecked((long)(uint)Marshal.ReadInt32(pointer, 148));
        return new RecordingSummary(fileName, start, end, fileSize, Marshal.ReadByte(pointer, 185), 0, 0);
    }

    private static DateTimeOffset ReadLegacyTime(IntPtr pointer, int offset)
    {
        var year = Marshal.ReadInt32(pointer, offset);
        var month = Marshal.ReadInt32(pointer, offset + 4);
        var day = Marshal.ReadInt32(pointer, offset + 8);
        var hour = Marshal.ReadInt32(pointer, offset + 12);
        var minute = Marshal.ReadInt32(pointer, offset + 16);
        var second = Marshal.ReadInt32(pointer, offset + 20);
        try { return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(8)); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MinValue; }
    }

    private int OnAlarm(int command, IntPtr alarmer, IntPtr alarmInfo, uint bufferLength, IntPtr user)
    {
        AlarmEventDetails? details = null;
        try
        {
            details = AlarmParser.Parse(command, alarmInfo, bufferLength);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"报警解析失败，已保留原始报文：{ex.Message}");
        }
        var length = (int)Math.Min(bufferLength, 4 * 1024 * 1024);
        var payload = new byte[length];
        if (length > 0 && alarmInfo != IntPtr.Zero)
            Marshal.Copy(alarmInfo, payload, 0, length);
        _alarms.Enqueue(new AdapterAlarmEvent(DateTimeOffset.UtcNow, command, payload, details));
        return 1;
    }

    public void Dispose()
    {
        _ptzWatchdog.Dispose();
        lock (_sdkGate)
        {
            lock (_liveGate)
            {
                foreach (var live in _liveReferences.Keys.ToArray())
                    DeleteLiveProxy(live.Channel, live.StreamType);
                _liveReferences.Clear();
                _liveSessionKeys.Clear();
            }
            foreach (var id in _playbacks.Keys.ToArray())
                if (_playbacks.TryRemove(id, out var session))
                {
                    if (session.Handle >= 0)
                        Native.NET_DVR_StopPlayBack(session.Handle);
                    session.Handle = -1;
                    session.Dispose();
                }
            if (_alarmHandle >= 0)
            {
                Native.NET_DVR_CloseAlarmChan_V30(_alarmHandle);
                _alarmHandle = -1;
            }
            if (_controlUserId >= 0)
            {
                foreach (var movement in _ptzCommands)
                    Native.NET_DVR_PTZControlWithSpeed_Other(_controlUserId, movement.Key, movement.Value.Command, 1, movement.Value.Speed);
                _ptzCommands.Clear();
                Native.NET_DVR_Logout(_controlUserId);
                _controlUserId = -1;
            }
            ReleaseSdkIfUnused();
        }
    }

    private void EnsureSdk()
    {
        if (_sdkInitialized)
            return;
        var library = Path.Combine(_sdkPath, "libhcnetsdk.so");
        if (!File.Exists(library))
            throw new FileNotFoundException($"找不到 HCNetSDK：{library}");
        ConfigureSdk(library);
        if (Native.NET_DVR_Init() == 0)
            throw SdkError("HCNetSDK 初始化失败");
        _sdkInitialized = true;
        _exceptionCallback = OnException;
        if (Native.NET_DVR_SetExceptionCallBack_V30(0, IntPtr.Zero, _exceptionCallback, IntPtr.Zero) == 0)
            Console.Error.WriteLine("设备异常回调注册失败，继续使用报警回调");
    }

    private void OnException(uint type, int userId, int handle, IntPtr user)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), type);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), userId);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), handle);
        _alarms.Enqueue(new AdapterAlarmEvent(
            DateTimeOffset.UtcNow,
            0x2001,
            payload,
            new AlarmEventDetails("device.exception", type, Array.Empty<int>(), null, null, null, null)));
    }

    private void ReleaseSdkIfUnused()
    {
        if (_controlUserId < 0 && _sdkInitialized)
        {
            Native.NET_DVR_Cleanup();
            _sdkInitialized = false;
        }
    }

    private void ConfigureSdk(string library)
    {
        // 原生库通过 LD_LIBRARY_PATH 找到同目录依赖；这里仅配置 SDK 组件目录。
        var sdkPath = new Native.SdkPath
        {
            Path = EncodeFixed(_sdkPath, 256),
            Reserved = new byte[128]
        };
        var sdkPathPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Native.SdkPath>());
        try
        {
            Marshal.StructureToPtr(sdkPath, sdkPathPtr, false);
            if (Native.NET_DVR_SetSDKInitCfg(2, sdkPathPtr) == 0)
                throw SdkError("设置 HCNetSDKCom 路径失败");
        }
        finally
        {
            Marshal.FreeHGlobal(sdkPathPtr);
        }

        SetLibraryPath(3, Path.Combine(_sdkPath, "libcrypto.so.3"));
        SetLibraryPath(4, Path.Combine(_sdkPath, "libssl.so.3"));
    }

    private static void SetLibraryPath(int type, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"缺少 SDK 依赖库：{path}");
        var ptr = Marshal.StringToCoTaskMemUTF8(path);
        try
        {
            if (Native.NET_DVR_SetSDKInitCfg(type, ptr) == 0)
                throw SdkError($"设置 SDK 依赖库路径失败：{Path.GetFileName(path)}");
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    private int Login(out byte[] deviceInfo)
    {
        var login = new Native.UserLoginInfo
        {
            DeviceAddress = EncodeFixed(_options.DeviceIp, 129),
            Port = _options.DevicePort,
            Username = EncodeFixed(_options.Username, 64),
            Password = EncodeFixed(_options.Password, 64),
            LoginCallback = IntPtr.Zero,
            User = IntPtr.Zero,
            UseAsyncLogin = 0,
            ProxyType = 0,
            UseUtcTime = 0,
            LoginMode = 0,
            Https = 0,
            ProxyId = 0,
            VerifyMode = 0,
            Reserved = new byte[119]
        };
        var loginPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Native.UserLoginInfo>());
        var devicePtr = Marshal.AllocHGlobal(344);
        try
        {
            Marshal.StructureToPtr(login, loginPtr, false);
            Zero(devicePtr, 344);
            var userId = Native.NET_DVR_Login_V40(loginPtr, devicePtr);
            if (userId < 0)
                throw SdkError("设备登录失败");
            deviceInfo = new byte[344];
            Marshal.Copy(devicePtr, deviceInfo, 0, deviceInfo.Length);
            return userId;
        }
        finally
        {
            Marshal.FreeHGlobal(loginPtr);
            Marshal.FreeHGlobal(devicePtr);
        }
    }

    private DeviceSummary Summarize(byte[] data)
    {
        var serial = Decode(data, 0, 48);
        var analogChannels = data[52];
        var digitalChannels = data[55] + (data[68] << 8);
        return new DeviceSummary(
            serial,
            analogChannels,
            digitalChannels,
            data[66],
            data[50],
            data[48],
            data[49],
            data[57] is 1 or 2 || data[58] is 1 or 2,
            Environment.GetEnvironmentVariable("HIK_DEVICE_MODEL"),
            _options.DeviceIp,
            _options.DevicePort);
    }

    private IsapiDeviceInfo? TryReadIsapiDeviceInfo()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                PreAuthenticate = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync($"http://{_options.DeviceIp}/ISAPI/System/deviceInfo")
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;
            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var root = XDocument.Parse(xml);
            static string? Value(XDocument document, string name) =>
                document.Descendants().FirstOrDefault(item => item.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;
            return new IsapiDeviceInfo(Value(root, "model"), Value(root, "serialNumber"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Console.Error.WriteLine($"ISAPI 设备信息同步失败：{ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<ChannelSummary>? TryReadIsapiChannels()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                PreAuthenticate = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync($"http://{_options.DeviceIp}/ISAPI/ContentMgmt/InputProxy/channels")
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var channels = XDocument.Parse(xml)
                .Descendants()
                .Where(element => element.Name.LocalName == "InputProxyChannel")
                .Select(element =>
                {
                    static string? Value(XElement parent, string name) =>
                        parent.Elements().FirstOrDefault(item => item.Name.LocalName == name)?.Value;

                    var source = element.Elements().FirstOrDefault(item => item.Name.LocalName == "sourceInputPortDescriptor");
                    var online = bool.TryParse(Value(element, "online"), out var onlineValue) && onlineValue;
                    var name = Value(element, "name");
                    var model = source is null ? null : Value(source, "model");
                    var capabilityValue = element.Descendants().FirstOrDefault(item => item.Name.LocalName is "ptz" or "ptzCapable")?.Value;
                    var ptzCapable = bool.TryParse(capabilityValue, out var capability)
                        ? capability
                        : LooksLikePtz(model) || LooksLikePtz(name);
                    return new ChannelSummary(
                        int.TryParse(Value(element, "id"), out var id) ? id : 0,
                        online,
                        0,
                        name,
                        model,
                        online,
                        ptzCapable);
                })
                .Where(channel => channel.ChannelNumber > 0)
                .OrderBy(channel => channel.ChannelNumber)
                .ToArray();
            return channels.Length == 0 ? null : channels;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Console.Error.WriteLine($"ISAPI 通道同步失败，回退 HCNetSDK：{ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<ChannelSummary> ReadIpChannels(int userId)
    {
        var buffer = Marshal.AllocHGlobal(IpParaConfigSize);
        var returned = 0u;
        try
        {
            Zero(buffer, IpParaConfigSize);
            WriteUInt32(buffer, 0, IpParaConfigSize);
            if (Native.NET_DVR_GetDVRConfig(userId, GetIpParaConfigV40, 0, buffer, IpParaConfigSize, ref returned) == 0)
                throw SdkError("读取 IP 通道配置失败");

            var count = (int)Math.Min(ReadUInt32(buffer, 12), 64);
            var startChannel = ReadUInt32(buffer, 16);
            var channels = new List<ChannelSummary>(count);
            for (var index = 0; index < count; index++)
            {
                var offset = StreamModeOffset + index * StreamModeSize;
                channels.Add(new ChannelSummary(
                    (int)startChannel + index,
                    Marshal.ReadByte(buffer, offset + 4) != 0,
                    Marshal.ReadByte(buffer, offset)));
            }
            return channels;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool LooksLikePtz(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("ptz", StringComparison.OrdinalIgnoreCase)
            || value.Contains("dome", StringComparison.OrdinalIgnoreCase)
            || value.Contains("2dc", StringComparison.OrdinalIgnoreCase)
            || value.Contains("球机", StringComparison.OrdinalIgnoreCase)
            || value.Contains("云台", StringComparison.OrdinalIgnoreCase));

    private PreviewResult Preview(int userId, int channel, int seconds)
    {
        var preview = new Native.PreviewInfo
        {
            Channel = (uint)channel,
            StreamType = 1,
            LinkMode = 0,
            PlayWindow = 0,
            Blocked = 1,
            PassbackRecord = 0,
            PreviewMode = 0,
            StreamId = new byte[32],
            ProtocolType = 0,
            Reserved1 = 0,
            VideoCodingType = 0,
            DisplayBufferCount = 0,
            NpqMode = 0,
            ReceiveMetadata = 0,
            DataType = 0,
            Reserved = new byte[213]
        };
        var callbackCount = 0;
        long receivedBytes = 0;
        var dataTypes = new Dictionary<uint, int>();
        Native.RealDataCallback callback = (_, dataType, _, size, _) =>
        {
            Interlocked.Increment(ref callbackCount);
            Interlocked.Add(ref receivedBytes, size);
            lock (dataTypes)
                dataTypes[dataType] = dataTypes.GetValueOrDefault(dataType) + 1;
        };

        var handle = Native.NET_DVR_RealPlay_V40(userId, ref preview, callback, IntPtr.Zero);
        if (handle < 0)
            throw SdkError($"通道 {channel} 开始预览失败");
        try
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
        finally
        {
            Native.NET_DVR_StopRealPlay(handle);
        }
        return new PreviewResult(channel, callbackCount, receivedBytes, dataTypes);
    }

    private static Exception SdkError(string message) =>
        new InvalidOperationException($"{message}，SDK 错误码：{Native.NET_DVR_GetLastError()}");

    private static byte[] EncodeFixed(string value, int size)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= size)
            throw new ArgumentException($"字段长度超过限制：{size - 1} 字节");
        var result = new byte[size];
        bytes.CopyTo(result, 0);
        return result;
    }

    private static string Decode(byte[] data, int offset, int length)
    {
        var end = Array.IndexOf(data, (byte)0, offset, length);
        if (end < 0)
            end = offset + length;
        return Encoding.UTF8.GetString(data, offset, end - offset);
    }

    private static void Zero(IntPtr pointer, int size) =>
        NativeMethods.memset(pointer, 0, (nuint)size);

    private static uint ReadUInt32(IntPtr pointer, int offset) =>
        unchecked((uint)Marshal.ReadInt32(pointer, offset));

    private static string Decode(IntPtr pointer, int offset, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(IntPtr.Add(pointer, offset), bytes, 0, length);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? length : end).Trim();
    }

    private static void WriteUInt32(IntPtr pointer, int offset, int value) =>
        Marshal.WriteInt32(pointer, offset, value);

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct Time
        {
            public uint Year;
            public uint Month;
            public uint Day;
            public uint Hour;
            public uint Minute;
            public uint Second;

            public static Time From(DateTimeOffset value)
            {
                var local = value.ToLocalTime();
                return new Time
                {
                    Year = (uint)local.Year,
                    Month = (uint)local.Month,
                    Day = (uint)local.Day,
                    Hour = (uint)local.Hour,
                    Minute = (uint)local.Minute,
                    Second = (uint)local.Second
                };
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct StreamInfo
        {
            public uint Size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Id;
            public uint Channel;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct VodPara
        {
            public uint Size;
            public StreamInfo StreamInfo;
            public Time BeginTime;
            public Time EndTime;
            public IntPtr Window;
            public byte DrawFrame;
            public byte VolumeType;
            public byte VolumeNumber;
            public byte StreamType;
            public uint FileIndex;
            public byte AudioFile;
            public byte CourseFile;
            public byte Download;
            public byte OptimalStreamType;
            public byte Async;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct SdkPath
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Path;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct UserLoginInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)] public byte[] DeviceAddress;
            public byte UseTransport;
            public ushort Port;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Username;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Password;
            public IntPtr LoginCallback;
            public IntPtr User;
            public uint UseAsyncLogin;
            public byte ProxyType;
            public byte UseUtcTime;
            public byte LoginMode;
            public byte Https;
            public uint ProxyId;
            public byte VerifyMode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 119)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct PreviewInfo
        {
            public uint Channel;
            public uint StreamType;
            public uint LinkMode;
            public uint PlayWindow;
            public uint Blocked;
            public uint PassbackRecord;
            public byte PreviewMode;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] StreamId;
            public byte ProtocolType;
            public byte Reserved1;
            public byte VideoCodingType;
            public uint DisplayBufferCount;
            public byte NpqMode;
            public byte ReceiveMetadata;
            public byte DataType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 213)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct SetupAlarmParam
        {
            public uint Size;
            public byte Level;
            public byte AlarmInfoType;
            public byte RetAlarmTypeV40;
            public byte RetDevInfoVersion;
            public byte RetVqdAlarmType;
            public byte FaceAlarmDetection;
            public byte Support;
            public byte BrokenNetHttp;
            public ushort TaskNo;
            public byte DeployType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] Reserved1;
            public byte AlarmTypeUrl;
            public byte CustomCtrl;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void RealDataCallback(int handle, uint dataType, IntPtr buffer, uint size, IntPtr user);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int MessageCallback(int command, IntPtr alarmer, IntPtr alarmInfo, uint bufferLength, IntPtr user);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ExceptionCallback(uint type, int userId, int handle, IntPtr user);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void PlayDataCallback(int handle, uint dataType, IntPtr buffer, uint size, IntPtr user);

        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_SetSDKInitCfg(int type, IntPtr buffer);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_Init();
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_Cleanup();
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_Login_V40(IntPtr loginInfo, IntPtr deviceInfo);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_Logout(int userId);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint NET_DVR_GetLastError();
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_GetDVRConfig(int userId, int command, int channel, IntPtr output, int outputSize, ref uint returned);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_RealPlay_V40(int userId, ref PreviewInfo preview, RealDataCallback callback, IntPtr user);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_StopRealPlay(int handle);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_PTZControlWithSpeed_Other(int userId, int channel, uint command, uint stop, uint speed);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_PTZPreset_Other(int userId, int channel, uint command, uint preset);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_SetDVRMessageCallBack_V31(MessageCallback callback, IntPtr user);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_SetExceptionCallBack_V30(uint reserved1, IntPtr reserved2, ExceptionCallback callback, IntPtr user);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_SetupAlarmChan_V41(int userId, ref SetupAlarmParam parameter);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_CloseAlarmChan_V30(int alarmHandle);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindFile_V50(int userId, IntPtr condition);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindFile_V40(int userId, IntPtr condition);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindFile_V30(int userId, IntPtr condition);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindNextFile_V50(int findHandle, IntPtr data);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindNextFile_V40(int findHandle, IntPtr data);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindNextFile_V30(int findHandle, IntPtr data);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_FindClose_V30(int findHandle);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_PlayBackByTime_V40(int userId, ref VodPara vodPara);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_SetPlayDataCallBack_V40(int playHandle, PlayDataCallback callback, IntPtr user);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_PlayBackControl_V40(int playHandle, uint controlCode, IntPtr input, uint inputLength, IntPtr output, IntPtr outputLength);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_PlayBackControl(int playHandle, uint controlCode, uint input, ref int output);
        [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NET_DVR_StopPlayBack(int playHandle);
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "memset", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr memset(IntPtr destination, int value, nuint count);
    }
}

internal sealed record ProbeResult(
    DeviceSummary Device,
    IReadOnlyList<ChannelSummary> IpChannels,
    PreviewResult? Preview);

internal sealed record DeviceSummary(
    string SerialNumber,
    int AnalogChannels,
    int DigitalChannels,
    int DigitalStartChannel,
    int DiskCount,
    int AlarmInputCount,
    int AlarmOutputCount,
    bool SupportsRtsp,
    string? Model = null,
    string? Ip = null,
    int ServicePort = 8000);

internal sealed record ChannelSummary(
    int ChannelNumber,
    bool Enabled,
    int StreamType,
    string? Name = null,
    string? Model = null,
    bool? Online = null,
    bool PtzCapable = false);

internal sealed record IsapiDeviceInfo(string? Model, string? SerialNumber);

internal sealed record PreviewResult(
    int Channel,
    int Callbacks,
    long Bytes,
    IReadOnlyDictionary<uint, int> DataTypes);
