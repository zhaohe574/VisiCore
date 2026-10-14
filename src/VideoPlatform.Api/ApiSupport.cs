using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using VideoPlatform.Application;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api;

public static class ApiSupport
{
    public const string CookieName = "__Secure-VideoPlatform";
    public static Actor Actor(HttpContext context) => context.Items["actor"] as Actor ?? throw new PlatformException(401, "auth.required", "请先登录");
    public static string? Ip(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();
    public static string Token(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return header[7..].Trim();
        if (context.Request.Path.StartsWithSegments("/hubs/v2") && context.Request.Query.TryGetValue("access_token", out var token)) return token.ToString();
        return context.Request.Cookies[CookieName] ?? "";
    }
    public static void SetCookie(HttpContext context, string token, DateTimeOffset expiresAt)
        => context.Response.Cookies.Append(CookieName, token, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expiresAt });
    public static (int Page, int Size, int Offset) Pagination(HttpContext context)
    {
        var page = int.TryParse(context.Request.Query["page"], out var p) ? Math.Clamp(p, 1, 100000) : 1;
        var size = int.TryParse(context.Request.Query["pageSize"], out var s) ? Math.Clamp(s, 1, 200) : 50;
        return (page, size, (page - 1) * size);
    }
    public static object Page(HttpContext context, IReadOnlyList<JsonObject> items, long total)
    {
        var (page, size, _) = Pagination(context);
        return new { items, total, page, pageSize = size };
    }
    public static string Search(HttpContext context) => context.Request.Query["search"].ToString().Trim();
}

public sealed class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ApiSupport.Token(Context);
        if (string.IsNullOrWhiteSpace(token)) return AuthenticateResult.NoResult();
        var actor = await sessions.AuthenticateAsync(token, Context.RequestAborted);
        if (actor is null) return AuthenticateResult.Fail("登录会话已失效");
        Context.Items["actor"] = actor;
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.UserId.ToString()), new Claim(ClaimTypes.Name, actor.Username), new Claim("sessionId", actor.SessionId.ToString())], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
