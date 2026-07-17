using System.Net;
using System.Text.Json;
using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class MediaMtxClientTests
{
    [Fact(DisplayName = "动态 RTSP 路径会强制使用 TCP 拉取上游码流")]
    public async Task ApplyPathUsesTcpForRtspSource()
    {
        string? payload = null;
        var handler = new DelegateHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new MediaMtxClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9997/") },
            new MediaMtxOptions(),
            new GatewayOptions { MaxReadersPerPath = 100 });

        await client.ApplyPathAsync(
            "live/camera/main",
            new Uri("rtsp://viewer:password@example.test:554/Streaming/Channels/101"),
            CancellationToken.None);

        Assert.NotNull(payload);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("tcp", document.RootElement.GetProperty("rtspTransport").GetString());
        Assert.True(document.RootElement.GetProperty("sourceOnDemand").GetBoolean());
    }

    [Fact(DisplayName = "实时转码输出路径只接受单一 publisher 且禁止覆盖")]
    public async Task PublisherPathDisablesPublisherOverride()
    {
        string? payload = null;
        var handler = new DelegateHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = new MediaMtxClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9997/") },
            new MediaMtxOptions(),
            new GatewayOptions { MaxReadersPerPath = 100 });

        await client.ApplyPublisherPathAsync(
            $"live/{Guid.NewGuid():N}/main",
            CancellationToken.None);

        Assert.NotNull(payload);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("publisher", document.RootElement.GetProperty("source").GetString());
        Assert.False(document.RootElement.GetProperty("sourceOnDemand").GetBoolean());
        Assert.False(document.RootElement.GetProperty("overridePublisher").GetBoolean());
        Assert.Equal(100, document.RootElement.GetProperty("maxReaders").GetInt32());
    }

    [Fact(DisplayName = "MediaMTX 路径就绪状态只读取 ready 字段")]
    public async Task PathReadinessUsesControlApiReadyField()
    {
        var handler = new DelegateHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ready\":true,\"source\":{}}")
            };
        });
        var client = new MediaMtxClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:9997/") },
            new MediaMtxOptions(),
            new GatewayOptions());

        Assert.True(await client.IsPathReadyAsync("live/camera/main", CancellationToken.None));
    }
}
