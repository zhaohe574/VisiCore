using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.IntegrationTests;

public sealed class StreamSessionOrchestratorTests(StreamSessionPostgreSqlFixture fixture)
    : IClassFixture<StreamSessionPostgreSqlFixture>
{
    [Fact(DisplayName = "同一摄像头并发签发时共享 assignment 且引用数准确")]
    public async Task ConcurrentIssueSharesAssignmentAndKeepsReferenceCount()
    {
        await fixture.ResetStreamStateAsync();
        var tasks = Enumerable.Range(0, 12).Select(async slot =>
        {
            await using var dbContext = fixture.CreateDbContext();
            var orchestrator = CreateOrchestrator(dbContext, maxClientSessions: 16);
            return await orchestrator.IssueAsync(
                fixture.UserId,
                fixture.UserSessionId,
                fixture.CameraId,
                CameraPermission.LiveView,
                "sub",
                slot,
                Guid.NewGuid(),
                CancellationToken.None);
        });

        var sessions = await Task.WhenAll(tasks);
        Assert.Equal(12, sessions.Select(item => item.Id).Distinct().Count());
        await using var verification = fixture.CreateDbContext();
        var assignment = await verification.StreamAssignments.SingleAsync();
        Assert.Equal(12, assignment.ReferenceCount);
        Assert.Equal(12, await verification.StreamSessions.CountAsync(item => item.RevokedAt == null));
    }

    [Fact(DisplayName = "同一连接票据并发消费时仅一次成功")]
    public async Task ConnectionTicketCanOnlyBeConsumedOnce()
    {
        await fixture.ResetStreamStateAsync();
        IssuedStreamSession issued;
        await using (var dbContext = fixture.CreateDbContext())
        {
            issued = await CreateOrchestrator(dbContext).IssueAsync(
                fixture.UserId,
                fixture.UserSessionId,
                fixture.CameraId,
                CameraPermission.LiveView,
                "sub",
                0,
                Guid.NewGuid(),
                CancellationToken.None);
        }
        var ticket = GetGatewayTicket(issued.GatewayUri);

        async Task<bool> TryConsumeAsync()
        {
            await using var dbContext = fixture.CreateDbContext();
            try
            {
                await CreateOrchestrator(dbContext).ConsumeTicketAsync(issued.Id, ticket, "integration", CancellationToken.None);
                return true;
            }
            catch (StreamTicketInvalidException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryConsumeAsync(), TryConsumeAsync());
        Assert.Single(results, item => item);
    }

    [Fact(DisplayName = "重复创建返回原会话后可领取新票据且旧票据失效")]
    public async Task DuplicateRequestCanExchangeReconnectTicket()
    {
        await fixture.ResetStreamStateAsync();
        var requestId = Guid.NewGuid();
        IssuedStreamSession issued;
        await using (var dbContext = fixture.CreateDbContext())
        {
            issued = await CreateOrchestrator(dbContext).IssueAsync(
                fixture.UserId,
                fixture.UserSessionId,
                fixture.CameraId,
                CameraPermission.LiveView,
                "sub",
                0,
                requestId,
                CancellationToken.None);
        }

        await using (var duplicateContext = fixture.CreateDbContext())
        {
            var duplicate = await Assert.ThrowsAsync<StreamSessionDuplicateRequestException>(() =>
                CreateOrchestrator(duplicateContext).IssueAsync(
                    fixture.UserId,
                    fixture.UserSessionId,
                    fixture.CameraId,
                    CameraPermission.LiveView,
                    "sub",
                    0,
                    requestId,
                    CancellationToken.None));
            Assert.Equal(issued.Id, duplicate.SessionId);
        }

        IssuedStreamSession reconnect;
        await using (var reconnectContext = fixture.CreateDbContext())
        {
            reconnect = await CreateOrchestrator(reconnectContext).IssueReconnectTicketAsync(
                issued.Id,
                fixture.UserId,
                fixture.UserSessionId,
                CancellationToken.None);
        }
        Assert.Equal(issued.Id, reconnect.Id);

        var originalTicket = GetGatewayTicket(issued.GatewayUri);
        var reconnectTicket = GetGatewayTicket(reconnect.GatewayUri);
        await using (var originalContext = fixture.CreateDbContext())
        {
            await Assert.ThrowsAsync<StreamTicketInvalidException>(() =>
                CreateOrchestrator(originalContext).ConsumeTicketAsync(
                    issued.Id,
                    originalTicket,
                    "integration",
                    CancellationToken.None));
        }
        await using (var reconnectConsumeContext = fixture.CreateDbContext())
        {
            var gatewaySession = await CreateOrchestrator(reconnectConsumeContext).ConsumeTicketAsync(
                issued.Id,
                reconnectTicket,
                "integration",
                CancellationToken.None);
            Assert.True(gatewaySession.Active);
        }
    }

    [Fact(DisplayName = "客户端活动流配额在事务锁内严格执行")]
    public async Task ClientQuotaIsStrictlyEnforced()
    {
        await fixture.ResetStreamStateAsync();
        var tasks = Enumerable.Range(0, 8).Select(async slot =>
        {
            await using var dbContext = fixture.CreateDbContext();
            try
            {
                await CreateOrchestrator(dbContext, maxClientSessions: 4).IssueAsync(
                    fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                    CameraPermission.LiveView, "sub", slot, Guid.NewGuid(), CancellationToken.None);
                return "issued";
            }
            catch (StreamSessionQuotaExceededException exception)
            {
                Assert.Equal("client", exception.QuotaType);
                return "quota";
            }
        });

        var results = await Task.WhenAll(tasks);
        Assert.Equal(4, results.Count(item => item == "issued"));
        Assert.Equal(4, results.Count(item => item == "quota"));

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(4, await verification.StreamSessions.CountAsync(item => item.RevokedAt == null));
        Assert.Equal(4, (await verification.StreamAssignments.SingleAsync()).ReferenceCount);
    }

    [Fact(DisplayName = "同一客户端请求编号并发重试时只创建一个会话")]
    public async Task DuplicateClientRequestIsDeduplicatedUnderConcurrency()
    {
        await fixture.ResetStreamStateAsync();
        var requestId = Guid.NewGuid();

        async Task<(IssuedStreamSession? Issued, Guid? DuplicateSessionId)> TryIssueAsync()
        {
            await using var dbContext = fixture.CreateDbContext();
            try
            {
                var issued = await CreateOrchestrator(dbContext).IssueAsync(
                    fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                    CameraPermission.LiveView, "sub", 0, requestId, CancellationToken.None);
                return (issued, null);
            }
            catch (StreamSessionDuplicateRequestException exception)
            {
                return (null, exception.SessionId);
            }
        }

        var results = await Task.WhenAll(TryIssueAsync(), TryIssueAsync());
        var issued = Assert.Single(results, item => item.Issued is not null).Issued!;
        var duplicate = Assert.Single(results, item => item.DuplicateSessionId is not null).DuplicateSessionId;
        Assert.Equal(issued.Id, duplicate);

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(1, await verification.StreamSessions.CountAsync(item => item.RevokedAt == null));
        Assert.Equal(1, (await verification.StreamAssignments.SingleAsync()).ReferenceCount);
    }

    [Fact(DisplayName = "票据消费与会话撤销并发时不会死锁且最终释放引用")]
    public async Task TicketConsumptionAndRevocationCanRaceSafely()
    {
        await fixture.ResetStreamStateAsync();
        IssuedStreamSession issued;
        await using (var dbContext = fixture.CreateDbContext())
        {
            issued = await CreateOrchestrator(dbContext).IssueAsync(
                fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                CameraPermission.LiveView, "sub", 0, Guid.NewGuid(), CancellationToken.None);
        }
        var ticket = GetGatewayTicket(issued.GatewayUri);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> ConsumeAsync()
        {
            await start.Task;
            await using var dbContext = fixture.CreateDbContext();
            try
            {
                await CreateOrchestrator(dbContext).ConsumeTicketAsync(issued.Id, ticket, "integration", CancellationToken.None);
                return true;
            }
            catch (StreamSessionInactiveException)
            {
                return false;
            }
        }

        async Task<bool> RevokeAsync()
        {
            await start.Task;
            await using var dbContext = fixture.CreateDbContext();
            return await CreateOrchestrator(dbContext).RevokeAsync(
                issued.Id, fixture.UserId, fixture.UserSessionId, false,
                "integration_race", CancellationToken.None);
        }

        var consumeTask = ConsumeAsync();
        var revokeTask = RevokeAsync();
        start.SetResult(true);
        await Task.WhenAll(consumeTask, revokeTask);
        Assert.True(await revokeTask);

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(0, (await verification.StreamAssignments.SingleAsync()).ReferenceCount);
        Assert.NotNull((await verification.StreamSessions.SingleAsync(item => item.Id == issued.Id)).RevokedAt);
        Assert.Equal(1, await verification.OutboxEvents.CountAsync(item => item.EventType == "stream.session.revoked"));
    }

    [Fact(DisplayName = "不同用户批量撤销共同流键时使用稳定 assignment 锁序")]
    public async Task BatchRevocationUsesStableAssignmentLockOrder()
    {
        await fixture.ResetStreamStateAsync();
        var now = DateTimeOffset.UtcNow;
        const int streamCount = 32;
        var streamKeys = Enumerable.Range(0, streamCount)
            .Select(index => $"batch-lock-test:{index:D2}")
            .ToList();
        await using (var setup = fixture.CreateDbContext())
        {
            setup.StreamAssignments.AddRange(streamKeys.Select(streamKey => new StreamAssignmentEntity
            {
                Id = Guid.NewGuid(),
                StreamKey = streamKey,
                GatewayName = "integration",
                ReferenceCount = 2,
                LastAccessedAt = now
            }));
            for (var index = 0; index < streamCount; index++)
            {
                setup.StreamSessions.Add(CreateSession(
                    $"00000000-0000-0000-0000-{index + 1:X12}",
                    fixture.UserId,
                    fixture.UserSessionId,
                    fixture.CameraId,
                    streamKeys[index],
                    index,
                    now));
                setup.StreamSessions.Add(CreateSession(
                    $"00000000-0000-0000-0000-{1000 + index:X12}",
                    fixture.SecondUserId,
                    fixture.SecondUserSessionId,
                    fixture.CameraId,
                    streamKeys[streamCount - index - 1],
                    index,
                    now));
            }
            await setup.SaveChangesAsync();
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task RevokeUserAsync(Guid userId)
        {
            await start.Task;
            await using var dbContext = fixture.CreateDbContext();
            await CreateOrchestrator(dbContext).RevokeForUserAsync(userId, "batch_lock_test", CancellationToken.None);
        }

        var first = RevokeUserAsync(fixture.UserId);
        var second = RevokeUserAsync(fixture.SecondUserId);
        start.SetResult(true);
        await Task.WhenAll(first, second);

        await using var verification = fixture.CreateDbContext();
        Assert.All(await verification.StreamAssignments.ToListAsync(), item => Assert.Equal(0, item.ReferenceCount));
        Assert.Equal(streamCount * 2, await verification.StreamSessions.CountAsync(item => item.RevokedAt != null));
        Assert.Equal(streamCount * 2, await verification.OutboxEvents.CountAsync(item => item.EventType == "stream.session.revoked"));
    }

    [Fact(DisplayName = "签发必须等待全局维护门闩释放")]
    public async Task IssueWaitsForGlobalMaintenanceLock()
    {
        await fixture.ResetStreamStateAsync();
        await using var maintenanceContext = fixture.CreateDbContext();
        await maintenanceContext.Database.OpenConnectionAsync();
        const string globalLockKey = "stream:global";
        await maintenanceContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_lock(hashtextextended({globalLockKey}, 0))");

        Task<IssuedStreamSession> issueTask;
        try
        {
            issueTask = Task.Run(async () =>
            {
                await using var issueContext = fixture.CreateDbContext();
                return await CreateOrchestrator(issueContext).IssueAsync(
                    fixture.UserId,
                    fixture.UserSessionId,
                    fixture.CameraId,
                    CameraPermission.LiveView,
                    "sub",
                    0,
                    Guid.NewGuid(),
                    CancellationToken.None);
            });
            await Task.Delay(150);
            Assert.False(issueTask.IsCompleted);
        }
        finally
        {
            await maintenanceContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_unlock(hashtextextended({globalLockKey}, 0))");
            await maintenanceContext.Database.CloseConnectionAsync();
        }

        var issued = await issueTask;
        Assert.NotEqual(Guid.Empty, issued.Id);
    }

    [Fact(DisplayName = "注销登录会话会批量撤销流并将共享引用归零")]
    public async Task LogoutRevokesAllClientStreamsAndReleasesAssignment()
    {
        await fixture.ResetStreamStateAsync();
        for (var slot = 0; slot < 6; slot++)
        {
            await using var dbContext = fixture.CreateDbContext();
            await CreateOrchestrator(dbContext).IssueAsync(
                fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                CameraPermission.LiveView, "sub", slot, Guid.NewGuid(), CancellationToken.None);
        }

        await using (var logoutContext = fixture.CreateDbContext())
        {
            Assert.True(await CreateOrchestrator(logoutContext)
                .RevokeLoginSessionAsync(fixture.UserSessionId, "integration_logout", CancellationToken.None));
        }
        await using var verification = fixture.CreateDbContext();
        Assert.Equal(0, (await verification.StreamAssignments.SingleAsync()).ReferenceCount);
        Assert.Equal(6, await verification.StreamSessions.CountAsync(item => item.RevokedAt != null));
        Assert.Equal(6, await verification.OutboxEvents.CountAsync(item => item.EventType == "stream.session.revoked"));
        Assert.NotNull((await verification.UserSessions.SingleAsync(item => item.Id == fixture.UserSessionId)).RevokedAt);
    }

    [Fact(DisplayName = "摄像头权限收回后续租与重连都会撤销既有会话")]
    public async Task RenewAndReconnectRevokeSessionWhenCameraPermissionIsRemoved()
    {
        await fixture.ResetStreamStateAsync();
        IssuedStreamSession renewalTarget;
        IssuedStreamSession reconnectTarget;
        await using (var issueContext = fixture.CreateDbContext())
        {
            var orchestrator = CreateOrchestrator(issueContext);
            renewalTarget = await orchestrator.IssueAsync(
                fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                CameraPermission.LiveView, "sub", 0, Guid.NewGuid(), CancellationToken.None);
            reconnectTarget = await orchestrator.IssueAsync(
                fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                CameraPermission.LiveView, "sub", 1, Guid.NewGuid(), CancellationToken.None);
        }

        List<UserRoleEntity> removedRoles;
        await using (var permissionContext = fixture.CreateDbContext())
        {
            removedRoles = await permissionContext.UserRoles
                .Where(item => item.UserId == fixture.UserId)
                .ToListAsync();
            permissionContext.UserRoles.RemoveRange(removedRoles);
            await permissionContext.SaveChangesAsync();
        }

        try
        {
            await using (var renewContext = fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<StreamSessionInactiveException>(() =>
                    CreateOrchestrator(renewContext).RenewAsync(
                        renewalTarget.Id, fixture.UserId, fixture.UserSessionId, CancellationToken.None));
            }
            await using (var reconnectContext = fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<StreamSessionInactiveException>(() =>
                    CreateOrchestrator(reconnectContext).IssueReconnectTicketAsync(
                        reconnectTarget.Id, fixture.UserId, fixture.UserSessionId, CancellationToken.None));
            }

            await using var verification = fixture.CreateDbContext();
            Assert.All(
                await verification.StreamSessions.Where(item => item.UserId == fixture.UserId).ToListAsync(),
                item =>
                {
                    Assert.NotNull(item.RevokedAt);
                    Assert.Equal("camera_permission_revoked", item.RevocationReason);
                });
            Assert.Equal(0, (await verification.StreamAssignments.SingleAsync()).ReferenceCount);
            Assert.Equal(2, await verification.OutboxEvents.CountAsync(item => item.EventType == "stream.session.revoked"));
        }
        finally
        {
            await using var restoreContext = fixture.CreateDbContext();
            restoreContext.UserRoles.AddRange(removedRoles);
            await restoreContext.SaveChangesAsync();
        }
    }

    [Fact(DisplayName = "摄像头资产权限变化会立即撤销已失权的活动流")]
    public async Task AssetPermissionChangeRevokesUnauthorizedCameraSessions()
    {
        await fixture.ResetStreamStateAsync();
        IssuedStreamSession issued;
        await using (var issueContext = fixture.CreateDbContext())
        {
            issued = await CreateOrchestrator(issueContext).IssueAsync(
                fixture.UserId, fixture.UserSessionId, fixture.CameraId,
                CameraPermission.LiveView, "sub", 0, Guid.NewGuid(), CancellationToken.None);
        }

        List<UserRoleEntity> removedRoles;
        await using (var permissionContext = fixture.CreateDbContext())
        {
            removedRoles = await permissionContext.UserRoles
                .Where(item => item.UserId == fixture.UserId)
                .ToListAsync();
            permissionContext.UserRoles.RemoveRange(removedRoles);
            await permissionContext.SaveChangesAsync();
        }

        try
        {
            await using (var revokeContext = fixture.CreateDbContext())
            {
                await CreateOrchestrator(revokeContext).RevokeUnauthorizedForCamerasAsync(
                    [fixture.CameraId], "asset_permission_changed", CancellationToken.None);
            }

            await using var verification = fixture.CreateDbContext();
            var session = await verification.StreamSessions.SingleAsync(item => item.Id == issued.Id);
            Assert.NotNull(session.RevokedAt);
            Assert.Equal("asset_permission_changed", session.RevocationReason);
            Assert.Equal(0, (await verification.StreamAssignments.SingleAsync()).ReferenceCount);
            Assert.Single(await verification.OutboxEvents.Where(item => item.EventType == "stream.session.revoked").ToListAsync());
        }
        finally
        {
            await using var restoreContext = fixture.CreateDbContext();
            restoreContext.UserRoles.AddRange(removedRoles);
            await restoreContext.SaveChangesAsync();
        }
    }

    private static StreamSessionEntity CreateSession(
        string id,
        Guid userId,
        Guid userSessionId,
        Guid cameraId,
        string streamKey,
        int slotNumber,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.Parse(id),
            UserId = userId,
            UserSessionId = userSessionId,
            ClientRequestId = Guid.NewGuid(),
            SlotNumber = slotNumber,
            CameraId = cameraId,
            Operation = CameraPermission.LiveView,
            Profile = "sub",
            StreamKey = streamKey,
            GatewayName = "integration",
            CreatedAt = now,
            LastRenewedAt = now,
            ExpiresAt = now.AddMinutes(2)
        };

    private static StreamSessionOrchestrator CreateOrchestrator(
        PlatformDbContext dbContext,
        int maxClientSessions = 64)
    {
        var settings = new StreamGatewayOptions
        {
            GatewayName = "integration",
            PublicBaseUri = "https://gateway.integration.test/",
            ControlToken = "integration-control-token-at-least-32-bytes",
            CommandBaseUri = "https://gateway-control.integration.test/",
            CommandToken = "integration-command-token-at-least-32-bytes",
            TicketLifetimeSeconds = 20,
            LeaseLifetimeSeconds = 120,
            RenewAfterSeconds = 40,
            MaxActiveSessionsPerClient = maxClientSessions,
            MaxActiveSessionsPerUser = Math.Max(128, maxClientSessions),
            MaxActiveSessionsPerCamera = 100,
            MaxMainProfileSessionsPerClient = Math.Min(8, maxClientSessions),
            CleanupIntervalSeconds = 10,
            TicketRetentionHours = 24,
            CommandPollIntervalSeconds = 1,
            CommandLockSeconds = 30,
            CommandMaxAttempts = 12,
            CommandRetentionDays = 7
        };
        return new StreamSessionOrchestrator(
            dbContext,
            Options.Create(settings),
            new RecorderOperationRoutingService(
                dbContext,
                new EdgeOperationReadinessService(dbContext, Options.Create(new EdgeOperationReadinessOptions()))),
            new PlatformAccessService(dbContext));
    }

    private static string GetGatewayTicket(Uri gatewayUri)
    {
        var segments = gatewayUri.AbsolutePath.Trim('/').Split('/');
        Assert.True(segments.Length >= 6);
        Assert.Equal("hls", segments[0]);
        Assert.True(Guid.TryParseExact(segments[1], "N", out _));
        Assert.NotEmpty(segments[2]);
        return segments[2];
    }
}
