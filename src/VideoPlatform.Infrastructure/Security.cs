using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public static class Passwords
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(24);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA512, 48);
        return $"pbkdf2:210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string hash)
    {
        try
        {
            var pieces = hash.Split(':');
            if (pieces.Length != 4 || pieces[0] != "pbkdf2") return false;
            var iterations = int.Parse(pieces[1]);
            if (iterations is < 100000 or > 1000000) return false;
            var expected = Convert.FromBase64String(pieces[3]);
            return CryptographicOperations.FixedTimeEquals(expected, Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(pieces[2]), iterations, HashAlgorithmName.SHA512, expected.Length));
        }
        catch (FormatException) { return false; }
    }
    public static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class SecretStore(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("VideoPlatform.v2.secrets");
    public string Protect(string text) => _protector.Protect(text);
    public string Unprotect(string text) => _protector.Unprotect(text);
}

public sealed class AccessService(Database db) : IAccessService
{
    // 必须同时满足账号有效、功能权限及显式数据范围，未配置范围不会自动获得全量访问。
    public const string ChannelPredicate = """
        exists(select 1 from users au where au.id=@userId and au.status='active' and (
          au.all_channels or exists(select 1 from user_roles ur join roles r on r.id=ur.role_id where ur.user_id=au.id and r.status='active' and (r.code='admin' or r.all_channels))
          or exists(select 1 from user_scopes s where s.user_id=au.id and ((s.type='channel' and s.scope_id=c.id) or (s.type='unit' and s.scope_id=c.unit_id) or (s.type='area' and s.scope_id=un.parent_id) or(s.type='workshop' and s.scope_id=ar.parent_id)))
          or exists(select 1 from role_scopes s join user_roles ur on ur.role_id=s.role_id join roles r on r.id=s.role_id where ur.user_id=au.id and r.status='active' and ((s.type='channel' and s.scope_id=c.id) or(s.type='unit' and s.scope_id=c.unit_id) or(s.type='area' and s.scope_id=un.parent_id) or(s.type='workshop' and s.scope_id=ar.parent_id)))
        ))
        """;
    public const string ChannelFrom = "channels c join devices d on d.id=c.device_id left join units un on un.id=c.unit_id left join areas ar on ar.id=un.parent_id";
    public const string ChannelColumns = "c.id,c.device_id,d.name as device_name,c.device_channel,c.name,c.alias,c.model,c.status,c.unit_id,c.ptz_capable,c.codec";

    public async Task<bool> HasPermissionAsync(long userId, string permission, CancellationToken ct = default)
        => (await db.OneAsync("select exists(select 1 from users u join user_roles ur on ur.user_id=u.id join roles r on r.id=ur.role_id join role_permissions p on p.role_id=r.id where u.id=@userId and u.status='active' and r.status='active' and p.permission_code=@permission) as allowed", new { userId, permission }, ct)).Flag("allowed");

    public async Task DemandAsync(Actor actor, string permission, CancellationToken ct = default)
        => Rules.Require(await HasPermissionAsync(actor.UserId, permission, ct), "当前账号没有此操作权限", "access.denied", 403);

    public async Task<bool> CanChannelAsync(long userId, long channelId, CancellationToken ct = default)
        => await db.OneAsync($"select c.id from {ChannelFrom} where c.id=@channelId and c.status<>'disabled' and d.enabled and ({ChannelPredicate})", new { userId, channelId }, ct) is not null;

    public async Task<JsonObject> ChannelAsync(Actor actor, long channelId, string permission, CancellationToken ct = default)
    {
        await DemandAsync(actor, permission, ct);
        var row = await db.OneAsync($"select {ChannelColumns} from {ChannelFrom} where c.id=@channelId and c.status<>'disabled' and d.enabled and ({ChannelPredicate})", new { userId = actor.UserId, channelId }, ct);
        return row ?? throw new PlatformException(403, "channel.denied", "通道不可用或不在授权范围内");
    }

    public async Task<bool> IsAdministratorAsync(long userId, CancellationToken ct = default)
        => (await db.OneAsync("select exists(select 1 from user_roles ur join roles r on r.id=ur.role_id join users u on u.id=ur.user_id where ur.user_id=@userId and r.code='admin' and r.status='active' and u.status='active') as allowed", new { userId }, ct)).Flag("allowed");

    public async Task<UserDto> UserAsync(long userId, CancellationToken ct = default)
    {
        var row = await db.OneAsync("select id,username,display_name,phone,status from users where id=@userId", new { userId }, ct) ?? throw new PlatformException(401, "auth.expired", "登录会话已失效");
        var permissions = await db.QueryAsync("select distinct p.permission_code from user_roles ur join roles r on r.id=ur.role_id and r.status='active' join role_permissions p on p.role_id=r.id where ur.user_id=@userId", new { userId }, ct);
        var roles = await db.QueryAsync("select role_id from user_roles where user_id=@userId", new { userId }, ct);
        return new UserDto(userId, row.Text("username"), row["displayName"]?.ToString(), row["phone"]?.ToString(), row.Text("status"), permissions.Select(p => p.Text("permissionCode")).ToArray(), roles.Select(p => p.Id("roleId")).ToArray());
    }
}

public sealed class SessionStore(Database db, AccessService access)
{
    public async Task<Actor?> AuthenticateAsync(string token, CancellationToken ct = default)
    {
        if (token.Length is < 32 or > 256) return null;
        var row = await db.OneAsync("select s.id,s.user_id,u.username,s.client_type,s.expires_at from sessions s join users u on u.id=s.user_id where s.token_hash=@hash and s.revoked_at is null and s.expires_at>now() and u.status='active'", new { hash = Passwords.TokenHash(token) }, ct);
        return row is null ? null : new Actor(Guid.Parse(row.Text("id")), row.Id("userId"), row.Text("username"), row.Text("clientType"), row.Time("expiresAt"));
    }

    public async Task<(AuthResponse Response, string Token)> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default)
    {
        Rules.Require(request.ClientType is "web" or "desktop", "客户端类型无效");
        Rules.Require(request.Username.Length <= 64 && request.Password.Length <= 256 && request.ClientVersion.Length <= 64, "登录参数过长");
        var row = await db.OneAsync("select id,password_hash,status,locked_until from users where username=@username", new { username = request.Username.Trim() }, ct);
        var valid = row is not null && row.Text("status") == "active" && (row["lockedUntil"] is null || row.Time("lockedUntil") < DateTimeOffset.UtcNow) && Passwords.Verify(request.Password, row.Text("passwordHash"));
        if (!valid)
        {
            if (row is not null) await db.ExecuteAsync("update users set failed_logins=failed_logins+1,locked_until=case when failed_logins>=4 then now()+interval '5 minutes' else locked_until end where id=@id", new { id = row.Id() }, ct);
            throw new PlatformException(401, "auth.invalid", "账号或密码不正确，或账号暂时不可用");
        }
        var token = Passwords.Token();
        var expiry = DateTimeOffset.UtcNow.AddHours(8);
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("update users set failed_logins=0,locked_until=null,last_login_at=now() where id=@id", new { id = row!.Id() }, ct);
            await tx.ExecuteAsync("insert into sessions(id,user_id,token_hash,client_type,client_version,client_ip,expires_at) values(@id,@userId,@hash,@clientType,@clientVersion,cast(@ip as inet),@expiry)", new { id = Guid.NewGuid(), userId = row!.Id(), hash = Passwords.TokenHash(token), request.ClientType, request.ClientVersion, ip, expiry }, ct);
            return true;
        }, ct);
        return (new AuthResponse(await access.UserAsync(row!.Id(), ct), request.ClientType == "desktop" ? token : null, expiry), token);
    }

    public async Task<AuthResponse> RefreshAsync(Actor actor, string token, CancellationToken ct = default)
    {
        // 稳定的不可预测令牌避免多标签页轮换竞争；撤销权始终保留在服务端。
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var changed = await db.ExecuteAsync("update sessions set expires_at=@expiresAt,last_seen_at=now() where id=@id and revoked_at is null and expires_at>now()", new { expiresAt, id = actor.SessionId }, ct);
        Rules.Require(changed == 1, "登录会话已失效", "auth.expired", 401);
        return new AuthResponse(await access.UserAsync(actor.UserId, ct), actor.ClientType == "desktop" ? token : null, expiresAt);
    }
}
