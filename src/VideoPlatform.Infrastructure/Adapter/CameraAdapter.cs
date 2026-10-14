using System.Text.Json;
using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure.Jobs;

namespace VideoPlatform.Infrastructure.Adapter;

// ════════════════════════════════════════════════════════════════════════
// 摄像机适配器端口（P1 阶段 2）
//
// 设计意图：把"泛型 JSON 透传 + 调用点手写字段映射"替换为**类型化方法**。
// 厂商路由（plugin_id → endpoint_url）、禁用插件 409、esight_pon 短路、
// /internal/sessions 跨插件扇出、错误码映射等行为**全部保留在 IDeviceAdapter 中**，
// 本端口只是其上的类型化外观（见 HttpCameraAdapter）。
//
// 三个刻意的设计决定：
//  1) 端口放在 Infrastructure 而非 Application —— 因为解析器依赖
//     Infrastructure 的 JsonDefaults 行访问扩展，且 AdapterContracts 的既有严格
//     解析器位于此层；上移会把 JsonDefaults 一并拖动，波及约 8 个 Api 文件与大量
//     Infrastructure 文件，收益为零。已核实的既有分层现实：Api 本就大量依赖 Infrastructure。
//  2) 复用而非重建既有严格解析器：AdapterInventory / AdapterExportStatus /
//     AdapterAlarmBatch 保持定义在 Jobs/AdapterContracts.cs，直接调用。
//  3) 记录（RecordingDto）直接复用 Contracts 中的公开定义 —— 已核实其 7 个字段与
//     适配器 RecordingSummary **逐字段相同**，端口据此顺带修好一处"声明了契约却未校验"。
//
// 解析严格度（逐端点显式声明，勿随意更改）：
//  严格（抛 InvalidDataException，中止本轮）：
//     /internal/sessions、/events、/exports*、/recordings/search
//  宽松（容忍缺字段，用默认值/兜底）：
//     /sync、/streams/probe、/live、/playback*、/health
//  说明：严格端点若被静默容错，会污染资源回收与报警入库；宽松端点若改为严格，
//        会让"设备没有子码流"这类**合法**结果变成异常。
// ════════════════════════════════════════════════════════════════════════

/// <summary>类型化摄像机适配器端口。所有方法在失败时抛出与既有实现一致的 PlatformException。</summary>
public interface ICameraAdapter
{
    // ── 设备生命周期 ────────────────────────────────────────────────────
    /// <summary>注册/更新设备到适配器（PUT /internal/devices/{id}）。返回值不被调用方使用。</summary>
    Task RegisterAsync(long deviceId, AdapterDeviceSpec spec, CancellationToken ct = default);
    Task DeregisterAsync(long deviceId, CancellationToken ct = default);
    Task<DeviceSnapshot> SyncAsync(long deviceId, CancellationToken ct = default);
    Task<AdapterHealth> HealthAsync(CancellationToken ct = default);

    // ── 码流能力 ────────────────────────────────────────────────────────
    Task<StreamCapability> ProbeStreamAsync(long deviceId, int channel, int streamType, CancellationToken ct = default);

    // ── 实时 ────────────────────────────────────────────────────────────
    Task<LiveSummary> StartLiveAsync(long deviceId, LiveStartSpec spec, CancellationToken ct = default);
    Task StopLiveAsync(long deviceId, Guid sessionId, CancellationToken ct = default);
    /// <summary>资源清单（GET /internal/sessions）。跨插件扇出合并，无 deviceId。</summary>
    Task<AdapterInventory> SessionsAsync(CancellationToken ct = default);

    // ── 回放 ────────────────────────────────────────────────────────────
    Task<IReadOnlyList<RecordingDto>> SearchRecordingsAsync(long deviceId, int channel, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
    Task<PlaybackSummary> StartPlaybackAsync(long deviceId, PlaybackStartSpec spec, CancellationToken ct = default);
    Task<PlaybackSummary> GetPlaybackAsync(long deviceId, Guid sessionId, CancellationToken ct = default);
    Task<PlaybackSummary?> ControlPlaybackAsync(long deviceId, Guid sessionId, PlaybackControlSpec spec, CancellationToken ct = default);
    Task StopPlaybackAsync(long deviceId, Guid sessionId, CancellationToken ct = default);

    // ── 云台 ────────────────────────────────────────────────────────────
    Task PtzAsync(long deviceId, int channel, string command, uint speed, bool stop, CancellationToken ct = default);
    Task PresetAsync(long deviceId, int channel, uint preset, CancellationToken ct = default);

    // ── 导出 ────────────────────────────────────────────────────────────
    Task<AdapterExportStatus> StartExportAsync(long deviceId, ExportSpec spec, CancellationToken ct = default);
    Task<AdapterExportStatus> GetExportAsync(long deviceId, Guid jobId, CancellationToken ct = default);
    Task<AdapterExportStatus> CancelExportAsync(long deviceId, Guid jobId, CancellationToken ct = default);

    // ── 报警 ────────────────────────────────────────────────────────────
    Task<AdapterAlarmBatch> EventsAsync(long deviceId, string previousCursor, int limit, DateTimeOffset receivedAt, CancellationToken ct = default);
    Task AckEventsAsync(long deviceId, string cursor, CancellationToken ct = default);
}

// ════════════════════════════════════════════════════════════════════════
// 请求规格：字段名必须与适配器 Contracts.cs 中的记录一一对应（JSON 为 camelCase）。
// 改动此处等于改动适配器契约，需同步两侧。
// ════════════════════════════════════════════════════════════════════════

/// <summary>对应适配器 DeviceRegistration。Port 用 int 而非 ushort，避免调用方反复转换（JSON 同为数字）。</summary>
public sealed record AdapterDeviceSpec(string Host, int Port, string Username, string Password, bool Enabled);

/// <summary>对应适配器 LiveStartRequest。</summary>
public sealed record LiveStartSpec(Guid SessionId, int Channel, int StreamType, string Profile);

/// <summary>对应适配器 PlaybackStartRequest。</summary>
public sealed record PlaybackStartSpec(Guid SessionId, long UserId, int Channel, DateTimeOffset Start, DateTimeOffset End, string Profile, int StreamType);

/// <summary>对应适配器 PlaybackControlRequest。</summary>
public sealed record PlaybackControlSpec(string Action, DateTimeOffset? Position = null, double? Speed = null);

/// <summary>对应适配器 ExportRequest。</summary>
public sealed record ExportSpec(Guid JobId, int Channel, DateTimeOffset Start, DateTimeOffset End, string OutputDirectory);

// ════════════════════════════════════════════════════════════════════════
// 响应模型与解析
// ════════════════════════════════════════════════════════════════════════

public sealed record AdapterChannelInfo(int Channel, string? Name = null, string? Model = null, bool Online = false, bool PtzCapable = false, string? Codec = null);

public sealed record AdapterDeviceSummary(
    string? SerialNumber = null, int AnalogChannels = 0, int DigitalChannels = 0, int DigitalStartChannel = 0,
    int DiskCount = 0, int AlarmInputCount = 0, int AlarmOutputCount = 0, bool SupportsRtsp = false,
    string? Model = null, string? Ip = null, int ServicePort = 8000);

/// <summary>设备同步/注册后的通道快照。形状校验失败时抛既有错误码，保持 502 语义不变。</summary>
public sealed record DeviceSnapshot(AdapterDeviceSummary Device, IReadOnlyList<AdapterChannelInfo> Channels)
{
    public static DeviceSnapshot Parse(JsonNode? value)
    {
        if (value is not JsonObject root)
            throw new PlatformException(502, "device.snapshot", "设备未返回有效通道快照");
        // 与既有 DeviceService.SyncAsync 的 Rules.Require 完全等效（同为 502/device.snapshot）。
        if (root["channels"] is not JsonArray channels)
            throw new PlatformException(502, "device.snapshot", "设备返回的通道快照格式无效");
        var device = root["device"] is JsonNode node ? AdapterJson.Deserialize<AdapterDeviceSummary>(node) ?? new AdapterDeviceSummary() : new AdapterDeviceSummary();
        var list = new List<AdapterChannelInfo>(channels.Count);
        foreach (var item in channels)
            if (item is not null && AdapterJson.Deserialize<AdapterChannelInfo>(item) is { } info) list.Add(info);
        return new(device, list);
    }
}

public sealed record StreamCapability(bool Available, string? Codec = null, int? Width = null, int? Height = null, int? BitrateKbps = null, string? Error = null)
{
    /// <summary>宽松：探测失败/缺字段是合法结果（"设备确认无此档位"），不得抛错。</summary>
    public static StreamCapability Parse(JsonNode? value)
        => value is null ? new(false, Error: "适配器未返回探测结果") : AdapterJson.Deserialize<StreamCapability>(value) ?? new(false, Error: "适配器未返回探测结果");
}

public sealed record LiveSummary(string Stream, int StreamType = 0, string? Codec = null, bool Transcoded = false, int? Width = null, int? Height = null, int? BitrateKbps = null)
{
    /// <summary>宽松：缺 stream 时由调用方按既有 502 media.stream 语义处理。</summary>
    public static LiveSummary Parse(JsonNode? value)
        => value is null ? new("") : AdapterJson.Deserialize<LiveSummary>(value) ?? new("");
}

public sealed record PlaybackSegment(DateTimeOffset Start, DateTimeOffset End, string? FileName = null, long FileSize = 0, int FileType = 0, int StreamType = 0, uint FileIndex = 0);

public sealed record PlaybackSummary(
    Guid Id = default, string Stream = "", string State = "", DateTimeOffset Start = default, DateTimeOffset End = default,
    DateTimeOffset CurrentTime = default, int Progress = 0, double Speed = 1d, IReadOnlyList<PlaybackSegment>? Segments = null,
    string? Codec = null, bool Transcoded = false, string? Error = null, int? Width = null, int? Height = null, int? BitrateKbps = null)
{
    /// <summary>宽松：回放状态缺字段不应打断会话建立；Segments 归一为空列表避免调用方判空。</summary>
    public static PlaybackSummary Parse(JsonNode? value)
    {
        var parsed = value is null ? new PlaybackSummary() : AdapterJson.Deserialize<PlaybackSummary>(value) ?? new PlaybackSummary();
        return parsed.Segments is null ? parsed with { Segments = [] } : parsed;
    }
}

public sealed record AdapterHealth(string Status = "unknown", string? Service = null, string? Version = null)
{
    public static AdapterHealth Parse(JsonNode? value)
        => value is null ? new() : AdapterJson.Deserialize<AdapterHealth>(value) ?? new();
}

public static class AdapterJson
{
    /// <summary>
    /// 统一的响应映射入口。使用 Web 默认选项（camelCase、属性名大小写不敏感），
    /// 与适配器 ASP.NET 默认序列化一致。
    /// 类型不匹配或 JSON 损坏会抛 JsonException —— 这属于**真实**的协议破损，不应吞掉。
    /// </summary>
    public static T? Deserialize<T>(JsonNode? value) where T : class
        => value is null ? null : value.Deserialize<T>(JsonDefaults.Options);
}
