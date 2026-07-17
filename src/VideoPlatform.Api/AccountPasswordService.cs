using Microsoft.EntityFrameworkCore;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public enum AccountPasswordChangeResult
{
    Succeeded,
    UserUnavailable,
    CurrentPasswordIncorrect,
    NewPasswordInvalid,
    PasswordUnchanged
}

public enum AccountPasswordResetResult
{
    Succeeded,
    UserUnavailable
}

public sealed class AccountPasswordService(
    PlatformDbContext dbContext,
    Argon2PasswordHasher passwordHasher)
{
    public async Task<AccountPasswordResetResult> ResetToUsernameAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);
        if (user is null)
        {
            return AccountPasswordResetResult.UserUnavailable;
        }

        user.PasswordHash = passwordHasher.Hash(user.Username);
        user.RequiresPasswordChange = true;
        await RevokeActiveSessionsAsync(userId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AccountPasswordResetResult.Succeeded;
    }

    public async Task<AccountPasswordChangeResult> ChangeAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 12 or > 256)
        {
            return AccountPasswordChangeResult.NewPasswordInvalid;
        }
        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return AccountPasswordChangeResult.CurrentPasswordIncorrect;
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.Id == userId && item.DisabledAt == null,
            cancellationToken);
        if (user is null)
        {
            return AccountPasswordChangeResult.UserUnavailable;
        }
        if (!passwordHasher.Verify(currentPassword, user.PasswordHash))
        {
            return AccountPasswordChangeResult.CurrentPasswordIncorrect;
        }
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return AccountPasswordChangeResult.PasswordUnchanged;
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.RequiresPasswordChange = false;
        await RevokeActiveSessionsAsync(userId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return AccountPasswordChangeResult.Succeeded;
    }

    private async Task RevokeActiveSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var activeSessions = await dbContext.UserSessions
            .Where(item => item.UserId == userId && item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var revokedAt = DateTimeOffset.UtcNow;
        foreach (var session in activeSessions)
        {
            session.RevokedAt = revokedAt;
        }
    }
}
