using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.NotificationWorker;

public sealed class AlertEventProcessor(PlatformDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessAsync(OutboxEventEntity outboxEvent, CancellationToken cancellationToken)
    {
        if (outboxEvent.EventType.Equals("health.state.changed", StringComparison.Ordinal))
        {
            var payload = JsonSerializer.Deserialize<HealthStateChangedPayload>(outboxEvent.PayloadJson, JsonOptions)
                ?? throw new InvalidOperationException("健康状态事件载荷无效。 ");
            if (payload.CurrentConnectivity == CameraConnectivity.Offline)
            {
                await OpenIncidentAsync(payload.ResourceType, payload.ResourceId, "offline", payload.ObservedAt, cancellationToken);
            }
            else if (payload.CurrentConnectivity == CameraConnectivity.Online)
            {
                await RecoverIncidentAsync(payload.ResourceType, payload.ResourceId, "offline", payload.ObservedAt, cancellationToken);
            }
            return;
        }
        if (outboxEvent.EventType.Equals("clock.synchronization.changed", StringComparison.Ordinal))
        {
            var payload = JsonSerializer.Deserialize<ClockSynchronizationChangedPayload>(outboxEvent.PayloadJson, JsonOptions)
                ?? throw new InvalidOperationException("时钟同步事件载荷无效。 ");
            if (payload.CurrentSynchronization == ClockSynchronization.Drifted)
            {
                await OpenIncidentAsync(payload.ResourceType, payload.ResourceId, "clock_skew", payload.ObservedAt, cancellationToken);
            }
            else if (payload.CurrentSynchronization == ClockSynchronization.Synchronized)
            {
                await RecoverIncidentAsync(payload.ResourceType, payload.ResourceId, "clock_skew", payload.ObservedAt, cancellationToken);
            }
        }
    }

    private async Task OpenIncidentAsync(
        string resourceType,
        Guid resourceId,
        string incidentType,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var resource = await ResolveResourceAsync(resourceType, resourceId, cancellationToken);
        var incident = await dbContext.AlertIncidents.SingleOrDefaultAsync(
            item => item.ResourceType == resourceType && item.ResourceId == resourceId &&
                    item.IncidentType == incidentType && item.ResolvedAt == null,
            cancellationToken);
        if (incident is null)
        {
            incident = new AlertIncidentEntity
            {
                Id = Guid.NewGuid(), ResourceType = resourceType, ResourceId = resourceId,
                RegionId = resource.RegionId, ResourceName = resource.Name, IncidentType = incidentType,
                OpenedAt = observedAt, LastObservedAt = observedAt
            };
            dbContext.AlertIncidents.Add(incident);
        }
        else
        {
            incident.LastObservedAt = observedAt;
            incident.RegionId = resource.RegionId;
            incident.ResourceName = resource.Name;
        }

        var channelIds = await ResolveChannelIdsAsync(resourceType, resource.RegionId, recoveryOnly: false, cancellationToken);
        await AddDeliveriesAsync(incident.Id, channelIds, NotificationEventType.Opened, observedAt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecoverIncidentAsync(
        string resourceType,
        Guid resourceId,
        string incidentType,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var incident = await dbContext.AlertIncidents.SingleOrDefaultAsync(
            item => item.ResourceType == resourceType && item.ResourceId == resourceId &&
                    item.IncidentType == incidentType && item.ResolvedAt == null,
            cancellationToken);
        if (incident is null)
        {
            return;
        }

        incident.ResolvedAt = observedAt;
        incident.LastObservedAt = observedAt;
        var channelIds = await ResolveChannelIdsAsync(resourceType, incident.RegionId, recoveryOnly: true, cancellationToken);
        await AddDeliveriesAsync(incident.Id, channelIds, NotificationEventType.Recovered, observedAt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResourceDescriptor> ResolveResourceAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        if (resourceType.Equals("camera", StringComparison.Ordinal))
        {
            var camera = await dbContext.Cameras.AsNoTracking().SingleOrDefaultAsync(item => item.Id == resourceId, cancellationToken)
                ?? throw new InvalidOperationException("告警关联的摄像头不存在。 ");
            return new ResourceDescriptor(camera.Alias, camera.RegionId);
        }
        if (resourceType.Equals("recorder", StringComparison.Ordinal))
        {
            var recorder = await dbContext.Recorders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == resourceId, cancellationToken)
                ?? throw new InvalidOperationException("告警关联的录像机不存在。 ");
            var regionId = await dbContext.DeviceWorkerAssignments.AsNoTracking()
                .Where(item => item.RecorderId == resourceId)
                .Select(item => (Guid?)item.DefaultRegionId)
                .SingleOrDefaultAsync(cancellationToken);
            return new ResourceDescriptor(recorder.Name, regionId);
        }
        throw new InvalidOperationException("告警资源类型不受支持。 ");
    }

    private async Task<IReadOnlyList<Guid>> ResolveChannelIdsAsync(
        string resourceType,
        Guid? regionId,
        bool recoveryOnly,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> regionLineage = regionId is null ? [] : await GetRegionLineageAsync(regionId.Value, cancellationToken);
        var rules = await dbContext.AlertRules.AsNoTracking()
            .Where(item => item.Enabled && (item.ResourceType == "*" || item.ResourceType == resourceType) &&
                           (!recoveryOnly || item.NotifyOnRecovery))
            .ToListAsync(cancellationToken);
        var matchingRuleIds = rules
            .Where(item => item.RegionId is null || regionLineage.Contains(item.RegionId.Value))
            .Select(item => item.Id)
            .ToList();
        if (matchingRuleIds.Count == 0)
        {
            return [];
        }

        var enabledChannelIds = await dbContext.NotificationChannels.AsNoTracking()
            .Where(item => item.Enabled)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        return await dbContext.AlertRuleChannels.AsNoTracking()
            .Where(item => matchingRuleIds.Contains(item.AlertRuleId) && enabledChannelIds.Contains(item.NotificationChannelId))
            .Select(item => item.NotificationChannelId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<Guid>> GetRegionLineageAsync(Guid regionId, CancellationToken cancellationToken)
    {
        var lineage = new HashSet<Guid> { regionId };
        var current = regionId;
        while (true)
        {
            var parentId = await dbContext.Regions.AsNoTracking()
                .Where(item => item.Id == current)
                .Select(item => item.ParentId)
                .SingleOrDefaultAsync(cancellationToken);
            if (parentId is null || !lineage.Add(parentId.Value))
            {
                return lineage;
            }
            current = parentId.Value;
        }
    }

    private async Task AddDeliveriesAsync(
        Guid incidentId,
        IReadOnlyList<Guid> channelIds,
        NotificationEventType eventType,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var existingChannelIds = await dbContext.NotificationDeliveries.AsNoTracking()
            .Where(item => item.AlertIncidentId == incidentId && item.EventType == eventType)
            .Select(item => item.NotificationChannelId)
            .ToListAsync(cancellationToken);
        foreach (var channelId in channelIds.Except(existingChannelIds))
        {
            dbContext.NotificationDeliveries.Add(new NotificationDeliveryEntity
            {
                Id = Guid.NewGuid(), AlertIncidentId = incidentId, NotificationChannelId = channelId,
                EventType = eventType, Status = NotificationDeliveryStatus.Pending,
                CreatedAt = createdAt, NextAttemptAt = createdAt
            });
        }
    }
}

public sealed record HealthStateChangedPayload(
    string ResourceType,
    Guid ResourceId,
    CameraConnectivity PreviousConnectivity,
    CameraConnectivity CurrentConnectivity,
    DateTimeOffset ObservedAt);

public sealed record ClockSynchronizationChangedPayload(
    string ResourceType,
    Guid ResourceId,
    ClockSynchronization PreviousSynchronization,
    ClockSynchronization CurrentSynchronization,
    int OffsetMilliseconds,
    DateTimeOffset ObservedAt);

internal sealed record ResourceDescriptor(string Name, Guid? RegionId);
