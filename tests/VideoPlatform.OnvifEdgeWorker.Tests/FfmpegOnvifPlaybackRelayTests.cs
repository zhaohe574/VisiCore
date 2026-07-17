using VideoPlatform.OnvifEdgeWorker;
using Xunit;

namespace VideoPlatform.OnvifEdgeWorker.Tests;

public sealed class FfmpegOnvifPlaybackRelayTests
{
    [Fact(DisplayName = "ONVIF 回放 FFmpeg 使用 TCP、时段 Range 和兼容编码输出")]
    public void CreateStartInfoUsesClockRangeAndH264CompatibilityMode()
    {
        var relay = new FfmpegOnvifPlaybackRelay(new OnvifEdgeOptions
        {
            PlaybackRelay = CreateOptions()
        });
        var sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var info = relay.CreateStartInfo(
            sessionId,
            new OnvifProfileGReplaySource(
                new Uri("rtsp://nvr.example:554/replay?recording=west"),
                "viewer",
                "device-password-for-test"),
            DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-13T01:00:00Z"));

        Assert.Contains("-rtsp_transport", info.ArgumentList);
        Assert.Contains("tcp", info.ArgumentList);
        Assert.Contains("-range", info.ArgumentList);
        Assert.Contains("clock=20260713T000000Z-20260713T010000Z", info.ArgumentList);
        Assert.Contains("libopenh264", info.ArgumentList);
        Assert.Contains("aac", info.ArgumentList);
        Assert.Contains($"playback/{sessionId:N}", info.ArgumentList.Last());
        Assert.DoesNotContain("device-password-for-test", info.ArgumentList.Last(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ONVIF 回放拒绝携带设备凭据的 Replay URI")]
    public void CreateStartInfoRejectsReplayUriWithEmbeddedCredentials()
    {
        var relay = new FfmpegOnvifPlaybackRelay(new OnvifEdgeOptions
        {
            PlaybackRelay = CreateOptions()
        });

        var exception = Assert.Throws<InvalidOperationException>(() => relay.CreateStartInfo(
            Guid.NewGuid(),
            new OnvifProfileGReplaySource(new Uri("rtsp://viewer:secret@nvr.example:554/replay"), "viewer", "device-password-for-test"),
            DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-13T01:00:00Z")));

        Assert.Contains("参数无效", exception.Message);
    }

    [Fact(DisplayName = "ONVIF 回放倍速使用受控输入读取速率")]
    public void CreateStartInfoAppliesValidatedPlaybackSpeed()
    {
        var relay = new FfmpegOnvifPlaybackRelay(new OnvifEdgeOptions { PlaybackRelay = CreateOptions() });
        var info = relay.CreateStartInfo(
            Guid.NewGuid(),
            new OnvifProfileGReplaySource(new Uri("rtsp://nvr.example:554/replay"), "viewer", "device-password-for-test"),
            DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-13T01:00:00Z"),
            4.0);

        var readRateIndex = info.ArgumentList.IndexOf("-readrate");
        Assert.True(readRateIndex >= 0);
        Assert.Equal("4", info.ArgumentList[readRateIndex + 1]);
    }

    private static OnvifPlaybackRelayOptions CreateOptions() => new()
    {
        Enabled = true,
        FfmpegExecutablePath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        PublisherBaseUri = "rtsp://127.0.0.1:8554",
        PublisherUsername = "edgepublisher",
        PublisherPassword = "publisher-password-with-at-least-32-chars",
        VideoMode = OnvifPlaybackRelayVideoMode.H264Compatible,
        ValidatedRecorderIds = [Guid.Parse("22222222-2222-2222-2222-222222222222")]
    };
}
