using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class DigitalTwinEndpoints
{
    public static void MapDigitalTwinEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/digital-twin").RequireAuthorization().WithTags("全厂空间数字孪生").AddEndpointFilter<AssetOperationFilter>();

        // 1. 获取所有场景列表
        group.MapGet("/scenes", async (HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.read", context.RequestAborted);
            var scenes = await service.GetScenesAsync(context.RequestAborted, actor);
            return Results.Ok(scenes);
        }).Produces<IReadOnlyList<DigitalTwinSceneDto>>().WithName("ListDigitalTwinScenes");

        // 2. 获取指定场景详情（聚合全部空间节点与光缆路径）
        group.MapGet("/scenes/{id:long}", async (long id, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.read", context.RequestAborted);
            var detail = await service.GetSceneDetailAsync(id, context.RequestAborted, actor);
            return Results.Ok(detail);
        }).Produces<DigitalTwinSceneDetailDto>().WithName("GetDigitalTwinSceneDetail");

        // 3. 获取场景下可选标定候选节点（已放置/未放置）
        group.MapGet("/scenes/{id:long}/candidates", async (long id, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.read", context.RequestAborted);
            var candidates = await service.GetCandidateNodesAsync(id, context.RequestAborted, actor);
            return Results.Ok(candidates);
        }).Produces<IReadOnlyList<CandidateNodeDto>>().WithName("GetDigitalTwinCandidateNodes");

        // 4. 保存单个空间节点标定
        group.MapPost("/scenes/{id:long}/coordinates", async (long id, SaveCoordinateRequest req, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            var node = await service.SaveCoordinateAsync(id, req, actor.UserId.ToString(), context.RequestAborted);
            return Results.Ok(node);
        }).Produces<DigitalTwinNodeDto>().WithName("SaveDigitalTwinCoordinate");

        // 5. 批量保存空间节点标定
        group.MapPost("/scenes/{id:long}/coordinates/batch", async (long id, BatchSaveCoordinatesRequest req, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            var count = await service.BatchSaveCoordinatesAsync(id, req, actor.UserId.ToString(), context.RequestAborted);
            return Results.Ok(new OperationCountResponse(count));
        }).Produces<OperationCountResponse>().WithName("BatchSaveDigitalTwinCoordinates");

        // 6. 移除指定空间标定
        group.MapDelete("/coordinates/{id:long}", async (long id, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            await service.DeleteCoordinateAsync(id, actor.UserId.ToString(), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteDigitalTwinCoordinate");

        // 7. 新增光缆走线路径
        group.MapPost("/scenes/{id:long}/cables", async (long id, SaveCablePathRequest req, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            var cable = await service.SaveCablePathAsync(id, req, null, actor.UserId.ToString(), context.RequestAborted);
            return Results.Created($"/api/v2/digital-twin/cables/{cable.Id}", cable);
        }).Produces<DigitalTwinCableDto>(201).WithName("CreateDigitalTwinCable");

        // 8. 修改光缆走线路径
        group.MapPut("/scenes/{id:long}/cables/{cableId:long}", async (long id, long cableId, SaveCablePathRequest req, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            var cable = await service.SaveCablePathAsync(id, req, cableId, actor.UserId.ToString(), context.RequestAborted);
            return Results.Ok(cable);
        }).Produces<DigitalTwinCableDto>().WithName("UpdateDigitalTwinCable");

        // 9. 删除光缆走线路径
        group.MapDelete("/cables/{cableId:long}", async (long cableId, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            await service.DeleteCablePathAsync(cableId, actor.UserId.ToString(), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteDigitalTwinCable");

        // 10. 智能一键 CAD 自动吸附对齐
        group.MapPost("/scenes/{id:long}/auto-align", async (long id, HttpContext context, DigitalTwinMapService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "map.manage", context.RequestAborted);
            var count = await service.AutoAlignCadAsync(id, actor.UserId.ToString(), context.RequestAborted);
            return Results.Ok(new OperationCountResponse(count));
        }).Produces<OperationCountResponse>().WithName("AutoAlignDigitalTwinCad");
    }
}
