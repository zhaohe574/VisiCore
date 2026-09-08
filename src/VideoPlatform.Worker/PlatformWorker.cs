using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoPlatform.Infrastructure.Jobs;

namespace VideoPlatform.Worker;

public sealed class PlatformWorker(DeviceSyncJob devices, AlarmIngestionJob alarms, MediaMaintenanceJob media,
    ExportProcessingJob exports, RetentionJob retention, JobHealth health, ILogger<PlatformWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(
            LoopAsync("devices", TimeSpan.FromSeconds(30), devices.RunAsync, stoppingToken),
            LoopAsync("alarms", TimeSpan.FromSeconds(2), alarms.RunAsync, stoppingToken),
            LoopAsync("media", TimeSpan.FromSeconds(5), media.RunAsync, stoppingToken),
            LoopAsync("reconciliation", TimeSpan.FromSeconds(30), media.ReconcileAsync, stoppingToken),
            LoopAsync("exports", TimeSpan.FromSeconds(2), exports.RunAsync, stoppingToken),
            LoopAsync("retention", TimeSpan.FromMinutes(5), retention.RunAsync, stoppingToken),
            LoopAsync("heartbeat", TimeSpan.FromSeconds(5), health.BeatAsync, stoppingToken));

    private async Task LoopAsync(string name, TimeSpan interval, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        var failures = 0;
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await action(ct);
                    failures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    failures = Math.Min(failures + 1, 6);
                    health.Set(name, "failed", "后台任务暂时失败，记录已保留并等待重试。" );
                    logger.LogError("后台任务 {Job} 本轮失败。异常类型：{ErrorType}", name, ex.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 1 << failures)), ct);
                }
                if (!await timer.WaitForNextTickAsync(ct)) break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
