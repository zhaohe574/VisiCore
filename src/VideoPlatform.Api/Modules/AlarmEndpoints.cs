using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class AlarmEndpoints
{
    public const string Columns = "e.id,e.device_id,d.name as device_name,e.channel_id,c.name as channel_name,e.event_type,e.occurred_at,e.state,e.recovered,e.owner_id,owner.display_name as owner_name,e.note,(e.image_payload is not null) as image_available,e.payload,e.version";
    public const string From = "alarm_events e join devices d on d.id=e.device_id left join channels c on c.id=e.channel_id left join units un on un.id=c.unit_id left join areas ar on ar.id=un.parent_id left join users owner on owner.id=e.owner_id";
    public const string Allowed = "((e.channel_id is not null and (" + AccessService.ChannelPredicate + ")) or (e.channel_id is null and exists(select 1 from users au where au.id=@userId and au.status='active' and (au.all_channels or exists(select 1 from user_roles ur join roles r on r.id=ur.role_id where ur.user_id=au.id and r.status='active' and (r.all_channels or r.code='admin'))))))";

    public static void MapAlarmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/alarms").RequireAuthorization().WithTags("报警中心");
        group.MapGet("", async (HttpContext context, Database db, AccessService access, string? state, long? deviceId, long? channelId, string? eventType, DateTimeOffset? from, DateTimeOffset? to) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "alarm.read");
            var (_, size, offset) = ApiSupport.Pagination(context);
            var where = $"({Allowed}) and (@state::text is null or e.state=@state) and (@deviceId::bigint is null or e.device_id=@deviceId) and (@channelId::bigint is null or e.channel_id=@channelId) and (@eventType::text is null or e.event_type=@eventType) and (@from::timestamptz is null or e.occurred_at>=@from) and (@to::timestamptz is null or e.occurred_at<=@to) and (e.event_type ilike @search or c.name ilike @search or d.name ilike @search)";
            var args = new { actor.UserId, state = string.IsNullOrEmpty(state) ? null : state, deviceId, channelId, eventType = string.IsNullOrEmpty(eventType) ? null : eventType, from, to, size, offset, search = $"%{ApiSupport.Search(context)}%" };
            var rows = await db.QueryAsync($"select {Columns} from {From} where {where} order by e.occurred_at desc,e.id desc limit @size offset @offset", args);
            var count = await db.OneAsync($"select count(*) as count from {From} where {where}", args);
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListAlarms");
        group.MapGet("/{id:long}", async (long id, HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "alarm.read");
            var row = await db.OneAsync($"select {Columns} from {From} where e.id=@id and ({Allowed})", new { id, actor.UserId }) ?? throw new PlatformException(404, "alarm.missing", "报警不存在或不可访问");
            var history = await db.QueryAsync("select h.id,h.action,h.note,u.username,h.created_at from alarm_history h left join users u on u.id=h.user_id where h.alarm_id=@id order by h.created_at,h.id", new { id });
            row["history"] = new JsonArray(history.Select(h => (JsonNode)h).ToArray());
            return Results.Ok(row);
        }).WithName("GetAlarm");
        group.MapGet("/{id:long}/image", async (long id, HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "alarm.read");
            var row = await db.OneAsync($"select e.image_payload from {From} where e.id=@id and ({Allowed})", new { id, actor.UserId });
            if (row?["imagePayload"] is null) return Results.NotFound();
            var bytes = Convert.FromBase64String(row.Text("imagePayload"));
            return Results.File(bytes, bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg");
        }).WithName("GetAlarmImage");
        group.MapPost("/{id:long}/actions", async (long id, AlarmActionRequest request, HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "alarm.ack");
            Rules.Require((request.Note?.Length ?? 0) <= 4096, "处理备注不能超过 4096 个字符");
            if (request.Action is "note" or "close") Rules.Text(request.Note, "处理备注", 4096);
            var admin = await access.IsAdministratorAsync(actor.UserId);
            var updated = await db.TransactionAsync(async tx =>
            {
                var row = await tx.OneAsync($"select e.* from {From} where e.id=@id and ({Allowed}) for update of e", new { id, actor.UserId }) ?? throw new PlatformException(404, "alarm.missing", "报警不存在或不可访问");
                if (request.Action is "close") Rules.Require(admin || row.Id("ownerId") == actor.UserId, "只有处理人或管理员可以关闭报警", "alarm.owner", 403);
                var next = Rules.AlarmTransition(row.Text("state"), request.Action);
                await tx.ExecuteAsync("update alarm_events set state=@next,owner_id=case when @action='claim' then @userId when @action='reopen' then null else owner_id end,note=case when @note::text is null then note else @note end,version=version+1 where id=@id", new { next, action = request.Action, actor.UserId, request.Note, id });
                await tx.ExecuteAsync("insert into alarm_history(alarm_id,user_id,action,note) values(@id,@userId,@action,@note)", new { id, actor.UserId, action = request.Action, note = request.Note ?? "" });
                await tx.ExecuteAsync("insert into audit_logs(user_id,action,resource,summary) values(@userId,@action,@resource,@summary)", new { actor.UserId, action = "alarm." + request.Action, resource = id.ToString(), summary = request.Note ?? request.Action });
                await tx.ExecuteAsync("insert into outbox(kind,resource_id,channel_id,device_id,version) select 'alarm.changed',id::text,channel_id,device_id,version from alarm_events where id=@id", new { id });
                return new { id, state = next };
            });
            return Results.Ok(updated);
        }).WithName("ActOnAlarm");
    }
}
