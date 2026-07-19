using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class RecorderClockObservationCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ClockMonitoringOptions> options,
    ILogger<RecorderClockObservationCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        settings.Validate();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeleteExpiredAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "录像机时钟观测清理失败。 ");
            }
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }

    private async Task DeleteExpiredAsync(ClockMonitoringOptions settings, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.ObservationRetentionDays);
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var ids = await dbContext.RecorderClockObservations
                .Where(item => item.ResponseReceivedAt < cutoff)
                .OrderBy(item => item.ResponseReceivedAt)
                .Select(item => item.Id)
                .Take(10000)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return;
            }
            await dbContext.RecorderClockObservations.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }
}
