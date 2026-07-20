using System.Text.Json;
using VisiCore.Core;
using VisiCore.OnvifEdgeWorker;
using Xunit;

namespace VisiCore.OnvifEdgeWorker.Tests;

public sealed class OnvifPlaybackRelayCommandExecutorTests
{
    [Fact(DisplayName = "ONVIF 回放启动回写不包含设备地址或凭据")]
    public async Task StartReturnsOnlyGatewayPath()
    {
        var camera = CreateCamera();
        var sessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var executor = new OnvifPlaybackRelayCommandExecutor(new FixedRelayManager());
        var command = new WorkerEdgeCommand(
            Guid.NewGuid(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EdgeCommandTypes.OnvifPlaybackRelayStart,
            "playback_relay_session",
            sessionId,
            JsonSerializer.Serialize(new PlaybackRelayStartCommandPayload(
                sessionId,
                camera.CameraId,
                DateTimeOffset.Parse("2026-07-13T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-13T01:00:00Z"))),
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "delivery-token");

        var resultJson = await executor.ExecuteAsync(CreateAssignment(camera), command, CancellationToken.None);
        using var result = JsonDocument.Parse(resultJson);

        Assert.Equal(sessionId, result.RootElement.GetProperty("playbackSessionId").GetGuid());
        Assert.Equal($"playback/{sessionId:N}", result.RootElement.GetProperty("pathName").GetString());
        Assert.DoesNotContain("rtsp", resultJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", resultJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nvr.example", resultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "ONVIF 回放停止不依赖录像机分配且可幂等返回")]
    public async Task StopDoesNotRequireAssignment()
    {
        var sessionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var executor = new OnvifPlaybackRelayCommandExecutor(new FixedRelayManager(stopped: false));
        var command = new WorkerEdgeCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EdgeCommandTypes.OnvifPlaybackRelayStop,
            "playback_relay_session",
            sessionId,
            JsonSerializer.Serialize(new PlaybackRelayStopCommandPayload(sessionId)),
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "delivery-token");

        var resultJson = await executor.ExecuteAsync(null, command, CancellationToken.None);
        using var result = JsonDocument.Parse(resultJson);

        Assert.Equal(sessionId, result.RootElement.GetProperty("playbackSessionId").GetGuid());
        Assert.False(result.RootElement.GetProperty("stopped").GetBoolean());
    }

    [Fact(DisplayName = "ONVIF 回放控制回写统一传输状态")]
    public async Task ControlReturnsNormalizedTransportState()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var position = DateTimeOffset.Parse("2026-07-13T00:30:00Z");
        var executor = new OnvifPlaybackRelayCommandExecutor(new FixedRelayManager());
        var command = new WorkerEdgeCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            EdgeCommandTypes.OnvifPlaybackRelayControl,
            "playback_relay_session",
            sessionId,
            JsonSerializer.Serialize(new PlaybackRelayControlCommandPayload(
                sessionId,
                cameraId,
                PlaybackTransportAction.Seek,
                position,
                null,
                Guid.NewGuid())),
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "delivery-token");

        var resultJson = await executor.ExecuteAsync(null, command, CancellationToken.None);
        using var result = JsonDocument.Parse(resultJson);

        Assert.Equal(sessionId, result.RootElement.GetProperty("playbackSessionId").GetGuid());
        Assert.Equal(position, result.RootElement.GetProperty("position").GetDateTimeOffset());
        Assert.True(result.RootElement.GetProperty("canPause").GetBoolean());
        Assert.True(result.RootElement.GetProperty("canSeek").GetBoolean());
        Assert.True(result.RootElement.GetProperty("canChangeSpeed").GetBoolean());
    }

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

    private sealed class FixedRelayManager(bool stopped = true) : IOnvifPlaybackRelayManager
    {
        public Task<OnvifPlaybackRelayStartResult> StartAsync(
            WorkerRecorderAssignment assignment,
            WorkerCameraRoute camera,
            WorkerEdgeCommand command,
            PlaybackRelayStartCommandPayload payload,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifPlaybackRelayStartResult(payload.PlaybackSessionId, camera.CameraId, $"playback/{payload.PlaybackSessionId:N}"));

        public Task<bool> StopAsync(Guid playbackSessionId, CancellationToken cancellationToken) => Task.FromResult(stopped);

        public Task<OnvifPlaybackRelayTransportResult> ControlAsync(
            PlaybackRelayControlCommandPayload payload,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifPlaybackRelayTransportResult(
                payload.PlaybackSessionId,
                payload.CameraId,
                payload.Action == PlaybackTransportAction.Pause,
                payload.Position ?? DateTimeOffset.UtcNow,
                payload.Speed ?? 1.0,
                true,
                true,
                true,
                null));
    }
}
