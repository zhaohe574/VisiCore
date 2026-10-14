using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class NetworkDeviceEndpoints
{
    public static void MapNetworkDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/network-devices").RequireAuthorization().WithTags("网络设备").AddEndpointFilter<AssetOperationFilter>();

        group.MapGet("", async (HttpContext context, NetworkDeviceService service, AccessService access,
            string? deviceType, string? status) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var (page, size, _) = ApiSupport.Pagination(context);
            var search = ApiSupport.Search(context);
            var result = await service.ListAsync(page, size, search, deviceType, status, context.RequestAborted, actor);
            return Results.Ok(result);
        }).Produces<PagedResponse<NetworkDeviceDto>>().WithName("ListNetworkDevices");

        group.MapGet("/{id:long}", async (long id, HttpContext context, NetworkDeviceService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var device = await service.GetAsync(id, context.RequestAborted, actor);
            return Results.Ok(device);
        }).Produces<NetworkDeviceDto>().WithName("GetNetworkDevice");

        group.MapPost("", async (NetworkDeviceRequest request, HttpContext context, NetworkDeviceService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.manage", context.RequestAborted);
            var created = await service.CreateAsync(request, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/network-devices/{created.Id}", await service.GetAsync(created.Id, context.RequestAborted, actor));
        }).Produces<NetworkDeviceDto>(201).WithName("CreateNetworkDevice");

        group.MapPut("/{id:long}", async (long id, NetworkDeviceRequest request, HttpContext context, NetworkDeviceService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.manage", context.RequestAborted);
            var updated = await service.UpdateAsync(id, request, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(await service.GetAsync(updated.Id, context.RequestAborted, actor));
        }).Produces<NetworkDeviceDto>().WithName("UpdateNetworkDevice");

        group.MapDelete("/{id:long}", async (long id, HttpContext context, NetworkDeviceService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.manage", context.RequestAborted);
            await service.DeleteAsync(id, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteNetworkDevice");

        group.MapPost("/{id:long}/probe", async (long id, HttpContext context, NetworkDeviceService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var result = await service.ProbeAsync(id, context.RequestAborted);
            return Results.Ok(result);
        }).Produces<NetworkDeviceProbeResultDto>().WithName("ProbeNetworkDevice");

        group.MapPost("/probe-all", async (HttpContext context, NetworkDeviceService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var results = await service.ProbeAllAsync(context.RequestAborted, actor);
            return Results.Ok(results);
        }).Produces<IReadOnlyList<NetworkDeviceProbeResultDto>>().WithName("ProbeAllNetworkDevices");
    }
}
