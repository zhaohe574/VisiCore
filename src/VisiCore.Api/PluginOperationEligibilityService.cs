using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

/// <summary>
/// 外部边缘插件的能力门禁。中心只依据已签名插件声明、设备绑定与边缘就绪状态路由操作，
/// 不加载或执行插件二进制。
/// </summary>
public sealed class PluginOperationEligibilityService(
    PlatformDbContext dbContext,
    EdgeOperationReadinessService readinessService,
    DevicePluginCapabilityService? capabilityService = null)
{
    public async Task<bool> CanRouteAsync(Guid recorderId, string operationType, CancellationToken cancellationToken) =>
        await GetReadyWorkerIdAsync(recorderId, operationType, cancellationToken) is not null;

    public async Task<Guid?> GetReadyWorkerIdAsync(Guid recorderId, string operationType, CancellationToken cancellationToken)
    {
        if (!EdgeOperationTypes.KnownTypes.Contains(operationType) ||
            !await SupportsOperationAsync(recorderId, operationType, cancellationToken) ||
            !await dbContext.Recorders.AsNoTracking().AnyAsync(
                item => item.Id == recorderId &&
                    dbContext.DevicePlugins.Any(plugin =>
                        plugin.Id == item.DevicePluginId &&
                        plugin.Enabled &&
                        plugin.RuntimeType == DevicePluginRuntimeTypes.ExternalEdge),
                cancellationToken))
        {
            return null;
        }

        return await readinessService.GetReadyWorkerIdAsync(recorderId, operationType, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetExportEligibleRecorderIdsAsync(
        IEnumerable<Guid> recorderIds,
        CancellationToken cancellationToken)
    {
        var candidates = recorderIds.Where(item => item != Guid.Empty).Distinct().ToArray();
        if (candidates.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var externalRecorders = await dbContext.Recorders.AsNoTracking()
            .Where(item => candidates.Contains(item.Id) &&
                dbContext.DevicePlugins.Any(plugin =>
                    plugin.Id == item.DevicePluginId &&
                    plugin.Enabled &&
                    plugin.RuntimeType == DevicePluginRuntimeTypes.ExternalEdge))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var eligible = new List<Guid>();
        foreach (var recorderId in externalRecorders)
        {
            if (await SupportsOperationAsync(recorderId, EdgeOperationTypes.PluginPlaybackExport, cancellationToken))
            {
                eligible.Add(recorderId);
            }
        }
        return await readinessService.GetReadyRecorderIdsAsync(
            eligible,
            EdgeOperationTypes.PluginPlaybackExport,
            cancellationToken);
    }

    private Task<bool> SupportsOperationAsync(Guid recorderId, string operationType, CancellationToken cancellationToken)
    {
        var capability = operationType switch
        {
            EdgeOperationTypes.PluginPlaybackExport => DevicePluginCapabilityKind.Export,
            EdgeOperationTypes.PluginPtz => DevicePluginCapabilityKind.Ptz,
            _ => DevicePluginCapabilityKind.Playback
        };
        return (capabilityService ?? new DevicePluginCapabilityService(dbContext))
            .SupportsAsync(recorderId, capability, cancellationToken);
    }
}
