using System.Net.Http;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Adapter;
using VideoPlatform.Infrastructure.Jobs;
using Xunit;

namespace VideoPlatform.Worker.Tests;

/// <summary>
/// 类型化适配器端口（P1 阶段 2）的契约测试。**不需要数据库**，可随 Worker.Tests 常规运行。
///
/// 覆盖重点是第 1 步刻意设计的**解析严格度边界**与第 3 步的"契约违背修复"，
/// 这两处最容易在后续改动中被无意破坏：
///  - 严格端点（recordings/events/sessions/exports）形状不符时必须抛错，不得静默容错；
///  - 宽松端点（probe/live/playback/sync/health）必须容忍缺字段，
///    尤其"适配器无响应"与"返回空对象"两种情况的既有差异不得合并。
/// </summary>
public sealed class CameraAdapterPortTests
{
    // ── DeviceSnapshot：保持既有 502/device.snapshot 语义 ──────────────────

    [Fact]
    public void DeviceSnapshotParsesChannelsAndPreservesMissingIpAsNull()
    {
        var node = JsonNode.Parse("""
            {"device":{"serialNumber":"SN-1","model":"DS-TEST","servicePort":8000},
             "channels":[{"channel":1,"name":"东门","model":"DS-2CD","online":true,"ptzCapable":true,"codec":"h265"},
                         {"channel":2,"name":"西门","online":false}]}
            """);
        var snapshot = DeviceSnapshot.Parse(node);
        Assert.Equal("SN-1", snapshot.Device.SerialNumber);
        Assert.Equal("DS-TEST", snapshot.Device.Model);
        Assert.Equal(2, snapshot.Channels.Count);
        Assert.True(snapshot.Channels[0].Online);
        Assert.True(snapshot.Channels[0].PtzCapable);
        Assert.Equal("h265", snapshot.Channels[0].Codec);
        Assert.False(snapshot.Channels[1].Online);
        // 适配器两侧的 ChannelInfo 都未填充 Ip；调用方需以 ?? "" 还原 Text("ip") 的空串语义。
        Assert.Null(snapshot.Channels[0].Ip);
    }

    [Fact]
    public void DeviceSnapshotWithoutChannelsArrayFailsWithExistingErrorCode()
    {
        // 与改动前的 Rules.Require(..., "device.snapshot", 502) 完全等效：
        // 必须是 502 且错误码为 device.snapshot，不得退化成 500。
        var error = Assert.Throws<PlatformException>(() => DeviceSnapshot.Parse(JsonNode.Parse("""{"device":{}}""")));
        Assert.Equal(502, error.Status);
        Assert.Equal("device.snapshot", error.Code);

        var notObject = Assert.Throws<PlatformException>(() => DeviceSnapshot.Parse(new JsonArray()));
        Assert.Equal(502, notObject.Status);
        Assert.Equal("device.snapshot", notObject.Code);
    }

    [Fact]
    public void DeviceSnapshotToleratesMissingDeviceBlock()
    {
        var snapshot = DeviceSnapshot.Parse(JsonNode.Parse("""{"channels":[]}"""));
        Assert.Empty(snapshot.Channels);
        Assert.Null(snapshot.Device.SerialNumber);
    }

    // ── StreamCapability：宽松，且必须区分"无响应"与"空对象" ───────────────

    [Fact]
    public void StreamCapabilityDistinguishesAbsentResponseFromEmptyObject()
    {
        // 无响应：沿用旧代码的兜底文案。
        var absent = StreamCapability.Parse(null);
        Assert.False(absent.Available);
        Assert.Equal("适配器未返回探测结果", absent.Error);

        // 空对象：旧代码此处 error 为 null（因为 result 非 null），不得被合并成同一结果。
        var empty = StreamCapability.Parse(JsonNode.Parse("{}"));
        Assert.False(empty.Available);
        Assert.Null(empty.Error);
    }

    [Fact]
    public void StreamCapabilityParsesRealProbeResult()
    {
        var result = StreamCapability.Parse(JsonNode.Parse("""{"available":true,"codec":"h264","width":1920,"height":1080,"bitrateKbps":4096}"""));
        Assert.True(result.Available);
        Assert.Equal("h264", result.Codec);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(4096, result.BitrateKbps);
    }

    // ── PlaybackSummary：宽松，Segments 归一为空列表 ──────────────────────

    [Fact]
    public void PlaybackSummaryNormalizesMissingSegmentsToEmptyList()
    {
        var parsed = PlaybackSummary.Parse(JsonNode.Parse("""{"stream":"vp2_x","state":"playing"}"""));
        Assert.NotNull(parsed.Segments);
        Assert.Empty(parsed.Segments!);
        Assert.Equal("playing", parsed.State);
    }

    [Fact]
    public void PlaybackSummaryParsesSegmentsAndMediaParameters()
    {
        var parsed = PlaybackSummary.Parse(JsonNode.Parse("""
            {"id":"11111111-1111-1111-1111-111111111111","stream":"vp2_y","state":"playing","progress":42,"speed":2,
             "codec":"h265","transcoded":true,"width":2560,"height":1440,"bitrateKbps":8192,
             "segments":[{"start":"2026-09-07T00:00:00Z","end":"2026-09-07T00:10:00Z","fileName":"a.ps","fileSize":100,"fileType":0,"streamType":1,"fileIndex":3}]}
            """));
        Assert.Equal(42, parsed.Progress);
        Assert.Equal(2d, parsed.Speed);
        Assert.True(parsed.Transcoded);
        Assert.Equal(2560, parsed.Width);
        var segment = Assert.Single(parsed.Segments!);
        Assert.Equal("a.ps", segment.FileName);
        Assert.Equal(3u, segment.FileIndex);
    }

    [Fact]
    public void AdapterHealthToleratesAbsentResponse()
    {
        var health = AdapterHealth.Parse(null);
        Assert.Equal("unknown", health.Status);
        var parsed = AdapterHealth.Parse(JsonNode.Parse("""{"status":"ok","service":"hikvision-adapter","version":"2.0.0"}"""));
        Assert.Equal("ok", parsed.Status);
        Assert.Equal("hikvision-adapter", parsed.Service);
    }

    // ── RecordingDto：锁定"与适配器 RecordingSummary 同形"这一契约前提 ────

    [Fact]
    public void RecordingDtoDeserializesAdapterRecordingSummaryShape()
    {
        // 适配器 Contracts.cs 的 RecordingSummary 序列化为 camelCase（ASP.NET 默认 Web 选项）。
        // 这是 ApiContractCatalog 声明 RecordingDto[] 能够成立的前提；一旦任一侧改字段名，
        // 本用例会失败，从而避免"声明与实现悄悄不一致"再次发生。
        var recording = AdapterJson.Deserialize<RecordingDto>(JsonNode.Parse("""
            {"fileName":"20260907_000000.ps","start":"2026-09-07T00:00:00+08:00","end":"2026-09-07T00:30:00+08:00",
             "fileSize":1048576,"fileType":0,"streamType":1,"fileIndex":7}
            """));
        Assert.NotNull(recording);
        Assert.Equal("20260907_000000.ps", recording!.FileName);
        Assert.Equal(1048576L, recording.FileSize);
        Assert.Equal(1, recording.StreamType);
        Assert.Equal(7u, recording.FileIndex);
    }

    // ── HttpCameraAdapter：路径构造与严格度（用桩传输，无需真实适配器） ─────

    [Fact]
    public async Task SearchRecordingsBuildsExpectedPathAndReturnsTypedList()
    {
        var transport = new StubTransport(JsonNode.Parse("""[{"fileName":"a.ps","start":"2026-09-07T00:00:00Z","end":"2026-09-07T00:01:00Z","fileSize":1,"fileType":0,"streamType":1,"fileIndex":0}]"""));
        var port = new HttpCameraAdapter(transport);
        var start = DateTimeOffset.Parse("2026-09-07T00:00:00Z");
        var recordings = await port.SearchRecordingsAsync(9, 3, start, start.AddHours(1));

        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal("/internal/devices/9/recordings/search", transport.Path);
        Assert.Single(recordings);
        Assert.Equal("a.ps", recordings[0].FileName);
    }

    [Theory]
    [InlineData("""{"not":"an array"}""")]                     // 形状错误 → 必须抛
    [InlineData("""[{"start":"2026-09-07T00:00:00Z"}]""")]     // 缺 fileName → 必须抛
    public async Task SearchRecordingsFailsClosedOnMalformedPayload(string payload)
    {
        var port = new HttpCameraAdapter(new StubTransport(JsonNode.Parse(payload)));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            port.SearchRecordingsAsync(1, 1, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ProbeStreamBuildsExpectedPathAndBody()
    {
        var transport = new StubTransport(JsonNode.Parse("""{"available":true,"codec":"h264"}"""));
        var port = new HttpCameraAdapter(transport);
        var result = await port.ProbeStreamAsync(4, 2, 1);

        Assert.Equal(HttpMethod.Post, transport.Method);
        Assert.Equal("/internal/devices/4/streams/probe", transport.Path);
        Assert.Contains("\"channel\":2", transport.Body);
        Assert.Contains("\"streamType\":1", transport.Body);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task EventsAsyncForwardsCursorAndLimitThenParsesStrictly()
    {
        var transport = new StubTransport(JsonNode.Parse("""{"items":[],"nextCursor":"0:0"}"""));
        var port = new HttpCameraAdapter(transport);
        var batch = await port.EventsAsync(5, "0:0", 100, DateTimeOffset.UtcNow);

        Assert.Equal(HttpMethod.Get, transport.Method);
        Assert.Equal("/internal/devices/5/events?after=0%3A0&limit=100", transport.Path);
        Assert.Empty(batch.Items);
    }

    [Fact]
    public async Task SessionsAsyncParsesInventoryStrictly()
    {
        var bootId = Guid.NewGuid();
        var transport = new StubTransport(JsonNode.Parse($$"""{"bootId":"{{bootId}}","devices":[],"live":[],"playback":[],"exports":[]}"""));
        var port = new HttpCameraAdapter(transport);
        var inventory = await port.SessionsAsync();

        Assert.Equal("/internal/sessions", transport.Path);
        Assert.Equal(bootId, inventory.BootId);
        Assert.Empty(inventory.Resources);
    }

    [Fact]
    public async Task DeregisterUsesDeleteAndCarriesNoBody()
    {
        var transport = new StubTransport(null);
        var port = new HttpCameraAdapter(transport);
        await port.DeregisterAsync(7);

        Assert.Equal(HttpMethod.Delete, transport.Method);
        Assert.Equal("/internal/devices/7", transport.Path);
        Assert.Null(transport.Body);
    }

    /// <summary>记录调用并返回预设响应的桩传输，用于验证端口的路径构造与解析行为。</summary>
    private sealed class StubTransport(JsonNode? response) : IDeviceAdapter
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        public Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
        {
            Method = method;
            Path = path;
            Body = body is null ? null : System.Text.Json.JsonSerializer.Serialize(body, JsonDefaults.Options);
            return Task.FromResult(response?.DeepClone());
        }
    }
}
