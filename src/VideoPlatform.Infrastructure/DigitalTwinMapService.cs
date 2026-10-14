using System.Text.Json;
using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure;

public sealed class DigitalTwinMapService(Database db, AuditStore audit, AssetQueryService assets)
{
    internal static string AssetType(string? type) => type switch { "terminal_box" => "terminal-box", "network_device" => "network-device", "onu_device" => "onu", _ => type ?? "" };
    private static bool Visible(IReadOnlySet<AssetRef> allowed, string? type, long? id)
        => id is > 0 && allowed.Contains(new(AssetType(type), id.Value));

    public async Task<IReadOnlyList<DigitalTwinSceneDto>> GetScenesAsync(CancellationToken ct = default, Actor? actor = null)
    {
        const string sql = @"
            select s.id, s.scene_code, s.name, s.level_type, s.workshop_id, w.name as workshop_name,
                   s.bg_image_url, s.natural_width, s.natural_height, s.default_zoom, s.min_zoom, s.max_zoom,
                   s.center_x_pct, s.center_y_pct, s.sort_order,
                   coalesce(c_count.count, 0) as node_count,
                   coalesce(cb_count.count, 0) as cable_count
            from map_scenes s
            left join workshops w on s.workshop_id = w.id
            left join (select scene_id, count(*) as count from map_coordinates group by scene_id) c_count on c_count.scene_id = s.id
            left join (select scene_id, count(*) as count from map_cable_paths group by scene_id) cb_count on cb_count.scene_id = s.id
            order by s.sort_order, s.id;";

        var rows = await db.QueryAsync(sql, ct: ct);
        var scenes = new List<DigitalTwinSceneDto>();
        foreach (var r in rows)
        {
            var nodeCount = (int)r.Id("nodeCount");
            var cableCount = (int)r.Id("cableCount");
            scenes.Add(new DigitalTwinSceneDto(
                r.Id("id"),
                r.Text("sceneCode"),
                r.Text("name"),
                r.Text("levelType"),
                r.Id("workshopId") > 0 ? r.Id("workshopId") : null,
                r.Text("workshopName"),
                r.Text("bgImageUrl"),
                (int)(r.NullableInt("naturalWidth") ?? 3840),
                (int)(r.NullableInt("naturalHeight") ?? 2160),
                r.Decimal("defaultZoom") ?? 1.0m,
                r.Decimal("minZoom") ?? 0.4m,
                r.Decimal("maxZoom") ?? 5.0m,
                r.Decimal("centerXPct") ?? 0.5m,
                r.Decimal("centerYPct") ?? 0.5m,
                (int)(r.NullableInt("sortOrder") ?? 0),
                nodeCount,
                cableCount,
                nodeCount,
                nodeCount
            ));
        }
        if (actor is null) return scenes;
        var scoped = new List<DigitalTwinSceneDto>();
        foreach (var scene in scenes) scoped.Add((await GetSceneDetailAsync(scene.Id, ct, actor)).Scene);
        return scoped;
    }

    public async Task<DigitalTwinSceneDetailDto> GetSceneDetailAsync(long sceneId, CancellationToken ct = default, Actor? actor = null)
    {
        const string sceneSql = @"
            select s.id, s.scene_code, s.name, s.level_type, s.workshop_id, w.name as workshop_name,
                   s.bg_image_url, s.natural_width, s.natural_height, s.default_zoom, s.min_zoom, s.max_zoom,
                   s.center_x_pct, s.center_y_pct, s.sort_order
            from map_scenes s
            left join workshops w on s.workshop_id = w.id
            where s.id = @sceneId;";

        var sceneRow = await db.OneAsync(sceneSql, new { sceneId }, ct)
            ?? throw new PlatformException(404, "scene.not_found", "数字孪生场景不存在");

        var nodes = new List<DigitalTwinNodeDto>();

        // 1. 终端箱
        const string boxSql = @"
            select mc.id as coordinate_id, mc.scene_id, mc.target_type, mc.target_id,
                   tb.name, tb.code, null as ip,
                   coalesce(case when onu_stats.online_count > 0 then 'online' else 'offline' end, 'offline') as status,
                   mc.x_pct, mc.y_pct, mc.altitude_m, mc.fov_yaw, mc.fov_angle, mc.fov_radius_m,
                   mc.icon_style, mc.remarks,
                   onu_stats.min_rx as rx_power, onu_stats.max_tx as tx_power,
                   coalesce(onu_stats.total_onu, 0) as onu_count,
                   coalesce(onu_stats.online_count, 0) as onu_online_count,
                   coalesce(pc_stats.computer_count, 0) as computer_count,
                   coalesce(cam_stats.channel_count, 0) as camera_count,
                   tb.location
            from map_coordinates mc
            join terminal_boxes tb on mc.target_id = tb.id
            left join (
                select terminal_box_id, count(*) as total_onu,
                       count(*) filter (where status in ('online', 'normal', 'up')) as online_count,
                       min(rx_power) as min_rx, max(tx_power) as max_tx
                from onu_devices group by terminal_box_id
            ) onu_stats on onu_stats.terminal_box_id = tb.id
            left join (
                select terminal_box_id, count(*) as computer_count from computers group by terminal_box_id
            ) pc_stats on pc_stats.terminal_box_id = tb.id
            left join (
                select terminal_box_id, count(*) as channel_count from channel_network_bindings group by terminal_box_id
            ) cam_stats on cam_stats.terminal_box_id = tb.id
            where mc.scene_id = @sceneId and mc.target_type = 'terminal_box';";

        var boxRows = await db.QueryAsync(boxSql, new { sceneId }, ct);
        foreach (var r in boxRows)
        {
            var rx = r.Decimal("rxPower");
            var status = r.Text("status");
            if (status == "online" && rx.HasValue && rx.Value < -27.0m)
                status = "abnormal";

            nodes.Add(new DigitalTwinNodeDto(
                r.Id("coordinateId"),
                sceneId,
                r.Text("targetType"),
                r.Id("targetId"),
                r.Text("name"),
                r.Text("code"),
                null,
                status,
                r.Decimal("xPct") ?? 0m,
                r.Decimal("yPct") ?? 0m,
                r.Decimal("altitudeM") ?? 1.5m,
                r.Decimal("fovYaw"),
                r.Decimal("fovAngle"),
                r.Decimal("fovRadiusM"),
                r.Text("iconStyle"),
                r.Text("remarks"),
                rx,
                r.Decimal("txPower"),
                (int)(r.NullableInt("onuCount") ?? 0),
                (int)(r.NullableInt("onuOnlineCount") ?? 0),
                (int)(r.NullableInt("computerCount") ?? 0),
                (int)(r.NullableInt("cameraCount") ?? 0),
                null,
                null,
                null,
                null,
                null,
                r.Text("location"),
                null,
                null
            ));
        }

        // 2. 核心/网络设备
        const string netDevSql = @"
            select mc.id as coordinate_id, mc.scene_id, mc.target_type, mc.target_id,
                   nd.name, nd.device_type as code, nd.management_ip as ip,
                   coalesce(nd.status, 'online') as status,
                   mc.x_pct, mc.y_pct, mc.altitude_m, mc.fov_yaw, mc.fov_angle, mc.fov_radius_m,
                   mc.icon_style, mc.remarks,
                   nd.model as sub_switch_model, nd.response_time_ms,
                   nd.location
            from map_coordinates mc
            join network_devices nd on mc.target_id = nd.id
            where mc.scene_id = @sceneId and mc.target_type = 'network_device';";

        var netDevRows = await db.QueryAsync(netDevSql, new { sceneId }, ct);
        foreach (var r in netDevRows)
        {
            nodes.Add(new DigitalTwinNodeDto(
                r.Id("coordinateId"),
                sceneId,
                r.Text("targetType"),
                r.Id("targetId"),
                r.Text("name"),
                r.Text("code"),
                r.Text("ip"),
                r.Text("status"),
                r.Decimal("xPct") ?? 0m,
                r.Decimal("yPct") ?? 0m,
                r.Decimal("altitudeM") ?? 2.0m,
                r.Decimal("fovYaw"),
                r.Decimal("fovAngle"),
                r.Decimal("fovRadiusM"),
                r.Text("iconStyle"),
                r.Text("remarks"),
                null, null, null, null, null, null,
                r.Text("subSwitchModel"),
                r.NullableInt("responseTimeMs"),
                null, null, null,
                r.Text("location"),
                null, null
            ));
        }

        // 3. 摄像头通道
        const string channelSql = @"
            select mc.id as coordinate_id, mc.scene_id, mc.target_type, mc.target_id,
                   c.name, coalesce(c.alias, c.name) as code, c.ip,
                   coalesce(c.status, 'offline') as status,
                   mc.x_pct, mc.y_pct, mc.altitude_m, mc.fov_yaw, mc.fov_angle, mc.fov_radius_m,
                   mc.icon_style, mc.remarks,
                   c.id as channel_id, c.device_id, coalesce(c.remark, '') as location
            from map_coordinates mc
            join channels c on mc.target_id = c.id
            where mc.scene_id = @sceneId and mc.target_type = 'channel';";

        var channelRows = await db.QueryAsync(channelSql, new { sceneId }, ct);
        foreach (var r in channelRows)
        {
            var chId = r.Id("channelId");
            nodes.Add(new DigitalTwinNodeDto(
                r.Id("coordinateId"),
                sceneId,
                r.Text("targetType"),
                r.Id("targetId"),
                r.Text("name"),
                r.Text("code"),
                r.Text("ip"),
                r.Text("status"),
                r.Decimal("xPct") ?? 0m,
                r.Decimal("yPct") ?? 0m,
                r.Decimal("altitudeM") ?? 4.5m,
                r.Decimal("fovYaw") ?? 0m,
                r.Decimal("fovAngle") ?? 60m,
                r.Decimal("fovRadiusM") ?? 25m,
                r.Text("iconStyle"),
                r.Text("remarks"),
                null, null, null, null, null, null, null, null,
                $"/api/v2/channels/{chId}/preview",
                chId,
                r.Id("deviceId") > 0 ? r.Id("deviceId") : null,
                r.Text("location"),
                null, null
            ));
        }

        // 4. 工位电脑
        const string compSql = @"
            select mc.id as coordinate_id, mc.scene_id, mc.target_type, mc.target_id,
                   cp.name, null as code, cp.ip,
                   coalesce(cp.status, 'online') as status,
                   mc.x_pct, mc.y_pct, mc.altitude_m, mc.fov_yaw, mc.fov_angle, mc.fov_radius_m,
                   mc.icon_style, mc.remarks,
                   cp.response_time_ms, cp.location, cp.owner as responsible_person, cp.department
            from map_coordinates mc
            join computers cp on mc.target_id = cp.id
            where mc.scene_id = @sceneId and mc.target_type = 'computer';";

        var compRows = await db.QueryAsync(compSql, new { sceneId }, ct);
        foreach (var r in compRows)
        {
            nodes.Add(new DigitalTwinNodeDto(
                r.Id("coordinateId"),
                sceneId,
                r.Text("targetType"),
                r.Id("targetId"),
                r.Text("name"),
                null,
                r.Text("ip"),
                r.Text("status"),
                r.Decimal("xPct") ?? 0m,
                r.Decimal("yPct") ?? 0m,
                r.Decimal("altitudeM") ?? 0.8m,
                null, null, null,
                r.Text("iconStyle"),
                r.Text("remarks"),
                null, null, null, null, null, null, null,
                r.NullableInt("responseTimeMs"),
                null, null, null,
                r.Text("location"),
                r.Text("responsiblePerson"),
                r.Text("department")
            ));
        }

        // 5. 光缆与走线路径
        const string cableSql = @"
            select id, scene_id, name, cable_type, from_device_type, from_device_id,
                   to_device_type, to_device_id, color, points_json, status, remarks
            from map_cable_paths
            where scene_id = @sceneId
            order by id;";

        var cableRows = await db.QueryAsync(cableSql, new { sceneId }, ct);
        var cables = cableRows.Select(r => new DigitalTwinCableDto(
            r.Id("id"),
            sceneId,
            r.Text("name"),
            r.Text("cableType"),
            r.Text("fromDeviceType"),
            r.Id("fromDeviceId") > 0 ? r.Id("fromDeviceId") : null,
            null,
            r.Text("toDeviceType"),
            r.Id("toDeviceId") > 0 ? r.Id("toDeviceId") : null,
            null,
            r.Text("color", "#38bdf8"),
            r.Text("pointsJson", "[]"),
            r.Text("status", "normal"),
            r.Text("remarks")
        )).ToList();

        if (actor is not null)
        {
            var allowed = await assets.AuthorizedRefsAsync(actor, ct);
            nodes = nodes.Where(n => Visible(allowed, n.TargetType, n.TargetId)).ToList();
            var onuIds = allowed.Where(a => a.Type == "onu").Select(a => a.Id).ToArray();
            var computerIds = allowed.Where(a => a.Type == "computer").Select(a => a.Id).ToArray();
            var channelIds = allowed.Where(a => a.Type == "channel").Select(a => a.Id).ToArray();
            var counts = await db.QueryAsync("""
                select b.id,
                  (select count(*) from onu_devices o where o.terminal_box_id=b.id and o.id=any(@onuIds)) as onus,
                  (select count(*) from onu_devices o where o.terminal_box_id=b.id and o.id=any(@onuIds) and (o.status in ('1','online') or o.run_status='1')) as online,
                  (select min(rx_power) from onu_devices o where o.terminal_box_id=b.id and o.id=any(@onuIds)) as rx,
                  (select max(tx_power) from onu_devices o where o.terminal_box_id=b.id and o.id=any(@onuIds)) as tx,
                  (select count(*) from computers c where c.terminal_box_id=b.id and c.id=any(@computerIds)) as computers,
                  (select count(*) from channel_network_bindings c where c.terminal_box_id=b.id and c.channel_id=any(@channelIds)) as channels
                from terminal_boxes b where b.id=any(@boxIds)
                """, new { onuIds, computerIds, channelIds, boxIds = nodes.Where(n => n.TargetType == "terminal_box").Select(n => n.TargetId).ToArray() }, ct);
            nodes = nodes.Select(n => n.TargetType != "terminal_box" ? n : counts.FirstOrDefault(c => c.Id() == n.TargetId) is { } count
                ? n with { OnuCount = (int)count.Id("onus"), OnuOnlineCount = (int)count.Id("online"), ComputerCount = (int)count.Id("computers"), CameraCount = (int)count.Id("channels"),
                    RxPower = count.Decimal("rx"), TxPower = count.Decimal("tx"), Status = count.Id("onus") == 0 ? "unknown" : count.Id("online") == 0 ? "offline" : count.Decimal("rx") < -27m ? "abnormal" : "online" } : n).ToList();
            cables = cables.Where(c => (c.FromDeviceId is null || Visible(allowed, c.FromDeviceType, c.FromDeviceId)) && (c.ToDeviceId is null || Visible(allowed, c.ToDeviceType, c.ToDeviceId))).ToList();
        }

        // 6. 统计面板
        var totalNodes = nodes.Count;
        var onlineNodes = nodes.Count(n => n.Status == "online");
        var abnormalNodes = nodes.Count(n => n.Status == "abnormal");
        var offlineNodes = nodes.Count(n => n.Status == "offline");
        var boxCount = nodes.Count(n => n.TargetType == "terminal_box");
        var camCount = nodes.Count(n => n.TargetType == "channel");
        var pcCount = nodes.Count(n => n.TargetType == "computer");
        var netCount = nodes.Count(n => n.TargetType == "network_device");

        decimal opticalHealth = 100m;
        if (boxCount > 0)
        {
            var validBoxes = nodes.Where(n => n.TargetType == "terminal_box").ToList();
            var normalBoxes = validBoxes.Count(n => n.Status == "online");
            opticalHealth = Math.Round((decimal)normalBoxes / validBoxes.Count * 100m, 1);
        }

        var stats = new DigitalTwinStatsDto(
            totalNodes,
            onlineNodes,
            abnormalNodes,
            offlineNodes,
            boxCount,
            camCount,
            pcCount,
            netCount,
            opticalHealth
        );

        var sceneDto = new DigitalTwinSceneDto(
            sceneRow.Id("id"),
            sceneRow.Text("sceneCode"),
            sceneRow.Text("name"),
            sceneRow.Text("levelType"),
            sceneRow.Id("workshopId") > 0 ? sceneRow.Id("workshopId") : null,
            sceneRow.Text("workshopName"),
            sceneRow.Text("bgImageUrl"),
            (int)(sceneRow.NullableInt("naturalWidth") ?? 3840),
            (int)(sceneRow.NullableInt("naturalHeight") ?? 2160),
            sceneRow.Decimal("defaultZoom") ?? 1.0m,
            sceneRow.Decimal("minZoom") ?? 0.4m,
            sceneRow.Decimal("maxZoom") ?? 5.0m,
            sceneRow.Decimal("centerXPct") ?? 0.5m,
            sceneRow.Decimal("centerYPct") ?? 0.5m,
            (int)(sceneRow.NullableInt("sortOrder") ?? 0),
            totalNodes,
            cables.Count,
            onlineNodes,
            totalNodes
        );

        return new DigitalTwinSceneDetailDto(sceneDto, nodes, cables, stats);
    }

    public async Task<DigitalTwinNodeDto> SaveCoordinateAsync(long sceneId, SaveCoordinateRequest req, string? actorUserId = null, CancellationToken ct = default)
    {
        Rules.Require(req.XPct >= 0m && req.XPct <= 1m, "横坐标 x_pct 必须在 0 到 1 之间");
        Rules.Require(req.YPct >= 0m && req.YPct <= 1m, "纵坐标 y_pct 必须在 0 到 1 之间");

        const string sql = @"
            insert into map_coordinates (scene_id, target_type, target_id, x_pct, y_pct, altitude_m, fov_yaw, fov_angle, fov_radius_m, icon_style, remarks)
            values (@sceneId, @targetType, @targetId, @xPct, @yPct, @altitudeM, @fovYaw, @fovAngle, @fovRadiusM, @iconStyle, @remarks)
            on conflict (scene_id, target_type, target_id) do update set
                x_pct = excluded.x_pct,
                y_pct = excluded.y_pct,
                altitude_m = excluded.altitude_m,
                fov_yaw = excluded.fov_yaw,
                fov_angle = excluded.fov_angle,
                fov_radius_m = excluded.fov_radius_m,
                icon_style = excluded.icon_style,
                remarks = excluded.remarks,
                updated_at = now()
            returning id;";

        var row = await db.OneAsync(sql, new
        {
            sceneId,
            targetType = req.TargetType,
            targetId = req.TargetId,
            xPct = req.XPct,
            yPct = req.YPct,
            altitudeM = req.AltitudeM ?? 1.5m,
            fovYaw = req.FovYaw ?? 0m,
            fovAngle = req.FovAngle ?? 60m,
            fovRadiusM = req.FovRadiusM ?? 25m,
            iconStyle = req.IconStyle,
            remarks = req.Remarks
        }, ct);

        var coordId = row?.Id("id") ?? 0;
        var uId = long.TryParse(actorUserId, out var pId) ? pId : (long?)null;
        await audit.WriteAsync(uId, "map.coordinate.save", $"scene/{sceneId}", $"标定空间节点: {req.TargetType}#{req.TargetId}", ct: ct);

        var detail = await GetSceneDetailAsync(sceneId, ct);
        return detail.Nodes.FirstOrDefault(n => n.CoordinateId == coordId)
            ?? throw new PlatformException(500, "node.not_found", "空间标定持久化异常");
    }

    public async Task<int> BatchSaveCoordinatesAsync(long sceneId, BatchSaveCoordinatesRequest req, string? actorUserId = null, CancellationToken ct = default)
    {
        var count = 0;
        await db.TransactionAsync(async tx =>
        {
            foreach (var item in req.Items)
            {
                const string sql = @"
                    insert into map_coordinates (scene_id, target_type, target_id, x_pct, y_pct, altitude_m, fov_yaw, fov_angle, fov_radius_m, icon_style, remarks)
                    values (@sceneId, @targetType, @targetId, @xPct, @yPct, @altitudeM, @fovYaw, @fovAngle, @fovRadiusM, @iconStyle, @remarks)
                    on conflict (scene_id, target_type, target_id) do update set
                        x_pct = excluded.x_pct,
                        y_pct = excluded.y_pct,
                        altitude_m = excluded.altitude_m,
                        fov_yaw = excluded.fov_yaw,
                        fov_angle = excluded.fov_angle,
                        fov_radius_m = excluded.fov_radius_m,
                        icon_style = excluded.icon_style,
                        remarks = excluded.remarks,
                        updated_at = now();";

                await tx.ExecuteAsync(sql, new
                {
                    sceneId,
                    targetType = item.TargetType,
                    targetId = item.TargetId,
                    xPct = Math.Clamp(item.XPct, 0m, 1m),
                    yPct = Math.Clamp(item.YPct, 0m, 1m),
                    altitudeM = item.AltitudeM ?? 1.5m,
                    fovYaw = item.FovYaw ?? 0m,
                    fovAngle = item.FovAngle ?? 60m,
                    fovRadiusM = item.FovRadiusM ?? 25m,
                    iconStyle = item.IconStyle,
                    remarks = item.Remarks
                }, ct);
                count++;
            }
            return true;
        }, ct);

        var uId = long.TryParse(actorUserId, out var pId) ? pId : (long?)null;
        await audit.WriteAsync(uId, "map.coordinate.batch_save", $"scene/{sceneId}", $"批量标定空间节点: {count} 个", ct: ct);
        return count;
    }

    public async Task DeleteCoordinateAsync(long coordinateId, string? actorUserId = null, CancellationToken ct = default)
    {
        const string sql = "delete from map_coordinates where id = @coordinateId;";
        await db.ExecuteAsync(sql, new { coordinateId }, ct);
        var uId = long.TryParse(actorUserId, out var pId) ? pId : (long?)null;
        await audit.WriteAsync(uId, "map.coordinate.delete", $"coordinate/{coordinateId}", $"移除空间标定: #{coordinateId}", ct: ct);
    }

    public async Task<IReadOnlyList<CandidateNodeDto>> GetCandidateNodesAsync(long sceneId, CancellationToken ct = default, Actor? actor = null)
    {
        var scene = await db.OneAsync("select workshop_id from map_scenes where id = @sceneId", new { sceneId }, ct)
            ?? throw new PlatformException(404, "scene.not_found", "场景不存在");

        var workshopId = scene.Id("workshopId") > 0 ? (long?)scene.Id("workshopId") : null;
        var candidates = new List<CandidateNodeDto>();

        // 1. 终端箱候选
        var boxSql = @"
            select tb.id, tb.name, tb.code, tb.location, tb.area_id, a.name as area_name,
                   exists(select 1 from map_coordinates mc where mc.scene_id = @sceneId and mc.target_type = 'terminal_box' and mc.target_id = tb.id) as is_placed
            from terminal_boxes tb
            left join areas a on tb.area_id = a.id
            where (@wsId is null or a.parent_id = @wsId)
            order by tb.code, tb.id;";

        var boxRows = await db.QueryAsync(boxSql, new { sceneId, wsId = workshopId }, ct);
        foreach (var r in boxRows)
        {
            candidates.Add(new CandidateNodeDto(
                "terminal_box",
                r.Id("id"),
                r.Text("name"),
                r.Text("code"),
                null,
                r.Text("location"),
                r.Id("areaId") > 0 ? r.Id("areaId") : null,
                r.Text("areaName"),
                r.Flag("isPlaced")
            ));
        }

        // 2. 监控摄像头候选
        var camSql = @"
            select c.id, c.name, coalesce(c.alias, c.name) as code, c.ip, coalesce(c.remark, '') as location, u.parent_id as area_id, a.name as area_name,
                   exists(select 1 from map_coordinates mc where mc.scene_id = @sceneId and mc.target_type = 'channel' and mc.target_id = c.id) as is_placed
            from channels c
            left join units u on c.unit_id = u.id
            left join areas a on u.parent_id = a.id
            where (@wsId is null or a.parent_id = @wsId)
            order by c.id;";

        var camRows = await db.QueryAsync(camSql, new { sceneId, wsId = workshopId }, ct);
        foreach (var r in camRows)
        {
            candidates.Add(new CandidateNodeDto(
                "channel",
                r.Id("id"),
                r.Text("name"),
                r.Text("code"),
                r.Text("ip"),
                r.Text("location"),
                r.Id("areaId") > 0 ? r.Id("areaId") : null,
                r.Text("areaName"),
                r.Flag("isPlaced")
            ));
        }

        // 3. 工位电脑候选
        var compSql = @"
            select cp.id, cp.name, null as code, cp.ip, cp.location, cp.area_id, a.name as area_name,
                   exists(select 1 from map_coordinates mc where mc.scene_id = @sceneId and mc.target_type = 'computer' and mc.target_id = cp.id) as is_placed
            from computers cp
            left join areas a on cp.area_id = a.id
            where (@wsId is null or cp.workshop_id = @wsId or a.parent_id = @wsId)
            order by cp.id;";

        var compRows = await db.QueryAsync(compSql, new { sceneId, wsId = workshopId }, ct);
        foreach (var r in compRows)
        {
            candidates.Add(new CandidateNodeDto(
                "computer",
                r.Id("id"),
                r.Text("name"),
                null,
                r.Text("ip"),
                r.Text("location"),
                r.Id("areaId") > 0 ? r.Id("areaId") : null,
                r.Text("areaName"),
                r.Flag("isPlaced")
            ));
        }

        // 4. 核心网络设备候选
        var netSql = @"
            select nd.id, nd.name, nd.device_type as code, nd.management_ip as ip, nd.location, nd.area_id, a.name as area_name,
                   exists(select 1 from map_coordinates mc where mc.scene_id = @sceneId and mc.target_type = 'network_device' and mc.target_id = nd.id) as is_placed
            from network_devices nd
            left join areas a on nd.area_id = a.id
            where (@wsId is null or nd.workshop_id = @wsId or a.parent_id = @wsId)
            order by nd.id;";

        var netRows = await db.QueryAsync(netSql, new { sceneId, wsId = workshopId }, ct);
        foreach (var r in netRows)
        {
            candidates.Add(new CandidateNodeDto(
                "network_device",
                r.Id("id"),
                r.Text("name"),
                r.Text("code"),
                r.Text("ip"),
                r.Text("location"),
                r.Id("areaId") > 0 ? r.Id("areaId") : null,
                r.Text("areaName"),
                r.Flag("isPlaced")
            ));
        }

        if (actor is null) return candidates;
        var allowed = await assets.AuthorizedRefsAsync(actor, ct);
        return candidates.Where(n => Visible(allowed, n.TargetType, n.TargetId)).ToArray();
    }

    public async Task<DigitalTwinCableDto> SaveCablePathAsync(long sceneId, SaveCablePathRequest req, long? cableId = null, string? actorUserId = null, CancellationToken ct = default)
    {
        Rules.Require(!string.IsNullOrWhiteSpace(req.Name), "光缆名称不能为空");

        if (cableId.HasValue && cableId.Value > 0)
        {
            const string updateSql = @"
                update map_cable_paths set
                    name = @name, cable_type = @cableType,
                    from_device_type = @fromDeviceType, from_device_id = @fromDeviceId,
                    to_device_type = @toDeviceType, to_device_id = @toDeviceId,
                    color = @color, points_json = cast(@pointsJson as jsonb),
                    status = @status, remarks = @remarks, updated_at = now()
                where id = @cableId and scene_id = @sceneId
                returning id;";

            await db.ExecuteAsync(updateSql, new
            {
                cableId = cableId.Value,
                sceneId,
                name = req.Name.Trim(),
                cableType = req.CableType,
                fromDeviceType = req.FromDeviceType,
                fromDeviceId = req.FromDeviceId,
                toDeviceType = req.ToDeviceType,
                toDeviceId = req.ToDeviceId,
                color = req.Color,
                pointsJson = string.IsNullOrWhiteSpace(req.PointsJson) ? "[]" : req.PointsJson,
                status = req.Status,
                remarks = req.Remarks
            }, ct);
        }
        else
        {
            const string insertSql = @"
                insert into map_cable_paths (scene_id, name, cable_type, from_device_type, from_device_id, to_device_type, to_device_id, color, points_json, status, remarks)
                values (@sceneId, @name, @cableType, @fromDeviceType, @fromDeviceId, @toDeviceType, @toDeviceId, @color, cast(@pointsJson as jsonb), @status, @remarks)
                returning id;";

            var row = await db.OneAsync(insertSql, new
            {
                sceneId,
                name = req.Name.Trim(),
                cableType = req.CableType,
                fromDeviceType = req.FromDeviceType,
                fromDeviceId = req.FromDeviceId,
                toDeviceType = req.ToDeviceType,
                toDeviceId = req.ToDeviceId,
                color = req.Color,
                pointsJson = string.IsNullOrWhiteSpace(req.PointsJson) ? "[]" : req.PointsJson,
                status = req.Status,
                remarks = req.Remarks
            }, ct);
            cableId = row?.Id("id");
        }

        var uId = long.TryParse(actorUserId, out var pId) ? pId : (long?)null;
        await audit.WriteAsync(uId, "map.cable.save", $"scene/{sceneId}", $"保存光缆路径: {req.Name}", ct: ct);
        var detail = await GetSceneDetailAsync(sceneId, ct);
        return detail.Cables.FirstOrDefault(c => c.Id == cableId)
            ?? throw new PlatformException(500, "cable.not_found", "光缆路径保存异常");
    }

    public async Task DeleteCablePathAsync(long cableId, string? actorUserId = null, CancellationToken ct = default)
    {
        const string sql = "delete from map_cable_paths where id = @cableId;";
        await db.ExecuteAsync(sql, new { cableId }, ct);
        var uId = long.TryParse(actorUserId, out var pId) ? pId : (long?)null;
        await audit.WriteAsync(uId, "map.cable.delete", $"cable/{cableId}", $"移除光缆走线: #{cableId}", ct: ct);
    }

    public async Task<int> AutoAlignCadAsync(long sceneId, string? actorUserId = null, CancellationToken ct = default)
    {
        var scene = await db.OneAsync("select scene_code, workshop_id from map_scenes where id = @sceneId", new { sceneId }, ct)
            ?? throw new PlatformException(404, "scene.not_found", "场景不存在");

        var sceneCode = scene.Text("sceneCode");
        var workshopId = scene.Id("workshopId") > 0 ? (long?)scene.Id("workshopId") : null;

        var itemsToSave = new List<SaveCoordinateRequest>();

        if (sceneCode == "pipe_making" || workshopId == 3)
        {
            // 制管车间 30 个终端箱 2.5D 等轴测空间网格精准对齐映射
            // 布局设计：
            // 西侧主柱列 (W1, W2): x_pct 从 0.24 到 0.38
            // 东侧主柱列 (E1, E2): x_pct 从 0.62 到 0.78
            // 纵向分布 (y_pct):
            // HG1~HG3 (北区/机组一区): y_pct = 0.16 ~ 0.38
            // HG4~HG6 (中区/机组二区): y_pct = 0.42 ~ 0.62
            // HG7~HG9 (南区/机组三区): y_pct = 0.66 ~ 0.84
            // SYJ (水压测试区): x_pct = 0.82 ~ 0.88, y_pct = 0.30 ~ 0.55
            // CKG (出口管区): x_pct = 0.84, y_pct = 0.75

            var boxRows = await db.QueryAsync(@"
                select tb.id, tb.code from terminal_boxes tb
                join areas a on tb.area_id = a.id
                where a.parent_id = 3
                order by tb.code;", ct: ct);

            foreach (var r in boxRows)
            {
                var boxId = r.Id("id");
                var code = r.Text("code").ToUpperInvariant();

                decimal x = 0.50m;
                decimal y = 0.50m;

                if (code.StartsWith("HG"))
                {
                    // 格式如 HG1-W1, HG7-E2
                    var parts = code.Split('-');
                    var numPart = parts[0].Replace("HG", "");
                    int.TryParse(numPart, out var lineNum);
                    if (lineNum <= 0) lineNum = 1;

                    var isWest = parts.Length > 1 && parts[1].StartsWith("W");
                    var isSecond = parts.Length > 1 && parts[1].EndsWith("2");

                    // 西列与东列 X 坐标
                    if (isWest)
                        x = isSecond ? 0.32m : 0.24m;
                    else
                        x = isSecond ? 0.74m : 0.64m;

                    // Y 轴平滑分布 (lineNum 1~9 映射到 0.16 ~ 0.84)
                    y = Math.Round(0.16m + (lineNum - 1) * 0.082m + (isSecond ? 0.02m : 0.0m), 4);
                }
                else if (code.StartsWith("SYJ"))
                {
                    var isWest = code.Contains("W");
                    var isSecond = code.EndsWith("2");
                    x = isWest ? 0.80m : 0.86m;
                    y = isSecond ? 0.46m : 0.32m;
                }
                else if (code.StartsWith("CKG"))
                {
                    x = 0.84m;
                    y = 0.76m;
                }

                itemsToSave.Add(new SaveCoordinateRequest("terminal_box", boxId, x, y, 1.5m, 0m, 60m, 25m, "box_cabinet", $"自动对齐 {code}"));
            }

            // 对齐下挂摄像头：根据与终端箱绑定关系，浮标自动环绕在终端箱周围
            var camRows = await db.QueryAsync(@"
                select c.id, c.name, b.terminal_box_id
                from channels c
                join channel_network_bindings b on b.channel_id = c.id
                join terminal_boxes tb on b.terminal_box_id = tb.id
                join areas a on tb.area_id = a.id
                where a.parent_id = 3;", ct: ct);

            var boxCamIndex = new Dictionary<long, int>();
            foreach (var c in camRows)
            {
                var chId = c.Id("id");
                var boxId = c.Id("terminalBoxId");
                var parentBox = itemsToSave.FirstOrDefault(item => item.TargetType == "terminal_box" && item.TargetId == boxId);
                if (parentBox is not null)
                {
                    boxCamIndex.TryGetValue(boxId, out var idx);
                    boxCamIndex[boxId] = idx + 1;

                    // 摄像头呈环形错开排列在箱体旁
                    var angle = (idx * 90) % 360;
                    var rad = angle * (Math.PI / 180.0);
                    var offsetX = (decimal)(Math.Cos(rad) * 0.025);
                    var offsetY = (decimal)(Math.Sin(rad) * 0.025);

                    itemsToSave.Add(new SaveCoordinateRequest(
                        "channel",
                        chId,
                        Math.Clamp(parentBox.XPct + offsetX, 0.05m, 0.95m),
                        Math.Clamp(parentBox.YPct + offsetY, 0.05m, 0.95m),
                        4.5m,
                        (decimal)angle,
                        60m,
                        25m,
                        "cam_bullet",
                        $"关联箱体 #{boxId}"
                    ));
                }
            }

            // 对齐工位电脑
            var compRows = await db.QueryAsync(@"
                select cp.id, cp.name, cp.terminal_box_id
                from computers cp
                join terminal_boxes tb on cp.terminal_box_id = tb.id
                join areas a on tb.area_id = a.id
                where a.parent_id = 3;", ct: ct);

            var boxCompIndex = new Dictionary<long, int>();
            foreach (var cp in compRows)
            {
                var compId = cp.Id("id");
                var boxId = cp.Id("terminalBoxId");
                var parentBox = itemsToSave.FirstOrDefault(item => item.TargetType == "terminal_box" && item.TargetId == boxId);
                if (parentBox is not null)
                {
                    boxCompIndex.TryGetValue(boxId, out var idx);
                    boxCompIndex[boxId] = idx + 1;

                    itemsToSave.Add(new SaveCoordinateRequest(
                        "computer",
                        compId,
                        Math.Clamp(parentBox.XPct + 0.035m, 0.05m, 0.95m),
                        Math.Clamp(parentBox.YPct + 0.015m * (idx + 1), 0.05m, 0.95m),
                        0.8m,
                        0m, 0m, 0m,
                        "pc_desktop",
                        $"工位电脑 关联箱体 #{boxId}"
                    ));
                }
            }
        }
        else if (sceneCode == "factory_overview")
        {
            // 全厂宏观总览：核心机房与各大车间地标标定
            // 总控核心 OLT (10.37.5.3) 与核心交换机
            var netDevRows = await db.QueryAsync("select id, name, management_ip from network_devices order by id;", ct: ct);
            foreach (var nd in netDevRows)
            {
                var devId = nd.Id("id");
                var ip = nd.Text("managementIp");
                if (ip == "10.37.5.3" || nd.Text("name").Contains("OLT"))
                {
                    itemsToSave.Add(new SaveCoordinateRequest("network_device", devId, 0.23m, 0.76m, 2.0m, 0m, 0m, 0m, "rack_core", "总控机房华为OLT核心网元"));
                }
                else if (ip == "10.37.4.1" || nd.Text("name").Contains("汇聚"))
                {
                    itemsToSave.Add(new SaveCoordinateRequest("network_device", devId, 0.25m, 0.74m, 2.0m, 0m, 0m, 0m, "rack_core", "总控机房核心交换机"));
                }
            }
        }

        var savedCount = await BatchSaveCoordinatesAsync(sceneId, new BatchSaveCoordinatesRequest(itemsToSave), actorUserId, ct);
        var uId = long.TryParse(actorUserId, out var pId) ? pId : (long?)null;
        await audit.WriteAsync(uId, "map.auto_align", $"scene/{sceneId}", $"智能自动吸附对齐空间点位: {savedCount} 个", ct: ct);
        return savedCount;
    }
}
