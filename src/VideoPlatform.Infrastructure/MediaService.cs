using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed class MediaService(Database db, AccessService access, IDeviceAdapter adapter, ISettingsStore settingsStore,
    SecretStore secrets, PlatformOptions options, IHttpClientFactory clients, AuditStore audit, ILogger<MediaService> logger)
{
    public async Task<JsonObject> StartAsync(Actor actor, long channelId, string kind, int streamType, string profile,
        DateTimeOffset? start = null, DateTimeOffset? end = null, CancellationToken ct = default)
    {
        Rules.Require(kind is "live" or "playback" && streamType is 1 or 2 && profile is "native" or "browser", "媒体参数无效");
        var channel = await access.ChannelAsync(actor, channelId, kind == "live" ? "live.view" : "playback.view", ct);
        Rules.Require(channel.Text("status") == "online", "通道当前离线", "channel.offline", 409);
        if (kind == "playback")
        {
            Rules.TimeRange(start!.Value, end!.Value);
            // Npgsql 的 timestamptz 只接受 UTC，接口允许带时区偏移的 ISO 8601 时间。
            start = start.Value.ToUniversalTime();
            end = end.Value.ToUniversalTime();
        }
        var settings = await settingsStore.ReadAsync(ct);
        var id = Guid.NewGuid();
        var token = Passwords.Token();
        var deviceId = channel.Id("deviceId");
        await db.TransactionAsync(async tx =>
        {
            // 以数据库事务串行预留配额，多个 API 进程也不会同时越过上限。
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002002)", ct: ct);
            var counts = await tx.OneAsync("select count(*) filter(where user_id=@userId and kind=@kind) as own,count(*) filter(where device_id=@deviceId and kind='playback') as device,count(*) filter(where kind='playback') as playback from media_sessions where closed_at is null and expires_at>now()", new { actor.UserId, deviceId, kind }, ct);
            Rules.Require(counts.Id("own") < (kind == "live" ? settings.LivePerUser : settings.PlaybackPerUser), "已达到个人播放窗口上限", "media.userQuota", 429);
            if (kind == "playback") Rules.Require(counts.Id("device") < settings.PlaybackPerDevice && counts.Id("playback") < settings.PlaybackGlobal, "已达到回放并发上限", "media.playbackQuota", 429);
            await tx.ExecuteAsync("insert into media_sessions(id,user_id,auth_session_id,device_id,channel_id,kind,stream_type,profile,token_hash,token_cipher,start_at,end_at,expires_at) values(@id,@userId,@authSession,@deviceId,@channelId,@kind,@streamType,@profile,@hash,@cipher,@start,@end,now()+interval '3 minutes')", new { id, actor.UserId, authSession = actor.SessionId, deviceId, channelId, kind, streamType, profile, hash = Passwords.TokenHash(token), cipher = secrets.Protect(token), start, end }, ct);
            return true;
        }, ct);
        try
        {
            var payload = kind == "live"
                ? (object)new { sessionId = id, channel = (int)channel.Id("deviceChannel"), streamType, profile }
                : new { sessionId = id, userId = actor.UserId, channel = (int)channel.Id("deviceChannel"), start, end, profile };
            var state = await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{deviceId}/{kind}", payload, ct) as JsonObject ?? new JsonObject();
            Rules.Require(!string.IsNullOrWhiteSpace(state.Text("stream")), "设备未提供媒体流", "media.stream", 502);
            await db.TransactionAsync(async tx =>
            {
                await tx.ExecuteAsync("select pg_advisory_xact_lock(72002002)", ct: ct);
                if (state.Flag("transcoded"))
                {
                    var transcodes = await tx.OneAsync("select count(distinct stream) as count from media_sessions where transcoded and closed_at is null and expires_at>now() and stream<>@stream", new { stream = state.Text("stream") }, ct);
                    Rules.Require(transcodes.Id("count") < settings.TranscodeGlobal, "兼容转码资源已满，请使用桌面端或稍后重试", "media.transcodeQuota", 429);
                }
                var updated = await tx.ExecuteAsync("update media_sessions set state=@state,stream=@stream,transcoded=@transcoded,codec=@codec,stream_type=@actualType where id=@id and closed_at is null and exists(select 1 from sessions where id=@authId and revoked_at is null and expires_at>now())", new { id, state = state.Text("state", "playing"), stream = state.Text("stream"), transcoded = state.Flag("transcoded"), codec = state.Text("codec", "unknown"), actualType = state.Id("streamType") > 0 ? (int)state.Id("streamType") : streamType, authId = actor.SessionId }, ct);
                Rules.Require(updated == 1, "媒体请求已取消或登录已失效", "media.cancelled", 409);
                return true;
            }, ct);
            await access.ChannelAsync(actor, channelId, kind == "live" ? "live.view" : "playback.view", ct);
            var row = (await db.OneAsync("select * from media_sessions where id=@id", new { id }, ct))!;
            await audit.WriteAsync(actor.UserId, $"{kind}.start", channelId.ToString(), $"建立媒体会话 {id}");
            return Grant(row, state);
        }
        catch
        {
            await StopInternalAsync(id, CancellationToken.None);
            throw;
        }
    }

    public async Task<JsonObject> GetAsync(Actor actor, Guid id, bool renew = false, CancellationToken ct = default)
    {
        var row = await OwnedAsync(actor, id, ct);
        await access.ChannelAsync(actor, row.Id("channelId"), row.Text("kind") == "live" ? "live.view" : "playback.view", ct);
        if (renew)
        {
            row = await db.OneAsync("update media_sessions set expires_at=now()+interval '3 minutes' where id=@id and closed_at is null and expires_at>now() returning *", new { id }, ct)
                ?? throw new PlatformException(404, "media.missing", "媒体会话已结束，不能续期");
        }
        JsonObject? state = null;
        if (row.Text("kind") == "playback")
            state = await adapter.SendAsync(HttpMethod.Get, $"/internal/devices/{row.Id("deviceId")}/playback/{id}", ct: ct) as JsonObject;
        return Grant(row, state);
    }

    public async Task<JsonObject> ControlAsync(Actor actor, Guid id, PlaybackControlRequest request, CancellationToken ct = default)
    {
        var row = await OwnedAsync(actor, id, ct);
        Rules.Require(row.Text("kind") == "playback", "此会话不是回放会话");
        await access.ChannelAsync(actor, row.Id("channelId"), "playback.view", ct);
        Rules.Require(request.Action is "pause" or "resume" or "seek" or "speed", "回放命令无效");
        if (request.Action == "seek") Rules.Require(request.Position is { } position && position >= row.Time("startAt") && position <= row.Time("endAt"), "定位时间超出录像范围");
        if (request.Action == "speed") Rules.Require(request.Speed is 0.25 or 0.5 or 1 or 2 or 4 or 8, "倍速只支持 0.25、0.5、1、2、4、8");
        var state = await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{row.Id("deviceId")}/playback/{id}/control", request, ct) as JsonObject;
        return Grant(row, state);
    }

    private async Task<JsonObject> OwnedAsync(Actor actor, Guid id, CancellationToken ct)
        => await db.OneAsync("select * from media_sessions where id=@id and user_id=@userId and auth_session_id=@authId and closed_at is null and expires_at>now()", new { id, actor.UserId, authId = actor.SessionId }, ct)
            ?? throw new PlatformException(404, "media.missing", "媒体会话已结束或不存在");

    public async Task StopAsync(Actor actor, Guid id, CancellationToken ct = default)
    {
        var row = await db.OneAsync("select user_id from media_sessions where id=@id", new { id }, ct);
        if (row is null) return;
        Rules.Require(row.Id("userId") == actor.UserId, "不能停止其他用户的媒体会话", "media.owner", 403);
        await StopInternalAsync(id, ct);
    }

    public async Task StopInternalAsync(Guid id, CancellationToken ct = default, DateTimeOffset? expectedExpiry = null)
    {
        var row = await db.OneAsync("update media_sessions set closed_at=coalesce(closed_at,now()),state='stopping' where id=@id and state<>'stopped' and (closed_at is not null or @expectedExpiry::timestamptz is null or expires_at<=@expectedExpiry) returning *", new { id, expectedExpiry }, ct);
        if (row is null) return;
        var viewers = await db.QueryAsync("select connection_id from viewer_connections where media_session_id=@id", new { id }, ct);
        var failed = false;
        foreach (var viewer in viewers)
        {
            try
            {
                await ZlmAsync("kick_session", new { id = viewer.Text("connectionId") }, ct);
                await db.ExecuteAsync("delete from viewer_connections where connection_id=@id", new { id = viewer.Text("connectionId") }, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or PlatformException or TaskCanceledException) { logger.LogWarning("播放连接 {ConnectionId} 关闭失败，将重试", viewer.Text("connectionId")); failed = true; }
        }
        try { await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{row.Id("deviceId")}/{row.Text("kind")}/{id}", ct: ct); }
        catch (PlatformException ex) when (ex.Status == 404) { }
        catch (Exception ex) when (ex is PlatformException or TaskCanceledException) { logger.LogWarning("媒体会话 {SessionId} 清理失败，将重试：{Reason}", id, ex.Message); failed = true; }
        if (!failed) await db.ExecuteAsync("update media_sessions set state='stopped' where id=@id", new { id }, ct);
        await audit.NotifyAsync("media.changed", id.ToString(), row.Id("userId"), row.Id("channelId"), ct: ct);
    }

    public JsonObject Grant(JsonObject row, JsonObject? state = null)
    {
        var stream = state?.Text("stream", row.Text("stream")) ?? row.Text("stream");
        var token = Uri.EscapeDataString(secrets.Unprotect(row.Text("tokenCipher")));
        var app = row.Text("kind") == "live" ? "live" : "playback";
        var path = $"{app}/{Uri.EscapeDataString(stream)}";
        var result = state?.DeepClone() as JsonObject ?? new JsonObject();
        result["id"] = row["id"]?.DeepClone();
        result["channelId"] = row["channelId"]?.DeepClone();
        result["streamType"] = row["streamType"]?.DeepClone();
        result["expiresAt"] = row["expiresAt"]?.DeepClone();
        result["state"] ??= row["state"]?.DeepClone();
        result["codec"] ??= row.Flag("transcoded") ? "h264" : row.Text("codec", "unknown");
        // 回放在创建响应之后才完成编码探测，优先返回适配器的当前状态。
        result["transcoded"] ??= row.Flag("transcoded");
        result["rtspUrl"] = $"{options.RtspsBase}/{path}?token={token}";
        result["httpFlvUrl"] = $"{options.PublicBase}/media/{path}.live.flv?token={token}";
        result["httpTsUrl"] = $"{options.PublicBase}/media/{path}.live.ts?token={token}";
        result["hlsUrl"] = "";
        if (app == "playback")
        {
            result["start"] = row["startAt"]?.DeepClone();
            result["end"] = row["endAt"]?.DeepClone();
            result["currentTime"] ??= row["startAt"]?.DeepClone();
            result["progress"] ??= 0;
            result["speed"] ??= 1;
            result["segments"] ??= new JsonArray();
        }
        return result;
    }

    public async Task<bool> AuthorizePlaybackAsync(string token, string stream, string app, string? connectionId, CancellationToken ct = default)
    {
        if (token.Length is < 32 or > 256 || app is not ("live" or "playback")) return false;
        return await db.TransactionAsync(async tx =>
        {
            var row = await tx.OneAsync("select m.* from media_sessions m join sessions s on s.id=m.auth_session_id where m.token_hash=@hash and m.stream=@stream and m.kind=@app and m.closed_at is null and m.expires_at>now() and s.revoked_at is null and s.expires_at>now() for update of m", new { hash = Passwords.TokenHash(token), stream, app }, ct);
            if (row is null || !await access.HasPermissionAsync(row.Id("userId"), app == "live" ? "live.view" : "playback.view", ct) || !await access.CanChannelAsync(row.Id("userId"), row.Id("channelId"), ct)) return false;
            if (!string.IsNullOrWhiteSpace(connectionId))
                await tx.ExecuteAsync("insert into viewer_connections(connection_id,media_session_id) values(@connectionId,@id) on conflict(connection_id) do update set media_session_id=excluded.media_session_id", new { connectionId, id = Guid.Parse(row.Text("id")) }, ct);
            return true;
        }, ct);
    }

    public async Task<JsonNode?> ZlmAsync(string action, object data, CancellationToken ct = default)
    {
        var values = System.Text.Json.JsonSerializer.SerializeToNode(data, JsonDefaults.Options)!.AsObject();
        values["secret"] = options.ZlmSecret;
        // ZLMediaKit 的表单接口需要明确的内容长度，避免分块 JSON 请求等待正文结束。
        using var body = new FormUrlEncodedContent(values.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value?.ToString() ?? "")));
        using var response = await clients.CreateClient("zlm").PostAsync($"{options.ZlmUrl.TrimEnd('/')}/index/api/{action}", body, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        if (result.Id("code") != 0)
        {
            if (action == "kick_session")
            {
                var active = await ZlmAsync("getAllSession", new { }, ct);
                if (active?["data"] is JsonArray connections && !connections.OfType<JsonObject>().Any(connection => connection.Text("id") == values.Text("id")))
                    return result;
            }
            throw new PlatformException(502, "media.control", "媒体服务操作未成功");
        }
        return result;
    }

    public async Task RevokeAsync(long userId, Guid? sessionId = null, CancellationToken ct = default)
    {
        var sessions = await db.QueryAsync("select id from media_sessions where user_id=@userId and closed_at is null and (@sessionId::uuid is null or auth_session_id=@sessionId)", new { userId, sessionId }, ct);
        foreach (var session in sessions) await StopInternalAsync(Guid.Parse(session.Text("id")), ct);
        var ptzs = await db.QueryAsync("select channel_id from ptz_leases where user_id=@userId and (@sessionId::uuid is null or auth_session_id=@sessionId)", new { userId, sessionId }, ct);
        foreach (var ptz in ptzs) await StopPtzInternalAsync(ptz.Id("channelId"), ct);
        var jobs = await db.QueryAsync("update export_jobs set state='cancelled',error='账号权限或会话已变化' where user_id=@userId and state in('queued','running') and (@sessionId::uuid is null or auth_session_id=@sessionId) returning id,device_id", new { userId, sessionId }, ct);
        foreach (var job in jobs)
        {
            try { await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{job.Id("deviceId")}/exports/{job.Text("id")}", ct: ct); }
            catch (PlatformException ex) { logger.LogWarning("撤销导出 {JobId} 时适配器不可用：{Reason}", job.Text("id"), ex.Message); }
        }
        await audit.NotifyAsync("access.changed", userId.ToString(), userId, ct: ct);
    }

    public async Task PtzAsync(Actor actor, long channelId, PtzRequest request, CancellationToken ct = default)
    {
        string[] commands = ["up", "down", "left", "right", "auto", "zoomIn", "zoomOut", "focusNear", "focusFar", "irisOpen", "irisClose"];
        Rules.Require(commands.Contains(request.Command) && request.Speed is >= 1 and <= 7, "云台命令或速度无效");
        var channel = await access.ChannelAsync(actor, channelId, "ptz.control", ct);
        Rules.Require(channel.Flag("ptzCapable"), "该通道不支持云台控制", "ptz.unsupported", 409);
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(@channelId)", new { channelId }, ct);
            var lease = await tx.OneAsync("select * from ptz_leases where channel_id=@channelId for update", new { channelId }, ct);
            Rules.Require(lease is null || lease.Time("expiresAt") <= DateTimeOffset.UtcNow || lease.Text("authSessionId") == actor.SessionId.ToString(), "其他值班员正在控制此云台", "ptz.busy", 409);
            await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{channel.Id("deviceId")}/ptz", new { channel = (int)channel.Id("deviceChannel"), command = request.Command, speed = request.Speed, stop = false }, ct);
            await tx.ExecuteAsync("insert into ptz_leases(channel_id,user_id,auth_session_id,command,speed,expires_at) values(@channelId,@userId,@authId,@command,@speed,now()+interval '10 seconds') on conflict(channel_id) do update set user_id=excluded.user_id,auth_session_id=excluded.auth_session_id,command=excluded.command,speed=excluded.speed,expires_at=excluded.expires_at", new { channelId, actor.UserId, authId = actor.SessionId, request.Command, request.Speed }, ct);
            if (lease is null || lease.Text("command") != request.Command)
                await tx.ExecuteAsync("insert into audit_logs(user_id,action,resource,summary) values(@userId,'ptz.start',@resource,@summary)", new { actor.UserId, resource = channelId.ToString(), summary = $"云台命令：{request.Command}，速度：{request.Speed}" }, ct);
            return true;
        }, ct);
    }

    public async Task StopPtzAsync(Actor actor, long channelId, CancellationToken ct = default)
    {
        var lease = await db.OneAsync("select auth_session_id from ptz_leases where channel_id=@channelId", new { channelId }, ct);
        if (lease is null) return;
        Rules.Require(lease.Text("authSessionId") == actor.SessionId.ToString(), "不能终止其他值班员的云台操作", "ptz.owner", 403);
        await StopPtzInternalAsync(channelId, ct);
    }

    public async Task StopPtzInternalAsync(long channelId, CancellationToken ct = default, DateTimeOffset? expectedExpiry = null)
    {
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(@channelId)", new { channelId }, ct);
            var lease = await tx.OneAsync("select p.*,c.device_id,c.device_channel from ptz_leases p join channels c on c.id=p.channel_id where p.channel_id=@channelId for update of p", new { channelId }, ct);
            if (lease is null) return true;
            if (expectedExpiry is { } expected && lease.Time("expiresAt") > expected) return true;
            await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{lease.Id("deviceId")}/ptz", new { channel = (int)lease.Id("deviceChannel"), command = lease.Text("command"), speed = (int)lease.Id("speed"), stop = true }, ct);
            await tx.ExecuteAsync("delete from ptz_leases where channel_id=@channelId", new { channelId }, ct);
            await tx.ExecuteAsync("insert into audit_logs(user_id,action,resource,summary) values(@userId,'ptz.stop',@resource,'云台停止')", new { userId = lease.Id("userId"), resource = channelId.ToString() }, ct);
            return true;
        }, ct);
    }
}
