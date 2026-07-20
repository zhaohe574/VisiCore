using System.Text.Json;
using VisiCore.Core;
using VisiCore.OnvifEdgeWorker;
using Xunit;

namespace VisiCore.OnvifEdgeWorker.Tests;

public sealed class OnvifProfileGCommandExecutorTests
{
    [Fact(DisplayName = "Profile G 检索命令只回写受控近似覆盖信息")]
    public async Task ExecuteReturnsApproximateCoverageWithoutDeviceUri()
    {
        var executor = new OnvifProfileGCommandExecutor(new FixedProfileGClient());
        var camera = CreateCamera();
        var command = new WorkerEdgeCommand(
            Guid.NewGuid(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EdgeCommandTypes.OnvifRecordingSearch,
            "recording_search",
            Guid.NewGuid(),
            JsonSerializer.Serialize(new RecordingSearchCommandPayload(camera.CameraId, DateTimeOffset.Parse("2026-07-13T00:00:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 10)),
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "delivery-token");

        var resultJson = await executor.ExecuteAsync(CreateAssignment(camera), command, CancellationToken.None);
        using var result = JsonDocument.Parse(resultJson);

        Assert.True(result.RootElement.GetProperty("coverageApproximate").GetBoolean());
        Assert.Equal(camera.CameraId, result.RootElement.GetProperty("cameraId").GetGuid());
        Assert.Equal("recording-north", result.RootElement.GetProperty("recordingToken").GetString());
        Assert.DoesNotContain("rtsp", resultJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", resultJson, StringComparison.OrdinalIgnoreCase);
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

    private sealed class FixedProfileGClient : IOnvifProfileGClient
    {
        public Task<OnvifProfileGProbeResult> ProbeAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifProfileGProbeResult("recording-north", "source-north"));

        public Task<OnvifProfileGSearchResult> SearchAsync(
            WorkerRecorderAssignment assignment,
            WorkerCameraRoute camera,
            RecordingSearchCommandPayload payload,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifProfileGSearchResult(
                camera.CameraId,
                "recording-north",
                "source-north",
                [new OnvifProfileGSegment("recording-north", "video-north", "Video", payload.StartedAt, payload.EndedAt, true)]));

        public Task<Uri> GetReplayUriAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, CancellationToken cancellationToken) =>
            Task.FromResult(new Uri("rtsp://nvr.example:554/replay"));

        public Task<OnvifProfileGReplaySource> GetReplaySourceAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, CancellationToken cancellationToken) =>
            Task.FromResult(new OnvifProfileGReplaySource(new Uri("rtsp://nvr.example:554/replay"), "viewer", "not-a-real-password"));
    }
}
