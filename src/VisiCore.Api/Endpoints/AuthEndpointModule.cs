using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;

/// <summary>
/// 管理员登录、登出和密码变更端点。
/// </summary>
public sealed class AuthEndpointModule : IApiEndpointModule
{
    public EndpointModulePhase Phase => EndpointModulePhase.Configured;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/auth/login", async (
            LoginRequest request,
            PlatformDbContext dbContext,
            Argon2PasswordHasher passwordHasher,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (!PlatformUsernamePolicy.TryNormalize(request.Username, out var username, out _))
                {
                    return Results.Unauthorized();
                }
                var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Username == username && item.DisabledAt == null, cancellationToken);
                if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
                {
                    return Results.Unauthorized();
                }

                var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                dbContext.UserSessions.Add(new UserSessionEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(12)
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(new LoginResponse(token, DateTimeOffset.UtcNow.AddHours(12), user.Username, user.RequiresPasswordChange));
            }
            catch (DbException exception)
            {
                loggerFactory.CreateLogger("VisiCore.Api.Login").LogError(exception, "登录时无法访问平台数据库。");
                return ApiProblems.Create(StatusCodes.Status503ServiceUnavailable, "中心账号服务暂不可用", "authentication_unavailable", "平台数据库连接失败，请稍后重试。");
            }
        });

        endpoints.MapPost("/api/v1/auth/logout", async (
            ClaimsPrincipal principal,
            StreamSessionOrchestrator orchestrator,
            PtzControlService ptzControlService,
            CancellationToken cancellationToken) =>
        {
            var sessionId = principal.FindFirstValue(ClaimTypes.Sid);
            if (!Guid.TryParse(sessionId, out var parsedSessionId))
            {
                return Results.Unauthorized();
            }

            try
            {
                await ptzControlService.RevokeForUserSessionAsync(parsedSessionId, "client_logout", cancellationToken);
                await orchestrator.RevokeLoginSessionAsync(parsedSessionId, "client_logout", cancellationToken);
                return Results.NoContent();
            }
            catch (StreamSessionConflictException exception)
            {
                return ApiProblems.Create(StatusCodes.Status409Conflict, "登录会话撤销冲突", "logout_conflict", exception.Message);
            }
        }).RequireAuthorization();

        endpoints.MapPut("/api/v1/auth/password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            AccountPasswordService passwordService,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await passwordService.ChangeAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
            if (result == AccountPasswordChangeResult.UserUnavailable)
            {
                return Results.Unauthorized();
            }
            if (result == AccountPasswordChangeResult.CurrentPasswordIncorrect)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "当前密码不正确", "password_current_invalid", errors: new Dictionary<string, string[]> { ["currentPassword"] = ["当前密码不正确。"] });
            }
            if (result == AccountPasswordChangeResult.NewPasswordInvalid)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "新密码无效", "password_new_invalid", errors: new Dictionary<string, string[]> { ["newPassword"] = ["新密码长度必须为 12 至 256 位。"] });
            }
            if (result == AccountPasswordChangeResult.PasswordUnchanged)
            {
                return ApiProblems.Create(StatusCodes.Status400BadRequest, "新密码未变化", "password_unchanged", errors: new Dictionary<string, string[]> { ["newPassword"] = ["新密码不能与当前密码相同。"] });
            }

            var cleanupErrors = new List<Exception>();
            async Task AttemptCleanupAsync(Func<IServiceProvider, Task> action)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await action(scope.ServiceProvider);
                }
                catch (Exception exception)
                {
                    cleanupErrors.Add(exception);
                }
            }

            await AttemptCleanupAsync(services => services.GetRequiredService<StreamSessionOrchestrator>().RevokeForUserAsync(userId, "password_changed", CancellationToken.None));
            await AttemptCleanupAsync(services => services.GetRequiredService<PtzControlService>().RevokeForUserAsync(userId, "password_changed", CancellationToken.None));
            await AttemptCleanupAsync(services => CancelUnauthorizedExportsForUsersAsync(
                [userId],
                true,
                services.GetRequiredService<PlatformDbContext>(),
                services.GetRequiredService<PlatformAccessService>(),
                services.GetRequiredService<EdgeCommandControlService>(),
                CancellationToken.None));
            await AttemptCleanupAsync(services => services.GetRequiredService<AuditService>().WriteAsync(
                principal,
                "user.password.change",
                "user",
                userId,
                new { CleanupIncomplete = cleanupErrors.Count > 0 },
                CancellationToken.None));
            if (cleanupErrors.Count > 0)
            {
                loggerFactory.CreateLogger("VisiCore.Api.AccountPasswordChange").LogError(new AggregateException(cleanupErrors), "账号 {UserId} 密码已修改，但后续安全清理存在失败。", userId);
            }
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("account-password-change");
    }

    private static async Task CancelUnauthorizedExportsForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        bool forceCancellation,
        PlatformDbContext dbContext,
        PlatformAccessService accessService,
        EdgeCommandControlService commandService,
        CancellationToken cancellationToken)
    {
        var distinctUserIds = userIds.Where(item => item != Guid.Empty).Distinct().ToList();
        if (distinctUserIds.Count == 0)
        {
            return;
        }

        var exports = await dbContext.PlaybackExports.AsNoTracking()
            .Where(item => distinctUserIds.Contains(item.RequestedByUserId) &&
                (item.Status == PlaybackExportStatus.Queued || item.Status == PlaybackExportStatus.Running))
            .Select(item => new { item.Id, item.CameraId, item.RequestedByUserId })
            .ToListAsync(cancellationToken);
        foreach (var export in exports)
        {
            if (forceCancellation || !await accessService.HasCameraPermissionAsync(export.RequestedByUserId, export.CameraId, CameraPermission.Export, cancellationToken))
            {
                var code = forceCancellation ? "cancelled_by_account_security_change" : "cancelled_by_authorization_change";
                var detail = forceCancellation ? "账号安全状态变化后，已取消录像导出任务。" : "权限范围变化后，发起人已失去录像导出权限。";
                await commandService.CancelPlaybackExportAsync(export.Id, code, detail, cancellationToken);
            }
        }
    }
}
