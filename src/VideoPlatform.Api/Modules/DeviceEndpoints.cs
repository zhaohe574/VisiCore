using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Adapter;

namespace VideoPlatform.Api.Modules;

public static class DeviceEndpoints
{
    public const string DeviceColumns = "d.id,d.name,d.host,d.port,d.username,d.enabled,d.status,d.model,d.serial_number,d.last_seen_at,(select count(*) from channels where device_id=d.id and status<>'disabled') as channel_count,(select count(*) from channels where device_id=d.id and status='online') as online_channels,coalesce(d.plugin_id, 'hikvision') as plugin_id,(select name from device_plugins where id=coalesce(d.plugin_id, 'hikvision')) as plugin_name,d.sync_error,d.terminal_box_id,tb.name as terminal_box_name,tb.code as terminal_box_code,d.onu_device_id,od.onu_name,od.alias as onu_alias,d.port_number as network_port,d.is_switch_port,d.port_label,od.rx_power as onu_rx_power";

    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("录像机与通道");
        group.MapGet("/devices", async (HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device.read");
            var (page, size, offset) = ApiSupport.Pagination(context);
            var managed = await access.HasPermissionAsync(actor.UserId, "device.manage");
            var filter = $"(outer_device.name ilike @search or outer_device.host ilike @search or coalesce(tb.name, '') ilike @search or coalesce(tb.code, '') ilike @search or coalesce(od.alias, '') ilike @search) and (@managed or exists(select 1 from {AccessService.ChannelFrom} where c.device_id=outer_device.id and ({AccessService.ChannelPredicate})))";
            var args = new { search = $"%{ApiSupport.Search(context)}%", managed, actor.UserId, size, offset };
            var rows = await db.QueryAsync($"select {System.Text.RegularExpressions.Regex.Replace(DeviceColumns, @"\bd\.", "outer_device.")} from devices outer_device left join terminal_boxes tb on tb.id=outer_device.terminal_box_id left join onu_devices od on od.id=outer_device.onu_device_id where {filter} order by outer_device.id limit @size offset @offset", args);
            var count = await db.OneAsync($"select count(*) as count from devices outer_device left join terminal_boxes tb on tb.id=outer_device.terminal_box_id left join onu_devices od on od.id=outer_device.onu_device_id where {filter}", args);
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListDevices");
        group.MapGet("/devices/{id:long}", async (long id, HttpContext context, Database db, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device.read");
            if (!await access.HasPermissionAsync(actor.UserId, "device.manage"))
                Rules.Require(await db.OneAsync($"select c.id from {AccessService.ChannelFrom} where c.device_id=@id and ({AccessService.ChannelPredicate}) limit 1", new { id, actor.UserId }) is not null, "录像机不在授权范围内", "device.denied", 403);
            return Results.Ok(await db.OneAsync($"select {DeviceColumns} from devices d left join terminal_boxes tb on tb.id=d.terminal_box_id left join onu_devices od on od.id=d.onu_device_id where d.id=@id", new { id }) ?? throw new PlatformException(404, "device.missing", "录像机不存在"));
        }).Produces<DeviceDto>().WithName("GetDevice");
        group.MapPost("/devices", async (DeviceRequest request, HttpContext context, Database db, AccessService access, SecretStore secrets, DeviceService devices, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
            Validate(request, true);
            var pluginId = string.IsNullOrWhiteSpace(request.PluginId) ? "hikvision" : request.PluginId.Trim();
            var row = await db.OneAsync("insert into devices(name,host,port,username,password_cipher,enabled,plugin_id,terminal_box_id,onu_device_id,port_number,is_switch_port,port_label) values(@name,@host,@port,@username,@password,@enabled,@pluginId,@terminalBoxId,@onuDeviceId,@portNumber,@isSwitchPort,@portLabel) returning id", new { name = request.Name.Trim(), host = request.Host.Trim(), request.Port, username = request.Username.Trim(), password = secrets.Protect(request.Password ?? ""), request.Enabled, pluginId, request.TerminalBoxId, request.OnuDeviceId, request.PortNumber, isSwitchPort = request.IsSwitchPort ?? false, portLabel = request.PortLabel?.Trim() });
            DeviceAdapter.InvalidateRouteCache(row.Id());
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "device.create", row.Id().ToString(), $"添加设备：{request.Name}", ApiSupport.Ip(context));

            if (request.Enabled)
            {
                try { await devices.SyncAsync(row.Id(), context.RequestAborted); } catch { /* ignore sync error on creation */ }
            }

            return Results.Created($"/api/v2/devices/{row.Id()}", row);
        }).WithName("CreateDevice");
        group.MapPut("/devices/{id:long}", async (long id, DeviceRequest request, HttpContext context, Database db, AccessService access, SecretStore secrets, MediaService media, DeviceService devices, ICameraAdapter camera, AuditStore audit) =>
        {
            await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
            Validate(request, false);
            var existing = await db.OneAsync("select password_cipher from devices where id=@id", new { id }) ?? throw new PlatformException(404, "device.missing", "设备不存在");
            var active = await db.QueryAsync("select id from media_sessions where device_id=@id and closed_at is null", new { id });
            foreach (var session in active) await media.StopInternalAsync(Guid.Parse(session.Text("id")));
            var leases = await db.QueryAsync("select p.channel_id from ptz_leases p join channels c on c.id=p.channel_id where c.device_id=@id", new { id });
            foreach (var lease in leases) await media.StopPtzInternalAsync(lease.Id("channelId"));
            var pluginId = string.IsNullOrWhiteSpace(request.PluginId) ? "hikvision" : request.PluginId.Trim();
            var passwordCipher = request.Password is null ? existing.Text("passwordCipher") : secrets.Protect(request.Password);
            await db.ExecuteAsync("update devices set name=@name,host=@host,port=@port,username=@username,password_cipher=@password,enabled=@enabled,plugin_id=@pluginId,status='unknown',terminal_box_id=@terminalBoxId,onu_device_id=@onuDeviceId,port_number=@portNumber,is_switch_port=@isSwitchPort,port_label=@portLabel where id=@id", new { id, name = request.Name.Trim(), host = request.Host.Trim(), request.Port, username = request.Username.Trim(), password = passwordCipher, request.Enabled, pluginId, request.TerminalBoxId, request.OnuDeviceId, request.PortNumber, isSwitchPort = request.IsSwitchPort ?? false, portLabel = request.PortLabel?.Trim() });
            DeviceAdapter.InvalidateRouteCache(id);
            try { await camera.DeregisterAsync(id); } catch (PlatformException ex) when (ex.Status == 404) { }
            if (!request.Enabled) await db.ExecuteAsync("update channels set status='disabled' where device_id=@id", new { id });
            if (request.Enabled)
            {
                try { await devices.SyncAsync(id, context.RequestAborted); } catch { }
            }
            await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id);
            await audit.WriteAsync(ApiSupport.Actor(context).UserId, "device.update", id.ToString(), $"修改设备：{request.Name}", ApiSupport.Ip(context));
            return Results.NoContent();
        }).WithName("UpdateDevice");
        group.MapDelete("/devices/{id:long}", async (long id, HttpContext context, Database db, AccessService access, MediaService media, ICameraAdapter camera, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device.manage");

            var dev = await db.OneAsync("select name from devices where id=@id", new { id });
            if (dev is null) return Results.NotFound(new { message = "设备不存在或已删除" });

            // 1. 停止该设备关联的活跃媒体会话与 PTZ 锁定
            var active = await db.QueryAsync("select id from media_sessions where device_id=@id and closed_at is null", new { id });
            foreach (var session in active)
            {
                try { await media.StopInternalAsync(Guid.Parse(session.Text("id"))); } catch { }
            }
            var leases = await db.QueryAsync("select p.channel_id from ptz_leases p join channels c on c.id=p.channel_id where c.device_id=@id", new { id });
            foreach (var lease in leases)
            {
                try { await media.StopPtzInternalAsync(lease.Id("channelId")); } catch { }
            }

            // 2. 通知适配器解绑与释放内部资源（在数据库删除前，以便能解析插件路由）
            try { await camera.DeregisterAsync(id); } catch (PlatformException ex) when (ex.Status == 404) { }
            DeviceAdapter.InvalidateRouteCache(id);

            // 3. 事务级级联彻底删除关联数据及设备本体
            await db.TransactionAsync(async tx =>
            {
                await tx.ExecuteAsync(@"
                    delete from viewer_connections where media_session_id in (select id from media_sessions where device_id = @id or channel_id in (select id from channels where device_id = @id));
                    delete from media_sessions where device_id = @id or channel_id in (select id from channels where device_id = @id);
                    delete from ptz_leases where channel_id in (select id from channels where device_id = @id);
                    delete from favorites where channel_id in (select id from channels where device_id = @id);
                    delete from alarm_history where alarm_id in (select id from alarm_events where device_id = @id or channel_id in (select id from channels where device_id = @id));
                    update alarm_events set recovery_of = null where device_id = @id or channel_id in (select id from channels where device_id = @id) or recovery_of in (select id from alarm_events where device_id = @id or channel_id in (select id from channels where device_id = @id));
                    delete from alarm_events where device_id = @id or channel_id in (select id from channels where device_id = @id);
                    delete from adapter_checkpoints where device_id = @id;
                    delete from export_jobs where device_id = @id or channel_id in (select id from channels where device_id = @id);
                    delete from outbox where device_id = @id or channel_id in (select id from channels where device_id = @id);
                    delete from channel_network_bindings where channel_id in (select id from channels where device_id = @id);
                    delete from device_offline_history where channel_id in (select id from channels where device_id = @id);
                    delete from user_scopes where type='channel' and scope_id in (select id from channels where device_id = @id);
                    delete from role_scopes where type='channel' and scope_id in (select id from channels where device_id = @id);
                    delete from channels where device_id = @id;
                    delete from devices where id = @id;
                ", new { id });
                return true;
            });

            await audit.WriteAsync(actor.UserId, "device.delete", id.ToString(), $"删除设备：{dev.Text("name")}", ApiSupport.Ip(context));
            await audit.NotifyAsync("device.changed", id.ToString());
            return Results.NoContent();
        }).Produces(204).WithName("DeleteDevice");
        foreach (var action in new[] { "test", "sync" })
            group.MapPost($"/devices/{{id:long}}/{action}", async (long id, HttpContext context, AccessService access, DeviceService devices) =>
            {
                await access.DemandAsync(ApiSupport.Actor(context), "device.manage");
                var snapshot = await devices.SyncAsync(id, context.RequestAborted);
                return Results.Ok(new { success = true, channels = (snapshot["channels"] as JsonArray)?.Count ?? 0 });
            }).WithName(action == "test" ? "TestDevice" : "SyncDevice");
        group.MapGet("/channels", async (HttpContext context, Database db, AccessService access, long? deviceId, long? unitId, long? areaId, long? workshopId, string? online, string? assigned) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "channel.read");
            var (_, size, offset) = ApiSupport.Pagination(context);
            bool? isOnline = online?.Trim().ToLowerInvariant() switch
            {
                "online" or "true" or "1" => true,
                "offline" or "false" or "0" => false,
                _ => null
            };
            bool? isAssigned = assigned?.Trim().ToLowerInvariant() switch
            {
                "assigned" or "true" or "1" => true,
                "unassigned" or "false" or "0" => false,
                _ => null
            };
            var where = $"c.status<>'disabled' and d.enabled and ({AccessService.ChannelPredicate}) and (@deviceId::bigint is null or c.device_id=@deviceId) and (@unitId::bigint is null or c.unit_id=@unitId) and (@areaId::bigint is null or un.parent_id=@areaId) and (@workshopId::bigint is null or ar.parent_id=@workshopId) and (@isAssigned::boolean is null or (@isAssigned = true and c.unit_id is not null) or (@isAssigned = false and c.unit_id is null)) and (@isOnline::boolean is null or (c.status='online')=@isOnline) and (c.name ilike @search or coalesce(c.alias, '') ilike @search or coalesce(c.ip, '') ilike @search or coalesce(c.remark, '') ilike @search or d.name ilike @search or c.device_channel::text ilike @search or coalesce(tb.name, '') ilike @search or coalesce(tb.code, '') ilike @search or coalesce(od.onu_name, '') ilike @search or coalesce(od.alias, '') ilike @search or coalesce(od.sn, '') ilike @search)";
            var args = new { actor.UserId, deviceId, unitId, areaId, workshopId, isOnline, isAssigned, search = $"%{ApiSupport.Search(context)}%", size, offset };
            var rows = await db.QueryAsync($"select {AccessService.ChannelColumns} from {AccessService.ChannelFrom} where {where} order by coalesce(c.sort_order, 0), d.id, c.device_channel limit @size offset @offset", args);
            var count = await db.OneAsync($"select count(*) as count from {AccessService.ChannelFrom} where {where}", args);
            return Results.Ok(ApiSupport.Page(context, rows, count.Id("count")));
        }).WithName("ListChannels");
        group.MapGet("/channels/{id:long}", async (long id, HttpContext context, AdministrationService service) =>
            Results.Ok(await service.ChannelAsync(ApiSupport.Actor(context), id, context.RequestAborted)))
            .Produces<ChannelDto>().WithName("GetChannel");
        group.MapPut("/channels/{id:long}", async (long id, ChannelUpdateRequest request, HttpContext context, AdministrationService service, VideoPlatform.Infrastructure.Nvr.INvrChannelManager nvrManager) =>
        {
            var updated = await service.UpdateChannelAsync(ApiSupport.Actor(context), id, request, ApiSupport.Ip(context), context.RequestAborted, nvrManager);
            return Results.Ok(updated);
        }).Produces<ChannelDto>().WithName("UpdateChannel");
        // 通道码流档位能力探测（B1）：由适配器直接探测设备该档位是否存在并缓存结果。
        // 客户端据此避免「先请求子码流 → 失败 → 回退主码流」的试错，每格可省约 1.2 秒与一次设备通道占用。
        group.MapPost("/channels/{id:long}/streams/probe", async (long id, StreamProbeRequest request, HttpContext context, AccessService access, ICameraAdapter camera) =>
        {
            Rules.Require(request.StreamType is 1 or 2, "码流类型只能是 1（主码流）或 2（子码流）");
            var channel = await access.ChannelAsync(ApiSupport.Actor(context), id, "live.view", context.RequestAborted);
            var result = await camera.ProbeStreamAsync(channel.Id("deviceId"), (int)channel.Id("deviceChannel"), request.StreamType, context.RequestAborted);
            // 语义与原先的手写字段拾取逐一对应：
            //  - 缺失/空响应 → Available=false 且 Error="适配器未返回探测结果"（由 StreamCapability.Parse 承担）
            //  - 分辨率/码率仅在 >0 时下传，0 或负数按"未知"处理
            return Results.Ok(new StreamCapabilityDto(
                request.StreamType,
                result.Available,
                result.Codec is { Length: > 0 } codec ? codec : null,
                result.Width is > 0 ? result.Width : null,
                result.Height is > 0 ? result.Height : null,
                result.BitrateKbps is > 0 ? result.BitrateKbps : null,
                result.Error is { Length: > 0 } error ? error : null));
        }).Produces<StreamCapabilityDto>().WithName("ProbeChannelStream");

        group.MapGet("/nvr/available-channels", async (HttpContext context, AccessService access, VideoPlatform.Infrastructure.Nvr.INvrChannelManager nvrManager) =>
        {
            var actor = ApiSupport.Actor(context);
            var hasPerm = await access.HasPermissionAsync(actor.UserId, "device.read", context.RequestAborted)
                       || await access.HasPermissionAsync(actor.UserId, "channel.assign", context.RequestAborted)
                       || await access.HasPermissionAsync(actor.UserId, "device.manage", context.RequestAborted);
            Rules.Require(hasPerm, "当前账号没有查看录像机通道权限", "access.denied", 403);
            return Results.Ok(await nvrManager.GetAvailableChannelsAsync(context.RequestAborted));
        }).Produces<IReadOnlyList<NvrAvailableChannelDto>>().WithName("GetNvrAvailableChannels");

        group.MapPost("/organization/channels/add-camera", async (AddCameraRequest request, HttpContext context, AdministrationService service, VideoPlatform.Infrastructure.Nvr.INvrChannelManager nvrManager) =>
        {
            var created = await service.AddCameraDirectAsync(ApiSupport.Actor(context), request, nvrManager, ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/channels/{created.Id}", created);
        }).Produces<ChannelDto>().WithName("AddCameraDirect");
    }

    private static bool IsValidHostOrCloudSee(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var trimmed = host.Trim();
        if (trimmed.Length > 253) return false;
        if (Uri.CheckHostName(trimmed) != UriHostNameType.Unknown) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[a-zA-Z0-9_-]{4,64}$");
    }

    private static void Validate(DeviceRequest request, bool create)
    {
        Rules.Text(request.Name, "录像机名称");
        Rules.Text(request.Username, "设备账号");
        Rules.Require(!string.IsNullOrWhiteSpace(request.Host), "请输入有效 IP 地址或云视通号");
        Rules.Require(IsValidHostOrCloudSee(request.Host), "请输入有效 IP 地址、主机名或云视通号");
        Rules.Require(request.Port is >= 1 and <= 65535, "设备服务端口必须为 1～65535");
        Rules.Require((request.Password?.Length ?? 0) <= 256, "设备密码长度超出限制");
    }
}
