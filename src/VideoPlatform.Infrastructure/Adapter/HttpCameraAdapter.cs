using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Infrastructure.Jobs;

namespace VideoPlatform.Infrastructure.Adapter;

/// <summary>
/// <see cref="ICameraAdapter"/> 的类型化外观：在既有 <see cref="IDeviceAdapter"/>（泛型 JSON 透传）
/// 之上补齐类型化方法。
///
/// 为什么是外观而不是重写：<see cref="IDeviceAdapter"/> 的实现（DeviceAdapter）已承担并经过验证的职责包括
/// 厂商路由（plugin_id → endpoint_url，30 秒缓存）、禁用插件 409、esight_pon 短路、
/// /internal/sessions 跨插件扇出合并、适配器错误 JSON → PlatformException 映射，以及
/// 503 不可达 / 504 超时语义。本类**原样复用**这些行为，只负责构造路径、组织载荷与解析响应，
/// 因此引入本端口不改变任何既有失败语义。
/// </summary>
public sealed class HttpCameraAdapter(IDeviceAdapter transport) : ICameraAdapter
{
    private static string Device(long deviceId) => $"/internal/devices/{deviceId}";

    // ── 设备生命周期 ────────────────────────────────────────────────────

    public Task RegisterAsync(long deviceId, AdapterDeviceSpec spec, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Put, Device(deviceId), spec, ct);

    public Task DeregisterAsync(long deviceId, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Delete, Device(deviceId), ct: ct);

    public async Task<DeviceSnapshot> SyncAsync(long deviceId, CancellationToken ct = default)
        => DeviceSnapshot.Parse(await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/sync", ct: ct));

    public async Task<AdapterHealth> HealthAsync(CancellationToken ct = default)
        => AdapterHealth.Parse(await transport.SendAsync(HttpMethod.Get, "/health", ct: ct));

    // ── 码流能力 ────────────────────────────────────────────────────────

    public async Task<StreamCapability> ProbeStreamAsync(long deviceId, int channel, int streamType, CancellationToken ct = default)
        => StreamCapability.Parse(await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/streams/probe",
            new { channel, streamType }, ct));

    // ── 实时 ────────────────────────────────────────────────────────────

    public async Task<LiveSummary> StartLiveAsync(long deviceId, LiveStartSpec spec, CancellationToken ct = default)
        => LiveSummary.Parse(await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/live", spec, ct));

    public Task StopLiveAsync(long deviceId, Guid sessionId, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Delete, $"{Device(deviceId)}/live/{sessionId}", ct: ct);

    public async Task<AdapterInventory> SessionsAsync(CancellationToken ct = default)
        => AdapterInventory.Parse(await transport.SendAsync(HttpMethod.Get, "/internal/sessions", ct: ct));

    // ── 回放 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 严格解析为 <see cref="RecordingDto"/> 数组。
    /// 这同时修好一处契约违背：<c>ApiContractCatalog</c> 早已声明该接口返回 <c>RecordingDto[]</c>，
    /// 但原实现把适配器原始 JSON 直接透传、从不校验。已核实 <see cref="RecordingDto"/>
    /// 与适配器 RecordingSummary 的 7 个字段逐字段相同，故线上形状不变。
    /// </summary>
    public async Task<IReadOnlyList<RecordingDto>> SearchRecordingsAsync(long deviceId, int channel, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var node = await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/recordings/search",
            new { channel, start, end }, ct);
        if (node is not JsonArray array)
            throw new InvalidDataException("适配器返回的录像清单格式无效。");
        var list = new List<RecordingDto>(array.Count);
        foreach (var item in array)
        {
            var recording = item is null ? null : AdapterJson.Deserialize<RecordingDto>(item);
            if (recording is null || string.IsNullOrWhiteSpace(recording.FileName))
                throw new InvalidDataException("适配器返回的录像条目格式无效。");
            list.Add(recording);
        }
        return list;
    }

    public async Task<PlaybackSummary> StartPlaybackAsync(long deviceId, PlaybackStartSpec spec, CancellationToken ct = default)
        => PlaybackSummary.Parse(await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/playback", spec, ct));

    public async Task<PlaybackSummary> GetPlaybackAsync(long deviceId, Guid sessionId, CancellationToken ct = default)
        => PlaybackSummary.Parse(await transport.SendAsync(HttpMethod.Get, $"{Device(deviceId)}/playback/{sessionId}", ct: ct));

    /// <summary>宽返回：控制指令的响应可能为空（既有实现同样容忍）。</summary>
    public async Task<PlaybackSummary?> ControlPlaybackAsync(long deviceId, Guid sessionId, PlaybackControlSpec spec, CancellationToken ct = default)
    {
        var node = await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/playback/{sessionId}/control", spec, ct);
        return node is null ? null : PlaybackSummary.Parse(node);
    }

    public Task StopPlaybackAsync(long deviceId, Guid sessionId, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Delete, $"{Device(deviceId)}/playback/{sessionId}", ct: ct);

    // ── 云台 ────────────────────────────────────────────────────────────

    public Task PtzAsync(long deviceId, int channel, string command, uint speed, bool stop, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/ptz", new { channel, command, speed, stop }, ct);

    public Task PresetAsync(long deviceId, int channel, uint preset, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/ptz/preset", new { channel, preset }, ct);

    // ── 导出 ────────────────────────────────────────────────────────────

    public async Task<AdapterExportStatus> StartExportAsync(long deviceId, ExportSpec spec, CancellationToken ct = default)
        => AdapterExportStatus.Parse(await transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/exports", spec, ct));

    public async Task<AdapterExportStatus> GetExportAsync(long deviceId, Guid jobId, CancellationToken ct = default)
        => AdapterExportStatus.Parse(await transport.SendAsync(HttpMethod.Get, $"{Device(deviceId)}/exports/{jobId}", ct: ct));

    public async Task<AdapterExportStatus> CancelExportAsync(long deviceId, Guid jobId, CancellationToken ct = default)
        => AdapterExportStatus.Parse(await transport.SendAsync(HttpMethod.Delete, $"{Device(deviceId)}/exports/{jobId}", ct: ct));

    // ── 报警 ────────────────────────────────────────────────────────────

    public async Task<AdapterAlarmBatch> EventsAsync(long deviceId, string previousCursor, int limit, DateTimeOffset receivedAt, CancellationToken ct = default)
    {
        var path = $"{Device(deviceId)}/events?after={Uri.EscapeDataString(previousCursor)}&limit={limit}";
        return AdapterAlarmBatch.Parse(deviceId, previousCursor, await transport.SendAsync(HttpMethod.Get, path, ct: ct), receivedAt);
    }

    public Task AckEventsAsync(long deviceId, string cursor, CancellationToken ct = default)
        => transport.SendAsync(HttpMethod.Post, $"{Device(deviceId)}/events/ack", new { cursor }, ct);
}
