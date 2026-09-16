using Microsoft.AspNetCore.Antiforgery;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/auth").WithTags("认证与个人资料");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).WithName("GetCsrfToken");
        group.MapPost("/login", async (LoginRequest request, HttpContext context, SessionStore sessions, AuditStore audit) =>
        {
            var (response, token) = await sessions.LoginAsync(request, ApiSupport.Ip(context), context.RequestAborted);
            if (request.ClientType == "web") ApiSupport.SetCookie(context, token, response.ExpiresAt);
            await audit.WriteAsync(response.User.Id, "auth.login", "session", $"客户端：{request.ClientType}，版本：{request.ClientVersion}", ApiSupport.Ip(context));
            return Results.Ok(response);
        }).RequireRateLimiting("login").Produces<AuthResponse>().WithName("Login");
        group.MapGet("/me", async (HttpContext context, AccessService access) => Results.Ok(await access.UserAsync(ApiSupport.Actor(context).UserId, context.RequestAborted))).RequireAuthorization().Produces<UserDto>().WithName("GetCurrentUser");
        group.MapPost("/refresh", async (HttpContext context, SessionStore sessions) =>
        {
            var response = await sessions.RefreshAsync(ApiSupport.Actor(context), ApiSupport.Token(context), context.RequestAborted);
            if (ApiSupport.Actor(context).ClientType == "web") ApiSupport.SetCookie(context, ApiSupport.Token(context), response.ExpiresAt);
            return Results.Ok(response);
        }).RequireAuthorization().Produces<AuthResponse>().WithName("RefreshSession");
        group.MapPost("/logout", async (HttpContext context, Database db, MediaService media, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await db.ExecuteAsync("update sessions set revoked_at=now() where id=@id", new { id = actor.SessionId });
            await media.RevokeAsync(actor.UserId, actor.SessionId);
            context.Response.Cookies.Delete(ApiSupport.CookieName, new CookieOptions { Secure = true, Path = "/" });
            await audit.WriteAsync(actor.UserId, "auth.logout", actor.SessionId.ToString(), "退出登录", ApiSupport.Ip(context));
            return Results.NoContent();
        }).RequireAuthorization().WithName("Logout");
        group.MapPut("/profile", async (ProfileRequest request, HttpContext context, Database db, AccessService access) =>
        {
            Rules.Require((request.DisplayName?.Length ?? 0) <= 128 && (request.Phone?.Length ?? 0) <= 32, "资料长度超出限制");
            var actor = ApiSupport.Actor(context);
            await db.ExecuteAsync("update users set display_name=@displayName,phone=@phone,updated_at=now() where id=@id", new { request.DisplayName, request.Phone, id = actor.UserId }, context.RequestAborted);
            return Results.Ok(await access.UserAsync(actor.UserId));
        }).RequireAuthorization().Produces<UserDto>().WithName("UpdateProfile");
        group.MapGet("/preferences", async (HttpContext context, Database db) =>
        {
            var actor = ApiSupport.Actor(context);
            return Results.Ok(await ReadPreferencesAsync(db, actor.UserId, context.RequestAborted));
        }).RequireAuthorization().Produces<UserPreferencesDto>().WithName("GetPreferences");
        group.MapPut("/preferences", async (UserPreferencesDto request, HttpContext context, Database db) =>
        {
            // 越界值直接拒绝而不是静默夹紧：客户端应显示真实错误，避免“保存成功但行为不同”。
            Rules.Require(request.Theme is "light" or "dark", "主题只能是 light 或 dark");
            Rules.Require(request.NetworkCachingMs is >= 200 and <= 5000, "网络缓存必须在 200～5000 毫秒之间");
            var actor = ApiSupport.Actor(context);
            await db.ExecuteAsync("""
                insert into user_preferences(user_id,theme,prefer_sub_stream_in_grid,hardware_decoding,network_caching_ms,show_diagnostics,updated_at)
                values(@userId,@theme,@preferSubStream,@hardwareDecoding,@networkCachingMs,@showDiagnostics,now())
                on conflict(user_id) do update set
                    theme=excluded.theme,
                    prefer_sub_stream_in_grid=excluded.prefer_sub_stream_in_grid,
                    hardware_decoding=excluded.hardware_decoding,
                    network_caching_ms=excluded.network_caching_ms,
                    show_diagnostics=excluded.show_diagnostics,
                    updated_at=now()
                """, new { userId = actor.UserId, theme = request.Theme, preferSubStream = request.PreferSubStreamInGrid,
                    hardwareDecoding = request.HardwareDecoding, request.NetworkCachingMs, request.ShowDiagnostics }, context.RequestAborted);
            return Results.Ok(await ReadPreferencesAsync(db, actor.UserId, context.RequestAborted));
        }).RequireAuthorization().Produces<UserPreferencesDto>().WithName("UpdatePreferences");
        group.MapPut("/password", async (PasswordRequest request, HttpContext context, Database db, MediaService media, AuditStore audit) =>
        {
            Rules.Password(request.NewPassword);
            var actor = ApiSupport.Actor(context);
            var user = await db.OneAsync("select password_hash from users where id=@id", new { id = actor.UserId });
            Rules.Require(Passwords.Verify(request.CurrentPassword, user.Text("passwordHash")), "当前密码不正确", "auth.password");
            await db.TransactionAsync(async tx =>
            {
                await tx.ExecuteAsync("update users set password_hash=@hash,updated_at=now() where id=@id", new { hash = Passwords.Hash(request.NewPassword), id = actor.UserId });
                await tx.ExecuteAsync("update sessions set revoked_at=now() where user_id=@id", new { id = actor.UserId });
                return true;
            });
            await media.RevokeAsync(actor.UserId);
            await audit.WriteAsync(actor.UserId, "auth.password", actor.UserId.ToString(), "本人修改密码，所有登录会话已撤销", ApiSupport.Ip(context));
            context.Response.Cookies.Delete(ApiSupport.CookieName, new CookieOptions { Secure = true, Path = "/" });
            return Results.NoContent();
        }).RequireAuthorization().WithName("ChangePassword");
    }

    /// <summary>读取账号偏好；尚无记录时返回默认值（与桌面端本机兜底一致），不写库。</summary>
    private static async Task<UserPreferencesDto> ReadPreferencesAsync(Database db, long userId, CancellationToken ct)
    {
        var row = await db.OneAsync("select theme,prefer_sub_stream_in_grid,hardware_decoding,network_caching_ms,show_diagnostics from user_preferences where user_id=@userId", new { userId }, ct);
        if (row is null) return new UserPreferencesDto();
        return new UserPreferencesDto(row.Text("theme"), row.Flag("preferSubStreamInGrid"), row.Flag("hardwareDecoding"),
            (int)row.Id("networkCachingMs"), row.Flag("showDiagnostics"));
    }
}
