using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("视频与云台");
        group.MapPost("/live-sessions", async (LiveRequest request, HttpContext context, MediaService media) => Results.Ok(await media.StartAsync(ApiSupport.Actor(context), request.ChannelId, "live", request.StreamType, request.Profile, ct: context.RequestAborted))).WithName("StartLive");
        group.MapPost("/playback-sessions", async (PlaybackRequest request, HttpContext context, MediaService media) => Results.Ok(await media.StartAsync(ApiSupport.Actor(context), request.ChannelId, "playback", 1, request.Profile, request.Start, request.End, context.RequestAborted))).WithName("StartPlayback");
        foreach (var kind in new[] { "live", "playback" })
        {
            group.MapGet($"/{kind}-sessions/{{id:guid}}", async (Guid id, HttpContext context, MediaService media) => Results.Ok(await media.GetAsync(ApiSupport.Actor(context), id, ct: context.RequestAborted))).WithName($"Get{kind}Session");
            group.MapPost($"/{kind}-sessions/{{id:guid}}/renew", async (Guid id, HttpContext context, MediaService media) => Results.Ok(await media.GetAsync(ApiSupport.Actor(context), id, true, context.RequestAborted))).WithName($"Renew{kind}Session");
            group.MapDelete($"/{kind}-sessions/{{id:guid}}", async (Guid id, HttpContext context, MediaService media) =>
            {
                await media.StopAsync(ApiSupport.Actor(context), id, context.RequestAborted);
                return Results.NoContent();
            }).WithName($"Stop{kind}Session");
        }
        group.MapPost("/recordings/search", async (RecordingRequest request, HttpContext context, AccessService access, IDeviceAdapter adapter) =>
        {
            Rules.TimeRange(request.Start, request.End, 7);
            var channel = await access.ChannelAsync(ApiSupport.Actor(context), request.ChannelId, "playback.view", context.RequestAborted);
            return Results.Ok(await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{channel.Id("deviceId")}/recordings/search", new { channel = (int)channel.Id("deviceChannel"), request.Start, request.End }, context.RequestAborted));
        }).WithName("SearchRecordings");
        group.MapPost("/playback-sessions/{id:guid}/control", async (Guid id, PlaybackControlRequest request, HttpContext context, MediaService media) => Results.Ok(await media.ControlAsync(ApiSupport.Actor(context), id, request, context.RequestAborted))).WithName("ControlPlayback");
        group.MapPost("/channels/{id:long}/ptz", async (long id, PtzRequest request, HttpContext context, MediaService media) =>
        {
            await media.PtzAsync(ApiSupport.Actor(context), id, request, context.RequestAborted);
            return Results.NoContent();
        }).WithName("StartPtz");
        group.MapPost("/channels/{id:long}/ptz/stop", async (long id, HttpContext context, MediaService media) =>
        {
            await media.StopPtzAsync(ApiSupport.Actor(context), id, context.RequestAborted);
            return Results.NoContent();
        }).WithName("StopPtz");
        group.MapPost("/channels/{id:long}/ptz/presets/{preset:int}", async (long id, int preset, HttpContext context, AccessService access, Database db, IDeviceAdapter adapter, AuditStore audit) =>
        {
            Rules.Require(preset is >= 1 and <= 300, "预置位编号必须为 1～300");
            var actor = ApiSupport.Actor(context);
            var channel = await access.ChannelAsync(actor, id, "ptz.control");
            Rules.Require(channel.Flag("ptzCapable"), "该通道不支持云台控制", "ptz.unsupported", 409);
            await db.TransactionAsync(async tx =>
            {
                await tx.ExecuteAsync("select pg_advisory_xact_lock(@id)", new { id });
                var lease = await tx.OneAsync("select auth_session_id from ptz_leases where channel_id=@id and expires_at>now()", new { id });
                Rules.Require(lease is null || lease.Text("authSessionId") == actor.SessionId.ToString(), "其他值班员正在控制云台", "ptz.busy", 409);
                await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{channel.Id("deviceId")}/ptz/preset", new { channel = (int)channel.Id("deviceChannel"), preset });
                return true;
            });
            await audit.WriteAsync(actor.UserId, "ptz.preset", id.ToString(), $"调用预置位：{preset}", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("CallPtzPreset");
        app.MapPost("/internal/zlm/on-play", async (JsonObject payload, HttpContext context, MediaService media, PlatformOptions options) =>
        {
            if (!InternalAllowed(context, options)) return Results.Json(new { code = -1, msg = "内部接口拒绝访问" });
            var token = QueryHelpers.ParseQuery(payload.Text("params")).TryGetValue("token", out var value) ? value.ToString() : "";
            if (IPAddress.TryParse(payload.Text("ip"), out var source) && IPAddress.IsLoopback(source) &&
                payload.Text("stream").StartsWith("vp2_", StringComparison.Ordinal) &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)), SHA256.HashData(Encoding.UTF8.GetBytes(options.AdapterKey))))
                return Results.Json(new { code = 0, msg = "允许内部媒体处理" });
            var allowed = await media.AuthorizePlaybackAsync(token, payload.Text("stream"), payload.Text("app"), payload["id"]?.ToString(), context.RequestAborted);
            return Results.Json(new { code = allowed ? 0 : -1, msg = allowed ? "允许播放" : "播放令牌无效或权限已撤销" });
        }).ExcludeFromDescription();
        app.MapPost("/internal/zlm/on-stream-changed", (HttpContext context, PlatformOptions options) => Results.Json(new { code = InternalAllowed(context, options) ? 0 : -1 })).ExcludeFromDescription();
    }

    private static bool InternalAllowed(HttpContext context, PlatformOptions options)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null || !IPAddress.IsLoopback(ip)) return false;
        var actual = Encoding.UTF8.GetBytes(context.Request.Query["key"].ToString());
        var expected = Encoding.UTF8.GetBytes(options.AdapterKey);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
