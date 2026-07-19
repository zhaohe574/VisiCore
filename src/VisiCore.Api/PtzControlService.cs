using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class PtzControlService(
    PlatformDbContext dbContext,
    EdgeOperationReadinessService readinessService,
    PlatformAccessService accessService,
    DevicePluginCapabilityService? capabilityService = null)
{
    public const string AggregateType = "ptz_control_lease";
    private static readonly TimeSpan LeaseLifetime = TimeSpan.FromSeconds(30);
    private const int MaxPulseMilliseconds = 2_000;

    public async Task<PtzLeaseGrant> AcquireAsync(
        Guid userId,
        Guid userSessionId,
        CameraEntity camera,
        CancellationToken cancellationToken)
    {
        if (!camera.SupportsPtz)
        {
            throw new PtzUnavailableException("摄像头未声明云台控制能力。");
        }

        var route = await ResolveOperationRouteAsync(camera.RecorderId, null, cancellationToken);
        if (await readinessService.GetReadyWorkerIdAsync(
                camera.RecorderId,
                route.OperationType,
                cancellationToken) is null)
        {
            throw new PtzUnavailableException("当前摄像头的云台控制服务尚未就绪，请确认对应边缘节点（Worker）已启用云台控制。");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireCameraLockAsync(camera.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var active = await GetActiveLeaseAsync(camera.Id, cancellationToken);
        if (active is not null && active.ExpiresAt > now)
        {
            throw new PtzLeaseConflictException("摄像头正由其他云台控制会话占用。");
        }
        if (active is not null)
        {
            active.RevokedAt = now;
            active.RevocationReason = "lease_expired";
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var lease = new PtzControlLeaseEntity
        {
            Id = Guid.NewGuid(),
            CameraId = camera.Id,
            UserId = userId,
            UserSessionId = userSessionId,
            LeaseTokenHash = HashToken(token),
            AcquiredAt = now,
            LastRenewedAt = now,
            ExpiresAt = now.Add(LeaseLifetime)
        };
        dbContext.PtzControlLeases.Add(lease);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PtzLeaseGrant(lease, token);
    }

    public async Task<PtzControlAcceptance> QueueCommandAsync(
        Guid userId,
        Guid userSessionId,
        CameraEntity camera,
        Guid leaseId,
        string leaseToken,
        PtzControlRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await AcquireCameraLockAsync(camera.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var lease = await dbContext.PtzControlLeases
            .FromSqlInterpolated($$"""SELECT * FROM ptz_control_leases WHERE "Id" = {{leaseId}} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (lease is null || lease.CameraId != camera.Id || lease.UserId != userId || lease.UserSessionId != userSessionId ||
            lease.ReleasedAt is not null || lease.RevokedAt is not null || lease.ExpiresAt <= now ||
            !FixedTimeEquals(lease.LeaseTokenHash, HashToken(leaseToken)))
        {
            throw new PtzLeaseInvalidException("云台控制租约已失效。");
        }
        if (request.Sequence != lease.LastSequence + 1)
        {
            throw new PtzSequenceConflictException(lease.LastSequence);
        }

        var route = await ResolveOperationRouteAsync(camera.RecorderId, request.Action, cancellationToken);
        var workerId = await readinessService.GetReadyWorkerIdAsync(
            camera.RecorderId,
            route.OperationType,
            cancellationToken);
        if (workerId is null)
        {
            lease.RevokedAt = now;
            lease.RevocationReason = "ptz_runtime_unavailable";
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new PtzUnavailableException("当前摄像头的云台控制服务已离线，请确认对应边缘节点（Worker）运行正常。");
        }

        lease.LastSequence = request.Sequence;
        lease.LastRenewedAt = now;
        lease.ExpiresAt = now.Add(LeaseLifetime);
        var command = new EdgeCommandEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = workerId.Value,
            RecorderId = camera.RecorderId,
            CommandType = route.CommandType,
            AggregateType = AggregateType,
            AggregateId = lease.Id,
            PayloadJson = JsonSerializer.Serialize(new PtzControlCommandPayload(
                lease.Id, camera.Id, request.Action, request.Motion, request.Speed, request.Sequence,
                request.Motion == PtzMotion.Start ? MaxPulseMilliseconds : 0)),
            CreatedAt = now,
            NextAttemptAt = now
        };
        dbContext.EdgeCommands.Add(command);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PtzControlAcceptance(lease, command.Id);
    }

    public Task RevokeForUserSessionAsync(Guid userSessionId, string reason, CancellationToken cancellationToken) =>
        RevokeLeasesAsync(
            dbContext.PtzControlLeases.Where(item => item.UserSessionId == userSessionId),
            reason,
            cancellationToken);

    public Task RevokeForUserAsync(Guid userId, string reason, CancellationToken cancellationToken) =>
        RevokeLeasesAsync(
            dbContext.PtzControlLeases.Where(item => item.UserId == userId),
            reason,
            cancellationToken);

    public Task RevokeForCamerasAsync(IEnumerable<Guid> cameraIds, string reason, CancellationToken cancellationToken)
    {
        var ids = cameraIds.Where(item => item != Guid.Empty).Distinct().ToList();
        return RevokeLeasesAsync(
            dbContext.PtzControlLeases.Where(item => ids.Contains(item.CameraId)),
            reason,
            cancellationToken);
    }

    public async Task RevokeUnauthorizedForCamerasAsync(
        IEnumerable<Guid> cameraIds,
        string reason,
        CancellationToken cancellationToken)
    {
        foreach (var cameraId in cameraIds.Where(item => item != Guid.Empty).Distinct().OrderBy(item => item))
        {
            var candidates = await dbContext.PtzControlLeases.AsNoTracking()
                .Where(item => item.CameraId == cameraId && item.ReleasedAt == null && item.RevokedAt == null)
                .Select(item => new { item.Id, item.UserId, item.CameraId })
                .ToListAsync(cancellationToken);
            var leaseIds = new List<Guid>();
            foreach (var candidate in candidates)
            {
                if (!await accessService.HasCameraPermissionAsync(
                        candidate.UserId,
                        candidate.CameraId,
                        CameraPermission.PtzControl,
                        cancellationToken))
                {
                    leaseIds.Add(candidate.Id);
                }
            }
            if (leaseIds.Count > 0)
            {
                await RevokeLeasesAsync(
                    dbContext.PtzControlLeases.Where(item => leaseIds.Contains(item.Id)),
                    reason,
                    cancellationToken);
            }
        }
    }

    private async Task RevokeLeasesAsync(
        IQueryable<PtzControlLeaseEntity> scopedLeases,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var candidateCameraIds = await scopedLeases
            .Where(item => item.ReleasedAt == null && item.RevokedAt == null)
            .Select(item => item.CameraId)
            .Distinct()
            .OrderBy(item => item)
            .ToListAsync(cancellationToken);
        foreach (var cameraId in candidateCameraIds)
        {
            await AcquireCameraLockAsync(cameraId, cancellationToken);
        }

        var leases = await scopedLeases
            .Where(item => item.ReleasedAt == null && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        if (leases.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var lease in leases)
        {
            lease.RevokedAt = now;
            lease.RevocationReason = reason;
        }

        var leaseIds = leases.Select(item => item.Id).ToList();
        var commands = await dbContext.EdgeCommands
            .Where(item => leaseIds.Contains(item.AggregateId) && item.AggregateType == AggregateType &&
                (item.CommandType == EdgeCommandTypes.OnvifPtzControl || item.CommandType == EdgeCommandTypes.PluginPtzControl) &&
                item.DeadLetteredAt == null)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var stopCommands = new List<EdgeCommandEntity>();
        foreach (var lease in leases)
        {
            var latest = commands
                .Where(item => item.AggregateId == lease.Id)
                .Select(TryReadControlPayload)
                .Where(item => item is not null)
                .Select(item => item!.Value)
                .OrderBy(item => item.Command.CreatedAt)
                .LastOrDefault();
            if (latest.Command is not null && latest.Payload.Motion == PtzMotion.Start &&
                (latest.Command.CompletedAt is not null || latest.Command.LockedUntil > now))
            {
                stopCommands.Add(new EdgeCommandEntity
                {
                    Id = Guid.NewGuid(),
                    WorkerId = latest.Command.WorkerId,
                    RecorderId = latest.Command.RecorderId,
                    CommandType = latest.Command.CommandType,
                    AggregateType = AggregateType,
                    AggregateId = lease.Id,
                    PayloadJson = JsonSerializer.Serialize(new PtzControlCommandPayload(
                        lease.Id,
                        lease.CameraId,
                        latest.Payload.Action,
                        PtzMotion.Stop,
                        1,
                        latest.Payload.Sequence == long.MaxValue ? long.MaxValue : latest.Payload.Sequence + 1,
                        0)),
                    CreatedAt = now,
                    NextAttemptAt = now
                });
            }
        }

        foreach (var command in commands.Where(item => item.CompletedAt is null))
        {
            command.DeadLetteredAt = now;
            command.LastError = "ptz_lease_revoked";
            command.LockedBy = null;
            command.LockedUntil = null;
        }
        dbContext.EdgeCommands.AddRange(stopCommands);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<PtzControlLeaseEntity?> GetActiveLeaseAsync(Guid cameraId, CancellationToken cancellationToken) =>
        await dbContext.PtzControlLeases
            .FromSqlInterpolated($$"""SELECT * FROM ptz_control_leases WHERE "CameraId" = {{cameraId}} AND "ReleasedAt" IS NULL AND "RevokedAt" IS NULL FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);

    private Task AcquireCameraLockAsync(Guid cameraId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockKey(cameraId)})", cancellationToken);

    private async Task<PtzOperationRoute> ResolveOperationRouteAsync(
        Guid recorderId,
        PtzAction? action,
        CancellationToken cancellationToken)
    {
        var pluginCapabilities = capabilityService ?? new DevicePluginCapabilityService(dbContext);
        if (!await pluginCapabilities.SupportsAsync(recorderId, DevicePluginCapabilityKind.Ptz, cancellationToken))
        {
            throw new PtzUnavailableException("摄像头所属协议插件未启用云台控制能力。");
        }
        var recorder = await dbContext.Recorders.AsNoTracking()
            .Where(item => item.Id == recorderId)
            .Select(item => new
            {
                item.AdapterType,
                PluginRuntimeType = dbContext.DevicePlugins
                    .Where(plugin => plugin.Id == item.DevicePluginId)
                    .Select(plugin => plugin.RuntimeType)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PtzUnavailableException("摄像头所属录像机不存在。");
        if (recorder.PluginRuntimeType == DevicePluginRuntimeTypes.Onvif)
        {
            if (action is PtzAction.FocusNear or PtzAction.FocusFar or PtzAction.IrisOpen or PtzAction.IrisClose)
            {
                throw new PtzUnavailableException("当前 ONVIF 云台控制仅支持平移、倾斜和变焦动作。");
            }
            return new PtzOperationRoute(EdgeCommandTypes.OnvifPtzControl, EdgeOperationTypes.OnvifPtz);
        }
        if (recorder.PluginRuntimeType == DevicePluginRuntimeTypes.ExternalEdge)
        {
            return new PtzOperationRoute(EdgeCommandTypes.PluginPtzControl, EdgeOperationTypes.PluginPtz);
        }
        throw new PtzUnavailableException("录像机适配器未启用云台控制命令。");
    }

    private static void ValidateRequest(PtzControlRequest request)
    {
        if (request.Sequence < 1 || request.Speed is < 1 or > 7 ||
            (request.Motion == PtzMotion.Stop && request.Speed != 1))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "云台控制命令速度或序列无效。");
        }
    }

    private static long LockKey(Guid cameraId) => BitConverter.ToInt64(SHA256.HashData(cameraId.ToByteArray()), 0);
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static PtzControlCommand? TryReadControlPayload(EdgeCommandEntity command)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PtzControlCommandPayload>(command.PayloadJson);
            return payload is null ? null : new PtzControlCommand(command, payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct PtzControlCommand(EdgeCommandEntity Command, PtzControlCommandPayload Payload);
    private readonly record struct PtzOperationRoute(string CommandType, string OperationType);
}

public sealed record PtzControlRequest(PtzAction Action, PtzMotion Motion, int Speed, long Sequence);
public sealed record PtzLeaseGrant(PtzControlLeaseEntity Lease, string LeaseToken);
public sealed record PtzControlAcceptance(PtzControlLeaseEntity Lease, Guid CommandId);
public sealed class PtzUnavailableException(string message) : Exception(message);
public sealed class PtzLeaseConflictException(string message) : Exception(message);
public sealed class PtzLeaseInvalidException(string message) : Exception(message);
public sealed class PtzSequenceConflictException(long lastSequence) : Exception("云台控制命令序列已过期。") { public long LastSequence { get; } = lastSequence; }
