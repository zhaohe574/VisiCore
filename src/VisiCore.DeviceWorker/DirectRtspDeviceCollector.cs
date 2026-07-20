using System.Net.Sockets;
using VisiCore.Core;

namespace VisiCore.DeviceWorker;

public sealed class DirectRtspDeviceCollector : IRecorderInventoryCollector
{
    public string Name => "direct-rtsp";

    public bool CanCollect(WorkerRecorderAssignment assignment) =>
        assignment.PluginRuntimeType?.Equals(DevicePluginRuntimeTypes.DirectRtsp, StringComparison.OrdinalIgnoreCase) == true ||
        assignment.AdapterType.Equals("direct-rtsp", StringComparison.OrdinalIgnoreCase);

    public Task<WorkerInventoryReport> CollectInventoryAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken)
    {
        var cameras = assignment.Cameras.Select(item => new WorkerCameraInventory(
            item.ChannelNumber,
            string.IsNullOrWhiteSpace(item.Alias) ? $"直连通道 {item.ChannelNumber}" : item.Alias,
            item.SupportsPtz,
            item.StreamingChannelMap)).ToList();
        return Task.FromResult(new WorkerInventoryReport(
            assignment.RecorderId,
            new RecorderCapabilities(
                CapabilityState.Supported,
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                CapabilityState.Unknown,
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                "direct-rtsp-v1"),
            cameras,
            DateTimeOffset.UtcNow));
    }

    public async Task<WorkerHealthReport> CollectHealthAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = assignment.Endpoints.Single(item => item.Protocol.Equals("Rtsp", StringComparison.OrdinalIgnoreCase));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
            return new WorkerHealthReport(
                assignment.RecorderId,
                true,
                new Dictionary<int, bool>(),
                null,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new WorkerHealthReport(
                assignment.RecorderId,
                false,
                new Dictionary<int, bool>(),
                exception.GetType().Name,
                DateTimeOffset.UtcNow);
        }
    }

    public Task<WorkerClockReport> CollectClockAsync(
        WorkerRecorderAssignment assignment,
        CancellationToken cancellationToken) =>
        Task.FromResult(new WorkerClockReport(
            assignment.RecorderId,
            null,
            "not_supported",
            DateTimeOffset.UtcNow));
}
