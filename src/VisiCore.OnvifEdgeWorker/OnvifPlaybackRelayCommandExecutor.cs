using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public sealed class OnvifPlaybackRelayCommandExecutor(IOnvifPlaybackRelayManager playbackRelayManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    public Task<string> ExecuteAsync(
        WorkerRecorderAssignment? assignment,
        WorkerEdgeCommand command,
        CancellationToken cancellationToken) =>
        command.CommandType switch
        {
            EdgeCommandTypes.OnvifPlaybackRelayStart => ExecuteStartAsync(RequireAssignment(assignment), command, cancellationToken),
            EdgeCommandTypes.OnvifPlaybackRelayStop => ExecuteStopAsync(command.PayloadJson, cancellationToken),
            EdgeCommandTypes.OnvifPlaybackRelayControl => ExecuteControlAsync(command.PayloadJson, cancellationToken),
            _ => throw new NotSupportedException("当前 ONVIF 回放执行器不支持该命令类型。 ")
        };

    private async Task<string> ExecuteStartAsync(
        WorkerRecorderAssignment assignment,
        WorkerEdgeCommand command,
        CancellationToken cancellationToken)
    {
        var payload = Deserialize<PlaybackRelayStartCommandPayload>(command.PayloadJson);
        var camera = assignment.Cameras.SingleOrDefault(item => item.CameraId == payload.CameraId)
            ?? throw new InvalidOperationException("回放目标摄像头不在当前录像机分配中。 ");
        var result = await playbackRelayManager.StartAsync(assignment, camera, command, payload, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            result.PlaybackSessionId,
            result.CameraId,
            result.PathName,
            startedAt = DateTimeOffset.UtcNow
        }, JsonOptions);
    }

    private async Task<string> ExecuteStopAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<PlaybackRelayStopCommandPayload>(payloadJson);
        if (payload.PlaybackSessionId == Guid.Empty)
        {
            throw new InvalidOperationException("回放中继停止命令缺少会话编号。 ");
        }
        var stopped = await playbackRelayManager.StopAsync(payload.PlaybackSessionId, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            payload.PlaybackSessionId,
            stopped,
            stoppedAt = DateTimeOffset.UtcNow
        }, JsonOptions);
    }

    private async Task<string> ExecuteControlAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = Deserialize<PlaybackRelayControlCommandPayload>(payloadJson);
        var result = await playbackRelayManager.ControlAsync(payload, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            result.PlaybackSessionId,
            result.CameraId,
            result.IsPaused,
            position = result.Position,
            result.Speed,
            result.CanPause,
            result.CanSeek,
            result.CanChangeSpeed,
            result.Detail,
            completedAt = DateTimeOffset.UtcNow
        }, JsonOptions);
    }

    private static WorkerRecorderAssignment RequireAssignment(WorkerRecorderAssignment? assignment) =>
        assignment ?? throw new InvalidOperationException("命令录像机已不在当前 ONVIF 边缘 Worker 分配中。 ");

    private static T Deserialize<T>(string payloadJson)
    {
        if (payloadJson.Length > 64 * 1024)
        {
            throw new InvalidOperationException("边缘命令载荷超过限制。 ");
        }
        return JsonSerializer.Deserialize<T>(payloadJson, JsonOptions)
            ?? throw new JsonException("边缘命令载荷无效。 ");
    }
}
