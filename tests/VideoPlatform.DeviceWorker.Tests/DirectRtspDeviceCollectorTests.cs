using VideoPlatform.Core;
using VideoPlatform.DeviceWorker;
using Xunit;

namespace VideoPlatform.DeviceWorker.Tests;

public sealed class DirectRtspDeviceCollectorTests
{
    [Fact(DisplayName = "直连采集器按插件运行时认领任意品牌设备")]
    public async Task CollectorUsesPluginRuntimeAndPreservesManualRoute()
    {
        var collector = new DirectRtspDeviceCollector();
        var assignment = new WorkerRecorderAssignment(
            Guid.NewGuid(), Guid.NewGuid(), "CAM-01-DIRECT", "北门摄像头", "任意品牌", "vendor-camera",
            "Asia/Shanghai",
            [new WorkerRecorderEndpoint("Rtsp", "camera.example", 554, false, "camera-01")],
            [],
            [new WorkerCameraRoute(Guid.NewGuid(), 1, "{\"main\":\"/main\",\"sub\":\"/sub\"}", "北门", false)],
            DeviceKinds.Camera,
            "vendor-camera",
            DevicePluginRuntimeTypes.DirectRtsp);

        Assert.True(collector.CanCollect(assignment));
        var inventory = await collector.CollectInventoryAsync(assignment, CancellationToken.None);

        var camera = Assert.Single(inventory.Cameras);
        Assert.Equal("北门", camera.Alias);
        Assert.Equal("{\"main\":\"/main\",\"sub\":\"/sub\"}", camera.StreamingChannelMap);
        Assert.Equal(CapabilityState.Supported, inventory.Capabilities.LiveStream);
        Assert.Equal(CapabilityState.Unsupported, inventory.Capabilities.Playback);
    }
}
