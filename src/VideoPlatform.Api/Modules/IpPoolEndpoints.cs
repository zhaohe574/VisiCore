using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class IpPoolEndpoints
{
    public static void MapIpPoolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/ip-pools").RequireAuthorization().WithTags("IP池管理").AddEndpointFilter<AssetOperationFilter>();

        group.MapGet("", async (HttpContext context, IpPoolService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.read", context.RequestAborted);
            var result = await service.ListAsync(context.RequestAborted, actor);
            return Results.Ok(result);
        }).Produces<IReadOnlyList<IpPoolDto>>().WithName("ListIpPools");

        group.MapGet("/{id:long}", async (long id, HttpContext context, IpPoolService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.read", context.RequestAborted);
            var pool = await service.GetAsync(id, context.RequestAborted);
            return Results.Ok(pool);
        }).Produces<IpPoolDto>().WithName("GetIpPool");

        group.MapPost("", async (IpPoolRequest request, HttpContext context, IpPoolService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.manage", context.RequestAborted);
            var created = await service.CreateAsync(request, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/ip-pools/{created.Id}", created);
        }).Produces<IpPoolDto>(201).WithName("CreateIpPool");

        group.MapPut("/{id:long}", async (long id, IpPoolRequest request, HttpContext context, IpPoolService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.manage", context.RequestAborted);
            var updated = await service.UpdateAsync(id, request, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(updated);
        }).Produces<IpPoolDto>().WithName("UpdateIpPool");

        group.MapDelete("/{id:long}", async (long id, HttpContext context, IpPoolService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.manage", context.RequestAborted);
            await service.DeleteAsync(id, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteIpPool");

        group.MapGet("/{id:long}/detail", async (long id, HttpContext context, IpPoolService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.read", context.RequestAborted);
            var detail = await service.GetDetailAsync(id, context.RequestAborted, actor);
            return Results.Ok(detail);
        }).Produces<IpPoolDetailDto>().WithName("GetIpPoolDetail");

        group.MapPost("/{id:long}/scan", async (long id, HttpContext context, IpPoolService service, AccessService access, string? subnetSlice) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.read", context.RequestAborted);
            var scan = await service.ScanAsync(id, subnetSlice, context.RequestAborted);
            return Results.Ok(scan);
        }).Produces<IpPoolScanResultDto>().WithName("ScanIpPool");

        group.MapGet("/check-ip", async (HttpContext context, IpPoolService service, AccessService access, string ip, string? excludeType, long? excludeId) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.read", context.RequestAborted);
            return Results.Ok(await service.CheckIpConflictAsync(ip, excludeType, excludeId, context.RequestAborted, actor));
        }).Produces<IpConflictCheckResultDto>().WithName("CheckIpConflict");

        group.MapGet("/recommend-ip", async (HttpContext context, IpPoolService service, AccessService access, long? workshopId, long? poolId) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "ip.read", context.RequestAborted);
            return Results.Ok(await service.RecommendIpAsync(workshopId, poolId, context.RequestAborted, actor));
        }).Produces<IpRecommendResultDto>().WithName("RecommendFreeIp");
    }
}
