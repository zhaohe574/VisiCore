using VideoPlatform.Api;
using VideoPlatform.Core;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class DevicePluginManifestTests
{
    [Fact(DisplayName = "任意品牌可安装标准 ONVIF 协议插件")]
    public void GenericOnvifPluginIsAccepted()
    {
        var manifest = new DevicePluginManifest(
            "Acme.ONVIF",
            "ACME ONVIF",
            "2.1.0",
            "ONVIF",
            "ONVIF",
            "Acme-ONVIF",
            ["CAMERA", "MATRIX"],
            [new("onvif", "ONVIF", 80, SupportsTls: true), new("rtsp", "RTSP", 554)],
            new(true, true, true, true, false, true),
            "ACME");

        var normalized = DevicePluginService.NormalizeAndValidate(manifest);

        Assert.Equal("acme.onvif", normalized.Key);
        Assert.Equal("onvif", normalized.RuntimeType);
        Assert.Equal([DeviceKinds.Camera, DeviceKinds.Matrix], normalized.SupportedDeviceKinds);
        Assert.Equal(["Onvif", "Rtsp"], normalized.Endpoints.Select(item => item.Protocol));
    }

    [Fact(DisplayName = "未知运行时不能伪装成已支持协议")]
    public void UnknownRuntimeIsRejected()
    {
        var manifest = new DevicePluginManifest(
            "custom-wire",
            "自定义协议",
            "1.0.0",
            "custom",
            "arbitrary-dll",
            "custom-wire",
            [DeviceKinds.Camera],
            [new("Rtsp", "RTSP", 554)],
            new(true, false, false, false, false, false));

        var exception = Assert.Throws<ArgumentException>(() => DevicePluginService.NormalizeAndValidate(manifest));

        Assert.Contains("运行时", exception.Message);
    }

    [Fact(DisplayName = "直连 RTSP 插件不能虚报回放和 PTZ 能力")]
    public void DirectRtspCannotDeclareUnsupportedCapabilities()
    {
        var manifest = new DevicePluginManifest(
            "direct-invalid",
            "无效直连",
            "1.0.0",
            "rtsp",
            DevicePluginRuntimeTypes.DirectRtsp,
            "direct-invalid",
            [DeviceKinds.Camera],
            [new("Rtsp", "RTSP", 554)],
            new(true, false, true, true, false, false));

        Assert.Throws<ArgumentException>(() => DevicePluginService.NormalizeAndValidate(manifest));
    }

    [Fact(DisplayName = "反序列化后的空能力集合返回校验错误而不是 500")]
    public void NullCapabilitiesAreRejected()
    {
        var manifest = new DevicePluginManifest(
            "null-capabilities",
            "空能力插件",
            "1.0.0",
            "rtsp",
            DevicePluginRuntimeTypes.DirectRtsp,
            "null-capabilities",
            [DeviceKinds.Camera],
            [new("Rtsp", "RTSP", 554)],
            null!);

        var exception = Assert.Throws<ArgumentException>(() => DevicePluginService.NormalizeAndValidate(manifest));

        Assert.Contains("能力", exception.Message);
    }

    [Fact(DisplayName = "Manifest 数组中的空项返回校验错误而不是 500")]
    public void NullArrayEntriesAreRejected()
    {
        var manifest = new DevicePluginManifest(
            "null-endpoint",
            "空端点插件",
            "1.0.0",
            "rtsp",
            DevicePluginRuntimeTypes.DirectRtsp,
            "null-endpoint",
            [DeviceKinds.Camera],
            [null!],
            new(true, false, false, false, false, false));

        var exception = Assert.Throws<ArgumentException>(() => DevicePluginService.NormalizeAndValidate(manifest));

        Assert.Contains("空项", exception.Message);
    }
}
