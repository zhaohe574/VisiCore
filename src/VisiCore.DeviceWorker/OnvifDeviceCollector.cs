using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.DeviceWorker;

public sealed class OnvifDeviceCollector(OnvifReadOnlyClient client) : IRecorderInventoryCollector
{
    public string Name => "onvif-readonly";

    public bool CanCollect(WorkerRecorderAssignment assignment) =>
        assignment.PluginRuntimeType?.Equals(DevicePluginRuntimeTypes.Onvif, StringComparison.OrdinalIgnoreCase) == true;

    public async Task<WorkerInventoryReport> CollectInventoryAsync(WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        var discovery = await client.DiscoverAsync(assignment, cancellationToken);
        var allocatedChannels = new HashSet<int>();
        var cameras = discovery.Channels.Select(channel =>
        {
            var channelNumber = CreateStableChannelNumber(assignment.RecorderId, channel.SourceToken, allocatedChannels);
            var streamingChannelMap = JsonSerializer.Serialize(new
            {
                main = channel.MainUri.AbsoluteUri,
                sub = channel.SubUri.AbsoluteUri,
                onvifSource = channel.SourceToken,
                onvifPtzProfile = channel.PtzProfileToken
            });
            if (streamingChannelMap.Length > 512)
            {
                throw new OnvifProtocolException("ONVIF 码流映射超过中心数据模型允许的长度。 ");
            }
            return new WorkerCameraInventory(
                channelNumber,
                channel.Name,
                channel.SupportsPtz,
                streamingChannelMap);
        }).ToList();
        return new WorkerInventoryReport(
            assignment.RecorderId,
            new RecorderCapabilities(
                CapabilityState.Supported,
                discovery.HasDeclaredProfileG ? CapabilityState.Unknown : CapabilityState.Unsupported,
                cameras.Any(item => item.SupportsPtz) ? CapabilityState.Supported : CapabilityState.Unsupported,
                CapabilityState.Unknown,
                CapabilityState.Unknown,
                CapabilityState.Unknown,
                CapabilityState.Unknown,
                discovery.Version),
            cameras,
            DateTimeOffset.UtcNow);
    }

    public async Task<WorkerHealthReport> CollectHealthAsync(WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        try
        {
            await client.PingAsync(assignment, cancellationToken);
            return new WorkerHealthReport(assignment.RecorderId, true, new Dictionary<int, bool>(), null, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new WorkerHealthReport(assignment.RecorderId, false, new Dictionary<int, bool>(), exception.GetType().Name, DateTimeOffset.UtcNow);
        }
    }

    public async Task<WorkerClockReport> CollectClockAsync(WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        var requestedAt = DateTimeOffset.UtcNow;
        try
        {
            var deviceTime = await client.GetSystemTimeAsync(assignment, cancellationToken);
            var respondedAt = DateTimeOffset.UtcNow;
            return new WorkerClockReport(
                assignment.RecorderId,
                new WorkerClockObservation(deviceTime, requestedAt, respondedAt),
                null,
                respondedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new WorkerClockReport(assignment.RecorderId, null, exception.GetType().Name, DateTimeOffset.UtcNow);
        }
    }

    private static int CreateStableChannelNumber(Guid recorderId, string sourceToken, ISet<int> allocated)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{recorderId:N}:{sourceToken}"));
        var candidate = (int)(BitConverter.ToUInt32(bytes, 0) % 2_000_000_000) + 1;
        while (!allocated.Add(candidate))
        {
            candidate = candidate == 2_000_000_000 ? 1 : candidate + 1;
        }
        return candidate;
    }
}
