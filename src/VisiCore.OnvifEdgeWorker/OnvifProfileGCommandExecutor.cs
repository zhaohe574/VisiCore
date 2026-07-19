using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public sealed class OnvifProfileGCommandExecutor(IOnvifProfileGClient profileGClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32
    };

    public async Task<string> ExecuteAsync(
        WorkerRecorderAssignment? assignment,
        WorkerEdgeCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.CommandType, EdgeCommandTypes.OnvifRecordingSearch, StringComparison.Ordinal))
        {
            throw new NotSupportedException("当前 ONVIF Profile G 执行器不支持该命令类型。 ");
        }
        var payload = JsonSerializer.Deserialize<RecordingSearchCommandPayload>(command.PayloadJson, JsonOptions)
            ?? throw new JsonException("ONVIF Profile G 检索命令载荷无效。 ");
        var currentAssignment = assignment ?? throw new InvalidOperationException("命令录像机已不在当前 ONVIF 边缘 Worker 分配中。 ");
        var camera = currentAssignment.Cameras.SingleOrDefault(item => item.CameraId == payload.CameraId)
            ?? throw new InvalidOperationException("检索目标摄像头不在当前 ONVIF 录像机分配中。 ");
        var result = await profileGClient.SearchAsync(currentAssignment, camera, payload, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            result.CameraId,
            result.RecordingToken,
            result.SourceToken,
            coverageApproximate = true,
            segments = result.Segments.Select(item => new
            {
                vendorSegmentId = $"{item.RecordingToken}:{item.TrackToken}",
                item.StartedAt,
                item.EndedAt,
                item.TrackType,
                item.CoverageApproximate
            }),
            observedAt = DateTimeOffset.UtcNow
        }, JsonOptions);
    }
}
