using Microsoft.Extensions.Logging;
using VisiCore.Core;
using VisiCore.DeviceWorker;

namespace VisiCore.EdgeAgent;

/// <summary>
/// Linux Edge Agent 的只读设备探测循环。中心仅下发分配与端点，凭据从当前内存信封解析结果取得。
/// </summary>
public sealed class EdgeAgentDeviceSynchronizer(
    EdgeAgentRuntimeSettings runtimeSettings,
    EdgeAgentControlPlaneClient controlPlaneClient,
    EdgeAgentCredentialResolver credentialResolver,
    OnvifDeviceCollector onvifCollector,
    DirectRtspDeviceCollector directRtspCollector,
    ILogger<EdgeAgentDeviceSynchronizer> logger)
{
    private readonly Dictionary<Guid, DateTimeOffset> nextInventorySync = [];
    private readonly Dictionary<Guid, DateTimeOffset> nextClockSync = [];

    public async Task SynchronizeAsync(
        EdgeAgentIdentity identity,
        IReadOnlyList<WorkerRecorderAssignment> assignments,
        IReadOnlyDictionary<string, AgentCredentialPayload> credentials,
        CancellationToken cancellationToken)
    {
        credentialResolver.Replace(credentials);
        foreach (var assignment in assignments)
        {
            if (!HasAllCredentialReferences(assignment))
            {
                await ReportUnavailableCredentialAsync(identity, assignment, cancellationToken);
                continue;
            }

            var collector = ResolveCollector(assignment, runtimeSettings.Snapshot());
            if (collector is null)
            {
                await ReportUnsupportedAdapterAsync(identity, assignment, cancellationToken);
                continue;
            }

            await SynchronizeAssignmentAsync(identity, assignment, collector, cancellationToken);
        }
    }

    private bool HasAllCredentialReferences(WorkerRecorderAssignment assignment) =>
        assignment.Endpoints.All(endpoint => credentialResolver.Contains(endpoint.CredentialReference));

    private IRecorderInventoryCollector? ResolveCollector(
        WorkerRecorderAssignment assignment,
        EdgeAgentRuntimeSettingsSnapshot settings)
    {
        if (settings.OnvifEnabled && onvifCollector.CanCollect(assignment))
        {
            return onvifCollector;
        }
        return settings.DirectRtspEnabled && directRtspCollector.CanCollect(assignment) ? directRtspCollector : null;
    }

    private async Task SynchronizeAssignmentAsync(
        EdgeAgentIdentity identity,
        WorkerRecorderAssignment assignment,
        IRecorderInventoryCollector collector,
        CancellationToken cancellationToken)
    {
        try
        {
            var health = await collector.CollectHealthAsync(assignment, cancellationToken);
            await controlPlaneClient.ReportHealthAsync(identity, health, cancellationToken);
            await ReportStatusAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceHealth, health.RecorderReachable, health.FailureKind, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await ReportProbeFailureAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceHealth, exception, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var settings = runtimeSettings.Snapshot();
        if (now >= nextInventorySync.GetValueOrDefault(assignment.RecorderId))
        {
            try
            {
                var inventory = await collector.CollectInventoryAsync(assignment, cancellationToken);
                await controlPlaneClient.ReportInventoryAsync(identity, inventory, cancellationToken);
                await ReportStatusAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceInventory, true, null, cancellationToken);
                nextInventorySync[assignment.RecorderId] = now.AddSeconds(settings.InventorySyncIntervalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await ReportProbeFailureAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceInventory, exception, cancellationToken);
            }
        }

        if (now >= nextClockSync.GetValueOrDefault(assignment.RecorderId))
        {
            try
            {
                var clock = await collector.CollectClockAsync(assignment, cancellationToken);
                await controlPlaneClient.ReportClockAsync(identity, clock, cancellationToken);
                nextClockSync[assignment.RecorderId] = now.AddSeconds(settings.ClockSyncIntervalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("边缘节点未能同步录像机 {RecorderId} 的时钟状态，失败类别 {FailureKind}。", assignment.RecorderId, ToFailureKind(exception));
            }
        }
    }

    private async Task ReportUnavailableCredentialAsync(EdgeAgentIdentity identity, WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        await controlPlaneClient.ReportHealthAsync(identity, new WorkerHealthReport(
            assignment.RecorderId,
            false,
            new Dictionary<int, bool>(),
            "credential_reference_mismatch",
            DateTimeOffset.UtcNow), cancellationToken);
        await ReportStatusAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceHealth, false, "credential_reference_mismatch", cancellationToken);
        await ReportStatusAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceInventory, false, "credential_reference_mismatch", cancellationToken);
    }

    private async Task ReportUnsupportedAdapterAsync(EdgeAgentIdentity identity, WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        await ReportStatusAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceHealth, false, "unsupported_adapter", cancellationToken);
        await ReportStatusAsync(identity, assignment.RecorderId, EdgeOperationTypes.DeviceInventory, false, "unsupported_adapter", cancellationToken);
    }

    private async Task ReportProbeFailureAsync(EdgeAgentIdentity identity, Guid recorderId, string operationType, Exception exception, CancellationToken cancellationToken)
    {
        var failureKind = exception is EdgeAgentCredentialUnavailableException ? "credential_reference_mismatch" : ToFailureKind(exception);
        try
        {
            await controlPlaneClient.ReportHealthAsync(identity, new WorkerHealthReport(
                recorderId,
                false,
                new Dictionary<int, bool>(),
                failureKind,
                DateTimeOffset.UtcNow), cancellationToken);
            await ReportStatusAsync(identity, recorderId, operationType, false, failureKind, cancellationToken);
        }
        catch (Exception reportException) when (reportException is not OperationCanceledException)
        {
            logger.LogWarning("边缘节点无法上报录像机 {RecorderId} 的探测失败状态，失败类别 {FailureKind}。", recorderId, ToFailureKind(reportException));
        }
    }

    private Task ReportStatusAsync(EdgeAgentIdentity identity, Guid recorderId, string operationType, bool ready, string? failureKind, CancellationToken cancellationToken) =>
        controlPlaneClient.ReportOperationStatusesAsync(identity, new WorkerOperationStatusReport([
            new WorkerOperationStatus(operationType, ready, ready ? null : failureKind, recorderId)
        ]), cancellationToken);

    private static string ToFailureKind(Exception exception) => exception switch
    {
        EdgeAgentCredentialUnavailableException => "credential_reference_mismatch",
        OnvifProtocolException => "onvif_protocol_error",
        HttpRequestException => "transport_failed",
        TimeoutException => "connect_timeout",
        _ => "probe_failed"
    };
}
