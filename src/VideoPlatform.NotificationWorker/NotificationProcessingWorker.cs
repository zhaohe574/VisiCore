using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.NotificationWorker;

public sealed class NotificationProcessingWorker(
    IServiceScopeFactory scopeFactory,
    NotificationDispatcher dispatcher,
    IOptions<NotificationWorkerOptions> options,
    ILogger<NotificationProcessingWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        settings.Validate();
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedOutbox = await ProcessOneOutboxAsync(settings, stoppingToken);
            var processedDelivery = await ProcessOneDeliveryAsync(settings, stoppingToken);
            if (!processedOutbox && !processedDelivery)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOneOutboxAsync(NotificationWorkerOptions settings, CancellationToken cancellationToken)
    {
        var eventId = await ClaimOutboxAsync(settings, cancellationToken);
        if (eventId == Guid.Empty)
        {
            return false;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var outboxEvent = await dbContext.OutboxEvents.SingleAsync(item => item.Id == eventId, cancellationToken);
            if (outboxEvent.EventType.Equals(NotificationChannelTestRequestedPayload.EventType, StringComparison.Ordinal))
            {
                var testProcessor = scope.ServiceProvider.GetRequiredService<NotificationChannelTestEventProcessor>();
                await testProcessor.ProcessAsync(outboxEvent, cancellationToken);
            }
            else
            {
                var processor = scope.ServiceProvider.GetRequiredService<AlertEventProcessor>();
                await processor.ProcessAsync(outboxEvent, cancellationToken);
            }
            outboxEvent.ProcessedAt = DateTimeOffset.UtcNow;
            outboxEvent.LockedBy = null;
            outboxEvent.LockedUntil = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await FailOutboxAsync(eventId, exception, settings, cancellationToken);
            logger.LogError("Outbox 事件 {EventId} 处理失败，失败类别 {FailureKind}。", eventId, exception.GetType().Name);
        }
        return true;
    }

    private async Task<Guid> ClaimOutboxAsync(NotificationWorkerOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var candidates = await dbContext.OutboxEvents.FromSqlRaw("""
            SELECT candidate.* FROM outbox_events AS candidate
            WHERE candidate."ProcessedAt" IS NULL
              AND candidate."EventType" IN ('health.state.changed', 'notification.channel.test.requested')
              AND candidate."DeadLetteredAt" IS NULL
              AND candidate."NextAttemptAt" <= NOW()
              AND (candidate."LockedUntil" IS NULL OR candidate."LockedUntil" < NOW())
              AND NOT EXISTS (
                  SELECT 1 FROM outbox_events AS earlier
                  WHERE earlier."AggregateType" = candidate."AggregateType"
                    AND earlier."AggregateId" = candidate."AggregateId"
                    AND earlier."ProcessedAt" IS NULL
                    AND earlier."DeadLetteredAt" IS NULL
                    AND (earlier."OccurredAt" < candidate."OccurredAt"
                         OR (earlier."OccurredAt" = candidate."OccurredAt" AND earlier."Id" < candidate."Id"))
              )
            ORDER BY candidate."OccurredAt", candidate."Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var outboxEvent = candidates.SingleOrDefault();
        if (outboxEvent is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Guid.Empty;
        }

        outboxEvent.LockedBy = _workerId;
        outboxEvent.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(settings.LockSeconds);
        outboxEvent.Attempts++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outboxEvent.Id;
    }

    private async Task FailOutboxAsync(Guid eventId, Exception exception, NotificationWorkerOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var outboxEvent = await dbContext.OutboxEvents.SingleAsync(item => item.Id == eventId, cancellationToken);
        outboxEvent.LastError = FailureKind(exception);
        outboxEvent.LockedBy = null;
        outboxEvent.LockedUntil = null;
        if (outboxEvent.Attempts >= settings.MaxAttempts)
        {
            outboxEvent.DeadLetteredAt = DateTimeOffset.UtcNow;
        }
        else
        {
            outboxEvent.NextAttemptAt = DateTimeOffset.UtcNow.Add(Backoff(outboxEvent.Attempts));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ProcessOneDeliveryAsync(NotificationWorkerOptions settings, CancellationToken cancellationToken)
    {
        var deliveryId = await ClaimDeliveryAsync(settings, cancellationToken);
        if (deliveryId == Guid.Empty)
        {
            return false;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var delivery = await dbContext.NotificationDeliveries.SingleAsync(item => item.Id == deliveryId, cancellationToken);
            var channel = await dbContext.NotificationChannels.AsNoTracking().SingleAsync(item => item.Id == delivery.NotificationChannelId, cancellationToken);
            var incident = await dbContext.AlertIncidents.AsNoTracking().SingleAsync(item => item.Id == delivery.AlertIncidentId, cancellationToken);
            if (!channel.Enabled)
            {
                throw new InvalidOperationException("通知渠道已停用。 ");
            }
            var context = await ResolveDeliveryContextAsync(dbContext, incident, cancellationToken);
            await dispatcher.SendAsync(channel, incident, delivery.EventType, context, cancellationToken);
            delivery.Status = NotificationDeliveryStatus.Sent;
            delivery.SentAt = DateTimeOffset.UtcNow;
            delivery.LastError = null;
            delivery.LockedBy = null;
            delivery.LockedUntil = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await FailDeliveryAsync(deliveryId, exception, settings, cancellationToken);
            logger.LogError("通知投递 {DeliveryId} 失败，失败类别 {FailureKind}。", deliveryId, exception.GetType().Name);
        }
        return true;
    }

    private async Task<Guid> ClaimDeliveryAsync(NotificationWorkerOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var candidates = await dbContext.NotificationDeliveries.FromSqlRaw("""
            SELECT * FROM notification_deliveries
            WHERE "Status" IN ('Pending', 'Failed')
              AND "NextAttemptAt" <= NOW()
              AND ("LockedUntil" IS NULL OR "LockedUntil" < NOW())
            ORDER BY "CreatedAt"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var delivery = candidates.SingleOrDefault();
        if (delivery is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Guid.Empty;
        }

        delivery.LockedBy = _workerId;
        delivery.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(settings.LockSeconds);
        delivery.Attempts++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return delivery.Id;
    }

    private async Task FailDeliveryAsync(Guid deliveryId, Exception exception, NotificationWorkerOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var delivery = await dbContext.NotificationDeliveries.SingleAsync(item => item.Id == deliveryId, cancellationToken);
        NotificationDeliveryFailurePolicy.Apply(delivery, FailureKind(exception), settings, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<NotificationDeliveryContext> ResolveDeliveryContextAsync(
        PlatformDbContext dbContext,
        AlertIncidentEntity incident,
        CancellationToken cancellationToken)
    {
        var deviceType = incident.ResourceType;
        if (incident.ResourceType.Equals("recorder", StringComparison.OrdinalIgnoreCase))
        {
            deviceType = await dbContext.Recorders.AsNoTracking()
                .Where(item => item.Id == incident.ResourceId)
                .Select(item => item.DeviceKind)
                .SingleOrDefaultAsync(cancellationToken)
                ?? "recorder";
        }

        return new NotificationDeliveryContext(
            await ResolveRegionPathAsync(dbContext, incident.RegionId, cancellationToken),
            deviceType);
    }

    private static async Task<string?> ResolveRegionPathAsync(
        PlatformDbContext dbContext,
        Guid? regionId,
        CancellationToken cancellationToken)
    {
        if (regionId is null)
        {
            return null;
        }

        var names = new Stack<string>();
        var visited = new HashSet<Guid>();
        var current = regionId;
        while (current is { } currentId && visited.Add(currentId))
        {
            var region = await dbContext.Regions.AsNoTracking()
                .Where(item => item.Id == currentId)
                .Select(item => new { item.Name, item.ParentId })
                .SingleOrDefaultAsync(cancellationToken);
            if (region is null)
            {
                break;
            }
            names.Push(region.Name);
            current = region.ParentId;
        }

        return names.Count == 0 ? null : string.Join(" / ", names);
    }

    private static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(1800, Math.Pow(2, Math.Min(attempts, 10)) * 5));

    private static string FailureKind(Exception exception) => exception.GetType().Name[..Math.Min(exception.GetType().Name.Length, 1024)];
}

public static class NotificationDeliveryFailurePolicy
{
    public static void Apply(
        NotificationDeliveryEntity delivery,
        string failureKind,
        NotificationWorkerOptions settings,
        DateTimeOffset occurredAt)
    {
        delivery.LastError = failureKind;
        delivery.LockedBy = null;
        delivery.LockedUntil = null;
        if (delivery.Attempts >= settings.MaxAttempts)
        {
            delivery.Status = NotificationDeliveryStatus.DeadLettered;
            delivery.NextAttemptAt = DateTimeOffset.MaxValue;
            return;
        }

        delivery.Status = NotificationDeliveryStatus.Failed;
        delivery.NextAttemptAt = occurredAt.Add(TimeSpan.FromSeconds(Math.Min(1800, Math.Pow(2, Math.Min(delivery.Attempts, 10)) * 5)));
    }
}

public sealed class NotificationWorkerOptions
{
    public int PollIntervalSeconds { get; init; } = 5;
    public int LockSeconds { get; init; } = 60;
    public int MaxAttempts { get; init; } = 8;
    public int DeliveryTimeoutSeconds { get; init; } = 15;

    public void Validate()
    {
        if (PollIntervalSeconds is < 1 or > 60 ||
            LockSeconds is < 30 or > 300 ||
            MaxAttempts is < 1 or > 20 ||
            DeliveryTimeoutSeconds is < 5 or > 60 ||
            LockSeconds < DeliveryTimeoutSeconds + 15)
        {
            throw new InvalidOperationException("通知 Worker 参数超出允许范围。 ");
        }
    }
}
