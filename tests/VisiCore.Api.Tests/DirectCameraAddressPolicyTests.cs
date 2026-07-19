using System.Text.Json;
using VisiCore.Api;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class DirectCameraAddressPolicyTests
{
    [Fact(DisplayName = "单个 RTSP 地址会安全复用为主子码流")]
    public void SingleAddressCreatesMainAndSubMappings()
    {
        var valid = DirectCameraAddressPolicy.TryNormalize(
            "rtsp://camera.example:8554/live/main?channel=1",
            null,
            out var address,
            out var error);

        Assert.True(valid, error);
        Assert.NotNull(address);
        Assert.Equal("camera.example", address.Host);
        Assert.Equal(8554, address.Port);
        Assert.Equal(address.MainPath, address.SubPath);
        Assert.DoesNotContain("@", address.MainUrl);
        using var map = JsonDocument.Parse(address.StreamingChannelMap);
        Assert.Equal("/live/main?channel=1", map.RootElement.GetProperty("main").GetString());
        Assert.Equal("/live/main?channel=1", map.RootElement.GetProperty("sub").GetString());
    }

    [Theory(DisplayName = "直连地址拒绝 URL 内嵌凭据和敏感查询参数")]
    [InlineData("rtsp://viewer:secret@camera.example/live")]
    [InlineData("rtsp://camera.example/live?token=secret")]
    [InlineData("rtsp://camera.example/live?PASSWORD=secret")]
    [InlineData("rtsp://camera.example/live?access_token=secret")]
    [InlineData("rtsp://camera.example/live?auth_token=secret")]
    [InlineData("rtsp://camera.example/live?signature=secret")]
    [InlineData("rtsp://camera.example/live?pwd=secret")]
    public void CredentialsInsideUrlAreRejected(string value)
    {
        var valid = DirectCameraAddressPolicy.TryNormalize(value, null, out _, out var error);

        Assert.False(valid);
        Assert.Contains("不能", error);
    }

    [Fact(DisplayName = "主子码流不能跨越设备端点")]
    public void MainAndSubMustShareEndpoint()
    {
        var valid = DirectCameraAddressPolicy.TryNormalize(
            "rtsp://camera-a.example/live/main",
            "rtsp://camera-b.example/live/sub",
            out _,
            out var error);

        Assert.False(valid);
        Assert.Contains("同一主机", error);
    }

    [Fact(DisplayName = "RTSPS 地址保留安全传输模式")]
    public void RtspsAddressPreservesTlsMode()
    {
        var valid = DirectCameraAddressPolicy.TryNormalize(
            "rtsps://camera.example:322/live/main",
            "rtsps://camera.example:322/live/sub",
            out var address,
            out var error);

        Assert.True(valid, error);
        Assert.True(address!.UseTls);
        Assert.StartsWith("rtsps://", address.MainUrl, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "通道码流映射在进入 Gateway 前拒绝无效字符串")]
    [InlineData("{\"main\":\"foo\",\"sub\":\"/live/sub\"}")]
    [InlineData("{\"main\":\"https://camera.example/live\",\"sub\":\"/live/sub\"}")]
    [InlineData("{\"main\":\"//other.example/live\",\"sub\":\"/live/sub\"}")]
    [InlineData("{\"main\":\"/live?access_token=secret\",\"sub\":\"/live/sub\"}")]
    public void InvalidChannelMappingsAreRejected(string mapping)
    {
        var valid = DirectCameraAddressPolicy.TryValidateStreamingMap(mapping, out _, out var error);

        Assert.False(valid);
        Assert.NotEmpty(error);
    }

    [Fact(DisplayName = "绝对通道地址不能越过登记的 RTSP 端点")]
    public void AbsoluteChannelMappingMustStayWithinEndpoint()
    {
        var endpoint = new RecorderEndpointRegistration(
            RecorderEndpointProtocol.Rtsp,
            "camera-a.example",
            554,
            "camera-a");

        var valid = DirectCameraAddressPolicy.TryValidateStreamingMap(
            "{\"main\":\"rtsp://camera-b.example/live/main\",\"sub\":\"/live/sub\"}",
            endpoint,
            out _,
            out var error);

        Assert.False(valid);
        Assert.Contains("不能越过", error);
    }
}
