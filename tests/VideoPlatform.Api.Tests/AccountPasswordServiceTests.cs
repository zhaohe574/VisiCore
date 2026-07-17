using Microsoft.EntityFrameworkCore;
using VideoPlatform.Api;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class AccountPasswordServiceTests
{
    [Fact(DisplayName = "重置密码会恢复为账号并要求下次登录修改密码")]
    public async Task ResetPasswordUsesUsernameAndRequiresPasswordChange()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new Argon2PasswordHasher();
        const string oldPassword = "old-password-123456";
        var user = CreateUser(passwordHasher.Hash(oldPassword));
        user.RequiresPasswordChange = false;
        var activeSession = CreateSession(user.Id);
        dbContext.Users.Add(user);
        dbContext.UserSessions.Add(activeSession);
        await dbContext.SaveChangesAsync();

        var service = new AccountPasswordService(dbContext, passwordHasher);
        var result = await service.ResetToUsernameAsync(user.Id, CancellationToken.None);

        Assert.Equal(AccountPasswordResetResult.Succeeded, result);
        Assert.True(passwordHasher.Verify(user.Username, user.PasswordHash));
        Assert.False(passwordHasher.Verify(oldPassword, user.PasswordHash));
        Assert.True(user.RequiresPasswordChange);
        Assert.NotNull(activeSession.RevokedAt);
    }

    [Fact(DisplayName = "修改密码会更新哈希并撤销全部有效登录会话")]
    public async Task ChangePasswordUpdatesHashAndRevokesActiveSessions()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new Argon2PasswordHasher();
        const string currentPassword = "current-password-123";
        const string newPassword = "new-password-456789";
        var user = CreateUser(passwordHasher.Hash(currentPassword));
        var activeSession = CreateSession(user.Id);
        var alreadyRevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var revokedSession = CreateSession(user.Id, alreadyRevokedAt);
        dbContext.Users.Add(user);
        dbContext.UserSessions.AddRange(activeSession, revokedSession);
        await dbContext.SaveChangesAsync();

        var service = new AccountPasswordService(dbContext, passwordHasher);
        var result = await service.ChangeAsync(user.Id, currentPassword, newPassword, CancellationToken.None);

        Assert.Equal(AccountPasswordChangeResult.Succeeded, result);
        Assert.False(passwordHasher.Verify(currentPassword, user.PasswordHash));
        Assert.True(passwordHasher.Verify(newPassword, user.PasswordHash));
        Assert.False(user.RequiresPasswordChange);
        Assert.NotNull(activeSession.RevokedAt);
        Assert.Equal(alreadyRevokedAt, revokedSession.RevokedAt);
    }

    [Fact(DisplayName = "当前密码错误时不会修改密码或撤销会话")]
    public async Task IncorrectCurrentPasswordLeavesAccountUnchanged()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new Argon2PasswordHasher();
        const string currentPassword = "current-password-123";
        var user = CreateUser(passwordHasher.Hash(currentPassword));
        var session = CreateSession(user.Id);
        dbContext.Users.Add(user);
        dbContext.UserSessions.Add(session);
        await dbContext.SaveChangesAsync();

        var service = new AccountPasswordService(dbContext, passwordHasher);
        var result = await service.ChangeAsync(
            user.Id,
            "incorrect-password",
            "new-password-456789",
            CancellationToken.None);

        Assert.Equal(AccountPasswordChangeResult.CurrentPasswordIncorrect, result);
        Assert.True(passwordHasher.Verify(currentPassword, user.PasswordHash));
        Assert.True(user.RequiresPasswordChange);
        Assert.Null(session.RevokedAt);
    }

    [Fact(DisplayName = "新密码必须满足长度要求且不能与当前密码相同")]
    public async Task NewPasswordMustMeetPolicyAndBeDifferent()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new Argon2PasswordHasher();
        const string currentPassword = "current-password-123";
        var user = CreateUser(passwordHasher.Hash(currentPassword));
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var service = new AccountPasswordService(dbContext, passwordHasher);

        Assert.Equal(
            AccountPasswordChangeResult.NewPasswordInvalid,
            await service.ChangeAsync(user.Id, currentPassword, "too-short", CancellationToken.None));
        Assert.Equal(
            AccountPasswordChangeResult.NewPasswordInvalid,
            await service.ChangeAsync(user.Id, currentPassword, new string('x', 257), CancellationToken.None));
        Assert.Equal(
            AccountPasswordChangeResult.PasswordUnchanged,
            await service.ChangeAsync(user.Id, currentPassword, currentPassword, CancellationToken.None));
        Assert.True(passwordHasher.Verify(currentPassword, user.PasswordHash));
        Assert.True(user.RequiresPasswordChange);
    }

    private static PlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PlatformDbContext(options);
    }

    private static UserEntity CreateUser(string passwordHash) => new()
    {
        Id = Guid.NewGuid(),
        Username = "viewer-account",
        PasswordHash = passwordHash,
        RequiresPasswordChange = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static UserSessionEntity CreateSession(Guid userId, DateTimeOffset? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
        RevokedAt = revokedAt
    };
}
