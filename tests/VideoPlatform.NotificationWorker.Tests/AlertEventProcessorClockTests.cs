using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.NotificationWorker;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.NotificationWorker.Tests;

public sealed class AlertEventProcessorClockTests
{
    [Fact(DisplayName = "时钟同步 Outbox 创建独立告警并在恢复时投递恢复通知")]
    public async Task ClockSynchronizationEventsOpenAndRecoverClockIncident()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var recorderId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        dbContext.Recorders.Add(new RecorderEntity
        {
            Id = recorderId,
            Code = "NVR-CLOCK-01",
            Name = "时钟验证录像机",
            Vendor = "Generic",
            AdapterType = "onvif-standard",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.NotificationChannels.Add(new NotificationChannelEntity
        {
            Id = channelId,
            Name = "运维企业微信",
            Type = NotificationChannelType.WeComWebhook,
            SecretReference = "wecom-operations",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.AlertRules.Add(new AlertRuleEntity
        {
            Id = ruleId,
            Name = "录像机运维告警",
            ResourceType = "recorder",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.AlertRuleChannels.Add(new AlertRuleChannelEntity { AlertRuleId = ruleId, NotificationChannelId = channelId });
        await dbContext.SaveChangesAsync();
        var processor = new AlertEventProcessor(dbContext);
        var openedAt = new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.FromHours(8));

        await processor.ProcessAsync(CreateEvent(recorderId, ClockSynchronization.Unknown, ClockSynchronization.Drifted, openedAt), CancellationToken.None);

        var incident = await dbContext.AlertIncidents.SingleAsync();
        Assert.Equal("clock_skew", incident.IncidentType);
        Assert.Null(incident.ResolvedAt);
        Assert.Single(await dbContext.NotificationDeliveries.Where(item => item.EventType == NotificationEventType.Opened).ToListAsync());

        await processor.ProcessAsync(CreateEvent(recorderId, ClockSynchronization.Drifted, ClockSynchronization.Synchronized, openedAt.AddMinutes(45)), CancellationToken.None);

        Assert.NotNull((await dbContext.AlertIncidents.SingleAsync()).ResolvedAt);
        Assert.Single(await dbContext.NotificationDeliveries.Where(item => item.EventType == NotificationEventType.Recovered).ToListAsync());
    }

    private static OutboxEventEntity CreateEvent(
        Guid recorderId,
        ClockSynchronization previous,
        ClockSynchronization current,
        DateTimeOffset observedAt) => new()
    {
        Id = Guid.NewGuid(),
        EventType = "clock.synchronization.changed",
        AggregateType = "recorder",
        AggregateId = recorderId,
        PayloadJson = JsonSerializer.Serialize(new ClockSynchronizationChangedPayload(
            "recorder",
            recorderId,
            previous,
            current,
            6000,
            observedAt)),
        OccurredAt = observedAt,
        NextAttemptAt = observedAt
    };
}
