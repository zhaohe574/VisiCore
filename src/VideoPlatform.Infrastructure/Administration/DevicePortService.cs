using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure;

public sealed class DevicePortService(Database db, AuditStore audit)
{
    public async Task<IReadOnlyList<DevicePortDto>> ListPortsAsync(string deviceType, long deviceId, CancellationToken ct = default, Actor? actor = null)
    {
        var sql = @"
            select dp.id, dp.device_id, dp.device_type, dp.port_name, dp.port_type, dp.speed, dp.status,
                   dp.slot_number, dp.port_index, dp.remarks,
                   dl.id as link_id, dl.local_port_id, dl.peer_port_id, dl.cable_type, dl.cable_label, dl.remarks as link_remarks,
                   peer_dp.id as peer_actual_port_id, peer_dp.device_id as peer_dev_id, peer_dp.device_type as peer_dev_type, peer_dp.port_name as peer_port_name,
                   coalesce(peer_nd.name, peer_onu.onu_name, peer_comp.name, '未知设备') as peer_dev_name
            from device_ports dp
            left join device_links dl on (dl.local_port_id = dp.id or dl.peer_port_id = dp.id)
            left join device_ports peer_dp on (
                case when dl.local_port_id = dp.id then dl.peer_port_id else dl.local_port_id end = peer_dp.id
            )
            left join network_devices peer_nd on (peer_dp.device_type = 'network_device' and peer_nd.id = peer_dp.device_id)
            left join onu_devices peer_onu on (peer_dp.device_type = 'onu_device' and peer_onu.id = peer_dp.device_id)
            left join computers peer_comp on (peer_dp.device_type = 'computer' and peer_comp.id = peer_dp.device_id)
            where dp.device_type = @deviceType and dp.device_id = @deviceId
            order by dp.port_index asc, dp.id asc";

        if (actor is not null) sql = sql.Replace("dp.slot_number,", $"({AssetScope.PortPredicate("peer_dp")}) as peer_allowed, dp.slot_number,", StringComparison.Ordinal);
        var rows = await db.QueryAsync(sql, new { deviceType, deviceId, userId = actor?.UserId }, ct);
        return rows.Select(row =>
        {
            var port = ToPortDto(row);
            return actor is not null && port.Link is { } link && !row.Flag("peerAllowed")
                ? port with { Link = link with { PeerPortId = 0, PeerDeviceId = 0, PeerDeviceName = "授权范围外资产", PeerPortName = "", PeerDeviceType = "restricted", CableLabel = null, Remarks = null } } : port;
        }).ToArray();
    }

    public async Task<IReadOnlyList<DevicePortDto>> BatchGeneratePortsAsync(string deviceType, long deviceId, BatchGeneratePortsRequest req, string actor, string? ip, CancellationToken ct = default)
    {
        await db.TransactionAsync(async tx =>
        {
            if (req.ReplaceExisting)
            {
                // 先删除该设备现有的端口（级联删除关联的链路）
                await tx.ExecuteAsync("delete from device_ports where device_type = @deviceType and device_id = @deviceId", new { deviceType, deviceId }, ct);
            }

            // 获取当前最大 port_index
            var maxIndexRow = await tx.OneAsync("select coalesce(max(port_index), 0) as max_idx from device_ports where device_type = @deviceType and device_id = @deviceId", new { deviceType, deviceId }, ct);
            var currentIndex = (int)(maxIndexRow?.Id("max_idx") ?? 0);

            // 1. 生成电口 (Copper)
            for (var i = 1; i <= Math.Max(0, req.CopperCount); i++)
            {
                currentIndex++;
                var portName = $"{req.CopperPrefix}{i}";
                await tx.ExecuteAsync(@"
                    insert into device_ports(device_id, device_type, port_name, port_type, speed, status, port_index)
                    values(@deviceId, @deviceType, @portName, 'copper', @speed, 'unused', @currentIndex)
                    on conflict(device_type, device_id, port_name) do update set speed = excluded.speed, port_type = 'copper'",
                    new { deviceId, deviceType, portName, speed = req.CopperSpeed, currentIndex }, ct);
            }

            // 2. 生成光口 (Fiber)
            for (var i = 1; i <= Math.Max(0, req.FiberCount); i++)
            {
                currentIndex++;
                var portName = $"{req.FiberPrefix}{i}";
                await tx.ExecuteAsync(@"
                    insert into device_ports(device_id, device_type, port_name, port_type, speed, status, port_index)
                    values(@deviceId, @deviceType, @portName, 'fiber', @speed, 'unused', @currentIndex)
                    on conflict(device_type, device_id, port_name) do update set speed = excluded.speed, port_type = 'fiber'",
                    new { deviceId, deviceType, portName, speed = req.FiberSpeed, currentIndex }, ct);
            }

            // 3. 生成电话/语音口 (POTS)
            for (var i = 1; i <= Math.Max(0, req.PotsCount); i++)
            {
                currentIndex++;
                var portName = $"{req.PotsPrefix}{i}";
                await tx.ExecuteAsync(@"
                    insert into device_ports(device_id, device_type, port_name, port_type, speed, status, port_index)
                    values(@deviceId, @deviceType, @portName, 'pots', 'other', 'unused', @currentIndex)
                    on conflict(device_type, device_id, port_name) do update set speed = 'other', port_type = 'pots'",
                    new { deviceId, deviceType, portName, currentIndex }, ct);
            }

            return true;
        }, ct);

        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "device_port.batch_generate", $"{deviceType}:{deviceId}",
            $"批量生成端口：电口{req.CopperCount}个，光口{req.FiberCount}个，电话口{req.PotsCount}个", ip, ct);

        return await ListPortsAsync(deviceType, deviceId, ct);
    }

    public async Task<DevicePortDto> CreatePortAsync(string deviceType, long deviceId, DevicePortRequest req, string actor, string? ip, CancellationToken ct = default)
    {
        var row = await db.OneAsync(@"
            insert into device_ports(device_id, device_type, port_name, port_type, speed, status, slot_number, port_index, remarks)
            values(@deviceId, @deviceType, @portName, @portType, @speed, @status, @slotNumber, @portIndex, @remarks)
            returning id",
            new
            {
                deviceId,
                deviceType,
                portName = req.PortName.Trim(),
                portType = req.PortType.Trim(),
                speed = req.Speed.Trim(),
                status = req.Status.Trim(),
                slotNumber = req.SlotNumber,
                portIndex = req.PortIndex,
                remarks = req.Remarks?.Trim()
            }, ct) ?? throw new PlatformException(500, "db.error", "创建端口失败");

        var portId = row.Id("id");
        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "device_port.create", portId.ToString(), $"新增端口：{req.PortName} ({req.PortType})", ip, ct);

        var ports = await ListPortsAsync(deviceType, deviceId, ct);
        return ports.First(p => p.Id == portId);
    }

    public async Task<DevicePortDto> UpdatePortAsync(long portId, DevicePortRequest req, string actor, string? ip, CancellationToken ct = default)
    {
        var existing = await db.OneAsync("select device_type, device_id from device_ports where id = @portId", new { portId }, ct)
            ?? throw new PlatformException(404, "port.not_found", "端口不存在");

        await db.ExecuteAsync(@"
            update device_ports
            set port_name = @portName, port_type = @portType, speed = @speed,
                status = @status, slot_number = @slotNumber, port_index = @portIndex,
                remarks = @remarks, updated_at = now()
            where id = @portId",
            new
            {
                portId,
                portName = req.PortName.Trim(),
                portType = req.PortType.Trim(),
                speed = req.Speed.Trim(),
                status = req.Status.Trim(),
                slotNumber = req.SlotNumber,
                portIndex = req.PortIndex,
                remarks = req.Remarks?.Trim()
            }, ct);

        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "device_port.update", portId.ToString(), $"更新端口：{req.PortName}", ip, ct);

        var ports = await ListPortsAsync(existing.Text("deviceType"), existing.Id("deviceId"), ct);
        return ports.First(p => p.Id == portId);
    }

    public async Task DeletePortAsync(long portId, string actor, string? ip, CancellationToken ct = default)
    {
        await db.ExecuteAsync("delete from device_ports where id = @portId", new { portId }, ct);
        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "device_port.delete", portId.ToString(), "删除端口", ip, ct);
    }

    public async Task<DeviceLinkDto> ConnectLinkAsync(ConnectLinkRequest req, string actor, string? ip, CancellationToken ct = default)
    {
        if (req.LocalPortId == req.PeerPortId)
            throw new PlatformException(400, "link.self", "不可将端口连接到自身");

        var localPort = await db.OneAsync("select id, device_type, device_id, port_name from device_ports where id = @id", new { id = req.LocalPortId }, ct)
            ?? throw new PlatformException(404, "port.not_found", "本端端口不存在");
        var peerPort = await db.OneAsync("select id, device_type, device_id, port_name from device_ports where id = @id", new { id = req.PeerPortId }, ct)
            ?? throw new PlatformException(404, "port.not_found", "对端端口不存在");

        var linkId = await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002030)", ct: ct);
            var previous = await tx.QueryAsync("select id,local_port_id,peer_port_id from device_links where local_port_id=any(@ports) or peer_port_id=any(@ports) for update", new { ports = new[] { req.LocalPortId, req.PeerPortId } }, ct);
            Rules.Require(previous.Count == 0 || req.ReplaceExisting, "端口已存在连接，请核对后显式选择替换", "asset.portConflict", 409);
            foreach (var link in previous)
                await AssetMutationService.AuditAsync(tx, long.TryParse(actor, out var replacingUser) ? replacingUser : null, "device_link.replace", link.Id().ToString(), $"替换连接：端口 {link.Id("localPortId")} → {link.Id("peerPortId")}", ip, ct);
            // 显式替换时在同一事务中释放原连接。
            await tx.ExecuteAsync("delete from device_links where local_port_id = @p or peer_port_id = @p", new { p = req.LocalPortId }, ct);
            await tx.ExecuteAsync("delete from device_links where local_port_id = @p or peer_port_id = @p", new { p = req.PeerPortId }, ct);

            var row = await tx.OneAsync(@"
                insert into device_links(local_port_id, peer_port_id, cable_type, cable_label, remarks)
                values(@localPortId, @peerPortId, @cableType, @cableLabel, @remarks)
                returning id",
                new
                {
                    localPortId = req.LocalPortId,
                    peerPortId = req.PeerPortId,
                    cableType = req.CableType.Trim(),
                    cableLabel = req.CableLabel?.Trim(),
                    remarks = req.Remarks?.Trim()
                }, ct) ?? throw new PlatformException(500, "db.error", "创建跳线链路失败");

            // 更新两端端口状态为 up
            await tx.ExecuteAsync("update device_ports set status = 'up' where id in (@l, @p) and status = 'unused'", new { l = req.LocalPortId, p = req.PeerPortId }, ct);
            await AssetMutationService.AuditAsync(tx, long.TryParse(actor, out var uid) ? uid : null,
                "device_link.connect", row.Id().ToString(), $"建立跳线互联：端口 {req.LocalPortId} → 端口 {req.PeerPortId}（{req.CableType}）", ip, ct);

            return row.Id("id");
        }, ct);

        var ports = await ListPortsAsync(localPort.Text("deviceType"), localPort.Id("deviceId"), ct);
        var target = ports.FirstOrDefault(p => p.Id == req.LocalPortId);
        return target?.Link ?? throw new PlatformException(500, "db.error", "获取新建跳线信息失败");
    }

    public async Task DisconnectLinkAsync(long linkId, string actor, string? ip, CancellationToken ct = default)
    {
        var link = await db.OneAsync("select local_port_id, peer_port_id from device_links where id = @linkId", new { linkId }, ct);
        if (link is not null)
        {
            await db.ExecuteAsync("delete from device_links where id = @linkId", new { linkId }, ct);
            await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "device_link.disconnect", linkId.ToString(), "断开跳线互联", ip, ct);
        }
    }

    public async Task<IReadOnlyList<PortCandidateDto>> GetPortCandidatesAsync(string? search, string? deviceType, CancellationToken ct = default, Actor? actor = null)
    {
        var where = "1=1";
        if (actor is not null) where += $" and {AssetScope.PortPredicate("dp")}";
        if (!string.IsNullOrWhiteSpace(search))
            where += " and (dp.port_name ilike @search or coalesce(nd.name,'') ilike @search or coalesce(onu.onu_name,'') ilike @search or coalesce(comp.name,'') ilike @search)";
        if (!string.IsNullOrWhiteSpace(deviceType))
            where += " and dp.device_type = @deviceType";

        var sql = $@"
            select dp.id as port_id, dp.device_id, dp.device_type, dp.port_name, dp.port_type, dp.speed,
                   coalesce(nd.name, onu.onu_name, comp.name, '未知设备') as dev_name,
                   dl.id as link_id,
                   case when dl.id is not null then
                        concat('已接至: ', coalesce(peer_nd.name, peer_onu.onu_name, peer_comp.name, '对端'), ' (', peer_dp.port_name, ')')
                   else null end as linked_peer_desc
            from device_ports dp
            left join network_devices nd on (dp.device_type = 'network_device' and nd.id = dp.device_id)
            left join onu_devices onu on (dp.device_type = 'onu_device' and onu.id = dp.device_id)
            left join computers comp on (dp.device_type = 'computer' and comp.id = dp.device_id)
            left join device_links dl on (dl.local_port_id = dp.id or dl.peer_port_id = dp.id)
            left join device_ports peer_dp on (
                case when dl.local_port_id = dp.id then dl.peer_port_id else dl.local_port_id end = peer_dp.id
            )
            left join network_devices peer_nd on (peer_dp.device_type = 'network_device' and peer_nd.id = peer_dp.device_id)
            left join onu_devices peer_onu on (peer_dp.device_type = 'onu_device' and peer_onu.id = peer_dp.device_id)
            left join computers peer_comp on (peer_dp.device_type = 'computer' and peer_comp.id = peer_dp.device_id)
            where {where}
            order by dev_name asc, dp.port_index asc, dp.id asc
            limit 200";

        if (actor is not null) sql = sql.Replace("dl.id as link_id,", $"dl.id as link_id, ({AssetScope.PortPredicate("peer_dp")}) as peer_allowed,", StringComparison.Ordinal);
        var rows = await db.QueryAsync(sql, new { search = $"%{search?.Trim()}%", deviceType, userId = actor?.UserId }, ct);
        return rows.Select(r => new PortCandidateDto(
            r.Id("portId"),
            r.Id("deviceId"),
            r.Text("deviceType"),
            r.Text("devName"),
            r.Text("portName"),
            r.Text("portType"),
            r.Text("speed"),
            r.TryGetPropertyValue("linkId", out var lid) && lid != null,
            actor is null || r.Flag("peerAllowed") ? r.Text("linkedPeerDesc") : "已连接授权范围外资产"
        )).ToArray();
    }

    private static DevicePortDto ToPortDto(JsonObject r)
    {
        DeviceLinkDto? link = null;
        if (r.TryGetPropertyValue("linkId", out var lid) && lid != null)
        {
            link = new DeviceLinkDto(
                r.Id("linkId"),
                r.Id("localPortId"),
                r.Id("peerPortId"),
                r.Text("peerDevType"),
                r.Id("peerDevId"),
                r.Text("peerDevName"),
                r.Text("peerPortName"),
                r.Text("cableType"),
                r.Text("cableLabel"),
                r.Text("linkRemarks")
            );
        }

        return new DevicePortDto(
            r.Id("id"),
            r.Id("deviceId"),
            r.Text("deviceType"),
            r.Text("portName"),
            r.Text("portType"),
            r.Text("speed"),
            r.Text("status"),
            (int)r.Id("slotNumber"),
            (int)r.Id("portIndex"),
            r.Text("remarks"),
            link
        );
    }
}
