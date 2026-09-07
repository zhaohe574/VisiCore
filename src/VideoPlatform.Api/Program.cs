using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Npgsql;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Runtime.InteropServices;

var snapshotPath = Environment.GetEnvironmentVariable("VIDEO_PLATFORM_SNAPSHOT_PATH")
    ?? "/var/lib/video-platform/adapter/device-snapshot.json";
var internalKey = Environment.GetEnvironmentVariable("PLATFORM_INTERNAL_KEY");
var connectionString = Environment.GetEnvironmentVariable("PLATFORM_DATABASE_URL");
var adapterUrl = Environment.GetEnvironmentVariable("HIK_ADAPTER_API_URL") ?? "http://127.0.0.1:5090";
var adapterKey = Environment.GetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY");
var alarmPath = Environment.GetEnvironmentVariable("VIDEO_PLATFORM_ALARM_PATH")
    ?? "/home/liteware/.local/share/video-platform/alarm-events.ndjson";
var releasePath = Environment.GetEnvironmentVariable("VIDEO_PLATFORM_RELEASE_PATH")
    ?? "/var/lib/video-platform/releases";
var onlineTrendPath = Environment.GetEnvironmentVariable("VIDEO_PLATFORM_ONLINE_TREND_PATH")
    ?? Path.Combine(releasePath, "device-online-trend.json");

var builder = WebApplication.CreateBuilder(args);
var liveSessions = new ConcurrentDictionary<Guid, LiveSessionGrant>();
var playbackSessions = new ConcurrentDictionary<Guid, PlaybackSessionGrant>();
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("PLATFORM_API_URL") ?? "http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024L * 1024 * 1024);
builder.Services.AddAuthentication("platform-status")
    .AddScheme<AuthenticationSchemeOptions, PlatformStatusAuthenticationHandler>("platform-status", _ => { });
builder.Services.AddSingleton(new SnapshotStore(snapshotPath));
builder.Services.AddSingleton(new OnlineTrendStore(onlineTrendPath));
builder.Services.AddSingleton<NetworkRateTracker>();
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddScoped(_ => new NpgsqlConnection(connectionString));
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddHostedService(sp => new MediaSessionReaper(liveSessions, playbackSessions, sp.GetRequiredService<HttpClient>(), adapterUrl, adapterKey));
builder.Services.AddSignalR();
builder.Services.AddHostedService(sp => new DeviceStatusBroadcaster(sp.GetRequiredService<SnapshotStore>(), sp.GetRequiredService<IHubContext<DeviceStatusHub>>()));
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddHostedService(sp => new AlarmIngestor(connectionString, alarmPath, sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<AlarmHub>>()));

var app = builder.Build();
  const string ChannelScopePredicate = "(not exists (select 1 from user_scopes us0 where us0.user_id=@user) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id join roles r0 on r0.id=rs0.role_id and r0.status='active' where ur0.user_id=@user) or exists (select 1 from user_scopes us where us.user_id=@user and ((us.scope_type='channel' and us.scope_id=c.id) or (us.scope_type='unit' and us.scope_id=c.unit_id) or (us.scope_type='area' and us.scope_id=un.area_id) or (us.scope_type='workshop' and us.scope_id=a.workshop_id))) or exists (select 1 from role_scopes rs join user_roles ur on ur.role_id=rs.role_id join roles r on r.id=rs.role_id and r.status='active' where ur.user_id=@user and ((rs.scope_type='channel' and rs.scope_id=c.id) or (rs.scope_type='unit' and rs.scope_id=c.unit_id) or (rs.scope_type='area' and rs.scope_id=un.area_id) or (rs.scope_type='workshop' and rs.scope_id=a.workshop_id))))";

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs")
        && string.IsNullOrWhiteSpace(BearerToken(context))
        && string.IsNullOrWhiteSpace(context.Request.Query["access_token"]))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

if (!string.IsNullOrWhiteSpace(connectionString))
    await BootstrapAsync(connectionString, snapshotPath);

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "video-platform-api",
    database = string.IsNullOrWhiteSpace(connectionString) ? "disabled" : "configured",
    adapterSnapshot = snapshotPath
}));

app.MapPost("/internal/zlm/on-play", async (JsonElement payload, NpgsqlConnection db) =>
{
    var appName = payload.TryGetProperty("app", out var appValue) ? appValue.GetString() : null;
    var stream = payload.TryGetProperty("stream", out var streamValue) ? streamValue.GetString() : null;
    var parameters = payload.TryGetProperty("params", out var paramsValue) ? paramsValue.GetString() : null;
    var token = QueryParameter(parameters, "token");
    var liveGrant = liveSessions.Values.FirstOrDefault(item => item.Stream == stream && item.Token == token && item.ExpiresAt > DateTimeOffset.UtcNow);
    var playbackGrant = playbackSessions.Values.FirstOrDefault(item => item.Stream == stream && item.Token == token && item.ExpiresAt > DateTimeOffset.UtcNow);
    var userId = appName == "live" ? liveGrant?.UserId : appName == "playback" ? playbackGrant?.UserId : null;
    var channel = appName == "live" ? liveGrant?.Channel : playbackGrant?.Channel;
    var allowed = false;
    if (userId is { } owner && channel is { } channelNumber)
    {
        await db.OpenAsync();
        await using var active = new NpgsqlCommand("select exists(select 1 from users u join sessions s on s.user_id=u.id where u.id=@user and u.status='active' and s.revoked_at is null and s.expires_at>now())", db);
        active.Parameters.AddWithValue("user", owner);
        allowed = (bool)(await active.ExecuteScalarAsync() ?? false)
            && await HasPermissionForUserAsync(db, owner, appName == "live" ? "live.view" : "playback.view")
            && await HasChannelAccessAsync(db, owner, channelNumber);
    }
    Console.WriteLine($"ZLMediaKit 播放回调：stream={stream},allowed={allowed}");
    return allowed ? Results.Json(new { code = 0 }) : Results.Json(new { code = -1, msg = "播放令牌无效或已过期" });
});

app.MapPost("/internal/zlm/on-stream-changed", (JsonElement payload) =>
{
    Console.WriteLine($"ZLMediaKit 流状态回调：{payload.GetRawText()}");
    return Results.Json(new { code = 0 });
});

app.MapHub<AlarmHub>("/hubs/alarm");
app.MapHub<DeviceStatusHub>("/hubs/device-status");
app.MapHub<MediaHub>("/hubs/media");

app.MapPost("/api/live-sessions", async (HttpContext context, LiveStartRequest request, NpgsqlConnection db, HttpClient client) =>
{
    if (request.Channel <= 0 || request.StreamType is not (1 or 2))
        return Results.BadRequest(new { error = "实时预览参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "live.view")) return Results.Forbid();
    if (!await HasChannelAccessAsync(db, user.Id, request.Channel)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(adapterKey)) return Results.Problem("适配服务控制密钥未配置", statusCode: 503);
    var id = Guid.NewGuid();
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{adapterUrl.TrimEnd('/')}/live/start")
    {
            Content = JsonContent.Create(new { channel = request.Channel, streamType = request.StreamType, sessionId = id })
    };
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var detail = JsonDocument.Parse(body).RootElement.TryGetProperty("detail", out var value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(detail))
                    return Results.Problem(detail, statusCode: 502);
            }
            catch (JsonException) { }
            return Results.Problem("适配服务拒绝实时预览", statusCode: 502);
        }
        using var adapterResponse = JsonDocument.Parse(body);
        var stream = adapterResponse.RootElement.GetProperty("stream").GetString();
        if (string.IsNullOrWhiteSpace(stream))
            return Results.Problem("适配服务未返回实时流标识", statusCode: 502);
        var streamType = adapterResponse.RootElement.TryGetProperty("streamType", out var streamTypeValue)
            ? streamTypeValue.GetInt32()
            : request.StreamType;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        liveSessions[id] = new LiveSessionGrant(user.Id, request.Channel, stream, streamType, token, expiresAt);
        return Results.Ok(WithLiveUrls(id, request.Channel, stream, streamType, token, expiresAt, context));
    }
    catch (HttpRequestException)
    {
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
});

app.MapGet("/api/live-sessions/{id:guid}", async (Guid id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "live.view")) return Results.Forbid();
    if (!liveSessions.TryGetValue(id, out var grant) || grant.UserId != user.Id || grant.ExpiresAt <= DateTimeOffset.UtcNow) return Results.NotFound();
    if (!await HasChannelAccessAsync(db, user.Id, grant.Channel)) return Results.Forbid();
    return Results.Ok(WithLiveUrls(id, grant.Channel, grant.Stream, grant.StreamType, grant.Token, grant.ExpiresAt, context));
});

app.MapPost("/api/live-sessions/{id:guid}/renew", async (Guid id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "live.view")) return Results.Forbid();
    if (!liveSessions.TryGetValue(id, out var current) || current.UserId != user.Id || current.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.NotFound();
    if (!await HasChannelAccessAsync(db, user.Id, current.Channel)) return Results.Forbid();
    var renewed = current with
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
    };
    if (!liveSessions.TryUpdate(id, renewed, current)) return Results.NotFound();
    return Results.Ok(WithLiveUrls(id, renewed.Channel, renewed.Stream, renewed.StreamType, renewed.Token, renewed.ExpiresAt, context));
});

app.MapDelete("/api/live-sessions/{id:guid}", async (Guid id, HttpContext context, NpgsqlConnection db, HttpClient client) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!liveSessions.TryGetValue(id, out var grant) || grant.UserId != user.Id)
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(adapterKey)) return Results.Problem("适配服务控制密钥未配置", statusCode: 503);
    if (!((ICollection<KeyValuePair<Guid, LiveSessionGrant>>)liveSessions).Remove(new(id, grant))) return Results.NotFound();
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{adapterUrl.TrimEnd('/')}/live/stop")
    {
        Content = JsonContent.Create(new { channel = grant.Channel, streamType = grant.StreamType, sessionId = id })
    };
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            liveSessions.TryAdd(id, grant with { ExpiresAt = DateTimeOffset.MinValue });
            return Results.Problem("适配服务拒绝停止实时预览", statusCode: 502);
        }
        liveSessions.TryRemove(id, out _);
        return Results.NoContent();
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        liveSessions.TryAdd(id, grant with { ExpiresAt = DateTimeOffset.MinValue });
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
});

app.MapPost("/api/auth/login", async (LoginRequest request, NpgsqlConnection db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        return Results.BadRequest(new { error = "用户名和密码不能为空" });

    await db.OpenAsync();
    const string sql = "select id, username, password_hash, status from users where username = @username";
    await using var command = new NpgsqlCommand(sql, db);
    command.Parameters.AddWithValue("username", request.Username.Trim());
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Unauthorized();

    var userId = reader.GetInt64(0);
    var username = reader.GetString(1);
    var passwordHash = reader.GetString(2);
    var status = reader.GetString(3);
    if (status != "active" || !PasswordHasher.Verify(request.Password, passwordHash))
        return Results.Unauthorized();
    await reader.CloseAsync();

    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
    await using var insert = new NpgsqlCommand("insert into sessions(token_hash, user_id, expires_at) values (@hash, @user, @expires)", db);
    insert.Parameters.AddWithValue("hash", tokenHash);
    insert.Parameters.AddWithValue("user", userId);
    insert.Parameters.AddWithValue("expires", expiresAt);
    await insert.ExecuteNonQueryAsync();
    await using var update = new NpgsqlCommand("update users set last_login_at = now(), updated_at = now() where id = @id", db);
    update.Parameters.AddWithValue("id", userId);
    await update.ExecuteNonQueryAsync();
    return Results.Ok(new { accessToken = token, expiresAt, user = new { id = userId, username } });
});

app.MapPost("/api/auth/refresh", async (HttpContext context, NpgsqlConnection db) =>
{
    var oldToken = BearerToken(context);
    if (oldToken is null) return Results.Unauthorized();
    await db.OpenAsync();
    var current = await AuthenticateAsync(context, db);
    if (current is null) return Results.Unauthorized();
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace("+", "-").Replace("/", "_").TrimEnd('=');
    var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
    await using var transaction = await db.BeginTransactionAsync();
    await using var revoke = new NpgsqlCommand("update sessions set revoked_at = now() where token_hash = @old and revoked_at is null", db, transaction);
    revoke.Parameters.AddWithValue("old", HashToken(oldToken));
    if (await revoke.ExecuteNonQueryAsync() == 0)
    {
        await transaction.RollbackAsync();
        return Results.Unauthorized();
    }
    await using var insert = new NpgsqlCommand("insert into sessions(token_hash, user_id, expires_at) values (@hash, @user, @expires)", db, transaction);
    insert.Parameters.AddWithValue("hash", HashToken(token));
    insert.Parameters.AddWithValue("user", current.Id);
    insert.Parameters.AddWithValue("expires", expiresAt);
    await insert.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    return Results.Ok(new { accessToken = token, expiresAt, user = new { id = current.Id, username = current.Username } });
});

app.MapPost("/api/auth/logout", async (HttpContext context, NpgsqlConnection db) =>
{
    var token = BearerToken(context);
    if (token is null)
        return Results.NoContent();
    await db.OpenAsync();
    await using var command = new NpgsqlCommand("update sessions set revoked_at = now() where token_hash = @hash and revoked_at is null", db);
    command.Parameters.AddWithValue("hash", HashToken(token));
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapGet("/api/auth/me", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    var permissions = await PermissionsAsync(db, user.Id);
    await using var profile = new NpgsqlCommand("select display_name, phone from users where id = @id", db);
    profile.Parameters.AddWithValue("id", user.Id);
    await using var reader = await profile.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Unauthorized();
    return Results.Ok(new
    {
        user.Id,
        user.Username,
        displayName = reader.IsDBNull(0) ? null : reader.GetString(0),
        phone = reader.IsDBNull(1) ? null : reader.GetString(1),
        permissions
    });
});

app.MapPut("/api/auth/profile", async (HttpContext context, NpgsqlConnection db, UserProfileRequest request) =>
{
    if (!ValidOptionalText(request.DisplayName, 128) || !ValidOptionalText(request.Phone, 32))
        return Results.BadRequest(new { error = "姓名或手机号参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    var displayName = request.DisplayName?.Trim();
    var phone = request.Phone?.Trim();
    await using var update = new NpgsqlCommand("update users set display_name = @displayName, phone = @phone, updated_at = now() where id = @id", db);
    update.Parameters.AddWithValue("displayName", (object?)displayName ?? DBNull.Value);
    update.Parameters.AddWithValue("phone", (object?)phone ?? DBNull.Value);
    update.Parameters.AddWithValue("id", user.Id);
    await update.ExecuteNonQueryAsync();
    await WriteAuditAsync(db, user.Id, "auth.profile.update", "profile", context);
    return Results.Ok(new { user.Id, user.Username, displayName, phone });
});

app.MapPut("/api/auth/password", async (HttpContext context, NpgsqlConnection db, PasswordChangeRequest request) =>
{
    if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 12)
        return Results.BadRequest(new { error = "新密码至少需要 12 个字符" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    await using var query = new NpgsqlCommand("select password_hash from users where id = @id", db);
    query.Parameters.AddWithValue("id", user.Id);
    var currentHash = (string?)await query.ExecuteScalarAsync();
    if (currentHash is null || !PasswordHasher.Verify(request.CurrentPassword, currentHash))
        return Results.BadRequest(new { error = "当前密码不正确" });
    await using var update = new NpgsqlCommand("update users set password_hash = @hash, updated_at = now() where id = @id", db);
    update.Parameters.AddWithValue("hash", PasswordHasher.Hash(request.NewPassword));
    update.Parameters.AddWithValue("id", user.Id);
    await update.ExecuteNonQueryAsync();
    await WriteAuditAsync(db, user.Id, "auth.password.change", "password", context);
    return Results.NoContent();
});

app.MapGet("/api/devices", async (HttpContext context, SnapshotStore store, NpgsqlConnection? db) =>
{
    if (!Authorized(context, internalKey))
    {
        if (db is null || !await HasPermissionAsync(context, db, "device.read"))
            return Results.Unauthorized();
    }
    return ReadDevice(store);
});

app.MapGet("/api/devices/{id:long}", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "device.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select id, device_key, ip, service_port, model, serial_number, status, last_seen_at from devices where id=@id", db);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    return Results.Ok(new
    {
        id = reader.GetInt64(0),
        deviceKey = reader.GetString(1),
        ip = reader.GetFieldValue<System.Net.IPAddress>(2).ToString(),
        servicePort = reader.GetInt32(3),
        model = reader.IsDBNull(4) ? null : reader.GetString(4),
        serialNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
        status = reader.GetString(6),
        lastSeenAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7)
    });
});

app.MapGet("/api/devices/{id:long}/channels", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "channel.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select c.id, c.device_channel, c.name, c.model, c.ptz_capable, c.status, c.unit_id from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where c.device_id=@device and c.status <> 'disabled' and " + ChannelScopePredicate + " order by c.device_channel", db);
    command.Parameters.AddWithValue("device", id);
    command.Parameters.AddWithValue("user", user.Id);
    var channels = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        channels.Add(new { id = reader.GetInt64(0), channel = reader.GetInt32(1), name = reader.IsDBNull(2) ? null : reader.GetString(2), model = reader.IsDBNull(3) ? null : reader.GetString(3), ptzCapable = reader.GetBoolean(4), status = reader.GetString(5), unitId = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6) });
    return Results.Ok(channels);
});

app.MapGet("/api/devices/{id:long}/stats", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "statistics.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select (select count(*) from channels c where c.device_id=@id and c.status <> 'disabled'), (select count(*) from channels c where c.device_id=@id and c.status='online'), (select count(*) from alarm_events e where e.device_id=@id and e.occurred_at >= current_date), (select count(*) from alarm_events e where e.device_id=@id and e.state='new')", db);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    return Results.Ok(new { deviceId = id, channels = reader.GetInt64(0), onlineChannels = reader.GetInt64(1), alarmsToday = reader.GetInt64(2), unacknowledgedAlarms = reader.GetInt64(3) });
});

app.MapGet("/api/channels", async (HttpContext context, SnapshotStore store, NpgsqlConnection? db) =>
{
    if (Authorized(context, internalKey))
        return ReadChannels(store);
    if (db is null) return Results.Unauthorized();
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "channel.read")) return Results.Forbid();
    var allowed = await AllowedChannelNumbersAsync(db, user.Id);
    var snapshot = store.Read();
    if (snapshot is null || snapshot["status"]?.GetValue<string>() != "ok")
        return Results.Problem("设备快照不存在或无法读取", statusCode: 503);
    var channels = snapshot["result"]?["ipChannels"]?.AsArray() ?? new JsonArray();
    var channelIds = new Dictionary<int, (long Id, long? UnitId, bool PtzCapable)>();
    await using (var channelQuery = new NpgsqlCommand("select id, device_channel, unit_id, ptz_capable from channels where status <> 'disabled'", db))
    await using (var channelReader = await channelQuery.ExecuteReaderAsync())
    {
        while (await channelReader.ReadAsync())
            channelIds[channelReader.GetInt32(1)] = (channelReader.GetInt64(0), channelReader.IsDBNull(2) ? null : channelReader.GetInt64(2), channelReader.GetBoolean(3));
    }
    var result = new JsonArray();
    foreach (var item in channels)
    {
        if (item is not JsonObject row || !allowed.Contains(row["channelNumber"]?.GetValue<int>() ?? 0))
            continue;
        var copy = row.DeepClone().AsObject();
        if (channelIds.TryGetValue(copy["channelNumber"]?.GetValue<int>() ?? 0, out var metadata))
        {
            copy["id"] = metadata.Id;
            copy["unitId"] = metadata.UnitId;
            copy["ptzCapable"] = metadata.PtzCapable;
        }
        result.Add(copy);
    }
    return Results.Ok(result);
});

app.MapGet("/api/stats", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "statistics.read") && !await HasPermissionForUserAsync(db, user.Id, "device.read"))
        return Results.Forbid();
    var scopedChannels = "(select count(*) from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where c.status <> 'disabled' and " + ChannelScopePredicate + ")";
    var scopedAlarmsToday = "(select count(*) from alarm_events e left join channels c on c.id=e.channel_id left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where e.occurred_at >= current_date and ((not exists (select 1 from user_scopes us0 where us0.user_id=@user) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id where ur0.user_id=@user)) or (c.id is not null and " + ChannelScopePredicate + ")))";
    var scopedNewAlarms = "(select count(*) from alarm_events e left join channels c on c.id=e.channel_id left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where e.state = 'new' and ((not exists (select 1 from user_scopes us0 where us0.user_id=@user) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id where ur0.user_id=@user)) or (c.id is not null and " + ChannelScopePredicate + ")))";
    await using var command = new NpgsqlCommand("select (select count(*) from users), (select count(*) from roles where status = 'active'), (select count(*) from workshops where status = 'active'), (select count(*) from areas where status = 'active'), (select count(*) from units where status = 'active'), " + scopedChannels + ", " + scopedAlarmsToday + ", " + scopedNewAlarms, db);
    command.Parameters.AddWithValue("user", user.Id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Problem("统计数据读取失败", statusCode: 503);
    return Results.Ok(new
    {
        users = reader.GetInt64(0),
        roles = reader.GetInt64(1),
        workshops = reader.GetInt64(2),
        areas = reader.GetInt64(3),
        units = reader.GetInt64(4),
        channels = reader.GetInt64(5),
        alarmsToday = reader.GetInt64(6),
        unacknowledgedAlarms = reader.GetInt64(7),
        activeLiveSessions = liveSessions.Count
    });
});

app.MapGet("/api/system-stats", async (HttpContext context, NpgsqlConnection db, NetworkRateTracker networkRateTracker) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "statistics.read")) return Results.Forbid();
    return Results.Ok(ReadSystemStats(networkRateTracker));
});

app.MapGet("/api/device-stats", async (HttpContext context, NpgsqlConnection db, OnlineTrendStore trendStore) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "statistics.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select d.id, d.device_key, d.ip, d.service_port, d.model, d.serial_number, d.status, d.last_seen_at, (select count(*) from devices), (select count(*) from devices where status='online'), (select count(*) from devices where status <> 'online'), (select count(*) from channels c where c.device_id=d.id and c.status <> 'disabled'), (select count(*) from channels c where c.device_id=d.id and c.status='online'), (select count(*) from channels c where c.device_id=d.id and c.status='offline'), (select count(*) from alarm_events e where e.device_id=d.id and e.occurred_at >= current_date), (select count(*) from alarm_events e where e.device_id=d.id and e.state='new') from devices d order by d.id limit 1", db);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var deviceId = reader.GetInt64(0);
    var deviceKey = reader.GetString(1);
    var ip = reader.GetFieldValue<System.Net.IPAddress>(2).ToString();
    var servicePort = reader.GetInt32(3);
    var model = reader.IsDBNull(4) ? null : reader.GetString(4);
    var serialNumber = reader.IsDBNull(5) ? null : reader.GetString(5);
    var status = reader.GetString(6);
    var lastSeenAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7);
    var deviceCount = reader.GetInt64(8);
    var onlineDevices = reader.GetInt64(9);
    var offlineDevices = reader.GetInt64(10);
    var channels = reader.GetInt64(11);
    var onlineChannels = reader.GetInt64(12);
    var offlineChannels = reader.GetInt64(13);
    var alarmsToday = reader.GetInt64(14);
    var unacknowledgedAlarms = reader.GetInt64(15);
    await reader.CloseAsync();
    var onlineTrend = trendStore.Record(onlineChannels, channels);
    return Results.Ok(new
    {
        deviceId, deviceKey, ip, servicePort, model, serialNumber, status, lastSeenAt,
        deviceCount, onlineDevices, offlineDevices, channels, onlineChannels, offlineChannels, alarmsToday, unacknowledgedAlarms, onlineTrend
    });
});

app.MapGet("/api/users", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "user.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select u.id, u.username, u.display_name, u.phone, u.status, u.last_login_at, coalesce(array_agg(r.id) filter (where r.id is not null), '{}'), coalesce(array_agg(r.name order by r.name) filter (where r.id is not null), '{}') from users u left join user_roles ur on ur.user_id = u.id left join roles r on r.id = ur.role_id group by u.id order by u.username", db);
    var users = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        users.Add(new
        {
            id = reader.GetInt64(0),
            username = reader.GetString(1),
            displayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            phone = reader.IsDBNull(3) ? null : reader.GetString(3),
            status = reader.GetString(4),
            lastLoginAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
            roleIds = reader.GetFieldValue<long[]>(6),
            roleNames = reader.GetFieldValue<string[]>(7)
        });
    return Results.Ok(users);
});

app.MapPost("/api/users", async (HttpContext context, UserCreateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Username, 64) || string.IsNullOrEmpty(request.Password) || request.Password.Length < 12
        || !ValidOptionalText(request.DisplayName, 128) || !ValidOptionalText(request.Phone, 32))
        return Results.BadRequest(new { error = "用户名不能为空，密码至少需要 12 个字符，姓名或手机号参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "user.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("insert into users(username, display_name, phone, password_hash, status) values (@username, @displayName, @phone, @hash, 'active') returning id", db);
    command.Parameters.AddWithValue("username", request.Username.Trim());
    command.Parameters.AddWithValue("displayName", (object?)request.DisplayName?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("phone", (object?)request.Phone?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("hash", PasswordHasher.Hash(request.Password));
    try
    {
        var id = (long)(await command.ExecuteScalarAsync() ?? 0L);
        await using var role = new NpgsqlCommand("insert into user_roles(user_id, role_id) select @user, id from roles where code = 'operator' and status = 'active' on conflict do nothing", db);
        role.Parameters.AddWithValue("user", id);
        await role.ExecuteNonQueryAsync();
        await WriteAuditAsync(db, user.Id, "user.create", $"username={request.Username.Trim()}", context);
        return Results.Created($"/api/users/{id}", new { id, username = request.Username.Trim(), displayName = request.DisplayName?.Trim(), phone = request.Phone?.Trim(), status = "active" });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict(new { error = "用户名已存在" });
    }
});

app.MapPut("/api/users/{id:long}", async (long id, HttpContext context, UserUpdateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Username, 64) || request.Status is not ("active" or "disabled" or "locked")
        || (request.Password is not null && request.Password.Length < 12)
        || !ValidOptionalText(request.DisplayName, 128) || !ValidOptionalText(request.Phone, 32))
        return Results.BadRequest(new { error = "账号资料、状态或密码参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "user.manage")) return Results.Forbid();
    if (id == user.Id && !string.Equals(request.Username.Trim(), user.Username, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "本人不能修改用户名" });
    if (id == user.Id && request.Status != "active") return Results.BadRequest(new { error = "不能停用或锁定当前登录账号" });
    await using var command = new NpgsqlCommand(request.Password is null
        ? "update users set username = @username, display_name = @displayName, phone = @phone, status = @status, updated_at = now() where id = @id"
        : "update users set username = @username, display_name = @displayName, phone = @phone, status = @status, password_hash = @hash, updated_at = now() where id = @id", db);
    command.Parameters.AddWithValue("username", request.Username.Trim());
    command.Parameters.AddWithValue("displayName", (object?)request.DisplayName?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("phone", (object?)request.Phone?.Trim() ?? DBNull.Value);
    command.Parameters.AddWithValue("status", request.Status);
    command.Parameters.AddWithValue("id", id);
    if (request.Password is not null) command.Parameters.AddWithValue("hash", PasswordHasher.Hash(request.Password));
    try
    {
        if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound();
        await WriteAuditAsync(db, user.Id, "user.update", $"user={id},status={request.Status}", context);
        return Results.Ok(new { id, username = request.Username.Trim(), displayName = request.DisplayName?.Trim(), phone = request.Phone?.Trim(), status = request.Status });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict(new { error = "用户名已存在" });
    }
});

app.MapPut("/api/users/{id:long}/role", async (long id, HttpContext context, UserRoleRequest request, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    await using var transaction = await db.BeginTransactionAsync();
    await using var userExists = new NpgsqlCommand("select exists (select 1 from users where id = @id)", db, transaction);
    userExists.Parameters.AddWithValue("id", id);
    if (!(bool)(await userExists.ExecuteScalarAsync() ?? false)) return Results.NotFound();
    if (request.RoleId is null)
    {
        await using var clearOnly = new NpgsqlCommand("delete from user_roles where user_id = @id", db, transaction);
        clearOnly.Parameters.AddWithValue("id", id);
        await clearOnly.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        await WriteAuditAsync(db, user.Id, "role.unassign", $"user={id}", context);
        return Results.NoContent();
    }
    await using var exists = new NpgsqlCommand("select exists (select 1 from roles where id = @role and status = 'active')", db, transaction);
    exists.Parameters.AddWithValue("role", request.RoleId.Value);
    if (!(bool)(await exists.ExecuteScalarAsync() ?? false)) return Results.NotFound();
    await using var clear = new NpgsqlCommand("delete from user_roles where user_id = @id", db, transaction);
    clear.Parameters.AddWithValue("id", id);
    await clear.ExecuteNonQueryAsync();
    await using var assign = new NpgsqlCommand("insert into user_roles(user_id, role_id) values (@id, @role)", db, transaction);
    assign.Parameters.AddWithValue("id", id);
    assign.Parameters.AddWithValue("role", request.RoleId.Value);
    await assign.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    await WriteAuditAsync(db, user.Id, "role.assign", $"user={id},role={request.RoleId.Value}", context);
    return Results.NoContent();
});

app.MapGet("/api/roles", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select r.id, r.name, r.code, r.status, count(distinct ur.user_id), coalesce(array_agg(distinct rp.permission_code) filter (where rp.permission_code is not null), '{}') from roles r left join user_roles ur on ur.role_id = r.id left join role_permissions rp on rp.role_id = r.id group by r.id order by r.name", db);
    var roles = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        roles.Add(new { id = reader.GetInt64(0), name = reader.GetString(1), code = reader.GetString(2), status = reader.GetString(3), userCount = reader.GetInt64(4), permissionCodes = reader.GetFieldValue<string[]>(5) });
    return Results.Ok(roles);
});

app.MapPost("/api/roles", async (HttpContext context, RoleCreateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 64) || !ValidBusinessText(request.Code, 64)
        || request.Status is not ("active" or "disabled"))
        return Results.BadRequest(new { error = "角色名称、编码或状态参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("insert into roles(name, code, status) values (@name, @code, @status) returning id", db);
    command.Parameters.AddWithValue("name", request.Name.Trim());
    command.Parameters.AddWithValue("code", request.Code.Trim());
    command.Parameters.AddWithValue("status", request.Status);
    try
    {
        var id = (long)(await command.ExecuteScalarAsync() ?? 0L);
        await WriteAuditAsync(db, user.Id, "role.create", $"role={id}", context);
        return Results.Created($"/api/roles/{id}", new { id, name = request.Name.Trim(), code = request.Code.Trim(), status = request.Status, userCount = 0, permissionCodes = Array.Empty<string>() });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict(new { error = "角色编码已存在" });
    }
});

app.MapPut("/api/roles/{id:long}", async (long id, HttpContext context, RoleUpdateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 64) || !ValidBusinessText(request.Code, 64)
        || request.Status is not ("active" or "disabled"))
        return Results.BadRequest(new { error = "角色名称、编码或状态参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("update roles set name=@name, code=@code, status=@status where id=@id", db);
    command.Parameters.AddWithValue("name", request.Name.Trim());
    command.Parameters.AddWithValue("code", request.Code.Trim());
    command.Parameters.AddWithValue("status", request.Status);
    command.Parameters.AddWithValue("id", id);
    try
    {
        if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound();
        await WriteAuditAsync(db, user.Id, "role.update", $"role={id},status={request.Status}", context);
        return Results.Ok(new { id, name = request.Name.Trim(), code = request.Code.Trim(), status = request.Status });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict(new { error = "角色编码已存在" });
    }
});

app.MapDelete("/api/roles/{id:long}", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    await using var transaction = await db.BeginTransactionAsync();
    await using var roleCheck = new NpgsqlCommand("select exists (select 1 from roles where id=@id), (select count(*) from user_roles where role_id=@id)", db, transaction);
    roleCheck.Parameters.AddWithValue("id", id);
    await using var reader = await roleCheck.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var exists = reader.GetBoolean(0);
    var userCount = reader.GetInt64(1);
    await reader.CloseAsync();
    if (!exists) return Results.NotFound();
    if (userCount > 0)
        return Results.Conflict(new { error = $"角色仍绑定 {userCount} 个账号，请先解绑后再删除" });
    await using var clearPermissions = new NpgsqlCommand("delete from role_permissions where role_id=@id", db, transaction);
    clearPermissions.Parameters.AddWithValue("id", id);
    await clearPermissions.ExecuteNonQueryAsync();
    await using var clearScopes = new NpgsqlCommand("delete from role_scopes where role_id=@id", db, transaction);
    clearScopes.Parameters.AddWithValue("id", id);
    await clearScopes.ExecuteNonQueryAsync();
    await using var delete = new NpgsqlCommand("delete from roles where id=@id", db, transaction);
    delete.Parameters.AddWithValue("id", id);
    await delete.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    await WriteAuditAsync(db, user.Id, "role.delete", $"role={id}", context);
    return Results.NoContent();
});

app.MapGet("/api/permissions", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select code, name, resource_type, operation_type from permissions order by resource_type, operation_type, code", db);
    var permissions = new List<object>(); await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) permissions.Add(new { code = reader.GetString(0), name = reader.GetString(1), resourceType = reader.GetString(2), operationType = reader.GetString(3) });
    return Results.Ok(permissions);
});

app.MapPut("/api/roles/{id:long}/permissions", async (long id, HttpContext context, RolePermissionsRequest request, NpgsqlConnection db) =>
{
    if (request.Codes is null || request.Codes.Length > 100 || request.Codes.Any(string.IsNullOrWhiteSpace)) return Results.BadRequest(new { error = "权限参数无效" });
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "role.manage")) return Results.Forbid();
    var codes = request.Codes.Distinct(StringComparer.Ordinal).ToArray();
    await using var transaction = await db.BeginTransactionAsync();
    await using var roleCheck = new NpgsqlCommand("select exists (select 1 from roles where id=@id)", db, transaction); roleCheck.Parameters.AddWithValue("id", id);
    if (!(bool)(await roleCheck.ExecuteScalarAsync() ?? false)) return Results.NotFound();
    await using var valid = new NpgsqlCommand("select count(*) from permissions where code = any(@codes)", db, transaction); valid.Parameters.AddWithValue("codes", codes);
    if ((long)(await valid.ExecuteScalarAsync() ?? 0L) != codes.Length) return Results.BadRequest(new { error = "包含未知权限编码" });
    await using var clear = new NpgsqlCommand("delete from role_permissions where role_id=@id", db, transaction); clear.Parameters.AddWithValue("id", id); await clear.ExecuteNonQueryAsync();
    await using var add = new NpgsqlCommand("insert into role_permissions(role_id, permission_code) select @id, unnest(@codes)", db, transaction); add.Parameters.AddWithValue("id", id); add.Parameters.AddWithValue("codes", codes); await add.ExecuteNonQueryAsync();
    await transaction.CommitAsync(); await WriteAuditAsync(db, user.Id, "role.permissions.update", $"role={id},count={codes.Length}", context); return Results.NoContent();
});

app.MapPost("/api/channels/{channel:int}/ptz/start", async (int channel, HttpContext context, PtzControlRequest request, NpgsqlConnection db, HttpClient client) =>
    await ForwardPtzAsync(channel, request, false, context, db, client, adapterUrl, adapterKey));

app.MapPost("/api/channels/{channel:int}/ptz/stop", async (int channel, HttpContext context, NpgsqlConnection db, HttpClient client) =>
    await ForwardPtzAsync(channel, new PtzControlRequest("stop", 4), true, context, db, client, adapterUrl, adapterKey));

app.MapPost("/api/channels/{channel:int}/ptz/preset/{preset:int}", async (int channel, int preset, HttpContext context, NpgsqlConnection db, HttpClient client) =>
{
    if (channel <= 0 || preset is < 1 or > 300)
        return Results.BadRequest(new { error = "预置位参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "ptz.control")) return Results.Forbid();
    if (!await HasChannelAccessAsync(db, user.Id, channel)) return Results.Forbid();
    if (!await HasPtzCapabilityAsync(db, user.Id, channel)) return Results.BadRequest(new { error = "该通道不支持云台控制" });
    if (string.IsNullOrWhiteSpace(adapterKey)) return Results.Problem("适配服务控制密钥未配置", statusCode: 503);
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{adapterUrl.TrimEnd('/')}/ptz/preset")
    {
        Content = JsonContent.Create(new { channel, preset })
    };
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        await WriteAuditAsync(db, user.Id, "ptz.preset", $"channel={channel},preset={preset}", context);
        return response.IsSuccessStatusCode
            ? Results.Content(body, "application/json", Encoding.UTF8)
            : Results.Problem("适配服务拒绝预置位调用", statusCode: 502);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
});

app.MapPost("/api/recordings/search", async (HttpContext context, RecordingSearchRequest request, NpgsqlConnection db, HttpClient client) =>
{
    if (request.Channel <= 0 || request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(7))
        return Results.BadRequest(new { error = "录像查询参数无效，时间范围不能超过 7 天" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "playback.view")) return Results.Forbid();
    if (!await HasChannelAccessAsync(db, user.Id, request.Channel)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(adapterKey)) return Results.Problem("适配服务控制密钥未配置", statusCode: 503);
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{adapterUrl.TrimEnd('/')}/recordings/search")
    {
        Content = JsonContent.Create(request)
    };
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode
            ? Results.Content(body, "application/json", Encoding.UTF8)
            : Results.Problem("适配服务拒绝录像查询", statusCode: 502);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
});

app.MapPost("/api/playback-sessions", async (HttpContext context, PlaybackStartRequest request, NpgsqlConnection db, HttpClient client) =>
{
    if (request.Channel <= 0 || request.End <= request.Start || request.End - request.Start > TimeSpan.FromDays(1))
        return Results.BadRequest(new { error = "回放参数无效，时间范围不能超过 1 天" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "playback.view")) return Results.Forbid();
    if (!await HasChannelAccessAsync(db, user.Id, request.Channel)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(adapterKey)) return Results.Problem("适配服务控制密钥未配置", statusCode: 503);
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{adapterUrl.TrimEnd('/')}/playback/start")
    {
        Content = JsonContent.Create(new { request.Channel, request.Start, request.End, userId = user.Id })
    };
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return Results.Problem("适配服务拒绝回放请求", statusCode: 502);
        using var adapterResponse = JsonDocument.Parse(body);
        if (!adapterResponse.RootElement.TryGetProperty("id", out var idValue)
            || !Guid.TryParse(idValue.GetString(), out var id)
            || !adapterResponse.RootElement.TryGetProperty("stream", out var streamValue)
            || string.IsNullOrWhiteSpace(streamValue.GetString()))
            return Results.Problem("适配服务未返回回放流标识", statusCode: 502);
        var stream = streamValue.GetString()!;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        playbackSessions[id] = new PlaybackSessionGrant(user.Id, request.Channel, stream, token, expiresAt);
        return Results.Content(WithPlaybackUrls(body, id, request.Channel, stream, token, expiresAt, context), "application/json", Encoding.UTF8);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
});

app.MapGet("/api/playback-sessions/{id:guid}", async (Guid id, HttpContext context, NpgsqlConnection db, HttpClient client) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "playback.view")) return Results.Forbid();
    if (!playbackSessions.TryGetValue(id, out var grant) || grant.UserId != user.Id || grant.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.NotFound();
    if (!await HasChannelAccessAsync(db, user.Id, grant.Channel)) return Results.Forbid();
    return await ForwardPlaybackAsync(HttpMethod.Get, $"/playback/{id}?userId={user.Id}", null, client, adapterUrl, adapterKey,
        body => WithPlaybackUrls(body, id, grant.Channel, grant.Stream, grant.Token, grant.ExpiresAt, context));
});

app.MapPost("/api/playback-sessions/{id:guid}/control", async (Guid id, HttpContext context, PlaybackControlRequest request, NpgsqlConnection db, HttpClient client) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "playback.view")) return Results.Forbid();
    if (!playbackSessions.TryGetValue(id, out var grant) || grant.UserId != user.Id || grant.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.NotFound();
    if (!await HasChannelAccessAsync(db, user.Id, grant.Channel)) return Results.Forbid();
    return await ForwardPlaybackAsync(HttpMethod.Post, $"/playback/{id}/control?userId={user.Id}", request, client, adapterUrl, adapterKey,
        body => WithPlaybackUrls(body, id, grant.Channel, grant.Stream, grant.Token, grant.ExpiresAt, context));
});

app.MapPost("/api/playback-sessions/{id:guid}/renew", async (Guid id, HttpContext context, NpgsqlConnection db, HttpClient client) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "playback.view")) return Results.Forbid();
    if (!playbackSessions.TryGetValue(id, out var current) || current.UserId != user.Id || current.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.NotFound();
    if (!await HasChannelAccessAsync(db, user.Id, current.Channel)) return Results.Forbid();
    var token = current.Token;
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
    var renewedSuccessfully = false;
    var result = await ForwardPlaybackAsync(HttpMethod.Get, $"/playback/{id}?userId={user.Id}", null, client, adapterUrl, adapterKey,
        body => WithPlaybackUrls(body, id, current.Channel, current.Stream, token, expiresAt, context),
        onSuccess: () => renewedSuccessfully = true);
    if (renewedSuccessfully && !playbackSessions.TryUpdate(id, current with { ExpiresAt = expiresAt }, current)) return Results.NotFound();
    return result;
});

app.MapDelete("/api/playback-sessions/{id:guid}", async (Guid id, HttpContext context, NpgsqlConnection db, HttpClient client) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!playbackSessions.TryGetValue(id, out var grant) || grant.UserId != user.Id)
        return Results.NotFound();
    if (!((ICollection<KeyValuePair<Guid, PlaybackSessionGrant>>)playbackSessions).Remove(new(id, grant))) return Results.NotFound();
    var deleted = false;
    var result = await ForwardPlaybackAsync(HttpMethod.Delete, $"/playback/{id}?userId={user.Id}", null, client, adapterUrl, adapterKey,
        onSuccess: () => deleted = true);
    if (deleted)
        playbackSessions.TryRemove(id, out _);
    else playbackSessions.TryAdd(id, grant with { ExpiresAt = DateTimeOffset.MinValue });
    return result;
});

app.MapGet("/api/alarms", async (HttpContext context, NpgsqlConnection db, int? limit, string? state, int? channel, string? eventType, DateTimeOffset? from, DateTimeOffset? to) =>
{
    if (channel is <= 0 || from > to || (!string.IsNullOrWhiteSpace(state) && state is not ("new" or "acknowledged" or "resolved")))
        return Results.BadRequest(new { error = "报警筛选参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "alarm.read"))
        return Results.Forbid();
    var size = Math.Clamp(limit ?? 50, 1, 200);
    var filters = new StringBuilder(" where ((not exists (select 1 from user_scopes us where us.user_id=@user) and not exists (select 1 from role_scopes rs join user_roles ur on ur.role_id=rs.role_id where ur.user_id=@user)) or (c.id is not null and " + ChannelScopePredicate + "))");
    if (!string.IsNullOrWhiteSpace(state)) filters.Append(" and e.state=@state");
    if (channel is > 0) filters.Append(" and c.device_channel=@channel");
    if (!string.IsNullOrWhiteSpace(eventType)) filters.Append(" and e.event_type=@eventType");
    if (from is not null) filters.Append(" and e.occurred_at>=@from");
    if (to is not null) filters.Append(" and e.occurred_at<=@to");
    await using var command = new NpgsqlCommand("select e.id, e.event_type, e.occurred_at, e.state, e.normalized_payload, e.channel_id, c.device_channel, octet_length(e.image_payload) from alarm_events e left join channels c on c.id=e.channel_id left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id" + filters + " order by e.occurred_at desc limit @limit", db);
    command.Parameters.AddWithValue("user", user.Id);
    command.Parameters.AddWithValue("limit", size);
    if (!string.IsNullOrWhiteSpace(state)) command.Parameters.AddWithValue("state", state.Trim());
    if (channel is > 0) command.Parameters.AddWithValue("channel", channel.Value);
    if (!string.IsNullOrWhiteSpace(eventType)) command.Parameters.AddWithValue("eventType", eventType.Trim());
    if (from is not null) command.Parameters.AddWithValue("from", from.Value);
    if (to is not null) command.Parameters.AddWithValue("to", to.Value);
    var alarms = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        alarms.Add(new
        {
            id = reader.GetInt64(0),
            eventType = reader.GetString(1),
            occurredAt = reader.GetFieldValue<DateTimeOffset>(2),
            state = reader.GetString(3),
            payload = reader.GetString(4),
            channelId = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
            channelNumber = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
            imageAvailable = !reader.IsDBNull(7) && reader.GetInt32(7) > 0
        });
    return Results.Ok(alarms);
});

app.MapGet("/api/alarms/{id:long}", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "alarm.read"))
        return Results.Forbid();
    await using var command = new NpgsqlCommand("select e.id, e.event_type, e.occurred_at, e.state, e.normalized_payload, e.channel_id, c.device_channel, octet_length(e.image_payload) from alarm_events e left join channels c on c.id=e.channel_id left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where e.id=@id and ((not exists (select 1 from user_scopes us0 where us0.user_id=@user) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id where ur0.user_id=@user)) or (c.id is not null and " + ChannelScopePredicate + "))", db);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("user", user.Id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound();
    return Results.Ok(new
    {
        id = reader.GetInt64(0),
        eventType = reader.GetString(1),
        occurredAt = reader.GetFieldValue<DateTimeOffset>(2),
        state = reader.GetString(3),
        payload = reader.GetString(4),
        channelId = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5),
        channelNumber = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
        imageAvailable = !reader.IsDBNull(7) && reader.GetInt32(7) > 0,
        imageLength = reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
    });
});

app.MapGet("/api/alarms/{id:long}/image", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "alarm.read"))
        return Results.Forbid();
    await using var command = new NpgsqlCommand("select e.image_payload from alarm_events e left join channels c on c.id=e.channel_id left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where e.id=@id and ((not exists (select 1 from user_scopes us0 where us0.user_id=@user) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id where ur0.user_id=@user)) or (c.id is not null and " + ChannelScopePredicate + "))", db);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("user", user.Id);
    var image = await command.ExecuteScalarAsync();
    return image is byte[] bytes && bytes.Length > 0 ? Results.File(bytes, "image/jpeg") : Results.NotFound();
});

app.MapPost("/api/alarms/{id:long}/ack", async (long id, HttpContext context, NpgsqlConnection db, AckRequest request) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "alarm.ack"))
        return Results.Forbid();
    await using var command = new NpgsqlCommand("update alarm_events e set state = 'acknowledged', acknowledged_by = @user, acknowledged_at = now(), handling_note = @note where e.id = @id and ((not exists (select 1 from user_scopes us0 where us0.user_id=@user) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id where ur0.user_id=@user)) or exists (select 1 from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where c.id=e.channel_id and " + ChannelScopePredicate + "))", db);
    command.Parameters.AddWithValue("user", user.Id);
    command.Parameters.AddWithValue("note", (object?)request.Note ?? DBNull.Value);
    command.Parameters.AddWithValue("id", id);
    if (await command.ExecuteNonQueryAsync() == 0)
        return Results.NotFound();
    await WriteAuditAsync(db, user.Id, "alarm.ack", $"alarm={id}", context);
    return Results.Ok(new { id, state = "acknowledged" });
});

app.MapGet("/api/workshops", async (HttpContext context, NpgsqlConnection db) =>
    await ReadBusinessRowsAsync(context, db, "select id, name, code, status from workshops order by name", "area.read"));

app.MapPost("/api/workshops", async (HttpContext context, BusinessCreateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 128) || !ValidBusinessText(request.Code, 64)) return Results.BadRequest(new { error = "车间名称或编码无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("insert into workshops(name, code) values (@name, @code) returning id", db);
    command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("code", request.Code.Trim());
    try
    {
        var id = (long)(await command.ExecuteScalarAsync() ?? 0L);
        await WriteAuditAsync(db, user.Id, "area.workshop.create", $"workshop={id}", context);
        return Results.Created($"/api/workshops/{id}", new { id, name = request.Name.Trim(), code = request.Code.Trim(), status = "active" });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "车间编码已存在" }); }
});

app.MapPut("/api/workshops/{id:long}", async (long id, HttpContext context, BusinessUpdateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 128) || !ValidBusinessText(request.Code, 64) || request.Status is not ("active" or "disabled")) return Results.BadRequest(new { error = "车间参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("update workshops set name=@name, code=@code, status=@status where id=@id", db);
    command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("code", request.Code.Trim()); command.Parameters.AddWithValue("status", request.Status); command.Parameters.AddWithValue("id", id);
    try
    {
        if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound();
        await WriteAuditAsync(db, user.Id, "area.workshop.update", $"workshop={id},status={request.Status}", context);
        return Results.Ok(new { id, name = request.Name.Trim(), code = request.Code.Trim(), status = request.Status });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "车间编码已存在" }); }
});

app.MapDelete("/api/workshops/{id:long}", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select exists (select 1 from workshops where id=@id), (select count(*) from areas where workshop_id=@id), (select count(*) from user_scopes where scope_type='workshop' and scope_id=@id) + (select count(*) from role_scopes where scope_type='workshop' and scope_id=@id)", db);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var exists = reader.GetBoolean(0);
    var childCount = reader.GetInt64(1);
    var scopeCount = reader.GetInt64(2);
    await reader.CloseAsync();
    if (!exists) return Results.NotFound();
    if (childCount > 0) return Results.Conflict(new { error = $"车间下还有 {childCount} 个区域，请先删除区域" });
    if (scopeCount > 0) return Results.Conflict(new { error = $"车间仍被 {scopeCount} 条数据范围引用，请先移除授权范围" });
    await using var delete = new NpgsqlCommand("delete from workshops where id=@id", db);
    delete.Parameters.AddWithValue("id", id);
    await delete.ExecuteNonQueryAsync();
    await WriteAuditAsync(db, user.Id, "area.workshop.delete", $"workshop={id}", context);
    return Results.NoContent();
});

app.MapGet("/api/areas", async (HttpContext context, NpgsqlConnection db, long? workshopId) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand(workshopId is null
        ? "select id, workshop_id, name, code, status from areas order by name"
        : "select id, workshop_id, name, code, status from areas where workshop_id = @workshop order by name", db);
    if (workshopId is not null) command.Parameters.AddWithValue("workshop", workshopId.Value);
    return Results.Ok(await ReadRowsAsync(command, 5));
});

app.MapPost("/api/areas", async (HttpContext context, BusinessCreateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 128) || !ValidBusinessText(request.Code, 64) || request.WorkshopId is null or <= 0) return Results.BadRequest(new { error = "区域参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var parent = new NpgsqlCommand("select 1 from workshops where id=@id and status='active'", db); parent.Parameters.AddWithValue("id", request.WorkshopId.Value);
    if (await parent.ExecuteScalarAsync() is null) return Results.BadRequest(new { error = "车间不存在或已停用" });
    await using var command = new NpgsqlCommand("insert into areas(workshop_id,name,code) values (@parent,@name,@code) returning id", db);
    command.Parameters.AddWithValue("parent", request.WorkshopId.Value); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("code", request.Code.Trim());
    try
    {
        var id = (long)(await command.ExecuteScalarAsync() ?? 0L); await WriteAuditAsync(db, user.Id, "area.create", $"area={id}", context);
        return Results.Created($"/api/areas/{id}", new { id, workshopId = request.WorkshopId.Value, name = request.Name.Trim(), code = request.Code.Trim(), status = "active" });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "同一车间下区域编码已存在" }); }
});

app.MapPut("/api/areas/{id:long}", async (long id, HttpContext context, BusinessUpdateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 128) || !ValidBusinessText(request.Code, 64) || request.Status is not ("active" or "disabled")) return Results.BadRequest(new { error = "区域参数无效" });
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("update areas set name=@name, code=@code, status=@status where id=@id", db);
    command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("code", request.Code.Trim()); command.Parameters.AddWithValue("status", request.Status); command.Parameters.AddWithValue("id", id);
    try
    {
        if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound(); await WriteAuditAsync(db, user.Id, "area.update", $"area={id},status={request.Status}", context); return Results.Ok(new { id, name = request.Name.Trim(), code = request.Code.Trim(), status = request.Status });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "同一车间下区域编码已存在" }); }
});

app.MapDelete("/api/areas/{id:long}", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select exists (select 1 from areas where id=@id), (select count(*) from units where area_id=@id), (select count(*) from user_scopes where scope_type='area' and scope_id=@id) + (select count(*) from role_scopes where scope_type='area' and scope_id=@id)", db);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var exists = reader.GetBoolean(0);
    var childCount = reader.GetInt64(1);
    var scopeCount = reader.GetInt64(2);
    await reader.CloseAsync();
    if (!exists) return Results.NotFound();
    if (childCount > 0) return Results.Conflict(new { error = $"区域下还有 {childCount} 个机组，请先删除机组" });
    if (scopeCount > 0) return Results.Conflict(new { error = $"区域仍被 {scopeCount} 条数据范围引用，请先移除授权范围" });
    await using var delete = new NpgsqlCommand("delete from areas where id=@id", db);
    delete.Parameters.AddWithValue("id", id);
    await delete.ExecuteNonQueryAsync();
    await WriteAuditAsync(db, user.Id, "area.delete", $"area={id}", context);
    return Results.NoContent();
});

app.MapGet("/api/units", async (HttpContext context, NpgsqlConnection db, long? areaId) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand(areaId is null
        ? "select id, area_id, name, code, status from units order by name"
        : "select id, area_id, name, code, status from units where area_id = @area order by name", db);
    if (areaId is not null) command.Parameters.AddWithValue("area", areaId.Value);
    return Results.Ok(await ReadRowsAsync(command, 5));
});

app.MapPost("/api/units", async (HttpContext context, BusinessCreateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 128) || !ValidBusinessText(request.Code, 64) || request.AreaId is null or <= 0) return Results.BadRequest(new { error = "机组参数无效" });
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var parent = new NpgsqlCommand("select 1 from areas where id=@id and status='active'", db); parent.Parameters.AddWithValue("id", request.AreaId.Value);
    if (await parent.ExecuteScalarAsync() is null) return Results.BadRequest(new { error = "区域不存在或已停用" });
    await using var command = new NpgsqlCommand("insert into units(area_id,name,code) values (@parent,@name,@code) returning id", db);
    command.Parameters.AddWithValue("parent", request.AreaId.Value); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("code", request.Code.Trim());
    try
    {
        var id = (long)(await command.ExecuteScalarAsync() ?? 0L); await WriteAuditAsync(db, user.Id, "area.unit.create", $"unit={id}", context);
        return Results.Created($"/api/units/{id}", new { id, areaId = request.AreaId.Value, name = request.Name.Trim(), code = request.Code.Trim(), status = "active" });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "同一区域下机组编码已存在" }); }
});

app.MapPut("/api/units/{id:long}", async (long id, HttpContext context, BusinessUpdateRequest request, NpgsqlConnection db) =>
{
    if (!ValidBusinessText(request.Name, 128) || !ValidBusinessText(request.Code, 64) || request.Status is not ("active" or "disabled")) return Results.BadRequest(new { error = "机组参数无效" });
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("update units set name=@name, code=@code, status=@status where id=@id", db);
    command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("code", request.Code.Trim()); command.Parameters.AddWithValue("status", request.Status); command.Parameters.AddWithValue("id", id);
    try
    {
        if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound(); await WriteAuditAsync(db, user.Id, "area.unit.update", $"unit={id},status={request.Status}", context); return Results.Ok(new { id, name = request.Name.Trim(), code = request.Code.Trim(), status = request.Status });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505") { return Results.Conflict(new { error = "同一区域下机组编码已存在" }); }
});

app.MapDelete("/api/units/{id:long}", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync(); var user = await AuthenticateAsync(context, db); if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "area.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select exists (select 1 from units where id=@id), (select count(*) from channels where unit_id=@id), (select count(*) from user_scopes where scope_type='unit' and scope_id=@id) + (select count(*) from role_scopes where scope_type='unit' and scope_id=@id)", db);
    command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var exists = reader.GetBoolean(0);
    var channelCount = reader.GetInt64(1);
    var scopeCount = reader.GetInt64(2);
    await reader.CloseAsync();
    if (!exists) return Results.NotFound();
    if (channelCount > 0) return Results.Conflict(new { error = $"机组下还有 {channelCount} 个通道，请先解除通道分配" });
    if (scopeCount > 0) return Results.Conflict(new { error = $"机组仍被 {scopeCount} 条数据范围引用，请先移除授权范围" });
    await using var delete = new NpgsqlCommand("delete from units where id=@id", db);
    delete.Parameters.AddWithValue("id", id);
    await delete.ExecuteNonQueryAsync();
    await WriteAuditAsync(db, user.Id, "area.unit.delete", $"unit={id}", context);
    return Results.NoContent();
});

app.MapGet("/api/channels/unassigned", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "channel.read")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select c.id, c.device_id, c.device_channel, c.name, c.model, c.status from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id where c.unit_id is null and c.status <> 'disabled' and " + ChannelScopePredicate, db);
    command.Parameters.AddWithValue("user", user.Id);
    return Results.Ok(await ReadRowsAsync(command, 6));
});

app.MapPut("/api/channels/{id:long}/unit", async (long id, HttpContext context, NpgsqlConnection db, UnitAssignmentRequest request) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "channel.assign")) return Results.Forbid();
    if (!await HasChannelAccessByIdAsync(db, user.Id, id)) return Results.Forbid();
    if (request.UnitId is not null)
    {
        await using var unitCheck = new NpgsqlCommand("select 1 from units where id = @unit and status = 'active'", db);
        unitCheck.Parameters.AddWithValue("unit", request.UnitId.Value);
        if (await unitCheck.ExecuteScalarAsync() is null)
            return Results.BadRequest(new { error = "机组不存在或已停用" });
    }
    await using var update = new NpgsqlCommand("update channels set unit_id = @unit where id = @id and status <> 'disabled'", db);
    update.Parameters.AddWithValue("unit", (object?)request.UnitId ?? DBNull.Value);
    update.Parameters.AddWithValue("id", id);
    if (await update.ExecuteNonQueryAsync() == 0)
        return Results.NotFound();
    await WriteAuditAsync(db, user.Id, "channel.assign", $"channel={id},unit={request.UnitId?.ToString() ?? "null"}", context);
    return Results.Ok(new { id, unitId = request.UnitId });
});

app.MapGet("/api/access-scopes/{target}/{id:long}", async (string target, long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    var permission = target.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user.manage" : "role.manage";
    if (!await HasPermissionForUserAsync(db, user.Id, permission)) return Results.Forbid();
    var table = target.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user_scopes" : target.Equals("role", StringComparison.OrdinalIgnoreCase) ? "role_scopes" : null;
    if (table is null) return Results.BadRequest(new { error = "范围目标无效" });
    var keyColumn = table == "user_scopes" ? "user_id" : "role_id";
    await using var command = new NpgsqlCommand($"select scope_type, scope_id from {table} where {keyColumn}=@id order by scope_type, scope_id", db);
    command.Parameters.AddWithValue("id", id);
    var scopes = new List<ScopeGrant>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) scopes.Add(new ScopeGrant(reader.GetString(0), reader.GetInt64(1)));
    return Results.Ok(scopes);
});

app.MapPut("/api/access-scopes/{target}/{id:long}", async (string target, long id, ScopeUpdateRequest request, HttpContext context, NpgsqlConnection db) =>
{
    if (request.Scopes is null || request.Scopes.Length > 500 || request.Scopes.Any(scope => !ValidScopeType(scope.ScopeType) || scope.ScopeId <= 0))
        return Results.BadRequest(new { error = "数据范围参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    var permission = target.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user.manage" : "role.manage";
    if (!await HasPermissionForUserAsync(db, user.Id, permission)) return Results.Forbid();
    var table = target.Equals("user", StringComparison.OrdinalIgnoreCase) ? "user_scopes" : target.Equals("role", StringComparison.OrdinalIgnoreCase) ? "role_scopes" : null;
    if (table is null) return Results.BadRequest(new { error = "范围目标无效" });
    var distinctScopes = request.Scopes.Distinct().ToArray();
    await using var transaction = await db.BeginTransactionAsync();
    var keyColumn = table == "user_scopes" ? "user_id" : "role_id";
    var targetTable = table == "user_scopes" ? "users" : "roles";
    await using var targetCheck = new NpgsqlCommand($"select exists (select 1 from {targetTable} where id=@id)", db, transaction);
    targetCheck.Parameters.AddWithValue("id", id);
    if (!(bool)(await targetCheck.ExecuteScalarAsync() ?? false)) return Results.NotFound();
    foreach (var scope in distinctScopes)
    {
        var existsSql = scope.ScopeType switch
        {
            "channel" => "select exists (select 1 from channels where id=@scope)",
            "unit" => "select exists (select 1 from units where id=@scope)",
            "area" => "select exists (select 1 from areas where id=@scope)",
            "workshop" => "select exists (select 1 from workshops where id=@scope)",
            _ => "select false"
        };
        await using var scopeCheck = new NpgsqlCommand(existsSql, db, transaction);
        scopeCheck.Parameters.AddWithValue("scope", scope.ScopeId);
        if (!(bool)(await scopeCheck.ExecuteScalarAsync() ?? false)) return Results.BadRequest(new { error = "数据范围对象不存在" });
    }
    await using var clear = new NpgsqlCommand($"delete from {table} where {keyColumn}=@id", db, transaction);
    clear.Parameters.AddWithValue("id", id);
    await clear.ExecuteNonQueryAsync();
    foreach (var scope in distinctScopes)
    {
        await using var add = new NpgsqlCommand($"insert into {table}({keyColumn},scope_type,scope_id) values (@id,@type,@scope)", db, transaction);
        add.Parameters.AddWithValue("id", id); add.Parameters.AddWithValue("type", scope.ScopeType); add.Parameters.AddWithValue("scope", scope.ScopeId);
        await add.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
    await WriteAuditAsync(db, user.Id, $"scope.{target.ToLowerInvariant()}.update", $"target={id},count={distinctScopes.Length}", context);
    return Results.NoContent();
});

app.MapGet("/api/desktop-releases", async (HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "desktop.release.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("select id, version, file_name, sha256, file_size, release_notes, minimum_version, force_update, status, download_count, published_at, created_at from desktop_releases order by created_at desc", db);
    var releases = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        releases.Add(new { id = reader.GetInt64(0), version = reader.GetString(1), fileName = reader.GetString(2), sha256 = reader.GetString(3), fileSize = reader.GetInt64(4), releaseNotes = reader.GetString(5), minimumVersion = reader.IsDBNull(6) ? null : reader.GetString(6), forceUpdate = reader.GetBoolean(7), status = reader.GetString(8), downloadCount = reader.GetInt64(9), publishedAt = reader.IsDBNull(10) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(10), createdAt = reader.GetFieldValue<DateTimeOffset>(11) });
    return Results.Ok(releases);
});

app.MapPost("/api/desktop-releases", async (HttpContext context, NpgsqlConnection db) =>
{
    if (!context.Request.HasFormContentType) return Results.BadRequest(new { error = "必须使用 multipart/form-data 上传安装包" });
    var form = await context.Request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var version = form["version"].ToString().Trim();
    var notes = form["releaseNotes"].ToString().Trim();
    var minimumVersion = form["minimumVersion"].ToString().Trim();
    var forceUpdate = string.Equals(form["forceUpdate"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
    if (file is null || file.Length <= 0 || file.Length > 1024L * 1024 * 1024 || !ValidReleaseVersion(version) || (minimumVersion.Length > 0 && !ValidReleaseVersion(minimumVersion)) || notes.Length > 4096)
        return Results.BadRequest(new { error = "版本或安装包参数无效" });
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (extension is not ".exe" and not ".msi" and not ".zip") return Results.BadRequest(new { error = "仅支持 exe、msi 或 zip 安装包" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "desktop.release.manage")) return Results.Forbid();
    Directory.CreateDirectory(releasePath);
    var storageName = $"{Guid.NewGuid():N}{extension}";
    var storagePath = Path.Combine(releasePath, storageName);
    try
    {
        await using (var output = new FileStream(storagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = file.OpenReadStream();
            var buffer = new byte[128 * 1024];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                hash.AppendData(buffer, 0, read);
            }
            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            await output.FlushAsync();
            await using var command = new NpgsqlCommand("insert into desktop_releases(version,file_name,storage_name,sha256,file_size,release_notes,minimum_version,force_update) values (@version,@fileName,@storage,@sha256,@size,@notes,@minimum,@force) returning id", db);
            command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("fileName", Path.GetFileName(file.FileName)); command.Parameters.AddWithValue("storage", storageName); command.Parameters.AddWithValue("sha256", sha256); command.Parameters.AddWithValue("size", file.Length); command.Parameters.AddWithValue("notes", notes); command.Parameters.AddWithValue("minimum", string.IsNullOrEmpty(minimumVersion) ? DBNull.Value : minimumVersion); command.Parameters.AddWithValue("force", forceUpdate);
            var id = (long)(await command.ExecuteScalarAsync() ?? 0L);
            await WriteAuditAsync(db, user.Id, "desktop.release.create", $"release={id},version={version}", context);
            return Results.Created($"/api/desktop-releases/{id}", new { id, version, sha256, fileSize = file.Length, status = "draft" });
        }
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        File.Delete(storagePath);
        return Results.Conflict(new { error = "版本号已存在" });
    }
    catch
    {
        File.Delete(storagePath);
        throw;
    }
});

app.MapPost("/api/desktop-releases/{id:long}/publish", async (long id, ReleasePublishRequest request, HttpContext context, NpgsqlConnection db) =>
{
    if (request.MinimumVersion is not null && request.MinimumVersion.Length > 0 && !ValidReleaseVersion(request.MinimumVersion)) return Results.BadRequest(new { error = "最低支持版本无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "desktop.release.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("update desktop_releases set status='published', minimum_version=@minimum, force_update=@force, published_at=now(), updated_at=now() where id=@id", db);
    command.Parameters.AddWithValue("minimum", string.IsNullOrWhiteSpace(request.MinimumVersion) ? DBNull.Value : request.MinimumVersion.Trim()); command.Parameters.AddWithValue("force", request.ForceUpdate); command.Parameters.AddWithValue("id", id);
    if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound();
    await WriteAuditAsync(db, user.Id, "desktop.release.publish", $"release={id}", context);
    return Results.NoContent();
});

app.MapPost("/api/desktop-releases/{id:long}/revoke", async (long id, HttpContext context, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "desktop.release.manage")) return Results.Forbid();
    await using var command = new NpgsqlCommand("update desktop_releases set status='revoked', updated_at=now() where id=@id", db); command.Parameters.AddWithValue("id", id);
    if (await command.ExecuteNonQueryAsync() == 0) return Results.NotFound();
    await WriteAuditAsync(db, user.Id, "desktop.release.revoke", $"release={id}", context);
    return Results.NoContent();
});

app.MapGet("/api/desktop-releases/latest", async (NpgsqlConnection db, string? currentVersion) =>
{
    await db.OpenAsync();
    await using var command = new NpgsqlCommand("select id, version, file_name, sha256, file_size, release_notes, minimum_version, force_update, published_at from desktop_releases where status='published' order by published_at desc nulls last, id desc limit 1", db);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var version = reader.GetString(1);
    var minimum = reader.IsDBNull(6) ? null : reader.GetString(6);
    var updateAvailable = string.IsNullOrWhiteSpace(currentVersion) || CompareReleaseVersions(currentVersion, version) < 0;
    var force = reader.GetBoolean(7) || (!string.IsNullOrWhiteSpace(currentVersion) && minimum is not null && CompareReleaseVersions(currentVersion, minimum) < 0);
    return Results.Ok(new { id = reader.GetInt64(0), version, fileName = reader.GetString(2), sha256 = reader.GetString(3), fileSize = reader.GetInt64(4), releaseNotes = reader.GetString(5), minimumVersion = minimum, forceUpdate = force, updateAvailable, publishedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8), downloadUrl = $"/api/desktop-releases/{reader.GetInt64(0)}/download" });
});

app.MapGet("/api/desktop-releases/{id:long}/download", async (long id, NpgsqlConnection db) =>
{
    await db.OpenAsync();
    await using var command = new NpgsqlCommand("select file_name, storage_name from desktop_releases where id=@id and status='published'", db); command.Parameters.AddWithValue("id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound();
    var fileName = reader.GetString(0); var storageName = reader.GetString(1); var fullPath = Path.Combine(releasePath, storageName);
    if (!File.Exists(fullPath)) return Results.NotFound();
    await reader.CloseAsync();
    await using var count = new NpgsqlCommand("update desktop_releases set download_count=download_count+1 where id=@id", db); count.Parameters.AddWithValue("id", id); await count.ExecuteNonQueryAsync();
    return Results.File(fullPath, "application/octet-stream", fileName, enableRangeProcessing: true);
});

app.Run();

static object ReadSystemStats(NetworkRateTracker networkRateTracker)
{
    var (load1, load5, load15) = ReadLinuxLoad();
    var (totalMemory, availableMemory) = ReadLinuxMemory();
    var (receivedBytes, transmittedBytes) = ReadLinuxNetwork();
    var (receivedBytesPerSecond, transmittedBytesPerSecond) = networkRateTracker.Calculate(receivedBytes, transmittedBytes);
    var uptimeSeconds = ReadLinuxUptime();
    var rootPath = Path.GetPathRoot(Environment.CurrentDirectory) ?? Path.DirectorySeparatorChar.ToString();
    long? totalDisk = null;
    long? freeDisk = null;
    try
    {
        var drive = new DriveInfo(rootPath);
        if (drive.IsReady)
        {
            totalDisk = drive.TotalSize;
            freeDisk = drive.AvailableFreeSpace;
        }
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }

    var usedMemory = totalMemory is > 0 && availableMemory is not null ? totalMemory - availableMemory : null;
    var usedDisk = totalDisk is > 0 && freeDisk is not null ? totalDisk - freeDisk : null;
    var process = Process.GetCurrentProcess();
    return new
    {
        hostName = Environment.MachineName,
        osDescription = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.OSArchitecture.ToString(),
        processorCount = Environment.ProcessorCount,
        serverTime = DateTimeOffset.UtcNow,
        uptimeSeconds,
        loadAverage = new
        {
            one = load1,
            five = load5,
            fifteen = load15,
            percent = load1 is not null && Environment.ProcessorCount > 0 ? Math.Round(Math.Min(100, load1.Value / Environment.ProcessorCount * 100), 1) : (double?)null
        },
        memory = new
        {
            totalBytes = totalMemory,
            availableBytes = availableMemory,
            usedBytes = usedMemory,
            usedPercent = totalMemory is > 0 && usedMemory is not null ? Math.Round((double)usedMemory.Value / totalMemory.Value * 100, 1) : (double?)null
        },
        disk = new
        {
            path = rootPath,
            totalBytes = totalDisk,
            freeBytes = freeDisk,
            usedBytes = usedDisk,
            usedPercent = totalDisk is > 0 && usedDisk is not null ? Math.Round((double)usedDisk.Value / totalDisk.Value * 100, 1) : (double?)null
        },
        process = new
        {
            workingSetBytes = process.WorkingSet64,
            cpuSeconds = Math.Round(process.TotalProcessorTime.TotalSeconds, 1)
        },
        network = new
        {
            receivedBytes,
            transmittedBytes,
            receivedBytesPerSecond,
            transmittedBytesPerSecond
        }
    };
}

static (double? One, double? Five, double? Fifteen) ReadLinuxLoad()
{
    try
    {
        var values = File.ReadAllText("/proc/loadavg").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (ParseDouble(values, 0), ParseDouble(values, 1), ParseDouble(values, 2));
    }
    catch (IOException) { return (null, null, null); }
    catch (UnauthorizedAccessException) { return (null, null, null); }
}

static double? ParseDouble(string[] values, int index) => index < values.Length && double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

static (long? TotalBytes, long? AvailableBytes) ReadLinuxMemory()
{
    try
    {
        long? total = null;
        long? available = null;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var value)) continue;
            if (parts[0] == "MemTotal:") total = value * 1024;
            if (parts[0] == "MemAvailable:") available = value * 1024;
            if (total is not null && available is not null) break;
        }
        return (total, available);
    }
    catch (IOException) { return (null, null); }
    catch (UnauthorizedAccessException) { return (null, null); }
}

static double? ReadLinuxUptime()
{
    try
    {
        var value = File.ReadAllText("/proc/uptime").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    }
    catch (IOException) { return null; }
    catch (UnauthorizedAccessException) { return null; }
}

static (long? ReceivedBytes, long? TransmittedBytes) ReadLinuxNetwork()
{
    try
    {
        long received = 0, transmitted = 0;
        foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
        {
            var fields = line.Split(':', 2);
            if (fields.Length != 2) continue;
            var values = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 9 || !long.TryParse(values[0], out var rx) || !long.TryParse(values[8], out var tx)) continue;
            received += rx;
            transmitted += tx;
        }
        return (received, transmitted);
    }
    catch (IOException) { return (null, null); }
    catch (UnauthorizedAccessException) { return (null, null); }
}

static IResult ReadDevice(SnapshotStore store)
{
    var snapshot = store.Read();
    if (snapshot is null)
        return Results.Problem("设备快照不存在或无法读取", statusCode: StatusCodes.Status503ServiceUnavailable);
    if (snapshot["status"]?.GetValue<string>() != "ok")
        return Results.Problem("海康适配服务当前不可用", statusCode: StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(snapshot["result"]?["device"]);
}

static IResult ReadChannels(SnapshotStore store)
{
    var snapshot = store.Read();
    if (snapshot is null)
        return Results.Problem("设备快照不存在或无法读取", statusCode: StatusCodes.Status503ServiceUnavailable);
    if (snapshot["status"]?.GetValue<string>() != "ok")
        return Results.Problem("海康适配服务当前不可用", statusCode: StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(snapshot["result"]?["ipChannels"] ?? new JsonArray());
}

static async Task BootstrapAsync(string connectionString, string snapshotPath)
{
    await using var db = new NpgsqlConnection(connectionString);
    await db.OpenAsync();
    await using var check = new NpgsqlCommand("select 1 from information_schema.tables where table_name = 'sessions'", db);
    if (await check.ExecuteScalarAsync() is null)
        throw new InvalidOperationException("数据库未执行迁移：缺少 sessions 表");

    await SyncSnapshotAsync(db, snapshotPath);

    var password = Environment.GetEnvironmentVariable("PLATFORM_BOOTSTRAP_PASSWORD");
    if (string.IsNullOrEmpty(password))
        return;
    var username = Environment.GetEnvironmentVariable("PLATFORM_BOOTSTRAP_USERNAME") ?? "admin";
    var hash = PasswordHasher.Hash(password);
    await using var role = new NpgsqlCommand("insert into roles(name, code) values ('系统管理员', 'admin') on conflict (code) do nothing", db);
    await role.ExecuteNonQueryAsync();
    await using var user = new NpgsqlCommand("insert into users(username, password_hash) values (@username, @hash) on conflict (username) do nothing", db);
    user.Parameters.AddWithValue("username", username);
    user.Parameters.AddWithValue("hash", hash);
    await user.ExecuteNonQueryAsync();
    await using var grant = new NpgsqlCommand("insert into user_roles(user_id, role_id) select u.id, r.id from users u cross join roles r where u.username = @username and r.code = 'admin' on conflict do nothing", db);
    grant.Parameters.AddWithValue("username", username);
    await grant.ExecuteNonQueryAsync();

}

static async Task SyncSnapshotAsync(NpgsqlConnection db, string snapshotPath)
{
    JsonObject? snapshot;
    try
    {
        snapshot = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))?.AsObject();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        return;
    }
    if (snapshot?["status"]?.GetValue<string>() != "ok")
        return;
    var device = snapshot["result"]?["device"]?.AsObject();
    var channels = snapshot["result"]?["ipChannels"]?.AsArray();
    if (device is null || channels is null)
        return;

    var serial = device["serialNumber"]?.GetValue<string>() ?? "unknown";
    var ipText = device["ip"]?.GetValue<string>() ?? Environment.GetEnvironmentVariable("PLATFORM_DEVICE_IP");
    if (!System.Net.IPAddress.TryParse(ipText, out var ip) || ip is null)
        return;
    var servicePort = device["servicePort"]?.GetValue<int>()
        ?? (int.TryParse(Environment.GetEnvironmentVariable("PLATFORM_DEVICE_PORT"), out var configuredPort) ? configuredPort : 8000);
    if (servicePort is < 1 or > 65535)
        return;
    var model = device["model"]?.GetValue<string>() ?? Environment.GetEnvironmentVariable("PLATFORM_DEVICE_MODEL");
    await using var transaction = await db.BeginTransactionAsync();
    await using var upsertDevice = new NpgsqlCommand("insert into devices(device_key, ip, service_port, model, serial_number, status, last_seen_at) values (@key, @ip, @port, @model, @serial, 'online', now()) on conflict (device_key) do update set ip = excluded.ip, service_port = excluded.service_port, model = excluded.model, serial_number = excluded.serial_number, status = excluded.status, last_seen_at = excluded.last_seen_at returning id", db, transaction);
    upsertDevice.Parameters.AddWithValue("key", serial);
    upsertDevice.Parameters.AddWithValue("ip", ip);
    upsertDevice.Parameters.AddWithValue("port", servicePort);
    upsertDevice.Parameters.AddWithValue("model", (object?)model ?? DBNull.Value);
    upsertDevice.Parameters.AddWithValue("serial", serial);
    var deviceId = (long)(await upsertDevice.ExecuteScalarAsync() ?? 0L);
    foreach (var node in channels)
    {
        if (node is not JsonObject channel || channel["channelNumber"] is null)
            continue;
        var number = channel["channelNumber"]!.GetValue<int>();
        var enabled = channel["enabled"]?.GetValue<bool>() ?? false;
        var online = channel["online"]?.GetValue<bool>() ?? false;
        var channelName = channel["name"]?.GetValue<string>();
        var channelModel = channel["model"]?.GetValue<string>();
        bool? ptzCapable = channel["ptzCapable"] is null
            ? LooksLikePtz(channelModel) || LooksLikePtz(channelName) ? true : null
            : channel["ptzCapable"]!.GetValue<bool>();
        await using var upsertChannel = new NpgsqlCommand("insert into channels(device_id, device_channel, name, model, ptz_capable, status) values (@device, @channel, @name, @model, coalesce(@ptz, false), @status) on conflict (device_id, device_channel) do update set name = excluded.name, model = excluded.model, ptz_capable = case when @ptz_present then excluded.ptz_capable else channels.ptz_capable end, status = excluded.status", db, transaction);
        upsertChannel.Parameters.AddWithValue("device", deviceId);
        upsertChannel.Parameters.AddWithValue("channel", number);
        upsertChannel.Parameters.AddWithValue("name", (object?)channel["name"]?.GetValue<string>() ?? DBNull.Value);
        upsertChannel.Parameters.AddWithValue("model", (object?)channel["model"]?.GetValue<string>() ?? DBNull.Value);
        upsertChannel.Parameters.Add("ptz", NpgsqlTypes.NpgsqlDbType.Boolean).Value = (object?)ptzCapable ?? DBNull.Value;
        upsertChannel.Parameters.AddWithValue("ptz_present", ptzCapable.HasValue);
        upsertChannel.Parameters.AddWithValue("status", enabled && online ? "online" : "offline");
        await upsertChannel.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
}

static bool Authorized(HttpContext context, string? expectedKey)
{
    if (string.IsNullOrEmpty(expectedKey))
        return false;
    var provided = Encoding.UTF8.GetBytes(context.Request.Headers["X-Platform-Key"].ToString());
    var expected = Encoding.UTF8.GetBytes(expectedKey);
    return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
}

static string? BearerToken(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
}

static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

static string? QueryParameter(string? query, string name)
{
    if (string.IsNullOrWhiteSpace(query)) return null;
    foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var pieces = part.Split('=', 2);
        if (pieces.Length == 2 && string.Equals(Uri.UnescapeDataString(pieces[0]), name, StringComparison.OrdinalIgnoreCase))
            return Uri.UnescapeDataString(pieces[1]);
    }
    return null;
}

static async Task<CurrentUser?> AuthenticateAsync(HttpContext context, NpgsqlConnection db)
{
    var token = BearerToken(context);
    if (string.IsNullOrEmpty(token))
        return null;
    await using var command = new NpgsqlCommand("select u.id, u.username from sessions s join users u on u.id = s.user_id where s.token_hash = @hash and s.revoked_at is null and s.expires_at > now() and u.status = 'active'", db);
    command.Parameters.AddWithValue("hash", HashToken(token));
    await using var reader = await command.ExecuteReaderAsync();
    return await reader.ReadAsync() ? new CurrentUser(reader.GetInt64(0), reader.GetString(1)) : null;
}

static async Task<bool> HasPermissionAsync(HttpContext context, NpgsqlConnection db, string permission)
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return false;
    await using var command = new NpgsqlCommand("select exists (select 1 from user_roles ur join roles r on r.id = ur.role_id and r.status = 'active' join role_permissions rp on rp.role_id = ur.role_id where ur.user_id = @user and rp.permission_code = @permission)", db);
    command.Parameters.AddWithValue("user", user.Id);
    command.Parameters.AddWithValue("permission", permission);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<IResult> ForwardPtzAsync(int channel, PtzControlRequest request, bool stop, HttpContext context, NpgsqlConnection db, HttpClient client, string adapterUrl, string? adapterKey)
{
    var command = request.Command switch
    {
        "up" => 21u,
        "down" => 22u,
        "left" => 23u,
        "right" => 24u,
        "auto" => 29u,
        "zoomIn" => 11u,
        "zoomOut" => 12u,
        "focusNear" => 13u,
        "focusFar" => 14u,
        "irisOpen" => 15u,
        "irisClose" => 16u,
        "stop" => 21u,
        _ => 0u
    };
    if (channel <= 0 || command == 0 || request.Speed is < 1 or > 7)
        return Results.BadRequest(new { error = "PTZ 参数无效" });
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null)
        return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, "ptz.control"))
        return Results.Forbid();
    if (!await HasChannelAccessAsync(db, user.Id, channel))
        return Results.Forbid();
    if (!await HasPtzCapabilityAsync(db, user.Id, channel))
        return Results.BadRequest(new { error = "该通道不支持云台控制" });
    if (string.IsNullOrWhiteSpace(adapterKey))
        return Results.Problem("适配服务控制密钥未配置", statusCode: 503);

    var payload = JsonSerializer.Serialize(new { channel, command, stop, speed = (uint)request.Speed });
    using var message = new HttpRequestMessage(HttpMethod.Post, $"{adapterUrl.TrimEnd('/')}/ptz")
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json")
    };
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        await WriteAuditAsync(db, user.Id, stop ? "ptz.stop" : "ptz.start", $"channel={channel},command={request.Command},speed={request.Speed}", context);
        return response.IsSuccessStatusCode
            ? Results.Content(body, "application/json", Encoding.UTF8)
            : Results.Problem("适配服务拒绝 PTZ 命令", statusCode: 502);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
}

static async Task<IResult> ForwardPlaybackAsync(HttpMethod method, string path, object? payload, HttpClient client, string adapterUrl, string? adapterKey, Func<string, string>? transform = null, Action? onSuccess = null)
{
    if (string.IsNullOrWhiteSpace(adapterKey))
        return Results.Problem("适配服务控制密钥未配置", statusCode: 503);
    using var message = new HttpRequestMessage(method, $"{adapterUrl.TrimEnd('/')}{path}");
    if (payload is not null)
        message.Content = JsonContent.Create(payload);
    message.Headers.Add("X-Adapter-Key", adapterKey);
    try
    {
        using var response = await client.SendAsync(message);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            onSuccess?.Invoke();
            return Results.NoContent();
        }
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
            onSuccess?.Invoke();
        return response.IsSuccessStatusCode
            ? Results.Content(transform?.Invoke(body) ?? body, "application/json", Encoding.UTF8)
            : Results.Problem("适配服务拒绝回放请求", statusCode: 502);
    }
    catch (HttpRequestException)
    {
        return Results.Problem("适配服务不可达", statusCode: 503);
    }
}

static string WithPlaybackUrls(string body, Guid id, int channel, string stream, string token, DateTimeOffset expiresAt, HttpContext context)
{
    var result = JsonNode.Parse(body)?.AsObject() ?? new JsonObject();
    var (host, scheme) = PublicMediaEndpoint(context);
    result["id"] = id;
    result["channel"] = channel;
    result["stream"] = stream;
    result["expiresAt"] = expiresAt;
    result["rtspUrl"] = $"rtsp://{host}:18554/playback/{stream}?token={token}";
    result["httpFlvUrl"] = $"{scheme}://{host}/media/playback/{stream}.live.flv?token={token}";
    result["hlsUrl"] = $"{scheme}://{host}/media/playback/{stream}/hls.m3u8?token={token}";
    return result.ToJsonString();
}

static object WithLiveUrls(Guid id, int channel, string stream, int streamType, string token, DateTimeOffset expiresAt, HttpContext context)
{
    var (host, scheme) = PublicMediaEndpoint(context);
    return new
    {
        id,
        channel,
        streamType,
        stream,
        expiresAt,
        rtspUrl = $"rtsp://{host}:18554/live/{stream}?token={token}",
        httpFlvUrl = $"{scheme}://{host}/media/live/{stream}.live.flv?token={token}",
        hlsUrl = $"{scheme}://{host}/media/live/{stream}/hls.m3u8?token={token}"
    };
}

static (string Host, string Scheme) PublicMediaEndpoint(HttpContext context)
{
    var host = Environment.GetEnvironmentVariable("PUBLIC_MEDIA_HOST")?.Trim();
    if (string.IsNullOrWhiteSpace(host))
        host = context.Request.Host.Host;
    var configuredScheme = Environment.GetEnvironmentVariable("PUBLIC_MEDIA_SCHEME")?.Trim().ToLowerInvariant();
    var forwardedScheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault()?.Trim().ToLowerInvariant();
    var scheme = configuredScheme is "http" or "https"
        ? configuredScheme
        : forwardedScheme is "http" or "https" ? forwardedScheme : context.Request.Scheme;
    return (host, scheme);
}

static bool LooksLikePtz(string? value) =>
    !string.IsNullOrWhiteSpace(value)
    && (value.Contains("ptz", StringComparison.OrdinalIgnoreCase)
        || value.Contains("dome", StringComparison.OrdinalIgnoreCase)
        || value.Contains("2dc", StringComparison.OrdinalIgnoreCase)
        || value.Contains("球机", StringComparison.OrdinalIgnoreCase)
        || value.Contains("云台", StringComparison.OrdinalIgnoreCase));

static async Task<bool> HasPermissionForUserAsync(NpgsqlConnection db, long userId, string permission)
{
    await using var command = new NpgsqlCommand("select exists (select 1 from user_roles ur join roles r on r.id = ur.role_id and r.status = 'active' join role_permissions rp on rp.role_id = ur.role_id where ur.user_id = @user and rp.permission_code = @permission)", db);
    command.Parameters.AddWithValue("user", userId);
    command.Parameters.AddWithValue("permission", permission);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<bool> HasChannelAccessAsync(NpgsqlConnection db, long userId, int channel)
{
    await using var command = new NpgsqlCommand("select exists (select 1 from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id where c.device_channel=@channel and c.status <> 'disabled' and " + ChannelScopePredicate + ")", db);
    command.Parameters.AddWithValue("user", userId);
    command.Parameters.AddWithValue("channel", channel);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<bool> HasPtzCapabilityAsync(NpgsqlConnection db, long userId, int channel)
{
    await using var command = new NpgsqlCommand("select exists (select 1 from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id where c.device_channel=@channel and c.ptz_capable and c.status <> 'disabled' and " + ChannelScopePredicate + ")", db);
    command.Parameters.AddWithValue("user", userId);
    command.Parameters.AddWithValue("channel", channel);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<bool> HasChannelAccessByIdAsync(NpgsqlConnection db, long userId, long channelId)
{
    await using var command = new NpgsqlCommand("select exists (select 1 from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id where c.id=@channelId and c.status <> 'disabled' and " + ChannelScopePredicate + ")", db);
    command.Parameters.AddWithValue("user", userId);
    command.Parameters.AddWithValue("channelId", channelId);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<HashSet<int>> AllowedChannelNumbersAsync(NpgsqlConnection db, long userId)
{
    await using var command = new NpgsqlCommand("select distinct c.device_channel from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id where c.status <> 'disabled' and " + ChannelScopePredicate, db);
    command.Parameters.AddWithValue("user", userId);
    var result = new HashSet<int>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) result.Add(reader.GetInt32(0));
    return result;
}

static bool ValidScopeType(string value) => value is "workshop" or "area" or "unit" or "channel";
static bool ValidReleaseVersion(string value) => value.Length is > 0 and <= 32 && Version.TryParse(value, out _);
static int CompareReleaseVersions(string left, string right)
{
    if (!Version.TryParse(left, out var a) || !Version.TryParse(right, out var b)) return 0;
    return new Version(a.Major, a.Minor, Math.Max(0, a.Build), Math.Max(0, a.Revision))
        .CompareTo(new Version(b.Major, b.Minor, Math.Max(0, b.Build), Math.Max(0, b.Revision)));
}

static async Task WriteAuditAsync(NpgsqlConnection db, long userId, string action, string summary, HttpContext context)
{
    await using var command = new NpgsqlCommand("insert into audit_logs(user_id, action, resource, parameter_summary, client_ip) values (@user, @action, @resource, @summary, @ip)", db);
    command.Parameters.AddWithValue("user", userId);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("resource", action.Split('.', 2)[0]);
    command.Parameters.AddWithValue("summary", summary);
    command.Parameters.AddWithValue("ip", context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback);
    await command.ExecuteNonQueryAsync();
}

static async Task<IResult> ReadBusinessRowsAsync(HttpContext context, NpgsqlConnection db, string sql, string permission)
{
    await db.OpenAsync();
    var user = await AuthenticateAsync(context, db);
    if (user is null) return Results.Unauthorized();
    if (!await HasPermissionForUserAsync(db, user.Id, permission)) return Results.Forbid();
    await using var command = new NpgsqlCommand(sql, db);
    return Results.Ok(await ReadRowsAsync(command, 4));
}

static async Task<List<object>> ReadRowsAsync(NpgsqlCommand command, int columns)
{
    var rows = new List<object>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var values = new object?[columns];
        for (var i = 0; i < columns; i++)
            values[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
        rows.Add(values);
    }
    return rows;
}

static async Task<string[]> PermissionsAsync(NpgsqlConnection db, long userId)
{
    await using var command = new NpgsqlCommand("select rp.permission_code from user_roles ur join roles r on r.id = ur.role_id and r.status = 'active' join role_permissions rp on rp.role_id = ur.role_id where ur.user_id = @user order by rp.permission_code", db);
    command.Parameters.AddWithValue("user", userId);
    var values = new List<string>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        values.Add(reader.GetString(0));
    return values.ToArray();
}

static bool ValidBusinessText(string? value, int maxLength) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;
static bool ValidOptionalText(string? value, int maxLength) => value is null || value.Trim().Length <= maxLength;

internal sealed record LoginRequest(string Username, string Password);
internal sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
internal sealed record UserProfileRequest(string? DisplayName, string? Phone);
internal sealed record CurrentUser(long Id, string Username);
internal sealed record PtzControlRequest(string Command, int Speed = 4);
internal sealed record RecordingSearchRequest(int Channel, DateTimeOffset Start, DateTimeOffset End);
internal sealed record PlaybackStartRequest(int Channel, DateTimeOffset Start, DateTimeOffset End);
internal sealed record PlaybackControlRequest(string Action, int? Position = null);
internal sealed record LiveStartRequest(int Channel, int StreamType = 2);
internal sealed record LiveSessionGrant(long UserId, int Channel, string Stream, int StreamType, string Token, DateTimeOffset ExpiresAt);
internal sealed record PlaybackSessionGrant(long UserId, int Channel, string Stream, string Token, DateTimeOffset ExpiresAt);
internal sealed record AckRequest(string? Note);
internal sealed record UnitAssignmentRequest(long? UnitId);
internal sealed record UserCreateRequest(string Username, string Password, string? DisplayName = null, string? Phone = null);
internal sealed record UserUpdateRequest(string Username, string? DisplayName, string? Phone, string Status, string? Password = null);
internal sealed record UserRoleRequest(long? RoleId);
internal sealed record RoleCreateRequest(string Name, string Code, string Status = "active");
internal sealed record RoleUpdateRequest(string Name, string Code, string Status);
internal sealed record RolePermissionsRequest(string[] Codes);
internal sealed record ScopeGrant(string ScopeType, long ScopeId);
internal sealed record ScopeUpdateRequest(ScopeGrant[] Scopes);
internal sealed record ReleasePublishRequest(string? MinimumVersion, bool ForceUpdate = false);
internal sealed record BusinessCreateRequest(string Name, string Code, long? WorkshopId = null, long? AreaId = null);
internal sealed record BusinessUpdateRequest(string Name, string Code, string Status);

#pragma warning disable CS0618
internal sealed class PlatformStatusAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public PlatformStatusAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
#pragma warning restore CS0618

internal static class PasswordHasher
{
    private const int Iterations = 120_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed class SnapshotStore
{
    private readonly string _path;

    public SnapshotStore(string path) => _path = path;

    public JsonObject? Read()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(_path))?.AsObject();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

internal sealed class NetworkRateTracker
{
    private readonly object _gate = new();
    private long? _received;
    private long? _transmitted;
    private DateTimeOffset? _sampledAt;

    public (double? ReceivedBytesPerSecond, double? TransmittedBytesPerSecond) Calculate(long? received, long? transmitted)
    {
        if (received is null || transmitted is null) return (null, null);
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = _sampledAt is null ? 0 : (now - _sampledAt.Value).TotalSeconds;
            var rxRate = elapsed > 0 && _received is not null ? Math.Max(0, (received.Value - _received.Value) / elapsed) : (double?)null;
            var txRate = elapsed > 0 && _transmitted is not null ? Math.Max(0, (transmitted.Value - _transmitted.Value) / elapsed) : (double?)null;
            _received = received;
            _transmitted = transmitted;
            _sampledAt = now;
            return (rxRate, txRate);
        }
    }
}

internal sealed class OnlineTrendStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public OnlineTrendStore(string path) => _path = path;

    public IReadOnlyList<OnlineTrendPoint> Record(long online, long total)
    {
        lock (_gate)
        {
            var points = Read();
            var hour = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
            hour = new DateTimeOffset(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, TimeSpan.Zero);
            var index = points.FindIndex(item => item.Timestamp == hour);
            var point = new OnlineTrendPoint(hour, online, total);
            if (index >= 0) points[index] = point;
            else points.Add(point);
            points = points.OrderBy(item => item.Timestamp).TakeLast(24).ToList();
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, JsonSerializer.Serialize(points));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"在线趋势写入失败：{ex.Message}");
            }
            return points;
        }
    }

    private List<OnlineTrendPoint> Read()
    {
        try
        {
            return JsonSerializer.Deserialize<List<OnlineTrendPoint>>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }
}

internal sealed record OnlineTrendPoint(DateTimeOffset Timestamp, long Online, long Total);

internal sealed class AlarmIngestor : BackgroundService
{
    private readonly string _connectionString;
    private readonly string _path;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<AlarmHub> _hub;
    private int _lineCount;

    public AlarmIngestor(string connectionString, string path, Microsoft.AspNetCore.SignalR.IHubContext<AlarmHub> hub)
    {
        _connectionString = connectionString;
        _path = path;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ImportAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NpgsqlException or JsonException)
            {
                Console.Error.WriteLine($"报警入库失败：{ex.Message}");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return;
        var lines = await File.ReadAllLinesAsync(_path, cancellationToken);
        if (lines.Length < _lineCount)
            _lineCount = 0;
        if (lines.Length == _lineCount)
            return;

        await using var db = new NpgsqlConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var events = new List<(int? Channel, object Payload)>();
        for (var i = _lineCount; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            using var document = JsonDocument.Parse(lines[i]);
            var root = document.RootElement;
            var receivedAt = root.GetProperty("receivedAt").GetDateTimeOffset();
            var command = root.GetProperty("command").GetInt32();
            var payloadBase64 = root.GetProperty("payloadBase64").GetString() ?? string.Empty;
            byte[] payload;
            try { payload = Convert.FromBase64String(payloadBase64); }
            catch (FormatException) { payload = Encoding.UTF8.GetBytes(lines[i]); }
            var eventType = root.TryGetProperty("eventType", out var eventTypeNode) && eventTypeNode.ValueKind == JsonValueKind.String
                ? eventTypeNode.GetString()
                : null;
            var alarmType = root.TryGetProperty("alarmType", out var alarmTypeNode) && alarmTypeNode.ValueKind == JsonValueKind.Number
                ? alarmTypeNode.GetUInt32()
                : (uint?)null;
            var channels = root.TryGetProperty("channels", out var channelsNode) && channelsNode.ValueKind == JsonValueKind.Array
                ? channelsNode.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number).Select(item => item.GetInt32()).Where(item => item > 0).Distinct().Take(512).ToArray()
                : Array.Empty<int>();
            var channel = channels.FirstOrDefault();
            var isRecovery = root.TryGetProperty("isRecovery", out var recoveryNode) && recoveryNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? recoveryNode.GetBoolean()
                : (bool?)null;
            DateTimeOffset? alarmTime = null;
            if (root.TryGetProperty("alarmTime", out var alarmTimeNode) && alarmTimeNode.ValueKind == JsonValueKind.String && alarmTimeNode.TryGetDateTimeOffset(out var parsedAlarmTime))
                alarmTime = parsedAlarmTime;
            byte[]? image = null;
            if (root.TryGetProperty("imageBase64", out var imageNode) && imageNode.ValueKind == JsonValueKind.String)
            {
                try { image = Convert.FromBase64String(imageNode.GetString() ?? string.Empty); }
                catch (FormatException) { image = null; }
            }
            var normalizedType = eventType ?? $"sdk:{command}";
            var normalizedPayload = JsonSerializer.Serialize(new
            {
                command,
                eventType = normalizedType,
                alarmType,
                channel = channel == 0 ? (int?)null : channel,
                channels,
                isRecovery,
                alarmTime,
                imageLength = image?.Length ?? 0,
                imageUrl = root.TryGetProperty("imageUrl", out var imageUrlNode) && imageUrlNode.ValueKind == JsonValueKind.String ? imageUrlNode.GetString() : null,
                payloadLength = payload.Length
            });
            await using var insert = new NpgsqlCommand("insert into alarm_events(device_id, channel_id, event_type, occurred_at, state, normalized_payload, raw_payload, image_payload, event_hash) values ((select id from devices order by id limit 1), (select id from channels where device_id = (select id from devices order by id limit 1) and device_channel = @channel limit 1), @type, @occurred, case when @recovery then 'resolved' else 'new' end, cast(@payload as jsonb), @raw, @image, @hash) on conflict (event_hash) do nothing", db, transaction);
            insert.Parameters.AddWithValue("channel", channel == 0 ? DBNull.Value : channel);
            insert.Parameters.AddWithValue("type", normalizedType);
            insert.Parameters.AddWithValue("occurred", alarmTime ?? receivedAt);
            insert.Parameters.AddWithValue("recovery", isRecovery ?? false);
            insert.Parameters.AddWithValue("payload", normalizedPayload);
            insert.Parameters.AddWithValue("raw", payload);
            insert.Parameters.AddWithValue("image", (object?)image ?? DBNull.Value);
            insert.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lines[i]))));
            if (await insert.ExecuteNonQueryAsync(cancellationToken) > 0)
                events.Add((channel == 0 ? null : channel, new { eventType = normalizedType, occurredAt = alarmTime ?? receivedAt, channel = channel == 0 ? (int?)null : channel, isRecovery, imageAvailable = image is { Length: > 0 } }));
        }
        await transaction.CommitAsync(cancellationToken);
        _lineCount = lines.Length;
        foreach (var item in events)
        {
            var recipients = await ReadAlarmRecipientsAsync(item.Channel, cancellationToken);
            foreach (var sessionHash in recipients)
                await _hub.Clients.Group(AlarmHub.SessionGroup(sessionHash)).SendAsync("alarm", item.Payload, cancellationToken);
        }
    }

    private async Task<string[]> ReadAlarmRecipientsAsync(int? channel, CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(_connectionString);
        await db.OpenAsync(cancellationToken);
        var channelFilter = channel is null
            ? " and not exists(select 1 from user_scopes us where us.user_id=u.id) and not exists(select 1 from role_scopes rs join user_roles ur on ur.role_id=rs.role_id where ur.user_id=u.id)"
            : " and exists (select 1 from channels c left join units un on un.id=c.unit_id left join areas a on a.id=un.area_id left join workshops w on w.id=a.workshop_id where c.device_channel=@channel and c.status <> 'disabled' and ((not exists (select 1 from user_scopes us0 where us0.user_id=u.id) and not exists (select 1 from role_scopes rs0 join user_roles ur0 on ur0.role_id=rs0.role_id join roles r0 on r0.id=rs0.role_id and r0.status='active' where ur0.user_id=u.id)) or exists (select 1 from user_scopes us where us.user_id=u.id and ((us.scope_type='channel' and us.scope_id=c.id) or (us.scope_type='unit' and us.scope_id=c.unit_id) or (us.scope_type='area' and us.scope_id=un.area_id) or (us.scope_type='workshop' and us.scope_id=a.workshop_id))) or exists (select 1 from role_scopes rs join user_roles ur on ur.role_id=rs.role_id join roles r on r.id=rs.role_id and r.status='active' where ur.user_id=u.id and ((rs.scope_type='channel' and rs.scope_id=c.id) or (rs.scope_type='unit' and rs.scope_id=c.unit_id) or (rs.scope_type='area' and rs.scope_id=un.area_id) or (rs.scope_type='workshop' and rs.scope_id=a.workshop_id)))))";
        await using var command = new NpgsqlCommand("select distinct s.token_hash from users u join sessions s on s.user_id=u.id and s.revoked_at is null and s.expires_at>now() where u.status='active' and exists (select 1 from user_roles ur join roles r on r.id=ur.role_id and r.status='active' join role_permissions rp on rp.role_id=ur.role_id and rp.permission_code='alarm.read' where ur.user_id=u.id)" + channelFilter, db);
        if (channel is not null) command.Parameters.AddWithValue("channel", channel.Value);
        var recipients = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) recipients.Add(reader.GetString(0));
        return recipients.ToArray();
    }
}

internal sealed class MediaSessionReaper(
    ConcurrentDictionary<Guid, LiveSessionGrant> live,
    ConcurrentDictionary<Guid, PlaybackSessionGrant> playback,
    HttpClient client, string adapterUrl, string? adapterKey) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var pair in live.ToArray())
                {
                    if (pair.Value.ExpiresAt > DateTimeOffset.UtcNow || !((ICollection<KeyValuePair<Guid, LiveSessionGrant>>)live).Remove(pair)) continue;
                    if (!await StopAsync(HttpMethod.Post, "/live/stop", new { channel = pair.Value.Channel, streamType = pair.Value.StreamType, sessionId = pair.Key }, stoppingToken)) live.TryAdd(pair.Key, pair.Value);
                }
                foreach (var pair in playback.ToArray())
                {
                    if (pair.Value.ExpiresAt > DateTimeOffset.UtcNow || !((ICollection<KeyValuePair<Guid, PlaybackSessionGrant>>)playback).Remove(pair)) continue;
                    if (!await StopAsync(HttpMethod.Delete, $"/playback/{pair.Key}?userId={pair.Value.UserId}", null, stoppingToken)) playback.TryAdd(pair.Key, pair.Value);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task<bool> StopAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, $"{adapterUrl.TrimEnd('/')}{path}");
            request.Headers.Add("X-Adapter-Key", adapterKey);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await client.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Console.Error.WriteLine($"过期媒体会话清理失败，将重试：{ex.Message}");
            return false;
        }
    }
}

internal abstract class AuthorizedHub : Microsoft.AspNetCore.SignalR.Hub
{
    private readonly string _permission;
    protected AuthorizedHub(string permission) => _permission = permission;

    protected abstract string GroupName { get; }
    protected virtual string? ScopedGroup(long userId) => null;

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var token = http?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = http?.Request.Query["access_token"].ToString();
        token = token?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? token[7..].Trim() : token?.Trim();
        var connectionString = Environment.GetEnvironmentVariable("PLATFORM_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(connectionString))
        {
            Context.Abort();
            return;
        }
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await using var userCommand = new NpgsqlCommand("select u.id from sessions s join users u on u.id=s.user_id where s.token_hash=@hash and s.revoked_at is null and s.expires_at>now() and u.status='active'", db);
        userCommand.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        var userId = await userCommand.ExecuteScalarAsync();
        if (userId is null)
        {
            Context.Abort();
            return;
        }
        await using var permissionCommand = new NpgsqlCommand("select exists (select 1 from user_roles ur join roles r on r.id=ur.role_id and r.status='active' join role_permissions rp on rp.role_id=ur.role_id where ur.user_id=@user and rp.permission_code=@permission)", db);
        permissionCommand.Parameters.AddWithValue("user", (long)userId);
        permissionCommand.Parameters.AddWithValue("permission", _permission);
        if (!(bool)(await permissionCommand.ExecuteScalarAsync() ?? false))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
        if (ScopedGroup((long)userId) is { } scopedGroup)
            await Groups.AddToGroupAsync(Context.ConnectionId, scopedGroup);
        if (this is AlarmHub)
            await Groups.AddToGroupAsync(Context.ConnectionId, AlarmHub.SessionGroup(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))));
        await base.OnConnectedAsync();
    }
}

internal sealed class AlarmHub : AuthorizedHub
{
    public static string SessionGroup(string hash) => $"alarm-session:{hash}";
    public const string ReadersGroup = "alarm-readers";
    public static string UserGroup(long userId) => $"alarm-reader:{userId}";
    protected override string GroupName => ReadersGroup;
    protected override string ScopedGroup(long userId) => UserGroup(userId);
    public AlarmHub() : base("alarm.read") { }
}

internal sealed class DeviceStatusHub : AuthorizedHub
{
    protected override string GroupName => "device-readers";
    public DeviceStatusHub() : base("device.read") { }
}

internal sealed class MediaHub : AuthorizedHub
{
    protected override string GroupName => "media-viewers";
    public MediaHub() : base("live.view") { }
}

internal sealed class DeviceStatusBroadcaster : BackgroundService
{
    private readonly SnapshotStore _store;
    private readonly IHubContext<DeviceStatusHub> _hub;
    private string? _lastSnapshot;

    public DeviceStatusBroadcaster(SnapshotStore store, IHubContext<DeviceStatusHub> hub)
    {
        _store = store;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = _store.Read();
                var current = snapshot?.ToJsonString();
                if (snapshot is not null && !string.IsNullOrWhiteSpace(current) && current != _lastSnapshot)
                {
                    _lastSnapshot = current;
                    await _hub.Clients.Group("device-readers").SendAsync("deviceStatus", new { updatedAt = DateTimeOffset.UtcNow }, stoppingToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.Error.WriteLine($"设备状态推送失败：{ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
