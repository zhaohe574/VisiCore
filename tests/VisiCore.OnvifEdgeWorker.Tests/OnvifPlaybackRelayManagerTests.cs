using Microsoft.Extensions.Logging.Abstractions;
using VisiCore.Core;
using VisiCore.OnvifEdgeWorker;
using Xunit;

namespace VisiCore.OnvifEdgeWorker.Tests;

public sealed class OnvifPlaybackRelayManagerTests
{
    [Fact(DisplayName = "ONVIF 回放停止先到时写入墓碑并拒绝后续启动")]
    public async Task StopBeforeStartRejectsLaterStart()
    {
        var profileClient = new FixedProfileGClient();
        var ffmpeg = new FixedFfmpegRelay();
        await using var manager = CreateManager(profileClient, ffmpeg, new FixedAuthorization());
        var camera = CreateCamera();
        var payload = CreateStartPayload(camera.CameraId);

        Assert.False(await manager.StopAsync(payload.PlaybackSessionId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(
            CreateAssignment(camera),
            camera,
            CreateStartCommand(payload),
            payload,
            CancellationToken.None));

        Assert.Equal(0, profileClient.ReplaySourceRequests);
        Assert.Equal(0, ffmpeg.StartRequests);
    }

    [Fact(DisplayName = "ONVIF 回放持续授权失效时自动停止 FFmpeg")]
    public async Task ContinuationAuthorizationFailureStopsRelay()
    {
        var profileClient = new FixedProfileGClient();
        var ffmpeg = new FixedFfmpegRelay();
        var authorization = new FixedAuthorization { ContinueAllowed = false };
        await using var manager = CreateManager(profileClient, ffmpeg, authorization);
        var camera = CreateCamera();
        var payload = CreateStartPayload(camera.CameraId);

        var result = await manager.StartAsync(
            CreateAssignment(camera),
            camera,
            CreateStartCommand(payload),
            payload,
            CancellationToken.None);

        Assert.Equal($"playback/{payload.PlaybackSessionId:N}", result.PathName);
        await ffmpeg.Process.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(authorization.ContinueChecks >= 1);
    }

    private static OnvifPlaybackRelayManager CreateManager(
        FixedProfileGClient profileClient,
        FixedFfmpegRelay ffmpeg,
        FixedAuthorization authorization) =>
        new(
            profileClient,
            ffmpeg,
            authorization,
            new OnvifEdgeOptions
            {
                PlaybackRelay = new OnvifPlaybackRelayOptions
                {
                    Enabled = true,
                    FfmpegExecutablePath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    PublisherBaseUri = "rtsp://127.0.0.1:8554",
                    PublisherUsername = "edgepublisher",
                    PublisherPassword = "publisher-password-with-at-least-32-chars",
                    MaxConcurrentRelays = 1,
                    MaxRelayLifetimeSeconds = 60,
                    StartupProbeMilliseconds = 100,
                    AuthorizationCheckSeconds = 2,
                    ValidatedRecorderIds = [Guid.Parse("11111111-1111-1111-1111-111111111111")]
                }
            },
            NullLogger<OnvifPlaybackRelayManager>.Instance);

    private static WorkerRecorderAssignment CreateAssignment(WorkerCameraRoute camera) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "ZNV-01",
        "通用 ONVIF 验证录像机",
        "Generic",
        "onvif-standard",
        "Asia/Shanghai",
        [],
        [],
        [camera]);

    private static WorkerCameraRoute CreateCamera() => new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        1,
        "{\"onvifSource\":\"source-north\"}");

    private static PlaybackRelayStartCommandPayload CreateStartPayload(Guid cameraId) => new(
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        cameraId,
        DateTimeOffset.UtcNow.AddMinutes(-10),
        DateTimeOffset.UtcNow.AddMinutes(-5));

    private static WorkerEdgeCommand CreateStartCommand(PlaybackRelayStartCommandPayload payload) => new(
        Guid.NewGuid(),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EdgeCommandTypes.OnvifPlaybackRelayStart,
        "playback_relay_session",
        payload.PlaybackSessionId,
        "{}",
        1,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(1),
        "delivery-token");

    private sealed class FixedProfileGClient : IOnvifProfileGClient
    {
        public int ReplaySourceRequests { get; private set; }

        public Task<OnvifProfileGProbeResult> ProbeAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifProfileGProbeResult("recording-north", "source-north"));

        public Task<OnvifProfileGSearchResult> SearchAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, RecordingSearchCommandPayload payload, CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifProfileGSearchResult(camera.CameraId, "recording-north", "source-north", []));

        public Task<Uri> GetReplayUriAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, CancellationToken cancellationToken) =>
            Task.FromResult(new Uri("rtsp://nvr.example:554/replay"));

        public Task<OnvifProfileGReplaySource> GetReplaySourceAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, CancellationToken cancellationToken)
        {
            ReplaySourceRequests++;
            return Task.FromResult(new OnvifProfileGReplaySource(
                new Uri("rtsp://nvr.example:554/replay"),
                "viewer",
                "device-password-for-test"));
        }
    }

    private sealed class FixedFfmpegRelay : IOnvifFfmpegPlaybackRelay
    {
        public int StartRequests { get; private set; }
        public FixedRelayProcess Process { get; } = new();

        public Task EnsureRtspClockRangeSupportedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public IOnvifPlaybackRelayProcess Start(
            Guid playbackSessionId,
            OnvifProfileGReplaySource source,
            DateTimeOffset startedAt,
            DateTimeOffset endedAt)
        {
            StartRequests++;
            return Process;
        }
    }

    private sealed class FixedRelayProcess : IOnvifPlaybackRelayProcess
    {
        public TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasExited => false;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult(true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedAuthorization : IOnvifPlaybackRelayAuthorization
    {
        public bool ContinueAllowed { get; init; } = true;
        public int ContinueChecks { get; private set; }

        public Task<bool> CanStartAsync(WorkerEdgeCommand command, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> CanContinueAsync(Guid playbackSessionId, CancellationToken cancellationToken)
        {
            ContinueChecks++;
            return Task.FromResult(ContinueAllowed);
        }
    }
}
