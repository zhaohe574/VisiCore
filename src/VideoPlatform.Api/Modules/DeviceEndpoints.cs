using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class DeviceEndpoints
{
    public const string DeviceColumns = "d.id,d.name,d.host,d.port,d.username,d.enabled,d.status,d.model,d.serial_number,d.last_seen_at,(select count(*) from channels where device_id=d.id and status<>'disabled') as channel_count,(select count(*) from channels where device_id=d.id and status='online') as online_channels";

    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("录像机与通道");
        group.MapGet("/devices", async (HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device.read");
            var (page, size, offset) = ApiSupport.Pagination(context);
            var managed = await access.HasPermissionAsync(actor.UserId, "device.manage");
            var filter = $"(outer_device.name ilike @search or outer_device.host ilike @search) and (@managed or exists(select 1 from {AccessService.ChannelFrom} where c.device_id=outer_device.id and ({AccessService.ChannelPredicate})))";
            var args = new { search = $"%{ApiSupport.Search(context)}%", managed, actor.UserId, size, offset };
            var rows = await db.QueryAsync($"select {DeviceColumns.Replace("d.", "outer_device.")} from devices outer_device where {filter} order by outer_device.id limit @size offset @offset", args);
            var count = await db.OneAsync($"select count(*) as count from devices outer_device where {filter}", args);
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListDevices");
        group.MapGet("/devices/{id:long}", async (long id, HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device.read");
            if (!await access.HasPermissionAsync(actor.UserId, "device.manage"))
                Rules.Require(await db.OneAsync($"select c.id from {AccessService.ChannelFrom} where c.device_id=@id and ({AccessService.ChannelPredicate}) limit 1", new { id, actor.UserId }) is not null, "录像机不在授权范围内", "device.denied", 403);
            return Results.Ok(await db.OneAsync($"select {DeviceColumns} from devices d where d.id=@id", new { id }) ?? throw new PlatformException(404, "device.missing", "录像机不存在"));
        }).Produces<DeviceDto>().WithName("GetDevice");
        group.MapPost("/devices", async (DeviceRequest request, HttpContext context, Database db, AccessService access, SecretStore secrets, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
            Validate(request, true);
            var row = await db.OneAsync("insert into devices(name,host,port,username,password_cipher,enabled) values(@name,@host,@port,@username,@password,@enabled) returning id", new { name = request.Name.Trim(), host = request.Host.Trim(), request.Port, username = request.Username.Trim(), password = secrets.Protect(request.Password!), request.Enabled });
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "device.create", row.Id().ToString(), $"添加录像机：{request.Name}", ApiSupport.Ip(context));
            return Results.Created($"/api/v2/devices/{row.Id()}", row);
        }).WithName("CreateDevice");
        group.MapPut("/devices/{id:long}", async (long id, DeviceRequest request, HttpContext context, Database db, AccessService access, SecretStore secrets, MediaService media, IDeviceAdapter adapter, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
            Validate(request, false);
            var existing = await db.OneAsync("select password_cipher from devices where id=@id", new { id }) ?? throw new PlatformException(404, "device.missing", "录像机不存在");
            var active = await db.QueryAsync("select id from media_sessions where device_id=@id and closed_at is null", new { id });
            foreach (var session in active) await media.StopInternalAsync(Guid.Parse(session.Text("id")));
            var leases = await db.QueryAsync("select p.channel_id from ptz_leases p join channels c on c.id=p.channel_id where c.device_id=@id", new { id });
            foreach (var lease in leases) await media.StopPtzInternalAsync(lease.Id("channelId"));
            await db.ExecuteAsync("update devices set name=@name,host=@host,port=@port,username=@username,password_cipher=@password,enabled=@enabled,status='unknown' where id=@id", new { id, name = request.Name.Trim(), host = request.Host.Trim(), request.Port, username = request.Username.Trim(), password = string.IsNullOrEmpty(request.Password) ? existing.Text("passwordCipher") : secrets.Protect(request.Password), request.Enabled });
            try { await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{id}"); } catch (PlatformException ex) when (ex.Status == 404) { }
            if (!request.Enabled) await db.ExecuteAsync("update channels set status='disabled' where device_id=@id", new { id });
            await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id);
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "device.update", id.ToString(), $"修改录像机：{request.Name}", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("UpdateDevice");
        group.MapDelete("/devices/{id:long}", async (long id, HttpContext context, Database db, AccessService access, MediaService media, IDeviceAdapter adapter, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
            var active = await db.QueryAsync("select id from media_sessions where device_id=@id and closed_at is null", new { id });
            foreach (var session in active) await media.StopInternalAsync(Guid.Parse(session.Text("id")));
            await db.ExecuteAsync("update devices set enabled=false,status='disabled' where id=@id; update channels set status='disabled' where device_id=@id", new { id });
            try { await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{id}"); } catch (PlatformException ex) when (ex.Status == 404) { }
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "device.disable", id.ToString(), "停用录像机并保留历史记录", ApiSupport.Ip(context));
            await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id);
            return Results.NoContent();
        }).WithName("DisableDevice");
        foreach (var action in new[] { "test", "sync" })
            group.MapPost($"/devices/{{id:long}}/{action}", async (long id, HttpContext context, AccessService access, DeviceService devices) =>
            {
                await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
                var snapshot = await devices.SyncAsync(id, context.RequestAborted);
                return Results.Ok(new { success = true, channels = (snapshot["channels"] as JsonArray)?.Count ?? 0 });
            }).WithName(action == "test" ? "TestDevice" : "SyncDevice");
        group.MapGet("/channels", async (HttpContext context, Database db, AccessService access, long? deviceId, long? unitId, bool? online) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "channel.read");
            var (_, size, offset) = ApiSupport.Pagination(context);
            var where = $"c.status<>'disabled' and d.enabled and ({AccessService.ChannelPredicate}) and (@deviceId::bigint is null or c.device_id=@deviceId) and (@unitId::bigint is null or c.unit_id=@unitId) and (@online::boolean is null or (c.status='online')=@online) and (c.name ilike @search or d.name ilike @search or c.device_channel::text ilike @search)";
            var args = new { actor.UserId, deviceId, unitId, online, search = $"%{ApiSupport.Search(context)}%", size, offset };
            var rows = await db.QueryAsync($"select {AccessService.ChannelColumns} from {AccessService.ChannelFrom} where {where} order by d.id,c.device_channel limit @size offset @offset", args);
            var count = await db.OneAsync($"select count(*) as count from {AccessService.ChannelFrom} where {where}", args);
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListChannels");
    }

    private static void Validate(DeviceRequest request, bool create)
    {
        Rules.Text(request.Name, "录像机名称");
        Rules.Text(request.Username, "设备账号");
        Rules.Require(Uri.CheckHostName(request.Host.Trim()) != UriHostNameType.Unknown && request.Host.Length <= 253, "请输入有效 IP 地址或主机名");
        Rules.Require(request.Port is >= 1 and <= 65535, "设备服务端口必须为 1～65535");
        Rules.Require(!create || !string.IsNullOrEmpty(request.Password), "请输入设备密码");
        Rules.Require((request.Password?.Length ?? 0) <= 256, "设备密码长度超出限制");
    }
}
