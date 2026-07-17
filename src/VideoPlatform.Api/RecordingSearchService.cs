using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class RecordingSearchService(
    PlatformDbContext dbContext,
    RecorderOperationRoutingService operationRoutingService)
{
    public const string AggregateType = "recording_search";

    public async Task<RecordingSearchCreation> CreateAsync(
        Guid userId,
        Guid userSessionId,
        CameraEntity camera,
        CreateRecordingSearchRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var route = await operationRoutingService.GetReadyRouteAsync(
            camera.RecorderId,
            RecorderOperation.RecordingSearch,
            cancellationToken);
        if (route is null)
        {
            throw new RecordingSearchUnavailableException("当前录像机尚未完成录像检索能力验收。");
        }
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({CreateRequestLockKey(userSessionId, request.ClientRequestId)})",
            cancellationToken);

        var existing = await dbContext.RecordingSearches.SingleOrDefaultAsync(
            item => item.UserSessionId == userSessionId && item.ClientRequestId == request.ClientRequestId,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RecordingSearchCreation(existing, false);
        }

        var search = new RecordingSearchEntity
        {
            Id = Guid.NewGuid(),
            CameraId = camera.Id,
            UserId = userId,
            UserSessionId = userSessionId,
            ClientRequestId = request.ClientRequestId,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            MaxResults = request.MaxResults,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(10)
        };
        var command = new EdgeCommandEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = route.WorkerId,
            RecorderId = camera.RecorderId,
            CommandType = route.CommandType,
            AggregateType = AggregateType,
            AggregateId = search.Id,
            PayloadJson = JsonSerializer.Serialize(new RecordingSearchCommandPayload(
                camera.Id,
                request.StartedAt,
                request.EndedAt,
                request.MaxResults)),
            CreatedAt = now,
            NextAttemptAt = now
        };
        dbContext.RecordingSearches.Add(search);
        dbContext.EdgeCommands.Add(command);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RecordingSearchCreation(search, true);
    }

    public static void ValidateRequest(CreateRecordingSearchRequest request)
    {
        if (request.ClientRequestId == Guid.Empty || request.MaxResults is < 1 or > 200 ||
            request.StartedAt >= request.EndedAt || request.EndedAt - request.StartedAt > TimeSpan.FromDays(31))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "录像检索参数无效。");
        }
    }

    private static long CreateRequestLockKey(Guid userSessionId, Guid clientRequestId)
    {
        Span<byte> bytes = stackalloc byte[32];
        userSessionId.TryWriteBytes(bytes);
        clientRequestId.TryWriteBytes(bytes[16..]);
        return BitConverter.ToInt64(SHA256.HashData(bytes), 0);
    }
}

public sealed class RecordingSearchCleanupService(IServiceScopeFactory scopeFactory, ILogger<RecordingSearchCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
                var now = DateTimeOffset.UtcNow;
                var expired = await dbContext.RecordingSearches
                    .Where(item => item.ExpiresAt <= now && item.Status != RecordingSearchStatus.Expired)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, RecordingSearchStatus.Expired)
                        .SetProperty(item => item.ResultJson, (string?)null)
                        .SetProperty(item => item.FailureKind, (string?)null), stoppingToken);
                if (expired > 0)
                {
                    logger.LogInformation("已清理 {Count} 条过期录像检索结果。", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "录像检索结果过期清理失败，下个周期继续重试。");
            }
        }
    }
}

public sealed record CreateRecordingSearchRequest(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int MaxResults,
    Guid ClientRequestId);

public sealed record RecordingSearchCreation(RecordingSearchEntity Search, bool Created);

public sealed class RecordingSearchUnavailableException(string message) : Exception(message);
