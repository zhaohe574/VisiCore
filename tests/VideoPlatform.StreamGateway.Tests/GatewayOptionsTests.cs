using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class GatewayOptionsTests
{
    [Fact(DisplayName = "生产中心地址必须使用 HTTPS 且控制令牌与命令令牌独立")]
    public void InsecureOrReusedGatewayConfigurationIsRejected()
    {
        var insecure = CreateValidOptions(centerBaseUri: "http://center.test/");
        var shared = "shared-token-at-least-32-bytes-long";
        var reused = CreateValidOptions(centerControlToken: shared, commandToken: shared);

        Assert.Throws<InvalidOperationException>(() => insecure.ValidateAndGetCenterBaseUri());
        Assert.Throws<InvalidOperationException>(() => reused.ValidateAndGetCenterBaseUri());
    }

    [Fact(DisplayName = "MediaMTX 明文 Control API 仅允许回环或显式受信任服务")]
    public void MediaMtxHttpApiMustBeLoopback()
    {
        var options = new MediaMtxOptions { ApiBaseUri = "http://192.0.2.10:9997/" };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetApiBaseUri());
    }

    [Fact(DisplayName = "MediaMTX HLS 上游必须是回环或显式受信任服务")]
    public void MediaMtxHlsUpstreamMustBeLoopback()
    {
        var options = new MediaMtxOptions { HlsBaseUri = "https://192.0.2.10:8888/" };
        var credentialsInUri = new MediaMtxOptions
        {
            HlsBaseUri = "http://reader:password@127.0.0.1:8888/"
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetHlsBaseUri());
        Assert.Throws<InvalidOperationException>(() => credentialsInUri.ValidateAndGetHlsBaseUri());
    }

    [Fact(DisplayName = "合法的网关与 MediaMTX 配置可以通过校验")]
    public void ValidConfigurationIsAccepted()
    {
        var mediaMtxOptions = new MediaMtxOptions
        {
            PublisherUsername = "relay",
            PublisherPassword = "publisher-password-at-least-32-bytes",
            HlsReaderUsername = "hlsproxy",
            HlsReaderPassword = "hls-reader-password-at-least-32-bytes"
        };
        Assert.Equal("https", CreateValidOptions().ValidateAndGetCenterBaseUri().Scheme);
        Assert.True(mediaMtxOptions.ValidateAndGetApiBaseUri().IsLoopback);
        Assert.True(mediaMtxOptions.ValidateAndGetHlsBaseUri().IsLoopback);
        mediaMtxOptions.ValidatePublisherCredentials();
        mediaMtxOptions.ValidateHlsReaderCredentials();
        Assert.True(mediaMtxOptions.IsPublisherCredentialsValid("relay", "publisher-password-at-least-32-bytes"));
        Assert.False(mediaMtxOptions.IsPublisherCredentialsValid("relay", "invalid-publisher-password"));
        Assert.True(mediaMtxOptions.IsHlsReaderCredentialsValid("hlsproxy", "hls-reader-password-at-least-32-bytes"));
        Assert.False(mediaMtxOptions.IsHlsReaderCredentialsValid("hlsproxy", "invalid-hls-reader-password"));
    }

    [Fact(DisplayName = "MediaMTX HLS 内部读取凭据必须独立且有效")]
    public void HlsReaderCredentialsMustBeIndependent()
    {
        var reused = new MediaMtxOptions
        {
            PublisherUsername = "shared",
            PublisherPassword = "shared-password-at-least-32-bytes-long",
            HlsReaderUsername = "shared",
            HlsReaderPassword = "shared-password-at-least-32-bytes-long"
        };
        var missing = new MediaMtxOptions
        {
            PublisherUsername = "relay",
            PublisherPassword = "publisher-password-at-least-32-bytes"
        };
        var reusedPassword = new MediaMtxOptions
        {
            PublisherUsername = "relay",
            PublisherPassword = "shared-password-at-least-32-bytes-long",
            HlsReaderUsername = "hlsproxy",
            HlsReaderPassword = "shared-password-at-least-32-bytes-long"
        };

        Assert.Throws<InvalidOperationException>(() => reused.ValidateHlsReaderCredentials());
        Assert.Throws<InvalidOperationException>(() => missing.ValidateHlsReaderCredentials());
        Assert.Throws<InvalidOperationException>(() => reusedPassword.ValidateHlsReaderCredentials());
    }

    [Fact(DisplayName = "Compose 内网 MediaMTX 必须显式列为受信任主机和地址")]
    public void TrustedContainerMediaMtxAddressesAreExplicitlyAllowed()
    {
        var options = new MediaMtxOptions
        {
            ApiBaseUri = "http://mediamtx:9997/",
            HlsBaseUri = "http://mediamtx:8888/",
            TrustedInternalHosts = ["mediamtx"],
            TrustedAuthCallbackAddresses = ["172.32.0.20"],
            TrustedHlsReaderAddresses = ["172.32.0.30"]
        };

        Assert.Equal("mediamtx", options.ValidateAndGetApiBaseUri().Host);
        Assert.Equal("mediamtx", options.ValidateAndGetHlsBaseUri().Host);
        Assert.True(options.IsTrustedAuthCallbackAddress(System.Net.IPAddress.Parse("172.32.0.20")));
        Assert.True(options.IsTrustedHlsReaderAddress("172.32.0.30"));
        Assert.False(options.IsTrustedAuthCallbackAddress(System.Net.IPAddress.Parse("172.32.0.21")));
        Assert.False(options.IsTrustedHlsReaderAddress("172.32.0.31"));
    }

    [Fact(DisplayName = "实时主码流转码只接受受控 FFmpeg、回环 RTSP 和已验收录像机")]
    public void LiveTranscodeConfigurationRequiresControlledLocalResources()
    {
        var valid = new LiveTranscodeOptions
        {
            Enabled = true,
            FfmpegExecutablePath = Environment.ProcessPath,
            MediaMtxRtspBaseUri = "rtsp://127.0.0.1:8554/",
            ValidatedRecorderIds = [Guid.NewGuid()]
        };
        valid.Validate();

        var remote = new LiveTranscodeOptions
        {
            Enabled = true,
            FfmpegExecutablePath = Environment.ProcessPath,
            MediaMtxRtspBaseUri = "rtsp://192.0.2.10:8554/",
            ValidatedRecorderIds = [Guid.NewGuid()]
        };
        var unvalidated = new LiveTranscodeOptions
        {
            Enabled = true,
            FfmpegExecutablePath = Environment.ProcessPath,
            MediaMtxRtspBaseUri = "rtsp://127.0.0.1:8554/"
        };

        Assert.Throws<InvalidOperationException>(() => remote.Validate());
        Assert.Throws<InvalidOperationException>(() => unvalidated.Validate());
    }

    [Fact(DisplayName = "内部原始路径与公开主码流路径使用严格格式")]
    public void LiveTranscodePathsAreStrictlyValidated()
    {
        var cameraId = Guid.NewGuid();

        Assert.True(LiveTranscodePath.IsInternalSource(LiveTranscodePath.BuildInternalSource(cameraId)));
        Assert.True(LiveTranscodePath.IsPublicMain(LiveTranscodePath.BuildPublicMain(cameraId)));
        Assert.False(LiveTranscodePath.IsPublicMain($"live/{cameraId:N}/sub"));
        Assert.False(LiveTranscodePath.IsPublicMain($"live/{cameraId:N}/main/extra"));
    }

    private static GatewayOptions CreateValidOptions(
        string centerBaseUri = "https://center.test/",
        string centerControlToken = "center-control-token-at-least-32-bytes",
        string commandToken = "gateway-command-token-at-least-32-bytes") =>
        new()
        {
            GatewayName = "integration",
            CenterBaseUri = centerBaseUri,
            CenterControlToken = centerControlToken,
            DeviceWorkerAccessToken = "device-worker-token-at-least-32-bytes",
            CommandToken = commandToken
        };
}
