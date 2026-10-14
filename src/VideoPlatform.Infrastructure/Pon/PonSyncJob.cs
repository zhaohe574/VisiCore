using Microsoft.Extensions.Logging;
using VideoPlatform.Infrastructure.Jobs;

namespace VideoPlatform.Infrastructure.Pon;

public sealed class PonSyncJob(PonService pon, JobHealth health, ILogger<PonSyncJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var count = await pon.SyncFromEsightAsync(ct);
            health.Set("pon", "healthy", null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Worker is shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "后台定时同步华为 eSight 光网资产与状态异常");
            health.Set("pon", "degraded", $"华为网管同步异常: {ex.Message}");
        }
    }
}

