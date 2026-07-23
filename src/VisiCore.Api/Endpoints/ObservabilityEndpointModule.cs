using VisiCore.Core;

/// <summary>
/// 管理端运营仪表端点。指标本身通过 OpenTelemetry 导出，此端点用于后台的实时概览。
/// </summary>
public sealed class ObservabilityEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/observability").RequireAuthorization();
        admin.MapGet("/overview", (PlatformTelemetry telemetry) => Results.Ok(telemetry.Snapshot))
            .RequireSystemPermission(SystemPermission.ManageOperations);
    }
}
