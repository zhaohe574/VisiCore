using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public enum DevicePluginCapabilityKind
{
    LiveView,
    Playback,
    Ptz,
    Export
}

public sealed class DevicePluginCapabilityService(PlatformDbContext dbContext)
{
    public async Task<bool> SupportsAsync(
        Guid recorderId,
        DevicePluginCapabilityKind capability,
        CancellationToken cancellationToken) =>
        (await GetSupportedRecorderIdsAsync([recorderId], capability, cancellationToken)).Contains(recorderId);

    public async Task<IReadOnlySet<Guid>> GetSupportedRecorderIdsAsync(
        IEnumerable<Guid> recorderIds,
        DevicePluginCapabilityKind capability,
        CancellationToken cancellationToken)
    {
        var requested = recorderIds.Where(item => item != Guid.Empty).Distinct().ToArray();
        if (requested.Length == 0)
        {
            return new HashSet<Guid>();
        }
        var recorders = await dbContext.Recorders.AsNoTracking()
            .Where(item => requested.Contains(item.Id))
            .Select(item => new { item.Id, item.DevicePluginId })
            .ToListAsync(cancellationToken);
        var pluginIds = recorders.Where(item => item.DevicePluginId != null)
            .Select(item => item.DevicePluginId!.Value)
            .Distinct()
            .ToArray();
        var plugins = await dbContext.DevicePlugins.AsNoTracking()
            .Where(item => pluginIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var supported = new HashSet<Guid>();
        foreach (var recorder in recorders)
        {
            if (recorder.DevicePluginId is null)
            {
                // 未迁移的历史设备继续按既有 AdapterType 和 Worker 就绪状态判断。
                supported.Add(recorder.Id);
                continue;
            }
            if (!plugins.TryGetValue(recorder.DevicePluginId.Value, out var plugin) || !plugin.Enabled)
            {
                continue;
            }
            if (TryReadCapability(plugin, capability))
            {
                supported.Add(recorder.Id);
            }
        }
        return supported;
    }

    private static bool TryReadCapability(DevicePluginEntity plugin, DevicePluginCapabilityKind capability)
    {
        try
        {
            var declared = DevicePluginService.NormalizeAndValidate(DevicePluginService.ParseManifest(plugin)).Capabilities;
            return capability switch
            {
                DevicePluginCapabilityKind.LiveView => declared.LiveView,
                DevicePluginCapabilityKind.Playback => declared.Playback,
                DevicePluginCapabilityKind.Ptz => declared.Ptz,
                DevicePluginCapabilityKind.Export => declared.Export,
                _ => false
            };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
