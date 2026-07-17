using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VideoPlatform.DeviceWorker;

public sealed class RecorderInventoryWorker(
    DeviceWorkerControlPlaneClient controlPlaneClient,
    WorkerCredentialStore credentialStore,
    RecorderInventoryCollectorRegistry collectorRegistry,
    IOptions<DeviceWorkerOptions> options,
    IOptions<ControlPlaneOptions> controlPlaneOptions,
    IOptions<OnvifReadOnlyOptions> onvifOptions,
    ILogger<RecorderInventoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.TryValidate(out var validationError) || !controlPlaneOptions.Value.TryGetBaseUri(out _, out validationError) ||
            !onvifOptions.Value.TryValidate(out validationError))
        {
            logger.LogCritical("设备 Worker 配置无效：{ValidationError}", validationError);
            return;
        }

        var nextInventorySync = new Dictionary<Guid, DateTimeOffset>();
        var nextClockSync = new Dictionary<Guid, DateTimeOffset>();
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<VideoPlatform.Core.WorkerRecorderAssignment> assignments;
            try
            {
                assignments = await controlPlaneClient.GetAssignmentsAsync(stoppingToken);
                credentialStore.Replace(assignments);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "无法从中心 API 获取设备 Worker 分配，60 秒后重试。 ");
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                continue;
            }

            foreach (var assignment in assignments)
            {
                IRecorderInventoryCollector? collector;
                try
                {
                    collector = collectorRegistry.Resolve(assignment);
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogCritical(exception, "录像机 {RecorderId} 的采集器匹配存在冲突，跳过本轮同步。", assignment.RecorderId);
                    continue;
                }
                if (collector is null)
                {
                    logger.LogInformation(
                        "录像机 {RecorderId} 的厂商 {Vendor} 和适配器 {AdapterType} 尚未配置可用只读采集器，跳过清单和健康采集。",
                        assignment.RecorderId,
                        assignment.Vendor,
                        assignment.AdapterType);
                    continue;
                }

                try
                {
                    var health = await collector.CollectHealthAsync(assignment, stoppingToken);
                    await controlPlaneClient.ReportHealthAsync(health, stoppingToken);
                    if (!health.RecorderReachable)
                    {
                        logger.LogWarning(
                            "录像机健康检测失败：录像机 {RecorderId}，采集器 {Collector}，失败类别 {FailureKind}。",
                            assignment.RecorderId,
                            collector.Name,
                            health.FailureKind);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "设备健康状态上报失败：录像机 {RecorderId}，采集器 {Collector}。", assignment.RecorderId, collector.Name);
                }

                if (DateTimeOffset.UtcNow >= nextInventorySync.GetValueOrDefault(assignment.RecorderId))
                {
                    try
                    {
                        var inventory = await collector.CollectInventoryAsync(assignment, stoppingToken);
                        await controlPlaneClient.ReportInventoryAsync(inventory, stoppingToken);
                        logger.LogInformation(
                            "设备清单同步已上报：录像机 {RecorderId}，采集器 {Collector}，发现 {Discovered} 路。",
                            assignment.RecorderId,
                            collector.Name,
                            inventory.Cameras.Count);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "设备清单同步失败：录像机 {RecorderId}，采集器 {Collector}。", assignment.RecorderId, collector.Name);
                    }
                    nextInventorySync[assignment.RecorderId] = DateTimeOffset.UtcNow.AddSeconds(settings.SyncIntervalSeconds);
                }

                if (DateTimeOffset.UtcNow >= nextClockSync.GetValueOrDefault(assignment.RecorderId))
                {
                    try
                    {
                        var clock = await collector.CollectClockAsync(assignment, stoppingToken);
                        await controlPlaneClient.ReportClockAsync(clock, stoppingToken);
                        if (clock.Observation is null)
                        {
                            logger.LogWarning(
                                "录像机时钟采集失败：录像机 {RecorderId}，采集器 {Collector}，失败类别 {FailureKind}。",
                                assignment.RecorderId,
                                collector.Name,
                                clock.FailureKind);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "录像机时钟状态上报失败：录像机 {RecorderId}，采集器 {Collector}。", assignment.RecorderId, collector.Name);
                    }
                    nextClockSync[assignment.RecorderId] = DateTimeOffset.UtcNow.AddSeconds(settings.ClockSyncIntervalSeconds);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.HealthPollIntervalSeconds), stoppingToken);
        }
    }
}

public sealed class DeviceWorkerOptions
{
    public int SyncIntervalSeconds { get; init; } = 300;
    public int HealthPollIntervalSeconds { get; init; } = 60;
    public int ClockSyncIntervalSeconds { get; init; } = 900;

    public bool TryValidate(out string validationError)
    {
        if (SyncIntervalSeconds is < 60 or > 86400)
        {
            validationError = "DeviceWorker:SyncIntervalSeconds 必须在 60 到 86400 之间。";
            return false;
        }
        if (HealthPollIntervalSeconds is < 30 or > 3600)
        {
            validationError = "DeviceWorker:HealthPollIntervalSeconds 必须在 30 到 3600 之间。";
            return false;
        }
        if (ClockSyncIntervalSeconds is < 300 or > 86400)
        {
            validationError = "DeviceWorker:ClockSyncIntervalSeconds 必须在 300 到 86400 之间。";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}
