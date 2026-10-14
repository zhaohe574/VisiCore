using System.Text.Json;
using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure.Pon;

public sealed class TerminalBoxService(Database db)
{
    public async Task<IReadOnlyList<TerminalBoxDto>> ListAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var rows = await db.QueryAsync($@"
            select 
                b.*,
                a.name as area_name,
                w.name as workshop_name,
                u.name as unit_name,
                (select count(*) from onu_devices where terminal_box_id = b.id) as onu_count,
                (select count(*) from channel_network_bindings where terminal_box_id = b.id) as channel_count,
                (select count(*) from computers where terminal_box_id = b.id) as computer_count
            from terminal_boxes b
            left join areas a on b.area_id = a.id
            left join workshops w on a.parent_id = w.id
            left join units u on b.unit_id = u.id
            where {(actor is null ? "true" : $"b.id in ({AssetScope.AllowedIds("terminal-box")})")}
            order by b.code, b.id", new { userId = actor?.UserId }, ct);

        return rows.Select(ToBoxDto).ToArray();
    }

    public async Task<TerminalBoxDto> GetAsync(long id, CancellationToken ct = default)
    {
        var row = await db.OneAsync(@"
            select 
                b.*,
                a.name as area_name,
                w.name as workshop_name,
                u.name as unit_name,
                (select count(*) from onu_devices where terminal_box_id = b.id) as onu_count,
                (select count(*) from channel_network_bindings where terminal_box_id = b.id) as channel_count,
                (select count(*) from computers where terminal_box_id = b.id) as computer_count
            from terminal_boxes b
            left join areas a on b.area_id = a.id
            left join workshops w on a.parent_id = w.id
            left join units u on b.unit_id = u.id
            where b.id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "terminal_box.not_found", "指定的终端箱不存在");

        return ToBoxDto(row);
    }

    public async Task<TerminalBoxDto> CreateAsync(TerminalBoxRequest request, CancellationToken ct = default)
    {
        var code = Rules.Text(request.Code, "箱体编号", 64);
        var name = Rules.Text(request.Name, "箱体名称", 128);
        var location = request.Location?.Trim();
        var remarks = request.Remarks?.Trim();
        var areaId = request.AreaId;
        var unitId = request.UnitId;

        if (areaId.HasValue && areaId.Value > 0)
        {
            var area = await db.OneAsync("select id from areas where id = @areaId", new { areaId }, ct)
                ?? throw new PlatformException(404, "area.not_found", "所选业务区域不存在");
        }
        else if (unitId.HasValue && unitId.Value > 0)
        {
            var unit = await db.OneAsync("select parent_id from units where id = @unitId", new { unitId }, ct)
                ?? throw new PlatformException(404, "unit.not_found", "所选组织单元不存在");
            areaId = unit.Id("parentId");
        }

        var existing = await db.OneAsync("select id from terminal_boxes where code = @code", new { code }, ct);
        Rules.Require(existing is null, $"箱体编号【{code}】已存在，不能重复", "terminal_box.code_duplicate", 409);

        var row = await db.OneAsync(@"
            insert into terminal_boxes(code, name, location, area_id, unit_id, remarks)
            values(@code, @name, @location, @areaId, @unitId, @remarks)
            returning id", new { code, name, location, areaId, unitId, remarks }, ct);

        return await GetAsync(row.Id(), ct);
    }

    public async Task<TerminalBoxDto> UpdateAsync(long id, TerminalBoxRequest request, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var code = Rules.Text(request.Code, "箱体编号", 64);
        var name = Rules.Text(request.Name, "箱体名称", 128);
        var location = request.Location?.Trim();
        var remarks = request.Remarks?.Trim();
        var areaId = request.AreaId;
        var unitId = request.UnitId;

        if (areaId.HasValue && areaId.Value > 0)
        {
            var area = await db.OneAsync("select id from areas where id = @areaId", new { areaId }, ct)
                ?? throw new PlatformException(404, "area.not_found", "所选业务区域不存在");
        }
        else if (unitId.HasValue && unitId.Value > 0)
        {
            var unit = await db.OneAsync("select parent_id from units where id = @unitId", new { unitId }, ct)
                ?? throw new PlatformException(404, "unit.not_found", "所选组织单元不存在");
            areaId = unit.Id("parentId");
        }

        var dup = await db.OneAsync("select id from terminal_boxes where code = @code and id <> @id", new { code, id }, ct);
        Rules.Require(dup is null, $"箱体编号【{code}】已被其他终端箱占用", "terminal_box.code_duplicate", 409);

        await db.ExecuteAsync(@"
            update terminal_boxes set
                code = @code,
                name = @name,
                location = @location,
                area_id = @areaId,
                unit_id = @unitId,
                remarks = @remarks,
                updated_at = now()
            where id = @id", new { id, code, name, location, areaId, unitId, remarks }, ct);

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        await db.TransactionAsync(async tx =>
        {
            // 解除所有绑定的 ONU 和 通道关联
            await tx.ExecuteAsync("update onu_devices set terminal_box_id = null where terminal_box_id = @id", new { id }, ct);
            await tx.ExecuteAsync("update channel_network_bindings set terminal_box_id = null where terminal_box_id = @id", new { id }, ct);
            await tx.ExecuteAsync("delete from terminal_boxes where id = @id", new { id }, ct);
            return true;
        }, ct);
    }

    public async Task<IReadOnlyList<TerminalBoxTopologyDto>> GetTopologyTreeAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var boxes = await ListAsync(ct, actor);
        var onus = await db.QueryAsync($@"
            select * from onu_devices where terminal_box_id is not null and {(actor is null ? "true" : $"id in ({AssetScope.AllowedIds("onu")})")} order by onu_name", new { userId = actor?.UserId }, ct);

        var bindings = await db.QueryAsync($@"
            select 
                b.*,
                c.name as channel_name,
                c.ip as channel_ip,
                c.status as channel_status
            from channel_network_bindings b
            join channels c on b.channel_id = c.id
            where b.onu_device_id is not null and {(actor is null ? "true" : $"c.id in (select c.id from {AccessService.ChannelFrom} where {AccessService.ChannelPredicate})")}", new { userId = actor?.UserId }, ct);

        var bindingMap = bindings
            .GroupBy(b => (b.Id("onuDeviceId"), (int)b.Id("portNumber"), b.Flag("isSwitchPort")))
            .ToDictionary(g => g.Key, g => g.First());

        var computers = await db.QueryAsync($@"
            select 
                c.id,
                c.name,
                c.ip,
                c.mac,
                c.owner,
                c.department,
                c.status,
                c.terminal_box_id,
                c.onu_device_id,
                c.port_number,
                c.is_switch_port,
                c.port_label
            from computers c
            where c.onu_device_id is not null and {(actor is null ? "true" : $"c.id in ({AssetScope.AllowedIds("computer")})")}", new { userId = actor?.UserId }, ct);

        var computerMap = computers
            .GroupBy(c => (c.Id("onuDeviceId"), (int)c.Id("portNumber"), c.Flag("isSwitchPort")))
            .ToDictionary(g => g.Key, g => g.First());

        var boundDevices = await db.QueryAsync(@"
            select 
                d.id,
                d.name,
                d.host,
                d.status,
                d.model,
                d.onu_device_id,
                d.port_number,
                d.is_switch_port
            from devices d
            where d.onu_device_id is not null", ct: ct);

        var deviceMap = boundDevices
            .GroupBy(d => (d.Id("onuDeviceId"), (int)d.Id("portNumber"), d.Flag("isSwitchPort")))
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<TerminalBoxTopologyDto>();

        foreach (var box in boxes)
        {
            var boxOnus = onus.Where(o => o.Id("terminalBoxId") == box.Id).ToList();
            var onuNodes = new List<NetworkTopologyOnuDto>();

            foreach (var o in boxOnus)
            {
                var onuId = o.Id();
                var hasSubSwitch = o.Flag("hasSubSwitch");
                var subSwitchModel = o.Text("subSwitchModel");
                var subSwitchPorts = (int)o.Id("subSwitchPorts");
                var ports = new List<NetworkTopologyPortDto>();

                // 1. 光猫自带 LAN 口 (通常 4 口)
                for (var p = 1; p <= 4; p++)
                {
                    bindingMap.TryGetValue((onuId, p, false), out var b);
                    computerMap.TryGetValue((onuId, p, false), out var comp);
                    deviceMap.TryGetValue((onuId, p, false), out var dev);
                    ports.Add(new NetworkTopologyPortDto(
                        p,
                        false,
                        $"ONU-LAN{p}",
                        b?.Id("channelId") == 0 ? null : b?.Id("channelId"),
                        b?.Text("channelName"),
                        b?.Text("channelIp"),
                        b?.Text("channelStatus"),
                        comp?.Id() == 0 ? null : comp?.Id(),
                        comp?.Text("name"),
                        comp?.Text("ip"),
                        comp?.Text("status"),
                        comp?.Text("owner"),
                        comp?.Text("department"),
                        dev?.Id() == 0 ? null : dev?.Id(),
                        dev?.Text("name"),
                        dev?.Text("host"),
                        dev?.Text("status"),
                        dev?.Text("model")
                    ));
                }

                // 2. 下挂交换机端口 (如果配置了交换机)
                if (hasSubSwitch && subSwitchPorts > 0)
                {
                    for (var sp = 1; sp <= subSwitchPorts; sp++)
                    {
                        bindingMap.TryGetValue((onuId, sp, true), out var b);
                        computerMap.TryGetValue((onuId, sp, true), out var comp);
                        deviceMap.TryGetValue((onuId, sp, true), out var dev);
                        ports.Add(new NetworkTopologyPortDto(
                            sp,
                            true,
                            $"SW-P{sp}",
                            b?.Id("channelId") == 0 ? null : b?.Id("channelId"),
                            b?.Text("channelName"),
                            b?.Text("channelIp"),
                            b?.Text("channelStatus"),
                            comp?.Id() == 0 ? null : comp?.Id(),
                            comp?.Text("name"),
                            comp?.Text("ip"),
                            comp?.Text("status"),
                            comp?.Text("owner"),
                            comp?.Text("department"),
                            dev?.Id() == 0 ? null : dev?.Id(),
                            dev?.Text("name"),
                            dev?.Text("host"),
                            dev?.Text("status"),
                            dev?.Text("model")
                        ));
                    }
                }

                onuNodes.Add(new NetworkTopologyOnuDto(
                    onuId,
                    o.Text("onuName"),
                    o.Text("alias"),
                    o.Text("sn"),
                    o.Text("terminalType", "EG8040C-10"),
                    o.Text("status") is "1" or "online" ? "online" : "offline",
                    o.Decimal("rxPower"),
                    hasSubSwitch,
                    subSwitchModel,
                    subSwitchPorts,
                    ports
                ));
            }

            result.Add(new TerminalBoxTopologyDto(
                box.Id,
                box.Code,
                box.Name,
                box.Location,
                box.AreaId,
                box.AreaName,
                onuNodes,
                box.UnitId,
                box.UnitName
            ));
        }

        return result;
    }

    public async Task<TerminalBoxTopologyDto?> GetTopologyByIdAsync(long id, CancellationToken ct = default, Actor? actor = null)
    {
        if (actor is not null) return (await GetTopologyTreeAsync(ct, actor)).FirstOrDefault(box => box.Id == id);
        var boxRow = await db.OneAsync(@"
            select tb.*, a.name as area_name, w.name as workshop_name, u.name as unit_name,
                   (select count(*) from onu_devices where terminal_box_id = tb.id) as onu_count,
                   (select count(*) from channel_network_bindings where terminal_box_id = tb.id) as channel_count,
                   (select count(*) from computers where terminal_box_id = tb.id) as computer_count
            from terminal_boxes tb
            left join areas a on tb.area_id = a.id
            left join workshops w on a.parent_id = w.id
            left join units u on tb.unit_id = u.id
            where tb.id = @id", new { id }, ct);
        if (boxRow is null) return null;

        var box = ToBoxDto(boxRow);
        var onus = await db.QueryAsync(@"
            select * from onu_devices where terminal_box_id = @id order by onu_name", new { id }, ct);

        var bindings = await db.QueryAsync(@"
            select 
                b.*,
                c.name as channel_name,
                c.ip as channel_ip,
                c.status as channel_status
            from channel_network_bindings b
            join channels c on b.channel_id = c.id
            where b.terminal_box_id = @id", new { id }, ct);

        var bindingMap = bindings
            .GroupBy(b => (b.Id("onuDeviceId"), (int)b.Id("portNumber"), b.Flag("isSwitchPort")))
            .ToDictionary(g => g.Key, g => g.First());

        var computers = await db.QueryAsync(@"
            select 
                c.id,
                c.name,
                c.ip,
                c.mac,
                c.owner,
                c.department,
                c.status,
                c.terminal_box_id,
                c.onu_device_id,
                c.port_number,
                c.is_switch_port,
                c.port_label
            from computers c
            where c.terminal_box_id = @id", new { id }, ct);

        var computerMap = computers
            .GroupBy(c => (c.Id("onuDeviceId"), (int)c.Id("portNumber"), c.Flag("isSwitchPort")))
            .ToDictionary(g => g.Key, g => g.First());

        var boundDevices = await db.QueryAsync(@"
            select 
                d.id,
                d.name,
                d.host,
                d.status,
                d.model,
                d.onu_device_id,
                d.port_number,
                d.is_switch_port
            from devices d
            where d.terminal_box_id = @id", new { id }, ct);

        var deviceMap = boundDevices
            .GroupBy(d => (d.Id("onuDeviceId"), (int)d.Id("portNumber"), d.Flag("isSwitchPort")))
            .ToDictionary(g => g.Key, g => g.First());

        var onuNodes = new List<NetworkTopologyOnuDto>();
        foreach (var o in onus)
        {
            var onuId = o.Id();
            var hasSubSwitch = o.Flag("hasSubSwitch");
            var subSwitchModel = o.Text("subSwitchModel");
            var subSwitchPorts = (int)o.Id("subSwitchPorts");
            var ports = new List<NetworkTopologyPortDto>();

            for (var p = 1; p <= 4; p++)
            {
                bindingMap.TryGetValue((onuId, p, false), out var b);
                computerMap.TryGetValue((onuId, p, false), out var comp);
                deviceMap.TryGetValue((onuId, p, false), out var dev);
                ports.Add(new NetworkTopologyPortDto(
                    p,
                    false,
                    $"ONU-LAN{p}",
                    b?.Id("channelId") == 0 ? null : b?.Id("channelId"),
                    b?.Text("channelName"),
                    b?.Text("channelIp"),
                    b?.Text("channelStatus"),
                    comp?.Id() == 0 ? null : comp?.Id(),
                    comp?.Text("name"),
                    comp?.Text("ip"),
                    comp?.Text("status"),
                    comp?.Text("owner"),
                    comp?.Text("department"),
                    dev?.Id() == 0 ? null : dev?.Id(),
                    dev?.Text("name"),
                    dev?.Text("host"),
                    dev?.Text("status"),
                    dev?.Text("model")
                ));
            }

            if (hasSubSwitch && subSwitchPorts > 0)
            {
                for (var sp = 1; sp <= subSwitchPorts; sp++)
                {
                    bindingMap.TryGetValue((onuId, sp, true), out var b);
                    computerMap.TryGetValue((onuId, sp, true), out var comp);
                    deviceMap.TryGetValue((onuId, sp, true), out var dev);
                    ports.Add(new NetworkTopologyPortDto(
                        sp,
                        true,
                        $"SW-P{sp}",
                        b?.Id("channelId") == 0 ? null : b?.Id("channelId"),
                        b?.Text("channelName"),
                        b?.Text("channelIp"),
                        b?.Text("channelStatus"),
                        comp?.Id() == 0 ? null : comp?.Id(),
                        comp?.Text("name"),
                        comp?.Text("ip"),
                        comp?.Text("status"),
                        comp?.Text("owner"),
                        comp?.Text("department"),
                        dev?.Id() == 0 ? null : dev?.Id(),
                        dev?.Text("name"),
                        dev?.Text("host"),
                        dev?.Text("status"),
                        dev?.Text("model")
                    ));
                }
            }

            onuNodes.Add(new NetworkTopologyOnuDto(
                onuId,
                o.Text("onuName"),
                o.Text("alias"),
                o.Text("sn"),
                o.Text("terminalType", "EG8040C-10"),
                o.Text("status") is "1" or "online" ? "online" : "offline",
                o.Decimal("rxPower"),
                hasSubSwitch,
                subSwitchModel,
                subSwitchPorts,
                ports
            ));
        }

        return new TerminalBoxTopologyDto(
            box.Id,
            box.Code,
            box.Name,
            box.Location,
            box.AreaId,
            box.AreaName,
            onuNodes,
            box.UnitId,
            box.UnitName
        );
    }

    private static TerminalBoxDto ToBoxDto(JsonObject r)
    {
        var areaName = r.Text("areaName");
        var wsName = r.Text("workshopName");
        var fullAreaName = !string.IsNullOrEmpty(wsName) && !string.IsNullOrEmpty(areaName)
            ? $"{wsName} / {areaName}"
            : areaName;

        return new TerminalBoxDto(
            r.Id("id"),
            r.Text("code"),
            r.Text("name"),
            r.Text("location"),
            r.Id("areaId") == 0 ? null : r.Id("areaId"),
            fullAreaName,
            r.Text("remarks"),
            r.Id("onuCount"),
            r.Id("channelCount"),
            r.NullableTime("createdAt") ?? DateTimeOffset.UtcNow,
            r.NullableTime("updatedAt") ?? DateTimeOffset.UtcNow,
            r.Id("unitId") == 0 ? null : r.Id("unitId"),
            r.Text("unitName") ?? fullAreaName,
            r.Id("computerCount")
        );
    }
}
