using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class StreamSessionOrchestrator(
    PlatformDbContext dbContext,
    IOptions<StreamGatewayOptions> options,
    RecorderOperationRoutingService operationRoutingService,
    PlatformAccessService accessService,
    DevicePluginCapabilityService? capabilityService = null)
{
    public const string PlaybackRelayAggregateType = "playback_relay_session";
    private static readonly string[] PlaybackRelayStartCommandTypes =
    [
        EdgeCommandTypes.OnvifPlaybackRelayStart,
        EdgeCommandTypes.PluginPlaybackRelayStart
    ];
    private static readonly string[] PlaybackRelayControlCommandTypes =
    [
        EdgeCommandTypes.OnvifPlaybackRelayControl,
        EdgeCommandTypes.PluginPlaybackRelayControl
    ];

    public async Task<IssuedStreamSession> IssueAsync(
        Guid userId,
        Guid userSessionId,
        Guid cameraId,
        CameraPermission operation,
        string? requestedProfile,
        int slotNumber,
        Guid clientRequestId,
        CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        if (operation != CameraPermission.LiveView)
        {
            throw new ArgumentOutOfRangeException(nameof(operation), "实时流网关只接受 LiveView；回放必须使用独立中继。");
        }
        if (userSessionId == Guid.Empty || clientRequestId == Guid.Empty || slotNumber is < 0 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(slotNumber), "客户端会话、请求编号和 0 至 63 的窗格编号不能为空。");
        }
        var recorderId = await dbContext.Cameras.AsNoTracking()
            .Where(item => item.Id == cameraId)
            .Select(item => item.RecorderId)
            .SingleOrDefaultAsync(cancellationToken);
        if (recorderId == Guid.Empty ||
            !await (capabilityService ?? new DevicePluginCapabilityService(dbContext)).SupportsAsync(
                recorderId,
                DevicePluginCapabilityKind.LiveView,
                cancellationToken))
        {
            throw new StreamSessionInactiveException("摄像头所属协议插件未启用实时预览能力。");
        }

        var profile = NormalizeProfile(requestedProfile);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await AcquireIssueLocksAsync(userId, userSessionId, cameraId, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                if (!await dbContext.UserSessions.AsNoTracking().AnyAsync(
                        item => item.Id == userSessionId && item.UserId == userId && item.RevokedAt == null && item.ExpiresAt > now,
                        cancellationToken))
                {
                    throw new StreamSessionInactiveException("当前登录会话已失效，不能创建流会话。");
                }

                var idempotent = await dbContext.StreamSessions
                    .FromSqlInterpolated($$"""
                        SELECT * FROM stream_sessions
                        WHERE "UserSessionId" = {{userSessionId}} AND "ClientRequestId" = {{clientRequestId}}
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);
                if (idempotent is not null)
                {
                    if (idempotent.RevokedAt is not null || idempotent.ExpiresAt <= now || idempotent.CameraId != cameraId ||
                        idempotent.Operation != operation || idempotent.Profile != profile || idempotent.SlotNumber != slotNumber)
                    {
                        throw new StreamSessionConflictException("同一客户端请求编号已用于其他或失效的流会话。");
                    }

                    throw new StreamSessionDuplicateRequestException(idempotent.Id);
                }

                var replaced = await dbContext.StreamSessions
                    .FromSqlInterpolated($$"""
                        SELECT * FROM stream_sessions
                        WHERE "UserSessionId" = {{userSessionId}} AND "SlotNumber" = {{slotNumber}} AND "RevokedAt" IS NULL
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                await RevokeLockedSessionsAsync(replaced, "slot_replaced", now, cancellationToken);

                await EnforceQuotasAsync(userId, userSessionId, cameraId, profile, settings, now, cancellationToken);
                var streamKey = $"live/{cameraId:N}/{profile}";
                await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO stream_assignments ("Id", "StreamKey", "GatewayName", "ReferenceCount", "LastAccessedAt")
                    VALUES ({{Guid.NewGuid()}}, {{streamKey}}, {{settings.GatewayName}}, 1, {{now}})
                    ON CONFLICT ("StreamKey") DO UPDATE
                    SET "ReferenceCount" = stream_assignments."ReferenceCount" + 1,
                        "LastAccessedAt" = EXCLUDED."LastAccessedAt",
                        "GatewayName" = EXCLUDED."GatewayName"
                    """, cancellationToken);

                var session = new StreamSessionEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    UserSessionId = userSessionId,
                    ClientRequestId = clientRequestId,
                    SlotNumber = slotNumber,
                    CameraId = cameraId,
                    Operation = operation,
                    Profile = profile,
                    StreamKey = streamKey,
                    GatewayName = settings.GatewayName,
                    CreatedAt = now,
                    LastRenewedAt = now,
                    ExpiresAt = now.AddSeconds(settings.LeaseLifetimeSeconds)
                };
                dbContext.StreamSessions.Add(session);
                await dbContext.SaveChangesAsync(cancellationToken);
                var issued = await IssueTicketAsync(session, settings, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return issued;
            }
            catch (Exception exception) when (IsRetryableDatabaseException(exception) && attempt < 3)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        throw new StreamSessionConflictException("流会话并发签发重试失败，请稍后重试。");
    }

    public async Task<CreatedPlaybackSession> IssuePlaybackAsync(
        Guid userId,
        Guid userSessionId,
        CameraEntity camera,
        PlaybackSessionRequest request,
        CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        ValidatePlaybackRequest(userSessionId, request);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await AcquireIssueLocksAsync(userId, userSessionId, camera.Id, cancellationToken);
                var now = DateTimeOffset.UtcNow;
                if (!await dbContext.UserSessions.AsNoTracking().AnyAsync(
                        item => item.Id == userSessionId && item.UserId == userId && item.RevokedAt == null && item.ExpiresAt > now,
                        cancellationToken))
                {
                    throw new StreamSessionInactiveException("当前登录会话已失效，不能创建回放会话。");
                }

                var idempotent = await dbContext.StreamSessions
                    .FromSqlInterpolated($$"""
                        SELECT * FROM stream_sessions
                        WHERE "UserSessionId" = {{userSessionId}} AND "ClientRequestId" = {{request.ClientRequestId}}
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .SingleOrDefaultAsync(cancellationToken);
                if (idempotent is not null)
                {
                    if (idempotent.RevokedAt is not null || idempotent.ExpiresAt <= now || idempotent.CameraId != camera.Id ||
                        idempotent.Operation != CameraPermission.Playback || idempotent.Profile != "playback" ||
                        idempotent.SlotNumber != request.SlotNumber || idempotent.PlaybackStartedAt != request.StartedAt ||
                        idempotent.PlaybackEndedAt != request.EndedAt)
                    {
                        throw new StreamSessionConflictException("同一客户端请求编号已用于其他或失效的回放会话。");
                    }

                    throw new StreamSessionDuplicateRequestException(idempotent.Id);
                }

                var route = await operationRoutingService.GetReadyRouteAsync(
                    camera.RecorderId,
                    RecorderOperation.PlaybackRelay,
                    cancellationToken);
                if (route is null)
                {
                    throw new PlaybackRelayUnavailableException("当前录像机尚未完成回放中继能力验收。");
                }
                var replaced = await dbContext.StreamSessions
                    .FromSqlInterpolated($$"""
                        SELECT * FROM stream_sessions
                        WHERE "UserSessionId" = {{userSessionId}} AND "SlotNumber" = {{request.SlotNumber}} AND "RevokedAt" IS NULL
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                await RevokeLockedSessionsAsync(replaced, "slot_replaced", now, cancellationToken);

                await EnforceQuotasAsync(userId, userSessionId, camera.Id, "playback", settings, now, cancellationToken);
                var sessionId = Guid.NewGuid();
                var streamKey = $"playback/{sessionId:N}";
                await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                    INSERT INTO stream_assignments ("Id", "StreamKey", "GatewayName", "ReferenceCount", "LastAccessedAt")
                    VALUES ({{Guid.NewGuid()}}, {{streamKey}}, {{settings.GatewayName}}, 1, {{now}})
                    """, cancellationToken);

                var session = new StreamSessionEntity
                {
                    Id = sessionId,
                    UserId = userId,
                    UserSessionId = userSessionId,
                    ClientRequestId = request.ClientRequestId,
                    SlotNumber = request.SlotNumber,
                    CameraId = camera.Id,
                    Operation = CameraPermission.Playback,
                    Profile = "playback",
                    StreamKey = streamKey,
                    GatewayName = settings.GatewayName,
                    CreatedAt = now,
                    LastRenewedAt = now,
                    ExpiresAt = now.AddSeconds(settings.LeaseLifetimeSeconds),
                    PlaybackStartedAt = request.StartedAt,
                    PlaybackEndedAt = request.EndedAt
                };
                var startCommand = new EdgeCommandEntity
                {
                    Id = Guid.NewGuid(),
                    WorkerId = route.WorkerId,
                    RecorderId = camera.RecorderId,
                    CommandType = route.CommandType,
                    AggregateType = PlaybackRelayAggregateType,
                    AggregateId = sessionId,
                    PayloadJson = JsonSerializer.Serialize(new PlaybackRelayStartCommandPayload(
                        sessionId,
                        camera.Id,
                        request.StartedAt,
                        request.EndedAt)),
                    CreatedAt = now,
                    NextAttemptAt = now
                };
                dbContext.StreamSessions.Add(session);
                dbContext.EdgeCommands.Add(startCommand);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new CreatedPlaybackSession(session, new PlaybackRelaySessionState(
                    PlaybackRelayStatus.Pending,
                    startCommand.Id,
                    null,
                    startCommand.Attempts));
            }
            catch (Exception exception) when (IsRetryableDatabaseException(exception) && attempt < 3)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        throw new StreamSessionConflictException("回放会话并发签发重试失败，请稍后重试。");
    }

    public async Task<PlaybackRelaySessionState> GetPlaybackRelayStateAsync(
        Guid playbackSessionId,
        CancellationToken cancellationToken)
    {
        var command = await dbContext.EdgeCommands.AsNoTracking()
            .Where(item => item.AggregateType == PlaybackRelayAggregateType &&
                item.AggregateId == playbackSessionId &&
                PlaybackRelayStartCommandTypes.Contains(item.CommandType))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (command is null)
        {
            return new PlaybackRelaySessionState(PlaybackRelayStatus.Failed, null, "start_command_missing", 0);
        }
        if (command.DeadLetteredAt is not null)
        {
            return new PlaybackRelaySessionState(PlaybackRelayStatus.Failed, command.Id, command.LastError, command.Attempts);
        }
        if (command.CompletedAt is not null)
        {
            return new PlaybackRelaySessionState(PlaybackRelayStatus.Ready, command.Id, null, command.Attempts);
        }
        return new PlaybackRelaySessionState(PlaybackRelayStatus.Pending, command.Id, command.LastError, command.Attempts);
    }

    public async Task<PlaybackTransportState> GetPlaybackTransportStateAsync(
        StreamSessionEntity session,
        CancellationToken cancellationToken)
    {
        var fallback = new PlaybackTransportState(
            PlaybackTransportStatus.Ready,
            false,
            session.PlaybackStartedAt ?? session.CreatedAt,
            1.0,
            true,
            true,
            true,
            null,
            null);
        var command = await dbContext.EdgeCommands.AsNoTracking()
            .Where(item => item.AggregateType == PlaybackRelayAggregateType &&
                item.AggregateId == session.Id &&
                PlaybackRelayControlCommandTypes.Contains(item.CommandType))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (command is null)
        {
            return fallback;
        }
        if (command.DeadLetteredAt is not null)
        {
            return fallback with
            {
                Status = PlaybackTransportStatus.Failed,
                Detail = command.LastError,
                CommandId = command.Id
            };
        }
        if (command.CompletedAt is null)
        {
            return fallback with { Status = PlaybackTransportStatus.Pending, CommandId = command.Id };
        }
        if (string.IsNullOrWhiteSpace(command.ResultJson))
        {
            return fallback with { CommandId = command.Id };
        }
        try
        {
            using var document = JsonDocument.Parse(command.ResultJson);
            var result = document.RootElement;
            var position = result.TryGetProperty("position", out var positionValue) &&
                           positionValue.TryGetDateTimeOffset(out var parsedPosition)
                ? parsedPosition
                : fallback.Position;
            var speed = result.TryGetProperty("speed", out var speedValue) && speedValue.TryGetDouble(out var parsedSpeed)
                ? parsedSpeed
                : fallback.Speed;
            return fallback with
            {
                IsPaused = result.TryGetProperty("isPaused", out var pausedValue) && pausedValue.ValueKind == JsonValueKind.True,
                Position = position,
                Speed = speed,
                CanPause = !result.TryGetProperty("canPause", out var canPauseValue) || canPauseValue.ValueKind != JsonValueKind.False,
                CanSeek = !result.TryGetProperty("canSeek", out var canSeekValue) || canSeekValue.ValueKind != JsonValueKind.False,
                CanChangeSpeed = !result.TryGetProperty("canChangeSpeed", out var canSpeedValue) || canSpeedValue.ValueKind != JsonValueKind.False,
                Detail = result.TryGetProperty("detail", out var detailValue) ? detailValue.GetString() : null,
                CommandId = command.Id
            };
        }
        catch (JsonException)
        {
            return fallback with
            {
                Status = PlaybackTransportStatus.Failed,
                Detail = "回放控制状态格式无效。",
                CommandId = command.Id
            };
        }
    }

    public async Task<QueuedPlaybackTransportCommand> QueuePlaybackTransportCommandAsync(
        Guid sessionId,
        Guid userId,
        Guid userSessionId,
        PlaybackTransportRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePlaybackTransportRequest(request);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var session = await FindLockedSessionAsync(sessionId, cancellationToken);
        if (session is null || session.Operation != CameraPermission.Playback || session.UserId != userId ||
            session.UserSessionId != userSessionId || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            throw new StreamSessionInactiveException("回放会话已失效或不属于当前登录会话。");
        }
        if (!await accessService.HasCameraPermissionAsync(userId, session.CameraId, CameraPermission.Playback, cancellationToken))
        {
            await RevokeLockedSessionsAsync([session], "camera_permission_revoked", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new StreamSessionInactiveException("摄像头回放权限已收回，回放会话已撤销。");
        }
        var playbackStartedAt = session.PlaybackStartedAt
            ?? throw new StreamSessionInactiveException("回放会话缺少开始时间。");
        var playbackEndedAt = session.PlaybackEndedAt
            ?? throw new StreamSessionInactiveException("回放会话缺少结束时间。");
        if (request.Action == PlaybackTransportAction.Seek &&
            (request.Position is null || request.Position.Value < playbackStartedAt || request.Position.Value > playbackEndedAt))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "定位时间不在当前回放范围内。");
        }
        var relayState = await GetPlaybackRelayStateAsync(session.Id, cancellationToken);
        if (relayState.Status != PlaybackRelayStatus.Ready)
        {
            throw new PlaybackRelayNotReadyException(relayState.Status, relayState.FailureKind);
        }
        var worker = await GetPlaybackRelayOwnerAsync(session.Id, cancellationToken)
            ?? throw new PlaybackRelayNotReadyException(PlaybackRelayStatus.Failed, "relay_owner_missing");
        var commandType = EdgeCommandTypes.GetPlaybackRelayControlCommandType(worker.StartCommandType)
            ?? throw new PlaybackRelayNotReadyException(PlaybackRelayStatus.Failed, "unsupported_relay_controller");
        var requestIdentity = JsonSerializer.Serialize(new { request.ClientRequestId });
        var existingCommand = await dbContext.EdgeCommands
            .Where(item => item.AggregateType == PlaybackRelayAggregateType &&
                item.AggregateId == session.Id &&
                item.CommandType == commandType &&
                EF.Functions.JsonContains(item.PayloadJson, requestIdentity))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingCommand is not null)
        {
            PlaybackRelayControlCommandPayload? existingPayload;
            try
            {
                existingPayload = JsonSerializer.Deserialize<PlaybackRelayControlCommandPayload>(existingCommand.PayloadJson);
            }
            catch (JsonException)
            {
                throw new StreamSessionConflictException("已有回放控制命令的参数无法读取。");
            }
            if (existingPayload is null || existingPayload.Action != request.Action ||
                existingPayload.Position != request.Position || existingPayload.Speed != request.Speed)
            {
                throw new StreamSessionConflictException("同一客户端请求编号不能用于不同的回放控制参数。");
            }
            await transaction.CommitAsync(cancellationToken);
            return new QueuedPlaybackTransportCommand(existingCommand.Id, session);
        }
        var command = new EdgeCommandEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = worker.WorkerId,
            RecorderId = worker.RecorderId,
            CommandType = commandType,
            AggregateType = PlaybackRelayAggregateType,
            AggregateId = session.Id,
            PayloadJson = JsonSerializer.Serialize(new PlaybackRelayControlCommandPayload(
                session.Id,
                session.CameraId,
                request.Action,
                request.Position,
                request.Speed,
                request.ClientRequestId)),
            CreatedAt = now,
            NextAttemptAt = now
        };
        dbContext.EdgeCommands.Add(command);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new QueuedPlaybackTransportCommand(command.Id, session);
    }

    public async Task<RenewedStreamSession> RenewAsync(
        Guid sessionId,
        Guid userId,
        Guid userSessionId,
        CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var session = await FindLockedSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId || session.UserSessionId != userSessionId)
        {
            throw new StreamSessionInactiveException("流会话不存在或不属于当前登录会话。");
        }
        if (session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            throw new StreamSessionInactiveException("流会话已撤销或租约已过期，不能续租。");
        }
        if (!await accessService.HasCameraPermissionAsync(session.UserId, session.CameraId, session.Operation, cancellationToken))
        {
            await RevokeLockedSessionsAsync([session], "camera_permission_revoked", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new StreamSessionInactiveException("摄像头权限已收回，流会话已撤销。");
        }

        var leaseExpiresAt = now.AddSeconds(settings.LeaseLifetimeSeconds);
        await dbContext.StreamSessions
            .Where(item => item.Id == sessionId && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastRenewedAt, now)
                .SetProperty(item => item.ExpiresAt, leaseExpiresAt), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RenewedStreamSession(sessionId, leaseExpiresAt, settings.RenewAfterSeconds);
    }

    public async Task<IssuedStreamSession> IssueReconnectTicketAsync(
        Guid sessionId,
        Guid userId,
        Guid userSessionId,
        CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var session = await FindLockedSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId || session.UserSessionId != userSessionId ||
            session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            throw new StreamSessionInactiveException("流会话已失效，不能签发重连票据。");
        }
        if (!await accessService.HasCameraPermissionAsync(session.UserId, session.CameraId, session.Operation, cancellationToken))
        {
            await RevokeLockedSessionsAsync([session], "camera_permission_revoked", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new StreamSessionInactiveException("摄像头权限已收回，流会话已撤销。");
        }
        if (session.Operation == CameraPermission.Playback)
        {
            var relayState = await GetPlaybackRelayStateAsync(session.Id, cancellationToken);
            if (relayState.Status != PlaybackRelayStatus.Ready)
            {
                throw new PlaybackRelayNotReadyException(relayState.Status, relayState.FailureKind);
            }
        }

        var issued = await IssueTicketAsync(session, settings, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return issued;
    }

    public async Task<GatewayStreamSession> ConsumeTicketAsync(
        Guid sessionId,
        string ticket,
        string gatewayName,
        CancellationToken cancellationToken)
    {
        var settings = GetValidatedSettings();
        if (!string.Equals(gatewayName, settings.GatewayName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(ticket))
        {
            throw new StreamTicketInvalidException("网关名称或连接票据无效。");
        }

        var ticketHash = HashToken(ticket);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var session = await FindLockedSessionAsync(sessionId, cancellationToken);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now || session.GatewayName != gatewayName)
        {
            throw new StreamSessionInactiveException("流会话已撤销或租约已过期。");
        }
        if (session.Operation == CameraPermission.Playback)
        {
            var relayState = await GetPlaybackRelayStateAsync(session.Id, cancellationToken);
            if (relayState.Status != PlaybackRelayStatus.Ready)
            {
                throw new StreamSessionInactiveException("回放中继尚未就绪或已失败。");
            }
        }

        var connectionTicket = await dbContext.StreamConnectionTickets
            .FromSqlInterpolated($$"""
                SELECT * FROM stream_connection_tickets
                WHERE "SessionId" = {{sessionId}} AND "TokenHash" = {{ticketHash}}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (connectionTicket is null || connectionTicket.RevokedAt is not null || connectionTicket.ConsumedAt is not null ||
            connectionTicket.ExpiresAt <= now || !FixedTimeHashEquals(connectionTicket.TokenHash, ticketHash))
        {
            throw new StreamTicketInvalidException("连接票据不存在、已使用或已过期。");
        }

        connectionTicket.ConsumedAt = now;
        connectionTicket.ConsumedByGateway = gatewayName;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToGatewaySession(session, true);
    }

    public async Task<IReadOnlyList<GatewayStreamSession>> InspectForGatewayAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken)
    {
        if (sessionIds.Count is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionIds), "单次最多查询 1000 个流会话状态。");
        }
        var now = DateTimeOffset.UtcNow;
        var sessions = await dbContext.StreamSessions.AsNoTracking()
            .Where(item => sessionIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        return sessions.Select(item => ToGatewaySession(item, item.RevokedAt is null && item.ExpiresAt > now)).ToList();
    }

    public bool IsGatewayControlTokenValid(string? presentedToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ControlToken) || settings.ControlToken.Length < 32 || string.IsNullOrWhiteSpace(presentedToken))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(settings.ControlToken)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken)));
    }

    public async Task<bool> RevokeAsync(
        Guid sessionId,
        Guid userId,
        Guid userSessionId,
        bool isSystemAdministrator,
        string reason,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithDatabaseRetryAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var session = await FindLockedSessionAsync(sessionId, cancellationToken);
            if (session is null || (!isSystemAdministrator && (session.UserId != userId || session.UserSessionId != userSessionId)))
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
            await RevokeLockedSessionsAsync([session], reason, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, "撤销流会话", cancellationToken);
    }

    public async Task<bool> RevokeLoginSessionAsync(
        Guid userSessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        return await ExecuteWithDatabaseRetryAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await AcquireAdvisoryLockAsync($"stream:client:{userSessionId:N}", cancellationToken);
            var loginSession = await dbContext.UserSessions
                .FromSqlInterpolated($$"""SELECT * FROM user_sessions WHERE "Id" = {{userSessionId}} FOR UPDATE""")
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (loginSession is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            var sessions = await dbContext.StreamSessions
                .FromSqlInterpolated($$"""SELECT * FROM stream_sessions WHERE "UserSessionId" = {{userSessionId}} AND "RevokedAt" IS NULL ORDER BY "Id" FOR UPDATE""")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            await RevokeLockedSessionsAsync(sessions, reason, now, cancellationToken);
            await dbContext.UserSessions
                .Where(item => item.Id == userSessionId && item.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, "注销登录会话", cancellationToken);
    }

    public Task RevokeForUserSessionAsync(Guid userSessionId, string reason, CancellationToken cancellationToken) =>
        RevokeBatchAsync(
            $$"""SELECT * FROM stream_sessions WHERE "UserSessionId" = {{userSessionId}} AND "RevokedAt" IS NULL ORDER BY "Id" FOR UPDATE""",
            reason,
            cancellationToken,
            $"stream:client:{userSessionId:N}");

    public Task RevokeForUserAsync(Guid userId, string reason, CancellationToken cancellationToken) =>
        RevokeBatchAsync(
            $$"""SELECT * FROM stream_sessions WHERE "UserId" = {{userId}} AND "RevokedAt" IS NULL ORDER BY "Id" FOR UPDATE""",
            reason,
            cancellationToken,
            $"stream:user:{userId:N}");

    public async Task RevokeForCamerasAsync(
        IEnumerable<Guid> cameraIds,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var cameraId in cameraIds.Where(item => item != Guid.Empty).Distinct().OrderBy(item => item))
        {
            await RevokeBatchAsync(
                $$"""SELECT * FROM stream_sessions WHERE "CameraId" = {{cameraId}} AND "RevokedAt" IS NULL ORDER BY "Id" FOR UPDATE""",
                reason,
                cancellationToken,
                $"stream:camera:{cameraId:N}");
        }
    }

    public async Task RevokeUnauthorizedForCamerasAsync(
        IEnumerable<Guid> cameraIds,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var cameraId in cameraIds.Where(item => item != Guid.Empty).Distinct().OrderBy(item => item))
        {
            await ExecuteWithDatabaseRetryAsync(async () =>
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                await AcquireAdvisoryLockAsync($"stream:camera:{cameraId:N}", cancellationToken);
                var sessions = await dbContext.StreamSessions
                    .FromSqlInterpolated($$"""
                        SELECT * FROM stream_sessions
                        WHERE "CameraId" = {{cameraId}} AND "RevokedAt" IS NULL
                        ORDER BY "Id"
                        FOR UPDATE
                        """)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);
                var unauthorized = new List<StreamSessionEntity>();
                foreach (var session in sessions)
                {
                    if (!await accessService.HasCameraPermissionAsync(
                            session.UserId,
                            session.CameraId,
                            session.Operation,
                            cancellationToken))
                    {
                        unauthorized.Add(session);
                    }
                }
                await RevokeLockedSessionsAsync(unauthorized, reason, DateTimeOffset.UtcNow, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return unauthorized.Count;
            }, "资产权限变化撤销流会话", cancellationToken);
        }
    }

    public async Task RevokeAllAsync(string reason, CancellationToken cancellationToken)
    {
        const int batchSize = 500;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var globalLockAcquired = false;
        try
        {
            await AcquireSessionAdvisoryLockAsync("stream:global", cancellationToken);
            globalLockAcquired = true;
            int revokedCount;
            do
            {
                revokedCount = await RevokeBatchRawAsync(
                    $"SELECT * FROM stream_sessions WHERE \"RevokedAt\" IS NULL ORDER BY \"Id\" LIMIT {batchSize} FOR UPDATE",
                    reason,
                    cancellationToken);
            } while (revokedCount == batchSize);
        }
        finally
        {
            if (globalLockAcquired)
            {
                await ReleaseSessionAdvisoryLockAsync("stream:global");
            }
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task<int> RevokeExpiredAsync(CancellationToken cancellationToken)
    {
        var ticketRetentionHours = GetValidatedTicketRetentionHours();
        return await ExecuteWithDatabaseRetryAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var expired = await dbContext.StreamSessions
                .FromSqlInterpolated($$"""
                    SELECT * FROM stream_sessions
                    WHERE "RevokedAt" IS NULL AND "ExpiresAt" <= {{now}}
                    ORDER BY "Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 500
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            await RevokeLockedSessionsAsync(expired, "lease_expired", now, cancellationToken);
            var ticketRetentionThreshold = now.AddHours(-ticketRetentionHours);
            await dbContext.StreamConnectionTickets
                .Where(item => item.ExpiresAt < ticketRetentionThreshold)
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return expired.Count;
        }, "回收过期流会话", cancellationToken);
    }

    private async Task<IssuedStreamSession> IssueTicketAsync(
        StreamSessionEntity session,
        ValidatedStreamGatewaySettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await dbContext.StreamConnectionTickets
            .Where(item => item.SessionId == session.Id && item.ConsumedAt == null && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now), cancellationToken);

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var ticketExpiresAt = now.AddSeconds(settings.TicketLifetimeSeconds);
        dbContext.StreamConnectionTickets.Add(new StreamConnectionTicketEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TokenHash = HashToken(token),
            GatewayName = settings.GatewayName,
            CreatedAt = now,
            ExpiresAt = ticketExpiresAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        var path = $"hls/{session.Id:N}/{token}/{session.StreamKey}/index.m3u8";
        return new IssuedStreamSession(
            session.Id,
            new Uri(settings.PublicBaseUri, path),
            ticketExpiresAt,
            session.ExpiresAt,
            settings.RenewAfterSeconds);
    }

    private async Task EnforceQuotasAsync(
        Guid userId,
        Guid userSessionId,
        Guid cameraId,
        string profile,
        ValidatedStreamGatewaySettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = dbContext.StreamSessions.AsNoTracking().Where(item => item.RevokedAt == null && item.ExpiresAt > now);
        if (await active.CountAsync(item => item.UserSessionId == userSessionId, cancellationToken) >= settings.MaxActiveSessionsPerClient)
        {
            throw new StreamSessionQuotaExceededException("client", settings.MaxActiveSessionsPerClient);
        }
        if (await active.CountAsync(item => item.UserId == userId, cancellationToken) >= settings.MaxActiveSessionsPerUser)
        {
            throw new StreamSessionQuotaExceededException("user", settings.MaxActiveSessionsPerUser);
        }
        if (await active.CountAsync(item => item.CameraId == cameraId, cancellationToken) >= settings.MaxActiveSessionsPerCamera)
        {
            throw new StreamSessionQuotaExceededException("camera", settings.MaxActiveSessionsPerCamera);
        }
        if (profile == "main" && await active.CountAsync(item => item.UserSessionId == userSessionId && item.Profile == "main", cancellationToken) >= settings.MaxMainProfileSessionsPerClient)
        {
            throw new StreamSessionQuotaExceededException("main_profile", settings.MaxMainProfileSessionsPerClient);
        }
    }

    private async Task AcquireIssueLocksAsync(Guid userId, Guid userSessionId, Guid cameraId, CancellationToken cancellationToken)
    {
        foreach (var key in new[] { "stream:global", $"stream:user:{userId:N}", $"stream:client:{userSessionId:N}", $"stream:camera:{cameraId:N}" })
        {
            await AcquireAdvisoryLockAsync(key, cancellationToken);
        }
    }

    private Task AcquireAdvisoryLockAsync(string key, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            cancellationToken);

    private Task AcquireSessionAdvisoryLockAsync(string key, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_lock(hashtextextended({key}, 0))",
            cancellationToken);

    private Task ReleaseSessionAdvisoryLockAsync(string key) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_unlock(hashtextextended({key}, 0))",
            CancellationToken.None);

    private async Task<StreamSessionEntity?> FindLockedSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await dbContext.StreamSessions
            .FromSqlInterpolated($$"""SELECT * FROM stream_sessions WHERE "Id" = {{sessionId}} FOR UPDATE""")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    private async Task RevokeBatchAsync(
        FormattableString query,
        string reason,
        CancellationToken cancellationToken,
        string? advisoryLockKey = null)
    {
        await ExecuteWithDatabaseRetryAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            if (advisoryLockKey is not null)
            {
                await AcquireAdvisoryLockAsync(advisoryLockKey, cancellationToken);
            }
            var sessions = await dbContext.StreamSessions.FromSqlInterpolated(query).AsNoTracking().ToListAsync(cancellationToken);
            await RevokeLockedSessionsAsync(sessions, reason, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return sessions.Count;
        }, "批量撤销流会话", cancellationToken);
    }

    private Task<int> RevokeBatchRawAsync(string query, string reason, CancellationToken cancellationToken) =>
        ExecuteWithDatabaseRetryAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var sessions = await dbContext.StreamSessions.FromSqlRaw(query).AsNoTracking().ToListAsync(cancellationToken);
            await RevokeLockedSessionsAsync(sessions, reason, DateTimeOffset.UtcNow, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return sessions.Count;
        }, "全局批量撤销流会话", cancellationToken);

    private async Task<T> ExecuteWithDatabaseRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception) when (IsRetryableDatabaseException(exception))
            {
                lastException = exception;
                dbContext.ChangeTracker.Clear();
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(attempt * 20), cancellationToken);
                }
            }
        }

        throw new StreamSessionConflictException($"{operationName}遇到数据库并发冲突，重试后仍未完成。", lastException);
    }

    private static bool IsRetryableDatabaseException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException &&
                postgresException.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                return true;
            }
        }
        return false;
    }

    private async Task RevokeLockedSessionsAsync(
        IReadOnlyCollection<StreamSessionEntity> sessions,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var targets = sessions.Where(item => item.RevokedAt is null).ToList();
        if (targets.Count == 0)
        {
            return;
        }
        var ids = targets.Select(item => item.Id).ToList();
        await dbContext.StreamConnectionTickets
            .Where(item => ids.Contains(item.SessionId) && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now), cancellationToken);
        await dbContext.StreamSessions
            .Where(item => ids.Contains(item.Id) && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RevokedAt, now)
                .SetProperty(item => item.RevocationReason, reason), cancellationToken);

        var playbackIds = targets
            .Where(item => item.Operation == CameraPermission.Playback)
            .Select(item => item.Id)
            .ToList();
        if (playbackIds.Count > 0)
        {
            await dbContext.EdgeCommands
                .Where(item => playbackIds.Contains(item.AggregateId) &&
                    item.AggregateType == PlaybackRelayAggregateType &&
                    (PlaybackRelayStartCommandTypes.Contains(item.CommandType) || PlaybackRelayControlCommandTypes.Contains(item.CommandType)) &&
                    item.CompletedAt == null && item.DeadLetteredAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.DeadLetteredAt, now)
                    .SetProperty(item => item.LastError, "session_revoked")
                    .SetProperty(item => item.LockedBy, (string?)null)
                    .SetProperty(item => item.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        }

        dbContext.OutboxEvents.AddRange(targets.Select(session => new OutboxEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "stream.session.revoked",
            AggregateType = "stream_session",
            AggregateId = session.Id,
            PayloadJson = JsonSerializer.Serialize(new StreamSessionRevokedPayload(
                session.Id,
                session.GatewayName,
                reason,
                now)),
            OccurredAt = now,
            NextAttemptAt = now
        }));

        foreach (var playbackSession in targets.Where(item => item.Operation == CameraPermission.Playback))
        {
            var worker = await GetPlaybackRelayOwnerAsync(playbackSession.Id, cancellationToken);
            if (worker is null)
            {
                continue;
            }
            var stopCommandType = EdgeCommandTypes.GetPlaybackRelayStopCommandType(worker.StartCommandType);
            if (stopCommandType is null)
            {
                continue;
            }
            dbContext.EdgeCommands.Add(new EdgeCommandEntity
            {
                Id = Guid.NewGuid(),
                WorkerId = worker.WorkerId,
                RecorderId = worker.RecorderId,
                CommandType = stopCommandType,
                AggregateType = PlaybackRelayAggregateType,
                AggregateId = playbackSession.Id,
                PayloadJson = JsonSerializer.Serialize(new PlaybackRelayStopCommandPayload(playbackSession.Id)),
                CreatedAt = now,
                NextAttemptAt = now
            });
        }

        foreach (var group in targets.GroupBy(item => item.StreamKey).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var count = group.Count();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                UPDATE stream_assignments
                SET "ReferenceCount" = GREATEST(0, "ReferenceCount" - {{count}}),
                    "LastAccessedAt" = {{now}}
                WHERE "StreamKey" = {{group.Key}}
                """, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<PlaybackWorkerAssignment?> GetPlaybackRelayOwnerAsync(
        Guid playbackSessionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.EdgeCommands
            .Where(item => item.AggregateType == PlaybackRelayAggregateType &&
                item.AggregateId == playbackSessionId &&
                PlaybackRelayStartCommandTypes.Contains(item.CommandType))
            .OrderBy(item => item.CreatedAt)
            .Select(item => new PlaybackWorkerAssignment(item.WorkerId, item.RecorderId, item.CommandType))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ValidatePlaybackRequest(Guid userSessionId, PlaybackSessionRequest request)
    {
        if (userSessionId == Guid.Empty || request.ClientRequestId == Guid.Empty || request.SlotNumber is < 0 or > 63 ||
            request.StartedAt >= request.EndedAt || request.EndedAt - request.StartedAt > TimeSpan.FromDays(31))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "回放会话参数无效。");
        }
    }

    private static void ValidatePlaybackTransportRequest(PlaybackTransportRequest request)
    {
        if (request.ClientRequestId == Guid.Empty || !Enum.IsDefined(request.Action) ||
            (request.Action == PlaybackTransportAction.Seek && request.Position is null) ||
            (request.Action == PlaybackTransportAction.Seek && request.Speed is not null) ||
            (request.Action == PlaybackTransportAction.SetSpeed && request.Speed is not (0.5 or 1.0 or 2.0 or 4.0)) ||
            (request.Action == PlaybackTransportAction.SetSpeed && request.Position is not null) ||
            (request.Action is PlaybackTransportAction.Pause or PlaybackTransportAction.Resume &&
                (request.Position is not null || request.Speed is not null)))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "回放控制参数无效。");
        }
    }

    private ValidatedStreamGatewaySettings GetValidatedSettings()
    {
        if (!options.Value.TryValidate(out var settings, out var error))
        {
            throw new StreamGatewayUnavailableException(error);
        }
        return settings;
    }

    private int GetValidatedTicketRetentionHours()
    {
        var ticketRetentionHours = options.Value.TicketRetentionHours;
        if (ticketRetentionHours is < 1 or > 168)
        {
            throw new StreamGatewayUnavailableException("票据审计保留期必须在 1 至 168 小时之间。");
        }
        return ticketRetentionHours;
    }

    private static GatewayStreamSession ToGatewaySession(StreamSessionEntity session, bool active) =>
        new(session.Id, session.StreamKey, session.CameraId, session.Operation, session.Profile, session.ExpiresAt, active, session.RevocationReason);

    private static string NormalizeProfile(string? profile) => profile?.Trim().ToLowerInvariant() switch
    {
        null or "" or "sub" => "sub",
        "main" => "main",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), "仅支持 main 或 sub 码流。")
    };

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeHashEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}

public sealed class StreamSessionCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<StreamGatewayOptions> options,
    ILogger<StreamSessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.CleanupIntervalSeconds, 5, 60));
        var configurationWarningWritten = false;
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<StreamSessionOrchestrator>();
                var revokedCount = await orchestrator.RevokeExpiredAsync(stoppingToken);
                configurationWarningWritten = false;
                if (revokedCount > 0)
                {
                    logger.LogInformation("已回收 {Count} 个过期流会话。", revokedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (StreamGatewayUnavailableException exception)
            {
                if (!configurationWarningWritten)
                {
                    logger.LogWarning("流会话回收暂未启用：{Reason}", exception.Message);
                    configurationWarningWritten = true;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "流会话过期回收执行失败，下个周期继续重试。");
            }
        }
    }
}

public sealed class StreamGatewayOptions
{
    public string GatewayName { get; init; } = "default";
    public string? PublicBaseUri { get; init; }
    public string? ControlToken { get; init; }
    public int TicketLifetimeSeconds { get; init; } = 20;
    public int LeaseLifetimeSeconds { get; init; } = 120;
    public int RenewAfterSeconds { get; init; } = 40;
    public int MaxActiveSessionsPerClient { get; init; } = 64;
    public int MaxActiveSessionsPerUser { get; init; } = 128;
    public int MaxActiveSessionsPerCamera { get; init; } = 100;
    public int MaxMainProfileSessionsPerClient { get; init; } = 8;
    public int CleanupIntervalSeconds { get; init; } = 10;
    public int TicketRetentionHours { get; init; } = 24;
    public string? CommandBaseUri { get; init; }
    public string? CommandToken { get; init; }
    public bool AllowInsecureLoopbackHttpForDevelopment { get; init; }
    public int CommandPollIntervalSeconds { get; init; } = 1;
    public int CommandLockSeconds { get; init; } = 30;
    public int CommandMaxAttempts { get; init; } = 12;
    public int CommandRetentionDays { get; init; } = 7;

    public bool TryValidate(out ValidatedStreamGatewaySettings settings, out string error)
    {
        if (string.IsNullOrWhiteSpace(PublicBaseUri) || !Uri.TryCreate(PublicBaseUri, UriKind.Absolute, out var publicBaseUri) ||
            !IsAllowedGatewayUri(publicBaseUri))
        {
            settings = null!;
            error = "流网关必须配置合法的 HTTPS 公共地址，或显式启用仅限本机回环的开发 HTTP。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(GatewayName) || GatewayName.Length > 64 || string.IsNullOrWhiteSpace(ControlToken) || ControlToken.Length < 32 ||
            TicketLifetimeSeconds is < 10 or > 60 || LeaseLifetimeSeconds is < 60 or > 300 ||
            RenewAfterSeconds is < 15 || RenewAfterSeconds >= LeaseLifetimeSeconds - 15 ||
            MaxActiveSessionsPerClient is < 1 or > 64 || MaxActiveSessionsPerUser < MaxActiveSessionsPerClient ||
            MaxActiveSessionsPerCamera is < 1 or > 1000 || MaxMainProfileSessionsPerClient is < 1 ||
            MaxMainProfileSessionsPerClient > MaxActiveSessionsPerClient || CleanupIntervalSeconds is < 5 or > 60 ||
            TicketRetentionHours is < 1 or > 168)
        {
            settings = null!;
            error = "流网关控制令牌、租期、续租间隔或配额配置无效。";
            return false;
        }
        if (!TryValidateCommand(out _, out error))
        {
            settings = null!;
            return false;
        }

        settings = new ValidatedStreamGatewaySettings(
            GatewayName,
            new Uri(publicBaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute),
            TicketLifetimeSeconds,
            LeaseLifetimeSeconds,
            RenewAfterSeconds,
            MaxActiveSessionsPerClient,
            MaxActiveSessionsPerUser,
            MaxActiveSessionsPerCamera,
            MaxMainProfileSessionsPerClient,
            TicketRetentionHours);
        error = string.Empty;
        return true;
    }

    public bool TryValidateCommand(out ValidatedStreamGatewayCommandSettings settings, out string error)
    {
        if (string.IsNullOrWhiteSpace(CommandBaseUri) ||
            !Uri.TryCreate(CommandBaseUri, UriKind.Absolute, out var commandBaseUri) ||
            !IsAllowedGatewayUri(commandBaseUri) ||
            string.IsNullOrWhiteSpace(CommandToken) || CommandToken.Length < 32 ||
            (!string.IsNullOrWhiteSpace(ControlToken) && string.Equals(ControlToken, CommandToken, StringComparison.Ordinal)) ||
            CommandPollIntervalSeconds is < 1 or > 30 || CommandLockSeconds is < 30 or > 300 ||
            CommandMaxAttempts is < 1 or > 50 || CommandRetentionDays is < 1 or > 90)
        {
            settings = null!;
            error = "流网关主动撤销地址、命令令牌或投递参数无效。";
            return false;
        }

        settings = new ValidatedStreamGatewayCommandSettings(
            new Uri(commandBaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute),
            CommandToken,
            CommandPollIntervalSeconds,
            CommandLockSeconds,
            CommandMaxAttempts,
            CommandRetentionDays);
        error = string.Empty;
        return true;
    }

    private bool IsAllowedGatewayUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        (AllowInsecureLoopbackHttpForDevelopment &&
         uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
         uri.IsLoopback);
}

public sealed record ValidatedStreamGatewaySettings(
    string GatewayName,
    Uri PublicBaseUri,
    int TicketLifetimeSeconds,
    int LeaseLifetimeSeconds,
    int RenewAfterSeconds,
    int MaxActiveSessionsPerClient,
    int MaxActiveSessionsPerUser,
    int MaxActiveSessionsPerCamera,
    int MaxMainProfileSessionsPerClient,
    int TicketRetentionHours);

public sealed record ValidatedStreamGatewayCommandSettings(
    Uri BaseUri,
    string Token,
    int PollIntervalSeconds,
    int LockSeconds,
    int MaxAttempts,
    int RetentionDays);

public sealed record IssuedStreamSession(
    Guid Id,
    Uri GatewayUri,
    DateTimeOffset TicketExpiresAt,
    DateTimeOffset LeaseExpiresAt,
    int RenewAfterSeconds);

public sealed record RenewedStreamSession(Guid Id, DateTimeOffset LeaseExpiresAt, int RenewAfterSeconds);

public sealed record GatewayStreamSession(
    Guid Id,
    string StreamKey,
    Guid CameraId,
    CameraPermission Operation,
    string Profile,
    DateTimeOffset LeaseExpiresAt,
    bool Active,
    string? RevocationReason);

public sealed record StreamSessionRevokedPayload(
    Guid SessionId,
    string GatewayName,
    string Reason,
    DateTimeOffset RevokedAt);

public sealed record PlaybackSessionRequest(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int SlotNumber,
    Guid ClientRequestId);

public sealed record PlaybackTransportRequest(
    PlaybackTransportAction Action,
    DateTimeOffset? Position,
    double? Speed,
    Guid ClientRequestId);

public enum PlaybackTransportStatus
{
    Pending,
    Ready,
    Failed
}

public sealed record PlaybackTransportState(
    PlaybackTransportStatus Status,
    bool IsPaused,
    DateTimeOffset Position,
    double Speed,
    bool CanPause,
    bool CanSeek,
    bool CanChangeSpeed,
    string? Detail,
    Guid? CommandId);

public sealed record QueuedPlaybackTransportCommand(Guid CommandId, StreamSessionEntity Session);

public enum PlaybackRelayStatus
{
    Pending,
    Ready,
    Failed
}

public sealed record PlaybackRelaySessionState(
    PlaybackRelayStatus Status,
    Guid? StartCommandId,
    string? FailureKind,
    int Attempts);

public sealed record CreatedPlaybackSession(
    StreamSessionEntity Session,
    PlaybackRelaySessionState RelayState);

public sealed record PlaybackWorkerAssignment(Guid WorkerId, Guid RecorderId, string StartCommandType);

public sealed class StreamGatewayUnavailableException(string message) : InvalidOperationException(message);
public sealed class StreamSessionInactiveException(string message) : InvalidOperationException(message);
public sealed class StreamSessionConflictException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
public sealed class StreamTicketInvalidException(string message) : InvalidOperationException(message);

public sealed class StreamSessionDuplicateRequestException(Guid sessionId)
    : InvalidOperationException("同一客户端请求编号已经创建流会话，请领取新的重连票据。")
{
    public Guid SessionId { get; } = sessionId;
}

public sealed class StreamSessionQuotaExceededException(string quotaType, int limit)
    : InvalidOperationException($"流会话配额已达到上限：{quotaType}={limit}。")
{
    public string QuotaType { get; } = quotaType;
    public int Limit { get; } = limit;
}

public sealed class PlaybackRelayUnavailableException(string message) : InvalidOperationException(message);

public sealed class PlaybackRelayNotReadyException(
    PlaybackRelayStatus status,
    string? failureKind) : InvalidOperationException("回放中继尚未就绪。")
{
    public PlaybackRelayStatus Status { get; } = status;
    public string? FailureKind { get; } = failureKind;
}
