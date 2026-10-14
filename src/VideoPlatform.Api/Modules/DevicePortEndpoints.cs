using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class DevicePortEndpoints
{
    public static void MapDevicePortEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("设备端口与链路").AddEndpointFilter<AssetOperationFilter>();

        // 1. 获取指定设备的所有端口
        group.MapGet("/devices/{deviceType}/{deviceId:long}/ports", async (string deviceType, long deviceId, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var ports = await service.ListPortsAsync(deviceType, deviceId, context.RequestAborted, actor);
            return Results.Ok(ports);
        }).Produces<IReadOnlyList<DevicePortDto>>().WithName("ListDevicePorts");

        // 2. 批量动态生成端口（自由输入数量）
        group.MapPost("/devices/{deviceType}/{deviceId:long}/ports/batch-generate", async (string deviceType, long deviceId, BatchGeneratePortsRequest req, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device_port.manage", context.RequestAborted);
            var ports = await service.BatchGeneratePortsAsync(deviceType, deviceId, req, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(ports);
        }).Produces<IReadOnlyList<DevicePortDto>>().WithName("BatchGenerateDevicePorts");

        // 3. 单个新增端口
        group.MapPost("/devices/{deviceType}/{deviceId:long}/ports", async (string deviceType, long deviceId, DevicePortRequest req, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device_port.manage", context.RequestAborted);
            var created = await service.CreatePortAsync(deviceType, deviceId, req, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/device-ports/{created.Id}", created);
        }).Produces<DevicePortDto>(201).WithName("CreateDevicePort");

        // 4. 更新单端口
        group.MapPut("/device-ports/{id:long}", async (long id, DevicePortRequest req, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device_port.manage", context.RequestAborted);
            var updated = await service.UpdatePortAsync(id, req, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(updated);
        }).Produces<DevicePortDto>().WithName("UpdateDevicePort");

        // 5. 删除单端口
        group.MapDelete("/device-ports/{id:long}", async (long id, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device_port.manage", context.RequestAborted);
            await service.DeletePortAsync(id, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteDevicePort");

        // 6. 建立点对点跳线链路
        group.MapPost("/device-links/connect", async (ConnectLinkRequest req, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device_port.manage", context.RequestAborted);
            var link = await service.ConnectLinkAsync(req, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(link);
        }).Produces<DeviceLinkDto>().WithName("ConnectDeviceLink");

        // 7. 断开跳线链路
        group.MapDelete("/device-links/{id:long}", async (long id, HttpContext context, DevicePortService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device_port.manage", context.RequestAborted);
            await service.DisconnectLinkAsync(id, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DisconnectDeviceLink");

        // 8. 查询跳线对端候选端口
        group.MapGet("/device-ports/candidates", async (HttpContext context, DevicePortService service, AccessService access, string? search, string? deviceType) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var candidates = await service.GetPortCandidatesAsync(search, deviceType, context.RequestAborted, actor);
            return Results.Ok(candidates);
        }).Produces<IReadOnlyList<PortCandidateDto>>().WithName("GetPortCandidates");
    }
}
