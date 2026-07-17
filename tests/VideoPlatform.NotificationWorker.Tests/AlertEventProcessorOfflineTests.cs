using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.Core;
using VideoPlatform.NotificationWorker;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.NotificationWorker.Tests;

public sealed class AlertEventProcessorOfflineTests
{
    [Fact(DisplayName = "录像机掉线按 Worker 分配区域匹配规则并发送恢复通知")]
    public async Task RecorderOfflineUsesAssignedRegionForNotificationRouting()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var recorderId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        dbContext.Regions.Add(new RegionEntity { Id = regionId, Code = "NORTH", Name = "北区" });
        dbContext.Recorders.Add(new RecorderEntity
        {
            Id = recorderId,
            Code = "NVR-NORTH-01",
            Name = "北区录像机",
            Vendor = "Generic",
            AdapterType = "onvif-standard",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.DeviceWorkerAssignments.Add(new DeviceWorkerAssignmentEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = Guid.NewGuid(),
            RecorderId = recorderId,
            DefaultRegionId = regionId
        });
        dbContext.NotificationChannels.Add(new NotificationChannelEntity
        {
            Id = channelId,
            Name = "北区运维群",
            Type = NotificationChannelType.WeComWebhook,
            SecretReference = "wecom-north",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var rule = new AlertRuleEntity
        {
            Id = Guid.NewGuid(),
            Name = "北区录像机掉线",
            ResourceType = "recorder",
            RegionId = regionId,
            NotifyOnRecovery = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AlertRules.Add(rule);
        dbContext.AlertRuleChannels.Add(new AlertRuleChannelEntity
        {
            AlertRuleId = rule.Id,
            NotificationChannelId = channelId
        });
        await dbContext.SaveChangesAsync();
        var processor = new AlertEventProcessor(dbContext);
        var openedAt = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.FromHours(8));

        await processor.ProcessAsync(CreateHealthEvent(recorderId, CameraConnectivity.SuspectedOffline, CameraConnectivity.Offline, openedAt), CancellationToken.None);
        await processor.ProcessAsync(CreateHealthEvent(recorderId, CameraConnectivity.SuspectedOffline, CameraConnectivity.Offline, openedAt.AddMinutes(1)), CancellationToken.None);

        var incident = await dbContext.AlertIncidents.SingleAsync();
        Assert.Equal(regionId, incident.RegionId);
        Assert.Equal("北区录像机", incident.ResourceName);
        Assert.Single(await dbContext.NotificationDeliveries.Where(item => item.EventType == NotificationEventType.Opened).ToListAsync());

        await processor.ProcessAsync(CreateHealthEvent(recorderId, CameraConnectivity.Recovering, CameraConnectivity.Online, openedAt.AddMinutes(8)), CancellationToken.None);

        incident = await dbContext.AlertIncidents.SingleAsync();
        Assert.Equal(openedAt.AddMinutes(8), incident.ResolvedAt);
        Assert.Single(await dbContext.NotificationDeliveries.Where(item => item.EventType == NotificationEventType.Recovered).ToListAsync());
    }

    private static OutboxEventEntity CreateHealthEvent(
        Guid recorderId,
        CameraConnectivity previous,
        CameraConnectivity current,
        DateTimeOffset observedAt) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "health.state.changed",
        AggregateType = "recorder",
        AggregateId = recorderId,
        PayloadJson = JsonSerializer.Serialize(new HealthStateChangedPayload(
            "recorder",
            recorderId,
            previous,
            current,
            observedAt)),
        OccurredAt = observedAt,
        NextAttemptAt = observedAt
    };
}
