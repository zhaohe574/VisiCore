using System.Net;
using System.Reflection;
using System.Text.Json;
using VideoPlatform.Core;
using VideoPlatform.StreamGateway;
using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class GatewaySourceUriTests
{
    [Fact(DisplayName = "直连 RTSP 相对路径保留查询字符串")]
    public void RelativeStreamPathPreservesQuery()
    {
        var method = typeof(GatewayAssignmentWorker).GetMethod(
            "BuildSourceUri",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到流地址构造方法。");
        var endpoint = new WorkerRecorderEndpoint("Rtsp", "camera.example", 8554, false, "camera-01");
        using var document = JsonDocument.Parse("\"/live/main?channel=1&subtype=0\"");

        var result = Assert.IsType<Uri>(method.Invoke(
            null,
            [endpoint, new NetworkCredential("viewer", "secret"), document.RootElement]));

        Assert.Equal("/live/main", result.AbsolutePath);
        Assert.Equal("?channel=1&subtype=0", result.Query);
        Assert.Equal("viewer", result.UserInfo.Split(':')[0]);
    }

    [Fact(DisplayName = "RTSPS 端点生成安全上游地址")]
    public void RelativeStreamPathUsesRtspsForTlsEndpoint()
    {
        var method = typeof(GatewayAssignmentWorker).GetMethod(
            "BuildSourceUri",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("未找到流地址构造方法。");
        var endpoint = new WorkerRecorderEndpoint("Rtsp", "camera.example", 322, true, "camera-01");
        using var document = JsonDocument.Parse("\"/live/main\"");

        var result = Assert.IsType<Uri>(method.Invoke(
            null,
            [endpoint, new NetworkCredential("viewer", "secret"), document.RootElement]));

        Assert.Equal("rtsps", result.Scheme);
        Assert.Equal(322, result.Port);
    }
}
