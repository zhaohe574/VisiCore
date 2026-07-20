using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VisiCore.StreamGateway;

/// <summary>
/// 外置 MediaMTX 不可达时，核心容器不报告就绪，便于 Docker 重启与部署诊断。
/// </summary>
public sealed class MediaMtxReadinessHealthCheck(IMediaMtxClient mediaMtxClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            await mediaMtxClient.ProbeAsync(cancellationToken);
            return HealthCheckResult.Healthy("外置 MediaMTX 控制面可达。 ");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("外置 MediaMTX 控制面不可达。", exception);
        }
    }
}
