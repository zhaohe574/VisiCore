using Microsoft.AspNetCore.Http;
using VisiCore.Api;

/// <summary>
/// 对外公开且最小化返回的离线设备查询端点。
/// </summary>
public sealed class PublicOfflineDevicesEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/public/offline-devices", async (
            string? region,
            string? name,
            string? deviceType,
            int? page,
            int? pageSize,
            HttpResponse response,
            PublicOfflineDeviceService offlineDeviceService,
            CancellationToken cancellationToken) =>
        {
            if (!PublicOfflineDeviceQuery.TryCreate(region, name, deviceType, page, pageSize, out var query, out var validationError))
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "离线设备查询参数无效", "offline_devices_query_invalid", validationError, new Dictionary<string, string[]> { ["query"] = [validationError] });
            }

            response.Headers.CacheControl = "no-store";
            return Results.Ok(await offlineDeviceService.ListAsync(query, cancellationToken));
        }).AllowAnonymous().RequireRateLimiting("public-offline-devices");
    }
}
