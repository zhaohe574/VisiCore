using System.Text.Json;
using VideoPlatform.Core;

namespace VideoPlatform.OnvifEdgeWorker;

public sealed class OnvifPtzCommandExecutor(OnvifPtzWatchdog ptzWatchdog)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public async Task<string> ExecuteAsync(
        WorkerRecorderAssignment? assignment,
        WorkerEdgeCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.CommandType, EdgeCommandTypes.OnvifPtzControl, StringComparison.Ordinal))
        {
            throw new NotSupportedException("当前 ONVIF 边缘 Worker 不支持该命令类型。 ");
        }
        var payload = JsonSerializer.Deserialize<PtzControlCommandPayload>(command.PayloadJson, JsonOptions)
            ?? throw new JsonException("ONVIF PTZ 命令载荷无效。 ");
        if (payload.Motion == PtzMotion.Start)
        {
            var currentAssignment = RequireAssignment(assignment);
            var camera = FindCamera(currentAssignment, payload.CameraId);
            await ptzWatchdog.StartAsync(currentAssignment, camera, payload, cancellationToken);
        }
        else if (payload.Motion == PtzMotion.Stop)
        {
            if (!await ptzWatchdog.StopActiveAsync(payload.CameraId, cancellationToken))
            {
                var currentAssignment = RequireAssignment(assignment);
                var camera = FindCamera(currentAssignment, payload.CameraId);
                await ptzWatchdog.StopAsync(currentAssignment, camera, payload.Action, cancellationToken);
            }
        }
        else
        {
            throw new InvalidOperationException("ONVIF PTZ 动作类型无效。 ");
        }
        return JsonSerializer.Serialize(new { payload.LeaseId, payload.CameraId, payload.Action, payload.Motion, payload.Sequence }, JsonOptions);
    }

    private static WorkerRecorderAssignment RequireAssignment(WorkerRecorderAssignment? assignment) =>
        assignment ?? throw new InvalidOperationException("命令录像机已不在当前 ONVIF 边缘 Worker 分配中。 ");

    private static WorkerCameraRoute FindCamera(WorkerRecorderAssignment assignment, Guid cameraId) =>
        assignment.Cameras.SingleOrDefault(item => item.CameraId == cameraId)
        ?? throw new InvalidOperationException("PTZ 命令目标摄像头不在当前录像机分配中。 ");
}
