using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class StreamGatewayRevocationWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<StreamGatewayOptions> options,
    ILogger<StreamGatewayRevocationWorker> logger) : BackgroundService
{
    public const string HttpClientName = "stream-gateway-command";

    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configurationWarningWritten = false;
        var nextRetentionCleanupAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.Value.TryValidateCommand(out var settings, out var error))
            {
                if (!configurationWarningWritten)
                {
                    logger.LogWarning("流网关主动撤销投递暂未启用：{Reason}", error);
                    configurationWarningWritten = true;
                }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            configurationWarningWritten = false;
            if (nextRetentionCleanupAt <= DateTimeOffset.UtcNow)
            {
                try
                {
                    await CleanupProcessedAsync(settings, stoppingToken);
                    nextRetentionCleanupAt = DateTimeOffset.UtcNow.AddHours(1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "流网关撤销 Outbox 历史清理失败，下个周期继续重试。");
                    nextRetentionCleanupAt = DateTimeOffset.UtcNow.AddMinutes(5);
                }
            }

            Guid eventId;
            try
            {
                eventId = await ClaimNextAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "流网关撤销投递循环执行失败，下个周期继续重试。");
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
                continue;
            }

            if (eventId == Guid.Empty)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken);
                continue;
            }

            try
            {
                await DispatchAsync(eventId, settings, stoppingToken);
                await CompleteAsync(eventId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                try
                {
                    await FailAsync(eventId, exception, settings, stoppingToken);
                }
                catch (Exception persistenceException)
                {
                    logger.LogError(
                        persistenceException,
                        "流会话撤销命令 {EventId} 的失败状态保存失败，锁过期后将重新领取。",
                        eventId);
                }
                logger.LogError(
                    "流会话撤销命令 {EventId} 投递失败，失败类别 {FailureKind}。",
                    eventId,
                    FailureKind(exception));
            }
        }
    }

    private async Task<Guid> ClaimNextAsync(
        ValidatedStreamGatewayCommandSettings settings,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var candidates = await dbContext.OutboxEvents.FromSqlRaw("""
            SELECT * FROM outbox_events
            WHERE "EventType" = 'stream.session.revoked'
              AND "ProcessedAt" IS NULL
              AND "DeadLetteredAt" IS NULL
              AND "NextAttemptAt" <= NOW()
              AND ("LockedUntil" IS NULL OR "LockedUntil" < NOW())
            ORDER BY "OccurredAt", "Id"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var outboxEvent = candidates.SingleOrDefault();
        if (outboxEvent is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Guid.Empty;
        }

        outboxEvent.LockedBy = workerId;
        outboxEvent.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(settings.LockSeconds);
        outboxEvent.Attempts++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outboxEvent.Id;
    }

    private async Task CleanupProcessedAsync(
        ValidatedStreamGatewayCommandSettings settings,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var threshold = DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays);
        var deleted = await dbContext.OutboxEvents
            .Where(item => item.EventType == "stream.session.revoked" &&
                           item.ProcessedAt != null && item.ProcessedAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation("已清理 {Count} 条过期流会话撤销命令。", deleted);
        }
    }

    private async Task DispatchAsync(
        Guid eventId,
        ValidatedStreamGatewayCommandSettings settings,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var outboxEvent = await dbContext.OutboxEvents.AsNoTracking()
            .SingleAsync(item => item.Id == eventId && item.LockedBy == workerId, cancellationToken);
        var payload = JsonSerializer.Deserialize<StreamSessionRevokedPayload>(outboxEvent.PayloadJson)
            ?? throw new InvalidOperationException("流会话撤销事件载荷无效。");
        if (!string.Equals(payload.GatewayName, options.Value.GatewayName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("撤销事件目标网关与当前网关配置不一致。");
        }
        var renewed = await dbContext.OutboxEvents
            .Where(item => item.Id == eventId && item.LockedBy == workerId && item.ProcessedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LockedUntil, DateTimeOffset.UtcNow.AddSeconds(settings.LockSeconds)), cancellationToken);
        if (renewed != 1)
        {
            throw new InvalidOperationException("流会话撤销命令锁已失效，停止本次投递。");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(settings.BaseUri, $"v1/control/sessions/{payload.SessionId:N}/revoke"));
        request.Headers.TryAddWithoutValidation("X-Stream-Gateway-Command-Token", settings.Token);
        request.Content = JsonContent.Create(new StreamGatewayRevocationCommand(
            eventId,
            payload.SessionId,
            payload.Reason,
            payload.RevokedAt));

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"流网关撤销命令返回 HTTP {(int)response.StatusCode}。",
                null,
                response.StatusCode);
        }
    }

    private async Task CompleteAsync(Guid eventId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        await dbContext.OutboxEvents
            .Where(item => item.Id == eventId && item.LockedBy == workerId && item.ProcessedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ProcessedAt, DateTimeOffset.UtcNow)
                .SetProperty(item => item.LockedBy, (string?)null)
                .SetProperty(item => item.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.LastError, (string?)null), cancellationToken);
    }

    private async Task FailAsync(
        Guid eventId,
        Exception exception,
        ValidatedStreamGatewayCommandSettings settings,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var outboxEvent = await dbContext.OutboxEvents
            .SingleOrDefaultAsync(item => item.Id == eventId && item.LockedBy == workerId, cancellationToken);
        if (outboxEvent is null)
        {
            return;
        }

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

    private static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempts, 8))));

    private static string FailureKind(Exception exception)
    {
        var status = exception is HttpRequestException { StatusCode: not null } requestException
            ? $":HTTP{(int)requestException.StatusCode.Value}"
            : string.Empty;
        var value = exception.GetType().Name + status;
        return value[..Math.Min(value.Length, 1024)];
    }
}

public sealed record StreamGatewayRevocationCommand(
    Guid CommandId,
    Guid SessionId,
    string Reason,
    DateTimeOffset RevokedAt);
