using System.Globalization;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class AdministrationEndpoints
{
    public static void MapAdministrationEndpoints(this WebApplication app)
    {
        // 宿主只需调用映射方法；服务由现有基础依赖构造，避免要求修改 Bootstrap。
        var service = ActivatorUtilities.CreateInstance<AdministrationService>(app.Services);
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("平台管理");

        group.MapGet("/organization", async Task<IResult> (HttpContext context) => Results.Ok(await service.OrganizationAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<OrganizationTreeDto>().WithName("GetOrganization");
        group.MapPost("/organization/{kind}", async (string kind, OrganizationRequest request, HttpContext context) =>
        {
            var result = await service.SaveOrganizationAsync(ApiSupport.Actor(context), kind, null, request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/organization/{kind}/{result.Id}", result);
        }).Produces<OrganizationNodeDto>(201).WithName("CreateOrganizationNode");
        group.MapPut("/organization/{kind}/{id:long}", async (string kind, long id, OrganizationRequest request, HttpContext context) =>
            Results.Ok(await service.SaveOrganizationAsync(ApiSupport.Actor(context), kind, id, request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<OrganizationNodeDto>().WithName("UpdateOrganizationNode");
        group.MapDelete("/organization/{kind}/{id:long}", async (string kind, long id, HttpContext context) =>
        {
            await service.DeleteOrganizationAsync(ApiSupport.Actor(context), kind, id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("DeleteOrganizationNode");
        group.MapPut("/channels/assignment", async (AssignmentRequest request, HttpContext context) =>
        {
            await service.AssignChannelsAsync(ApiSupport.Actor(context), request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("AssignChannels");

        group.MapGet("/users", async (HttpContext context) =>
        {
            var (page, size, _) = ApiSupport.Pagination(context);
            return Results.Ok(await service.UsersAsync(ApiSupport.Actor(context), page, size, ApiSupport.Search(context), context.RequestAborted));
        }).Produces<AdministrationPage<UserDto>>().WithName("GetUsers");
        group.MapPost("/users", async (UserRequest request, HttpContext context) =>
        {
            var result = await service.SaveUserAsync(ApiSupport.Actor(context), null, request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/users/{result.Id}", result);
        }).Produces<UserDto>(201).WithName("CreateUser");
        group.MapPut("/users/{id:long}", async (long id, UserRequest request, HttpContext context) =>
            Results.Ok(await service.SaveUserAsync(ApiSupport.Actor(context), id, request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<UserDto>().WithName("UpdateUser");

        group.MapGet("/roles", async Task<IResult> (HttpContext context) => Results.Ok(await service.RolesAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<IReadOnlyList<AdministrationRoleDto>>().WithName("GetRoles");
        group.MapPost("/roles", async (RoleRequest request, HttpContext context) =>
        {
            var result = await service.SaveRoleAsync(ApiSupport.Actor(context), null, request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/roles/{result.Id}", result);
        }).Produces<AdministrationRoleDto>(201).WithName("CreateRole");
        group.MapPut("/roles/{id:long}", async (long id, RoleRequest request, HttpContext context) =>
            Results.Ok(await service.SaveRoleAsync(ApiSupport.Actor(context), id, request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<AdministrationRoleDto>().WithName("UpdateRole");
        group.MapDelete("/roles/{id:long}", async (long id, HttpContext context) =>
        {
            await service.DeleteRoleAsync(ApiSupport.Actor(context), id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("DeleteRole");
        group.MapGet("/permissions", async Task<IResult> (HttpContext context) => Results.Ok(await service.PermissionsAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<IReadOnlyList<PermissionDto>>().WithName("GetPermissions");
        group.MapPut("/roles/{id:long}/permissions", async (long id, PermissionRequest request, HttpContext context) =>
            Results.Ok(await service.SetPermissionsAsync(ApiSupport.Actor(context), id, request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<AdministrationRoleDto>().WithName("SetRolePermissions");
        group.MapGet("/scopes/{kind}/{id:long}", async (string kind, long id, HttpContext context) =>
            Results.Ok(await service.ScopesAsync(ApiSupport.Actor(context), kind, id, context.RequestAborted)))
            .Produces<ScopeRequest>().WithName("GetScopes");
        group.MapPut("/scopes/{kind}/{id:long}", async (string kind, long id, ScopeRequest request, HttpContext context) =>
            Results.Ok(await service.SaveScopesAsync(ApiSupport.Actor(context), kind, id, request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<ScopeRequest>().WithName("UpdateScopes");

        group.MapGet("/sessions", async (HttpContext context) =>
        {
            var (page, size, _) = ApiSupport.Pagination(context);
            return Results.Ok(await service.SessionsAsync(ApiSupport.Actor(context), page, size, ApiSupport.Search(context), context.RequestAborted));
        }).Produces<AdministrationPage<AdministrationSessionDto>>().WithName("GetSessions");
        group.MapDelete("/sessions/{id:guid}", async (Guid id, HttpContext context) =>
        {
            await service.RevokeSessionAsync(ApiSupport.Actor(context), id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("RevokeSession");
        group.MapGet("/audit", async (HttpContext context) =>
        {
            var (page, size, _) = ApiSupport.Pagination(context);
            return Results.Ok(await service.AuditAsync(ApiSupport.Actor(context), page, size, ApiSupport.Search(context), context.Request.Query["action"], QueryTime(context, "from"), QueryTime(context, "to"), context.RequestAborted));
        }).Produces<AdministrationPage<AdministrationAuditDto>>().WithName("GetAudit");
        group.MapGet("/settings", async Task<IResult> (HttpContext context) => Results.Ok(await service.SettingsAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<PlatformSettings>().WithName("GetSettings");
        group.MapPut("/settings", async (PlatformSettings request, HttpContext context) =>
            Results.Ok(await service.SaveSettingsAsync(ApiSupport.Actor(context), request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<PlatformSettings>().WithName("UpdateSettings");
        group.MapGet("/dashboard", async Task<IResult> (HttpContext context) => Results.Ok(await service.DashboardAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<DashboardDto>().WithName("GetDashboard");
        group.MapGet("/system", async Task<IResult> (HttpContext context) => Results.Ok(await service.SystemAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<SystemStatisticsDto>().WithName("GetSystemStatistics");

        group.MapGet("/favorites", async Task<IResult> (HttpContext context) => Results.Ok(await service.FavoritesAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<IReadOnlyList<ChannelDto>>().WithName("GetFavorites");
        group.MapPut("/favorites", async (FavoritesRequest request, HttpContext context) =>
            Results.Ok(await service.SaveFavoritesAsync(ApiSupport.Actor(context), request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<IReadOnlyList<ChannelDto>>().WithName("UpdateFavorites");
        group.MapGet("/layouts", async Task<IResult> (HttpContext context) => Results.Ok(await service.LayoutsAsync(ApiSupport.Actor(context), context.RequestAborted)))
            .Produces<IReadOnlyList<AdministrationLayoutDto>>().WithName("GetLayouts");
        group.MapPost("/layouts", async (LayoutRequest request, HttpContext context) =>
        {
            var result = await service.SaveLayoutAsync(ApiSupport.Actor(context), null, request, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/layouts/{result.Id}", result);
        }).Produces<AdministrationLayoutDto>(201).WithName("CreateLayout");
        group.MapPut("/layouts/{id:long}", async (long id, LayoutRequest request, HttpContext context) =>
            Results.Ok(await service.SaveLayoutAsync(ApiSupport.Actor(context), id, request, ApiSupport.Ip(context), context.RequestAborted)))
            .Produces<AdministrationLayoutDto>().WithName("UpdateLayout");
        group.MapDelete("/layouts/{id:long}", async (long id, HttpContext context) =>
        {
            await service.DeleteLayoutAsync(ApiSupport.Actor(context), id, ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).WithName("DeleteLayout");
    }

    private static DateTimeOffset? QueryTime(HttpContext context, string key)
    {
        var value = context.Request.Query[key].ToString();
        if (string.IsNullOrWhiteSpace(value)) return null;
        Rules.Require(DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed), $"查询参数 {key} 不是有效时间");
        return parsed.ToUniversalTime();
    }
}
