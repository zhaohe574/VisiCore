using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;

/// <summary>
/// 设备插件安装、启停和删除端点。
/// </summary>
public sealed class PluginEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var plugins = endpoints.MapGroup("/api/v1/admin/device-plugins")
            .RequireAuthorization()
            .RequireSystemPermission(SystemPermission.ManageAssets);

        plugins.MapGet("/", async (PlatformDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var values = await dbContext.DevicePlugins.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
            var usages = await dbContext.Recorders.AsNoTracking()
                .Where(item => item.DevicePluginId != null)
                .GroupBy(item => item.DevicePluginId!.Value)
                .Select(group => new { PluginId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.PluginId, item => item.Count, cancellationToken);
            return Results.Ok(values.Select(item => ToResponse(item, usages.GetValueOrDefault(item.Id))));
        });

        plugins.MapPost("/install", async (InstallDevicePluginRequest request, ClaimsPrincipal principal, DevicePluginService pluginService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                var plugin = await pluginService.InstallAsync(request.Manifest, cancellationToken);
                await auditService.WriteAsync(principal, "device-plugin.install", "device_plugin", plugin.Id, new { plugin.Key, plugin.Version, plugin.ProtocolType, plugin.RuntimeType, plugin.PackageHash }, cancellationToken);
                return Results.Ok(ToResponse(plugin, 0));
            }
            catch (ArgumentException exception)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "插件清单无效", "plugin_manifest_invalid", exception.Message, new Dictionary<string, string[]> { ["manifest"] = [exception.Message] });
            }
            catch (InvalidOperationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "插件安装冲突", "plugin_install_conflict", exception.Message);
            }
        });

        plugins.MapPatch("/{pluginId:guid}/status", async (Guid pluginId, SetDevicePluginStatusRequest request, ClaimsPrincipal principal, DevicePluginService pluginService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                var plugin = await pluginService.SetEnabledAsync(pluginId, request.Enabled, cancellationToken);
                await auditService.WriteAsync(principal, request.Enabled ? "device-plugin.enable" : "device-plugin.disable", "device_plugin", plugin.Id, new { plugin.Key, plugin.Version }, cancellationToken);
                return Results.Ok(ToResponse(plugin, 0));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "插件状态变更冲突", "plugin_status_conflict", exception.Message);
            }
        });

        plugins.MapDelete("/{pluginId:guid}", async (Guid pluginId, ClaimsPrincipal principal, DevicePluginService pluginService, AuditService auditService, CancellationToken cancellationToken) =>
        {
            try
            {
                await pluginService.RemoveAsync(pluginId, cancellationToken);
                await auditService.WriteAsync(principal, "device-plugin.remove", "device_plugin", pluginId, new { pluginId }, cancellationToken);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "插件删除冲突", "plugin_remove_conflict", exception.Message);
            }
        });
    }

    private static DevicePluginResponse ToResponse(DevicePluginEntity plugin, int usageCount) => new(
        plugin.Id,
        plugin.Key,
        plugin.Name,
        plugin.Version,
        plugin.ProtocolType,
        plugin.RuntimeType,
        plugin.AdapterType,
        plugin.Vendor,
        plugin.Description,
        plugin.PackageHash,
        plugin.IsBuiltIn,
        plugin.Enabled,
        plugin.InstalledAt,
        plugin.UpdatedAt,
        usageCount,
        DevicePluginService.ParseManifest(plugin));
}
