using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public sealed class OnvifEdgeOperationStatusReporter(
    OnvifEdgeControlPlaneClient controlPlaneClient,
    OnvifEdgeOptions options,
    OnvifPtzWatchdog ptzWatchdog,
    OnvifOperationReadinessValidator readinessValidator,
    ILogger<OnvifEdgeOperationStatusReporter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReportSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.OperationStatusRefreshSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReportSafelyAsync(stoppingToken);
        }
    }

    private async Task ReportSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var failureKind = ptzWatchdog.HasUnconfirmedStop
                ? "stop_unconfirmed"
                : options.Ptz.Enabled ? "validation_required" : "disabled_by_policy";
            var assignments = (await controlPlaneClient.GetAssignmentsAsync(cancellationToken))
                .Where(IsOnvifAssignment)
                .ToList();
            var statuses = new List<WorkerOperationStatus>(assignments.Count * 3);
            foreach (var assignment in assignments)
            {
                statuses.Add(new WorkerOperationStatus(EdgeOperationTypes.OnvifPtz, false, failureKind, assignment.RecorderId));
                statuses.Add(await readinessValidator.GetRecordingSearchStatusAsync(assignment, cancellationToken));
                statuses.Add(await readinessValidator.GetPlaybackRelayStatusAsync(assignment, cancellationToken));
            }
            if (statuses.Count == 0)
            {
                return;
            }
            await controlPlaneClient.ReportOperationStatusesAsync(
                new WorkerOperationStatusReport(statuses),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "ONVIF 边缘运行态上报失败，将在下个周期重试。");
        }
    }

    private static bool IsOnvifAssignment(WorkerRecorderAssignment assignment) =>
        assignment.PluginRuntimeType?.Equals(DevicePluginRuntimeTypes.Onvif, StringComparison.OrdinalIgnoreCase) == true;
}
