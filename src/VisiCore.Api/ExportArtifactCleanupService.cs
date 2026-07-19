using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VisiCore.Api;

public sealed class ExportArtifactCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportArtifactCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<LocalExportArtifactStore>();
                var settings = store.GetSettings();
                var service = scope.ServiceProvider.GetRequiredService<PlaybackExportArtifactService>();
                var deleted = await service.DeleteExpiredAsync(stoppingToken);
                var temporary = await store.DeleteStaleTemporaryFilesAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("已清理 {Count} 个过期录像导出文件。", deleted);
                }
                if (temporary.Deleted > 0)
                {
                    logger.LogInformation("已清理 {Count} 个中断上传遗留的录像导出临时文件。", temporary.Deleted);
                }
                if (temporary.Failed > 0)
                {
                    logger.LogWarning("有 {Count} 个录像导出临时文件尚未清理，将在下一轮重试。", temporary.Failed);
                }
                await Task.Delay(TimeSpan.FromMinutes(settings.CleanupIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "录像导出归档清理失败，5 分钟后重试。");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
