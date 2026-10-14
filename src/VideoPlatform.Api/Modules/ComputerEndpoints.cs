using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class ComputerEndpoints
{
    public static void MapComputerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/computers").RequireAuthorization().WithTags("电脑台账");
        group.MapPost("/batch-update", async (ComputerBatchUpdateRequest request, HttpContext context, AssetMutationService service) =>
            Results.Ok(await service.UpdateComputersAsync(ApiSupport.Actor(context), request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<AssetBatchResultDto>().WithName("BatchUpdateComputers");

        group.MapGet("", async (HttpContext context, ComputerService service, AccessService access,
            long? workshopId, long? areaId, long? unitId, string? status) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.read", context.RequestAborted);
            var (page, size, _) = ApiSupport.Pagination(context);
            var search = ApiSupport.Search(context);
            var result = await service.ListAsync(page, size, search, workshopId, areaId, unitId, status, context.RequestAborted, actor);
            return Results.Ok(result);
        }).Produces<PagedResponse<ComputerDto>>().WithName("ListComputers");

        group.MapGet("/{id:long}", async (long id, HttpContext context, ComputerService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.read", context.RequestAborted);
            var comp = await service.GetAsync(id, context.RequestAborted, actor);
            return Results.Ok(comp);
        }).Produces<ComputerDto>().WithName("GetComputer");

        group.MapPost("", async (ComputerRequest request, HttpContext context, ComputerService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.manage", context.RequestAborted);
            var created = await service.CreateAsync(request, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted, actor);
            return Results.Created($"/api/v2/computers/{created.Id}", created);
        }).Produces<ComputerDto>(201).WithName("CreateComputer");

        group.MapPut("/{id:long}", async (long id, ComputerRequest request, HttpContext context, ComputerService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.manage", context.RequestAborted);
            var updated = await service.UpdateAsync(id, request, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted, actor);
            return Results.Ok(updated);
        }).Produces<ComputerDto>().WithName("UpdateComputer");

        group.MapDelete("/{id:long}", async (long id, HttpContext context, ComputerService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.manage", context.RequestAborted);
            await service.DeleteAsync(id, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted, actor);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteComputer");

        group.MapPost("/{id:long}/probe", async (long id, HttpContext context, ComputerService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.read", context.RequestAborted);
            var probe = await service.ProbeAsync(id, context.RequestAborted, actor);
            return Results.Ok(probe);
        }).Produces<ComputerProbeResultDto>().WithName("ProbeComputer");

        group.MapPost("/probe-all", async (HttpContext context, ComputerService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "computer.read", context.RequestAborted);
            var probes = await service.ProbeAllAsync(context.RequestAborted, actor);
            return Results.Ok(probes);
        }).Produces<IReadOnlyList<ComputerProbeResultDto>>().WithName("ProbeAllComputers");
    }
}
