using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public enum RecorderOperation
{
    RecordingSearch,
    PlaybackRelay
}

public sealed record ReadyRecorderOperationRoute(
    Guid WorkerId,
    string CommandType,
    string ReadinessOperationType);

public sealed class RecorderOperationRoutingService(
    PlatformDbContext dbContext,
    EdgeOperationReadinessService readinessService,
    DevicePluginCapabilityService? capabilityService = null)
{
    public async Task<ReadyRecorderOperationRoute?> GetReadyRouteAsync(
        Guid recorderId,
        RecorderOperation operation,
        CancellationToken cancellationToken)
    {
        var pluginCapabilities = capabilityService ?? new DevicePluginCapabilityService(dbContext);
        if (!await pluginCapabilities.SupportsAsync(
                recorderId,
                DevicePluginCapabilityKind.Playback,
                cancellationToken))
        {
            return null;
        }
        var metadata = await dbContext.Recorders.AsNoTracking()
            .Where(item => item.Id == recorderId)
            .Select(item => new
            {
                item.AdapterType,
                PluginRuntimeType = dbContext.DevicePlugins
                    .Where(plugin => plugin.Id == item.DevicePluginId)
                    .Select(plugin => plugin.RuntimeType)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        var route = Resolve(metadata?.PluginRuntimeType, metadata?.AdapterType, operation);
        if (route is null)
        {
            return null;
        }

        var workerId = await readinessService.GetReadyWorkerIdAsync(
            recorderId,
            route.ReadinessOperationType,
            cancellationToken);
        return workerId is null
            ? null
            : new ReadyRecorderOperationRoute(workerId.Value, route.CommandType, route.ReadinessOperationType);
    }

    private static RecorderOperationRoute? Resolve(string? pluginRuntimeType, string? adapterType, RecorderOperation operation) =>
        (pluginRuntimeType, operation) switch
        {
            (DevicePluginRuntimeTypes.Onvif, RecorderOperation.RecordingSearch) =>
                new(EdgeCommandTypes.OnvifRecordingSearch, EdgeOperationTypes.OnvifRecordingSearch),
            (DevicePluginRuntimeTypes.Onvif, RecorderOperation.PlaybackRelay) =>
                new(EdgeCommandTypes.OnvifPlaybackRelayStart, EdgeOperationTypes.OnvifPlaybackRelay),
            (DevicePluginRuntimeTypes.ExternalEdge, RecorderOperation.RecordingSearch) =>
                new(EdgeCommandTypes.PluginRecordingSearch, EdgeOperationTypes.PluginRecordingSearch),
            (DevicePluginRuntimeTypes.ExternalEdge, RecorderOperation.PlaybackRelay) =>
                new(EdgeCommandTypes.PluginPlaybackRelayStart, EdgeOperationTypes.PluginPlaybackRelay),
            _ => null
        };

    private sealed record RecorderOperationRoute(string CommandType, string ReadinessOperationType);
}
