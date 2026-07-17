using System.Collections.Concurrent;
using VideoPlatform.Core;

namespace VideoPlatform.OnvifEdgeWorker;

public sealed class OnvifOperationReadinessValidator(
    IOnvifProfileGClient profileGClient,
    IOnvifFfmpegPlaybackRelay ffmpegRelay,
    OnvifEdgeOptions options)
{
    private readonly ConcurrentDictionary<(Guid RecorderId, string OperationType), CachedStatus> statusCache = new();

    public Task<WorkerOperationStatus> GetRecordingSearchStatusAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken) =>
        GetCachedAsync(
            assignment,
            EdgeOperationTypes.OnvifRecordingSearch,
            ValidateRecordingSearchAsync,
            cancellationToken);

    public Task<WorkerOperationStatus> GetPlaybackRelayStatusAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken) =>
        GetCachedAsync(
            assignment,
            EdgeOperationTypes.OnvifPlaybackRelay,
            ValidatePlaybackRelayAsync,
            cancellationToken);

    private async Task<WorkerOperationStatus> GetCachedAsync(
        WorkerRecorderAssignment assignment,
        string operationType,
        Func<WorkerRecorderAssignment, CancellationToken, Task<WorkerOperationStatus>> validator,
        CancellationToken cancellationToken)
    {
        var key = (assignment.RecorderId, operationType);
        var now = DateTimeOffset.UtcNow;
        if (statusCache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Status;
        }

        var status = await validator(assignment, cancellationToken);
        var lifetime = status.IsReady
            ? TimeSpan.FromMinutes(options.PlaybackRelay.ValidationCacheMinutes)
            : TimeSpan.FromMinutes(1);
        statusCache[key] = new CachedStatus(status, now.Add(lifetime));
        return status;
    }

    private async Task<WorkerOperationStatus> ValidateRecordingSearchAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken)
    {
        if (!options.ProfileG.Enabled)
        {
            return Unready(EdgeOperationTypes.OnvifRecordingSearch, assignment.RecorderId, "disabled_by_policy");
        }
        var camera = assignment.Cameras.FirstOrDefault();
        if (camera is null)
        {
            return Unready(EdgeOperationTypes.OnvifRecordingSearch, assignment.RecorderId, "camera_missing");
        }
        try
        {
            _ = await profileGClient.ProbeAsync(assignment, camera, cancellationToken);
            return new WorkerOperationStatus(EdgeOperationTypes.OnvifRecordingSearch, true, null, assignment.RecorderId);
        }
        catch (NotSupportedException)
        {
            return Unready(EdgeOperationTypes.OnvifRecordingSearch, assignment.RecorderId, "configuration_invalid");
        }
        catch
        {
            return Unready(EdgeOperationTypes.OnvifRecordingSearch, assignment.RecorderId, "validation_required");
        }
    }

    private async Task<WorkerOperationStatus> ValidatePlaybackRelayAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken)
    {
        if (!options.ProfileG.Enabled || !options.PlaybackRelay.Enabled)
        {
            return Unready(EdgeOperationTypes.OnvifPlaybackRelay, assignment.RecorderId, "disabled_by_policy");
        }
        if (!options.PlaybackRelay.IsRecorderExplicitlyValidated(assignment.RecorderId))
        {
            return Unready(EdgeOperationTypes.OnvifPlaybackRelay, assignment.RecorderId, "validation_required");
        }
        var camera = assignment.Cameras.FirstOrDefault();
        if (camera is null)
        {
            return Unready(EdgeOperationTypes.OnvifPlaybackRelay, assignment.RecorderId, "camera_missing");
        }
        try
        {
            options.PlaybackRelay.ValidateEnabled();
            await ffmpegRelay.EnsureRtspClockRangeSupportedAsync(cancellationToken);
            _ = await profileGClient.GetReplaySourceAsync(assignment, camera, cancellationToken);
            return new WorkerOperationStatus(EdgeOperationTypes.OnvifPlaybackRelay, true, null, assignment.RecorderId);
        }
        catch (NotSupportedException)
        {
            return Unready(EdgeOperationTypes.OnvifPlaybackRelay, assignment.RecorderId, "configuration_invalid");
        }
        catch
        {
            return Unready(EdgeOperationTypes.OnvifPlaybackRelay, assignment.RecorderId, "validation_required");
        }
    }

    private static WorkerOperationStatus Unready(string operationType, Guid recorderId, string failureKind) =>
        new(operationType, false, failureKind, recorderId);

    private sealed record CachedStatus(WorkerOperationStatus Status, DateTimeOffset ExpiresAt);
}
