using VisiCore.OnvifEdgeWorker;
using Xunit;

namespace VisiCore.OnvifEdgeWorker.Tests;

public sealed class OnvifEdgeOptionsTests
{
    [Fact(DisplayName = "ONVIF Worker 仅接受显式白名单中的开发中心地址")]
    public void TrustedDevelopmentCenterHostIsAccepted()
    {
        var options = new OnvifEdgeOptions
        {
            CenterBaseUri = "http://visicore-api:8080/",
            AccessToken = "onvif-edge-access-token-at-least-32-bytes",
            AllowInsecureCenterHttpForDevelopment = true,
            TrustedDevelopmentHttpHosts = ["visicore-api"]
        };

        Assert.Equal("visicore-api", options.ValidateAndGetCenterBaseUri().Host);
    }

    [Fact(DisplayName = "显式开发模式兼容既有的 24 位 Worker 令牌")]
    public void DevelopmentModeAcceptsExisting24CharacterWorkerToken()
    {
        var options = new OnvifEdgeOptions
        {
            CenterBaseUri = "http://visicore-api:8080/",
            AccessToken = new string('a', 24),
            AllowInsecureCenterHttpForDevelopment = true,
            TrustedDevelopmentHttpHosts = ["visicore-api"]
        };

        Assert.Equal("visicore-api", options.ValidateAndGetCenterBaseUri().Host);
    }

    [Fact(DisplayName = "ONVIF Worker 不接受未登记的开发中心地址")]
    public void UntrustedDevelopmentCenterHostIsRejected()
    {
        var options = new OnvifEdgeOptions
        {
            CenterBaseUri = "http://untrusted:8080/",
            AccessToken = "onvif-edge-access-token-at-least-32-bytes",
            AllowInsecureCenterHttpForDevelopment = true
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetCenterBaseUri());
    }
}
