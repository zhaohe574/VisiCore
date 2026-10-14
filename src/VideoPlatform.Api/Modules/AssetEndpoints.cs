using VideoPlatform.Contracts;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class AssetEndpoints
{
    public static void MapAssetEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/assets").RequireAuthorization().WithTags("资产工作台");
        group.MapGet("/search", async (HttpContext context, AssetQueryService service, string? search,
            string? type, long? workshopId, long? areaId, long? unitId, string? status) =>
        {
            var (page, size, _) = ApiSupport.Pagination(context);
            return Results.Ok(await service.SearchAsync(ApiSupport.Actor(context), page, size, search, type, workshopId, areaId, unitId, status, context.RequestAborted));
        }).Produces<PagedResponse<AssetDto>>().WithName("SearchAssets");
        group.MapGet("/summary", async (HttpContext context, AssetQueryService service, string? search,
            string? type, long? workshopId, long? areaId, long? unitId, string? status) =>
            Results.Ok(await service.SummaryAsync(ApiSupport.Actor(context), search, type, workshopId, areaId, unitId, status, context.RequestAborted)))
            .Produces<AssetSummaryDto>().WithName("GetAssetSummary");
        group.MapGet("/{type}/{id:long}/context", async (HttpContext context, AssetQueryService service, string type, long id) =>
            Results.Ok(await service.ContextAsync(ApiSupport.Actor(context), type, id, context.RequestAborted)))
            .Produces<AssetContextDto>().WithName("GetAssetContext");
    }
}
