using Microsoft.EntityFrameworkCore;
using VisiCore.Persistence;

/// <summary>
/// 定时从持久化真相源聚合业务仪表，避免只依赖进程内计数而在重启后失真。
/// </summary>
public sealed class PlatformTelemetryCollector(
    IServiceScopeFactory scopeFactory,
    PlatformTelemetry telemetry,
    ILogger<PlatformTelemetryCollector> logger) : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EdgeOnlineWindow = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "平台遥测采集失败，本轮保留上一次有效快照。");
            }

            await Task.Delay(CollectionInterval, stoppingToken);
        }
    }

    internal async Task CollectAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var now = DateTimeOffset.UtcNow;
        var edgeDeadline = now.Subtract(EdgeOnlineWindow);

        var activeSessions = await dbContext.StreamSessions.AsNoTracking()
            .LongCountAsync(item => item.RevokedAt == null && item.ExpiresAt > now, cancellationToken);
        var onlineEdgeAgents = await dbContext.EdgeAgents.AsNoTracking()
            .LongCountAsync(item => item.DisabledAt == null && item.LastSeenAt != null && item.LastSeenAt >= edgeDeadline, cancellationToken);
        var staleEdgeAgents = await dbContext.EdgeAgents.AsNoTracking()
            .LongCountAsync(item => item.DisabledAt == null && (item.LastSeenAt == null || item.LastSeenAt < edgeDeadline), cancellationToken);
        var upgradeFailureSummaries = await dbContext.UpgradeTargets.AsNoTracking()
            .Where(item => item.Status == "failed")
            .Select(item => item.FailureSummary)
            .ToListAsync(cancellationToken);
        var upgradeFailures = upgradeFailureSummaries
            .GroupBy(UpgradeFailureMetricCode.Normalize, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (long)group.LongCount(), StringComparer.Ordinal);
        var backupResults = await dbContext.PlatformBackups.AsNoTracking()
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Status) ? "unknown" : item.Status)
            .Select(group => new { Result = group.Key, Count = group.LongCount() })
            .ToDictionaryAsync(item => item.Result, item => item.Count, StringComparer.Ordinal, cancellationToken);

        telemetry.Update(new PlatformTelemetrySnapshot(
            activeSessions,
            onlineEdgeAgents,
            staleEdgeAgents,
            upgradeFailures,
            backupResults,
            now));
    }
}
