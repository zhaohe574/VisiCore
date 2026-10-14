using System.Net;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

/// <summary>授权资产的统一查询投影，所有业务编号仍来自原实体表。</summary>
public sealed class AssetQueryService(Database db, AccessService access)
{
    public static readonly IReadOnlyDictionary<string, string> Permissions = new Dictionary<string, string>
    {
        ["channel"] = "channel.read", ["device"] = "device.read", ["computer"] = "computer.read",
        ["terminal-box"] = "pon.read", ["onu"] = "pon.read", ["network-device"] = "network_device.read",
        ["virtual-machine"] = "network_device.read", ["ip-pool"] = "ip.read"
    };

    private static readonly string Projection = $$"""
        with raw_assets as (
          select 'channel'::text as type,c.id,coalesce(nullif(c.alias,''),c.name) as name,
            c.device_channel::text as code,c.ip,c.status,ar.parent_id as workshop_id,un.parent_id as area_id,
            c.unit_id,cnb.terminal_box_id,cnb.onu_device_id,cnb.port_number,cnb.is_switch_port,
            d.last_seen_at as observed_at,od.health_status,od.root_cause,od.rx_power,c.device_id
          from {{AccessService.ChannelFrom}}
          where @channels and d.enabled and c.status<>'disabled' and ({{AccessService.ChannelPredicate}})
          union all
          select 'device',d.id,d.name,coalesce(d.serial_number,''),d.host,d.status,
            null::bigint,null::bigint,null::bigint,null::bigint,null::bigint,null::int,false,
            d.last_seen_at,null::text,null::text,null::numeric,null::bigint
          from devices d where @devices and (@deviceManager or exists(
            select 1 from {{AccessService.ChannelFrom}} where c.device_id=d.id and ({{AccessService.ChannelPredicate}})))
          union all
          select 'computer',pc.id,pc.name,coalesce(pc.mac,''),pc.ip,pc.status,
            coalesce(pc.workshop_id,ar.parent_id),coalesce(pc.area_id,un.parent_id),pc.unit_id,
            pc.terminal_box_id,pc.onu_device_id,pc.port_number,pc.is_switch_port,pc.last_probed_at,
            od.health_status,od.root_cause,od.rx_power,null::bigint
          from computers pc left join units un on un.id=pc.unit_id left join areas ar on ar.id=coalesce(pc.area_id,un.parent_id)
            left join onu_devices od on od.id=pc.onu_device_id where @computers
          union all
          select 'terminal-box',tb.id,tb.name,tb.code,null::text,'unknown',ar.parent_id,
            coalesce(tb.area_id,un.parent_id),tb.unit_id,tb.id,null::bigint,null::int,false,
            null::timestamptz,null::text,null::text,null::numeric,null::bigint
          from terminal_boxes tb left join units un on un.id=tb.unit_id
            left join areas ar on ar.id=coalesce(tb.area_id,un.parent_id) where @pon
          union all
          select 'onu',od.id,coalesce(nullif(od.alias,''),od.onu_name),od.sn,od.ip,
            case when od.status in ('1','online') then 'online' when od.status in ('2','offline') then 'offline' else 'unknown' end,
            ar.parent_id,coalesce(tb.area_id,un.parent_id),tb.unit_id,od.terminal_box_id,od.id,
            null::int,false,od.last_synced_at,od.health_status,od.root_cause,od.rx_power,null::bigint
          from onu_devices od left join terminal_boxes tb on tb.id=od.terminal_box_id
            left join units un on un.id=tb.unit_id left join areas ar on ar.id=coalesce(tb.area_id,un.parent_id) where @pon
          union all
          select 'network-device',nd.id,nd.name,coalesce(nd.model,''),nd.management_ip,nd.status,
            nd.workshop_id,nd.area_id,null::bigint,null::bigint,null::bigint,null::int,false,
            nd.last_probed_at,null::text,null::text,null::numeric,null::bigint
          from network_devices nd where @network
          union all
          select 'virtual-machine',vm.id,vm.vm_name,coalesce(vm.os,''),vm.ip,
            case when vm.status='running' then 'online' when vm.status='stopped' then 'offline' else 'unknown' end,
            nd.workshop_id,nd.area_id,null::bigint,null::bigint,null::bigint,null::int,false,
            null::timestamptz,null::text,null::text,null::numeric,vm.host_device_id
          from virtual_machines vm join network_devices nd on nd.id=vm.host_device_id where @network
          union all
          select 'ip-pool',p.id,p.name,p.cidr,p.cidr,'unknown',p.workshop_id,p.area_id,
            null::bigint,null::bigint,null::bigint,null::int,false,
            p.last_scanned_at,null::text,null::text,null::numeric,null::bigint
          from ip_pools p where @pools
        ), assets as (
          select * from raw_assets where type in ('channel','device') or ({{AssetScope.Predicate("unit_id", "area_id", "workshop_id")}})
        )
        """;

    private async Task<Dictionary<string, object?>> ArgumentsAsync(Actor actor, string? search, string? type,
        long? workshopId, long? areaId, long? unitId, string? status, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(type)) Rules.Require(Permissions.ContainsKey(type), "资产类型无效");
        Rules.Require(search is null || search.Length <= 256, "搜索内容过长");
        if (!string.IsNullOrEmpty(status)) Rules.Require(status is "online" or "offline" or "unknown" or "abnormal", "资产状态无效");
        var rows = await db.QueryAsync("""
            select distinct p.permission_code from users u join user_roles ur on ur.user_id=u.id
            join roles r on r.id=ur.role_id join role_permissions p on p.role_id=r.id
            where u.id=@userId and u.status='active' and r.status='active'
            """, new { userId = actor.UserId }, ct);
        var permissions = rows.Select(r => r.Text("permissionCode")).ToHashSet();
        Rules.Require(Permissions.Values.Any(permissions.Contains), "当前账号没有资产查看权限", "access.denied", 403);
        return new()
        {
            ["userId"] = actor.UserId, ["channels"] = permissions.Contains("channel.read"),
            ["devices"] = permissions.Contains("device.read"), ["deviceManager"] = permissions.Contains("device.manage"),
            ["computers"] = permissions.Contains("computer.read"), ["pon"] = permissions.Contains("pon.read"),
            ["network"] = permissions.Contains("network_device.read"), ["pools"] = permissions.Contains("ip.read"),
            ["search"] = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%",
            ["type"] = string.IsNullOrEmpty(type) ? null : type, ["workshopId"] = workshopId,
            ["areaId"] = areaId, ["unitId"] = unitId, ["status"] = string.IsNullOrEmpty(status) ? null : status
        };
    }

    private static readonly string Filter = $$"""
        (@search::text is null or (type<>'computer' and (name ilike @search or code ilike @search or coalesce(ip,'') ilike @search or id::text ilike @search))
          or (type='computer' and exists(select 1 from computers c left join terminal_boxes tb on tb.id=c.terminal_box_id left join onu_devices onu on onu.id=c.onu_device_id where c.id=assets.id and {{ComputerService.SearchPredicate}})))
        and (@type::text is null or type=@type) and (@workshopId::bigint is null or workshop_id=@workshopId)
        and (@areaId::bigint is null or area_id=@areaId) and (@unitId::bigint is null or unit_id=@unitId)
        and (@status::text is null or status=@status or (@status='abnormal' and (status='offline' or health_status in ('abnormal','critical','warning','subhealthy'))))
        """;

    public async Task<PagedResponse<AssetDto>> SearchAsync(Actor actor, int page, int pageSize, string? search,
        string? type, long? workshopId, long? areaId, long? unitId, string? status, CancellationToken ct = default)
    {
        var args = await ArgumentsAsync(actor, search, type, workshopId, areaId, unitId, status, ct);
        args["offset"] = checked((page - 1) * pageSize); args["pageSize"] = pageSize;
        var rows = await db.QueryAsync(Projection + $"select * from assets where {Filter} order by type,name,id limit @pageSize offset @offset", args, ct);
        var total = (await db.OneAsync(Projection + $"select count(*) as count from assets where {Filter}", args, ct)).Id("count");
        return new(rows.Select(ToDto).ToArray(), total, page, pageSize);
    }

    public async Task<AssetSummaryDto> SummaryAsync(Actor actor, string? search, string? type,
        long? workshopId, long? areaId, long? unitId, string? status, CancellationToken ct = default)
    {
        var args = await ArgumentsAsync(actor, search, type, workshopId, areaId, unitId, status, ct);
        var rows = await db.QueryAsync(Projection + $"""
            select type,count(*) as total,count(*) filter(where status='online') as online,
            count(*) filter(where status='offline') as offline,count(*) filter(where status not in ('online','offline')) as unknown,
            count(*) filter(where type in ('channel','computer','terminal-box') and unit_id is null and area_id is null and workshop_id is null) as missing_organization,
            count(*) filter(where type in ('channel','computer') and onu_device_id is null) as missing_network
            from assets where {Filter} group by type
            """, args, ct);
        return new(rows.Sum(r => r.Id("total")), rows.Sum(r => r.Id("online")), rows.Sum(r => r.Id("offline")),
            rows.Sum(r => r.Id("unknown")), rows.Sum(r => r.Id("missingOrganization")), rows.Sum(r => r.Id("missingNetwork")),
            rows.Select(r => new AssetTypeCountDto(r.Text("type"), r.Id("total"), r.Id("online"), r.Id("offline"), r.Id("unknown"))).ToArray());
    }

    public async Task<HashSet<AssetRef>> AuthorizedRefsAsync(Actor actor, CancellationToken ct = default)
    {
        var args = await ArgumentsAsync(actor, null, null, null, null, null, null, ct);
        return (await db.QueryAsync(Projection + "select type,id from assets", args, ct))
            .Select(r => new AssetRef(r.Text("type"), r.Id())).ToHashSet();
    }

    public async Task<AssetContextDto> ContextAsync(Actor actor, string type, long id, CancellationToken ct = default)
    {
        Rules.Require(Permissions.TryGetValue(type, out var permission) && id > 0, "资产编号或类型无效");
        await access.DemandAsync(actor, permission!, ct);
        var args = await ArgumentsAsync(actor, null, null, null, null, null, null, ct);
        args["targetType"] = type; args["targetId"] = id;
        var root = await db.OneAsync(Projection + "select * from assets where type=@targetType and id=@targetId", args, ct)
            ?? throw new PlatformException(404, "asset.not_found", "资产不存在或不在授权范围内");
        var asset = ToDto(root);
        args["boxId"] = asset.TerminalBoxId; args["onuId"] = asset.OnuDeviceId;
        args["deviceId"] = type is "device" or "network-device" ? id : asset.DeviceId;
        args["ip"] = asset.Ip is not null && IPAddress.TryParse(asset.Ip, out _) ? asset.Ip : null;
        args["portType"] = type switch { "network-device" => "network_device", "onu" => "onu_device", "computer" => "computer", _ => "onu_device" };
        args["portId"] = type is "network-device" or "onu" or "computer" ? id : asset.OnuDeviceId;
        var related = (await db.QueryAsync(Projection + """
            select * from assets where not (type=@targetType and id=@targetId) and (
              (@boxId::bigint is not null and terminal_box_id=@boxId)
              or (@onuId::bigint is not null and onu_device_id=@onuId)
              or (@deviceId::bigint is not null and @targetType in ('channel','device') and
                  ((type='device' and id=@deviceId) or (type='channel' and device_id=@deviceId)))
              or (@deviceId::bigint is not null and @targetType in ('network-device','virtual-machine') and
                  ((type='network-device' and id=@deviceId) or (type='virtual-machine' and device_id=@deviceId)))
              or (type='ip-pool' and @ip::inet is not null and @ip::inet <<= ip::cidr)
              or (@targetType='ip-pool' and exists(select 1 from ip_pools p where p.id=@targetId and
                  type in ('channel','computer','onu','device','network-device','virtual-machine') and
                  case when ip ~ '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' then ip::inet <<= p.cidr::cidr else false end))
              or (@portId::bigint is not null and exists(select 1 from device_ports local
                  join device_links l on l.local_port_id=local.id or l.peer_port_id=local.id
                  join device_ports peer on peer.id=case when l.local_port_id=local.id then l.peer_port_id else l.local_port_id end
                  where local.device_type=@portType and local.device_id=@portId and peer.device_id=assets.id
                  and assets.type=case peer.device_type when 'network_device' then 'network-device' when 'onu_device' then 'onu' else peer.device_type end))
            ) order by type,name,id
            """, args, ct)).Select(ToDto).ToArray();
        var organization = new List<AssetOrganizationDto>();
        foreach (var (kind, table, nodeId) in new[] { ("workshop", "workshops", asset.WorkshopId), ("area", "areas", asset.AreaId), ("unit", "units", asset.UnitId) })
            if (nodeId is not null)
            {
                var node = await db.OneAsync($"select name from {table} where id=@id", new { id = nodeId }, ct);
                if (node is not null) organization.Add(new(kind, nodeId.Value, node.Text("name")));
            }
        var evidence = new List<AssetEvidenceDto> { new("资产状态", asset.Status, "平台资产采集", asset.ObservedAt) };
        var healthTime = type == "onu" ? asset.ObservedAt : related.FirstOrDefault(a => a.Ref.Type == "onu")?.ObservedAt;
        if (type == "onu" || related.Any(a => a.Ref.Type == "onu" && a.Ref.Id == asset.OnuDeviceId))
        {
            if (!string.IsNullOrWhiteSpace(asset.HealthStatus)) evidence.Add(new("ONU 健康", asset.HealthStatus, "全光网同步", healthTime));
            if (asset.RxPower is not null) evidence.Add(new("接收光功率", $"{asset.RxPower:0.00} dBm", "全光网同步", healthTime));
            if (!string.IsNullOrWhiteSpace(asset.RootCause)) evidence.Add(new("诊断线索", asset.RootCause, "全光网诊断", healthTime));
        }
        var channels = related.Where(a => a.Ref.Type == "channel").Select(a => a.Ref.Id).Concat(type == "channel" ? [id] : Array.Empty<long>()).ToArray();
        var alarms = new List<AssetAlarmDto>();
        if (channels.Length > 0 && await access.HasPermissionAsync(actor.UserId, "alarm.read", ct))
            alarms = (await db.QueryAsync("select id,channel_id,event_type,state,recovered,occurred_at from alarm_events where channel_id=any(@channels) order by occurred_at desc limit 30", new { channels }, ct))
                .Select(r => new AssetAlarmDto(r.Id(), NullableId(r, "channelId"), r.Text("eventType"), r.Text("state"), r.Flag("recovered"), r.Time("occurredAt"))).ToList();
        var history = new List<AssetHistoryDto>();
        if (await access.HasPermissionAsync(actor.UserId, "pon.read", ct) && (asset.TerminalBoxId is not null || asset.OnuDeviceId is not null || channels.Length > 0))
            history = (await db.QueryAsync("""
                select * from device_offline_history where (channel_id=any(@channels)
                    or (channel_id is null and onu_device_id=any(@onus))) order by down_time desc limit 30
                """, new { channels, onus = related.Where(a => a.Ref.Type == "onu").Select(a => a.Ref.Id).Concat(type == "onu" ? [id] : Array.Empty<long>()).ToArray() }, ct))
                .Select(r => new AssetHistoryDto(r.Id(), r.Text("deviceName"), r.Text("eventType"), r.Time("downTime"), r.NullableTime("upTime"), r.Text("reason"))).ToList();
        var ports = new List<AssetPortDto>();
        if (type is "network-device" or "onu" or "computer")
            ports = (await db.QueryAsync($$"""
                select p.id,p.port_name,p.status,case when {{AssetScope.PortPredicate("peer")}} then peer.id end as peer_port_id,
                  case when {{AssetScope.PortPredicate("peer")}} then l.cable_label else null end as cable_label
                from device_ports p left join device_links l on l.local_port_id=p.id or l.peer_port_id=p.id
                left join device_ports peer on peer.id=case when l.local_port_id=p.id then l.peer_port_id else l.local_port_id end
                where p.device_type=@deviceType and p.device_id=@id order by p.id
                """, new { deviceType = type switch { "network-device" => "network_device", "onu" => "onu_device", _ => type }, id, userId = actor.UserId }, ct))
                .Select(r => new AssetPortDto(r.Id(), r.Text("portName"), r.Text("status"), NullableId(r, "peerPortId"), r.Text("cableLabel"))).ToList();
        return new(asset, organization, related, evidence, history, alarms, ports);
    }

    private static long? NullableId(JsonNode r, string key) => r[key] is null ? null : r.Id(key);
    private static AssetDto ToDto(JsonObject r) => new(new(r.Text("type"), r.Id()), r.Text("name"), r.Text("code"),
        r.Text("ip"), r.Text("status"), NullableId(r, "workshopId"), NullableId(r, "areaId"), NullableId(r, "unitId"),
        NullableId(r, "terminalBoxId"), NullableId(r, "onuDeviceId"), r.NullableInt("portNumber"), r.Flag("isSwitchPort"),
        r.NullableTime("observedAt"), r.Text("healthStatus"), r.Text("rootCause"), r.Decimal("rxPower"), NullableId(r, "deviceId"));
}
