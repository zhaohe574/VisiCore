using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class PlatformSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PlatformSession";
    public const string PasswordChangeRequiredClaim = "password_change_required";

    private readonly PlatformDbContext _dbContext;

    public PlatformSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PlatformDbContext dbContext)
        : base(options, logger, encoder)
    {
        _dbContext = dbContext;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.Fail("缺少会话令牌。 ");
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var session = await _dbContext.UserSessions
            .AsNoTracking()
            .Where(item => item.TokenHash == tokenHash && item.RevokedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow)
            .Join(
                _dbContext.Users.AsNoTracking().Where(item => item.DisabledAt == null),
                item => item.UserId,
                user => user.Id,
                (item, user) => new { Session = item, User = user })
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (session is null)
        {
            return AuthenticateResult.Fail("会话无效、过期或已被撤销。 ");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.User.Id.ToString()),
            new(ClaimTypes.Name, session.User.Username),
            new(ClaimTypes.Sid, session.Session.Id.ToString())
        };
        if (session.User.IsSystemAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, "system-administrator"));
        }
        if (session.User.RequiresPasswordChange)
        {
            claims.Add(new Claim(PasswordChangeRequiredClaim, bool.TrueString));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
