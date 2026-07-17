using Microsoft.Extensions.Diagnostics.HealthChecks;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class DatabaseReadinessHealthCheck(PlatformDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("平台数据库可连接。")
                : HealthCheckResult.Unhealthy("平台数据库不可连接。");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("平台数据库就绪检查失败。", exception);
        }
    }
}

public sealed class DatabaseConfigurationMissingHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Unhealthy("平台数据库连接字符串尚未配置。"));
}
