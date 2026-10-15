using System.Globalization;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed partial class AdministrationService
{
    public async Task<OrganizationTreeDto> OrganizationAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "area.read", ct);
        var rows = await db.QueryAsync(ScopeCte + """
            select 'workshop' as type,id,name,code,status,null::bigint as parent_id from visible_workshops
            union all select 'area',id,name,code,status,parent_id from visible_areas
            union all select 'unit',id,name,code,status,parent_id from visible_units
            order by type, code, id
            """, new { userId = actor.UserId }, ct);
        OrganizationNodeDto[] Nodes(string type) => rows.Where(r => r.Text("type") == type)
            .OrderBy(r => r, OrganizationCodeComparer.RowInstance)
            .Select(ToNode).ToArray();
        return new(Nodes("workshop"), Nodes("area"), Nodes("unit"));
    }

    public async Task<OrganizationNodeDto> SaveOrganizationAsync(Actor actor, string kind, long? id, OrganizationRequest request, string? ip, CancellationToken ct = default)
    {
        var table = OrganizationTable(kind);
        var name = Rules.Text(request.Name, "组织名称");
        var code = Rules.Text(request.Code, "组织编码", 64);
        ValidateStatus(request.Status);
        Rules.Require(kind == "workshops" ? request.ParentId is null : request.ParentId is > 0, "组织父级不正确");
        var result = await WriteAsync(actor, "area.manage", async tx =>
        {
            JsonObject? previous = null;
            if (id is not null)
            {
                previous = await RequiredAsync(tx, table, id.Value, ct);
                await DemandNodeScopeAsync(tx, actor.UserId, kind, id.Value, ct);
            }
            else if (kind == "workshops") await DemandFullScopeAsync(tx, actor.UserId, ct);
            if (request.ParentId is { } parentId)
            {
                var parentKind = kind == "areas" ? "workshops" : "areas";
                var parent = await RequiredAsync(tx, parentKind, parentId, ct);
                Rules.Require(parent.Text("status") == "active", "父级组织已停用", "organization.disabled", 409);
                if (previous is null || previous.Id("parentId") != parentId)
                    await DemandNodeScopeAsync(tx, actor.UserId, parentKind, parentId, ct);
            }
            var args = new { id, name, code, request.Status, request.ParentId };
            var sql = id is null
                ? $"insert into {table}(name,code,status{(kind == "workshops" ? "" : ",parent_id")}) values(@name,@code,@status{(kind == "workshops" ? "" : ",@parentId")}) returning *"
                : $"update {table} set name=@name,code=@code,status=@status{(kind == "workshops" ? "" : ",parent_id=@parentId")} where id=@id returning *";
            var row = (await tx.OneAsync(sql, args, ct))!;
            await AuditAsync(tx, actor, id is null ? "organization.create" : "organization.update", $"{kind}/{row.Id()}", $"组织：{name}，状态：{request.Status}", ip, ct);
            var changed = previous is not null && (previous.Text("status") != request.Status || previous.Id("parentId") != (request.ParentId ?? 0));
            var affected = changed ? await UsersWithInaccessibleResourcesAsync(tx, ct) : [];
            if (changed) await AccessChangedAsync(tx, await AllUserIdsAsync(tx, ct), ct);
            return (Node: ToNode(row), Affected: affected);
        }, ct: ct);
        await RevokeUsersAsync(result.Affected);
        return result.Node;
    }

    public Task DeleteOrganizationAsync(Actor actor, string kind, long id, string? ip, CancellationToken ct = default)
    {
        var table = OrganizationTable(kind);
        var scopeType = kind switch { "workshops" => "workshop", "areas" => "area", _ => "unit" };
        return WriteAsync(actor, "area.manage", async tx =>
        {
            var row = await RequiredAsync(tx, table, id, ct);
            await DemandNodeScopeAsync(tx, actor.UserId, kind, id, ct);
            var childSql = kind switch
            {
                "workshops" => "exists(select 1 from areas where parent_id=@id)",
                "areas" => "exists(select 1 from units where parent_id=@id)",
                _ => "exists(select 1 from channels where unit_id=@id)"
            };
            var refs = await tx.OneAsync($"select {childSql} or exists(select 1 from user_scopes where type=@scopeType and scope_id=@id) or exists(select 1 from role_scopes where type=@scopeType and scope_id=@id) as referenced", new { id, scopeType }, ct);
            Rules.Require(!refs.Flag("referenced"), "组织仍有子级、通道或授权范围引用，不能删除", "organization.referenced", 409);
            await tx.ExecuteAsync($"delete from {table} where id=@id", new { id }, ct);
            await AuditAsync(tx, actor, "organization.delete", $"{kind}/{id}", $"删除组织：{row.Text("name")}", ip, ct);
            return true;
        }, ct: ct);
    }

    public async Task AssignChannelsAsync(Actor actor, AssignmentRequest request, string? ip, CancellationToken ct = default)
    {
        var ids = ValidateIds(request.ChannelIds, 1000, false);
        Rules.Require(request.UnitId is null or > 0, "目标单元无效");
        var affected = await WriteAsync(actor, "channel.assign", async tx =>
        {
            await DemandChannelsAsync(tx, actor.UserId, ids, false, ct);
            if (request.UnitId is { } unitId)
            {
                var unit = await RequiredAsync(tx, "units", unitId, ct);
                Rules.Require(unit.Text("status") == "active", "目标单元已停用", "organization.disabled", 409);
                await DemandNodeScopeAsync(tx, actor.UserId, "units", unitId, ct);
            }
            var before = await tx.QueryAsync("select id,unit_id from channels where id=any(@ids) order by id for update", new { ids }, ct);
            await tx.ExecuteAsync("update channels set unit_id=@unitId,updated_at=now() where id=any(@ids)", new { request.UnitId, ids }, ct);
            // 每条变更都保留来源和目标，批量操作回滚时审计一并回滚。
            foreach (var row in before)
                await AuditAsync(tx, actor, "channel.assign", $"channels/{row.Id()}", $"单元：{row.Text("unitId", "未分配")} → {request.UnitId?.ToString(CultureInfo.InvariantCulture) ?? "未分配"}", ip, ct);
            var changed = before.Any(r => r.Id("unitId") != (request.UnitId ?? 0));
            var users = changed ? await UsersWithInaccessibleResourcesAsync(tx, ct) : [];
            if (changed) await AccessChangedAsync(tx, await AllUserIdsAsync(tx, ct), ct);
            return users;
        }, ct: ct);
        await RevokeUsersAsync(affected);
    }

    public async Task SortChannelsAsync(Actor actor, ChannelSortRequest request, string? ip, CancellationToken ct = default)
    {
        var ids = ValidateIds(request.ChannelIds, 1000, false);
        Rules.Require(request.UnitId > 0, "目标单元无效");
        await WriteAsync(actor, "channel.assign", async tx =>
        {
            var unit = await RequiredAsync(tx, "units", request.UnitId, ct);
            Rules.Require(unit.Text("status") == "active", "目标单元已停用", "organization.disabled", 409);
            await DemandNodeScopeAsync(tx, actor.UserId, "units", request.UnitId, ct);
            await DemandChannelsAsync(tx, actor.UserId, ids, false, ct);
            for (var i = 0; i < ids.Length; i++)
            {
                await tx.ExecuteAsync("update channels set sort_order=@sortOrder,updated_at=now() where id=@id and unit_id=@unitId", new { sortOrder = i + 1, id = ids[i], unitId = request.UnitId }, ct);
            }
            await AuditAsync(tx, actor, "channel.sort", $"units/{request.UnitId}/channels", $"更新单元通道排序：{string.Join(",", ids)}", ip, ct);
            await AccessChangedAsync(tx, await AllUserIdsAsync(tx, ct), ct);
            return (long[])[];
        }, ct: ct);
    }

    public async Task<ChannelDto> UpdateChannelAsync(Actor actor, long id, ChannelUpdateRequest request, string? ip, CancellationToken ct = default, Nvr.INvrChannelManager? nvr = null)
    {
        Rules.Require(request.Alias is null || request.Alias.Length <= 256, "别名长度超出限制");
        Rules.Require(request.Ip is null || request.Ip.Length <= 253, "IP地址长度超出限制");
        Rules.Require(request.Username is null || request.Username.Length <= 128, "账号长度超出限制");
        Rules.Require(request.Password is null || request.Password.Length <= 256, "密码长度超出限制");
        Rules.Require(request.UnitId is null or > 0, "目标单元无效");
        return await WriteAsync(actor, "channel.assign", async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002030)", ct: ct);
            await DemandChannelsAsync(tx, actor.UserId, [id], false, ct);
            if (request.UnitId is { } unitId)
            {
                var unit = await RequiredAsync(tx, "units", unitId, ct);
                Rules.Require(unit.Text("status") == "active", "目标单元已停用", "organization.disabled", 409);
                await DemandNodeScopeAsync(tx, actor.UserId, "units", unitId, ct);
            }
            var before = await RequiredAsync(tx, "channels", id, ct);

            // 增量/部分更新处理：若入参为 null 则保留原值；空字符串（""）表示清空别名/备注
            var alias = request.Alias switch
            {
                null => before.Text("alias"),
                "" => null,
                _ => request.Alias.Trim()
            };
            var channelIp = string.IsNullOrWhiteSpace(request.Ip)
                ? before.Text("ip")
                : request.Ip.Trim();
            var username = request.Username is null
                ? before.Text("username")
                : (string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim());
            // 访问密码：留空表示不修改，沿用原密码
            var password = request.ClearPassword ? null : string.IsNullOrWhiteSpace(request.Password)
                ? before.Text("password")
                : request.Password.Trim();
            var remark = request.Remark switch
            {
                null => before.Text("remark"),
                "" => null,
                _ => request.Remark.Trim()
            };

            var aliasChanged = request.Alias != null && alias != before.Text("alias");
            var ipChanged = !string.IsNullOrWhiteSpace(request.Ip) && channelIp != before.Text("ip");
            var userChanged = request.Username != null && username != before.Text("username");
            var pwdChanged = (request.ClearPassword || !string.IsNullOrWhiteSpace(request.Password)) && password != before.Text("password");
            if (ipChanged) await AssetMutationService.ValidateIpAsync(tx, channelIp, "channel", id, ct);
            if (request.TerminalBoxId.HasValue || request.OnuDeviceId.HasValue)
            {
                await AssetScope.DemandBindingAsync(tx, actor, request.TerminalBoxId, request.OnuDeviceId, ct);
                await AssetMutationService.ValidateNetworkBindingAsync(tx, "channel", id, request.TerminalBoxId,
                    request.OnuDeviceId, request.NetworkPort ?? 1, request.IsSwitchPort ?? false, ct);
            }

            var effectiveNvrManager = nvr ?? nvrManager;
            if (effectiveNvrManager != null && (aliasChanged || ipChanged || userChanged || pwdChanged))
            {
                var deviceId = before.Id("deviceId");
                var deviceChan = (int)before.Id("deviceChannel");
                var dev = await tx.OneAsync("select id, host, port, username, password_cipher, coalesce(plugin_id, 'hikvision') as plugin_id, enabled from devices where id = @deviceId", new { deviceId }, ct);
                if (dev != null && dev.Flag("enabled") && !PluginIds.IsInProcess(dev.Text("pluginId")))
                {
                    // 强制严格模式：向物理录像机下发成功才允许保存平台，失败立即阻断报错！
                    await effectiveNvrManager.UpdateIpcChannelAsync(
                        deviceId,
                        deviceChan,
                        channelIp,
                        null,
                        username,
                        request.ClearPassword ? "" : password,
                        null,
                        alias ?? before.Text("name"),
                        ct);
                }
            }

            if (request.UnitId.HasValue)
                await tx.ExecuteAsync("update channels set alias=@alias,unit_id=@unitId,ip=@channelIp,username=@username,password=@password,remark=@remark,updated_at=now() where id=@id", new { id, alias, unitId = request.UnitId.Value, channelIp, username, password, remark }, ct);
            else
                await tx.ExecuteAsync("update channels set alias=@alias,ip=@channelIp,username=@username,password=@password,remark=@remark,updated_at=now() where id=@id", new { id, alias, channelIp, username, password, remark }, ct);

            if (request.TerminalBoxId.HasValue || request.OnuDeviceId.HasValue)
            {
                var boxId = request.TerminalBoxId;
                var onuId = request.OnuDeviceId;
                var portNum = request.NetworkPort ?? 1;
                var isSwitch = request.IsSwitchPort ?? false;
                var label = request.PortLabel ?? (isSwitch ? $"SW-P{portNum}" : $"LAN{portNum}");
                await tx.ExecuteAsync(@"
                    insert into channel_network_bindings(channel_id, terminal_box_id, onu_device_id, port_number, is_switch_port, port_label)
                    values(@id, @boxId, @onuId, @portNum, @isSwitch, @label)
                    on conflict (channel_id) do update set
                        terminal_box_id = excluded.terminal_box_id,
                        onu_device_id = excluded.onu_device_id,
                        port_number = excluded.port_number,
                        is_switch_port = excluded.is_switch_port,
                        port_label = excluded.port_label,
                        updated_at = now()", new { id, boxId, onuId, portNum, isSwitch, label }, ct);
            }
            else if (request.ClearNetworkBinding == true)
            {
                await tx.ExecuteAsync("delete from channel_network_bindings where channel_id = @id", new { id }, ct);
            }

            await AuditAsync(tx, actor, "channel.update", $"channels/{id}", $"别名：{before["alias"]?.ToString() ?? "无"} → {alias ?? "无"}", ip, ct);
            var row = await tx.OneAsync($"select {AccessService.ChannelColumns} from {AccessService.ChannelFrom} where c.id=@id", new { id, userId = actor.UserId }, ct);
            return ToChannel(row!);
        }, ct: ct);
    }

    public async Task BatchSetNetworkBindingsAsync(Actor actor, BatchChannelNetworkBindingRequest request, string? ip, CancellationToken ct = default)
    {
        var ids = ValidateIds(request.ChannelIds, 1000, false);
        await WriteAsync(actor, "channel.assign", async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002030)", ct: ct);
            await DemandChannelsAsync(tx, actor.UserId, ids, false, ct);
            if (request.TerminalBoxId is { } boxId && boxId > 0)
            {
                var box = await tx.OneAsync("select id from terminal_boxes where id = @boxId", new { boxId }, ct);
                Rules.Require(box is not null, "指定的网络终端箱不存在", "terminal_box.not_found", 404);
            }
            if (request.OnuDeviceId is { } onuId && onuId > 0)
            {
                var onu = await tx.OneAsync("select id from onu_devices where id = @onuId", new { onuId }, ct);
                Rules.Require(onu is not null, "指定的承载 ONU 不存在", "onu.not_found", 404);
            }

            await AssetScope.DemandBindingAsync(tx, actor, request.TerminalBoxId, request.OnuDeviceId, ct);

            var startPort = request.StartPortNumber ?? 1;
            var isSwitch = request.IsSwitchPort ?? false;

            for (var i = 0; i < ids.Length; i++)
            {
                var channelId = ids[i];
                if (request.TerminalBoxId.HasValue || request.OnuDeviceId.HasValue)
                {
                    var portNum = startPort + i;
                    await AssetMutationService.ValidateNetworkBindingAsync(tx, "channel", channelId, request.TerminalBoxId, request.OnuDeviceId, portNum, isSwitch, ct);
                    var label = isSwitch ? $"SW-P{portNum}" : $"LAN{portNum}";
                    await tx.ExecuteAsync(@"
                        insert into channel_network_bindings(channel_id, terminal_box_id, onu_device_id, port_number, is_switch_port, port_label)
                        values(@channelId, @boxId, @onuId, @portNum, @isSwitch, @label)
                        on conflict (channel_id) do update set
                            terminal_box_id = excluded.terminal_box_id,
                            onu_device_id = excluded.onu_device_id,
                            port_number = excluded.port_number,
                            is_switch_port = excluded.is_switch_port,
                            port_label = excluded.port_label,
                            updated_at = now()",
                        new { channelId, boxId = request.TerminalBoxId, onuId = request.OnuDeviceId, portNum, isSwitch, label }, ct);
                }
                else
                {
                    await tx.ExecuteAsync("delete from channel_network_bindings where channel_id = @channelId", new { channelId }, ct);
                }
            }

            await AuditAsync(tx, actor, "channel.batch_network_binding", "channels/batch",
                $"批量设置 {ids.Length} 路通道网络承载：终端箱={request.TerminalBoxId}，ONU={request.OnuDeviceId}", ip, ct);
            return true;
        }, ct: ct);
    }

    public async Task BatchClaimChannelsAsync(Actor actor, BatchClaimChannelsRequest request, string? ip, CancellationToken ct = default)
    {
        var ids = ValidateIds(request.ChannelIds, 1000, false);
        Rules.Require(request.UnitId > 0, "目标组织单元无效");
        var affected = await WriteAsync(actor, "channel.assign", async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002030)", ct: ct);
            var unit = await RequiredAsync(tx, "units", request.UnitId, ct);
            Rules.Require(unit.Text("status") == "active", "目标单元已停用", "organization.disabled", 409);
            await DemandNodeScopeAsync(tx, actor.UserId, "units", request.UnitId, ct);
            await DemandChannelsAsync(tx, actor.UserId, ids, false, ct);

            var before = await tx.QueryAsync("select id, unit_id from channels where id=any(@ids) order by id for update", new { ids }, ct);
            await tx.ExecuteAsync("update channels set unit_id=@unitId, updated_at=now() where id=any(@ids)", new { unitId = request.UnitId, ids }, ct);

            if (request.TerminalBoxId.HasValue || request.OnuDeviceId.HasValue)
            {
                await AssetScope.DemandBindingAsync(tx, actor, request.TerminalBoxId, request.OnuDeviceId, ct);
                var startPort = request.StartPortNumber ?? 1;
                var isSwitch = request.IsSwitchPort ?? false;
                for (var i = 0; i < ids.Length; i++)
                {
                    var channelId = ids[i];
                    var portNum = startPort + i;
                    await AssetMutationService.ValidateNetworkBindingAsync(tx, "channel", channelId, request.TerminalBoxId, request.OnuDeviceId, portNum, isSwitch, ct);
                    var label = isSwitch ? $"SW-P{portNum}" : $"LAN{portNum}";
                    await tx.ExecuteAsync(@"
                        insert into channel_network_bindings(channel_id, terminal_box_id, onu_device_id, port_number, is_switch_port, port_label)
                        values(@channelId, @boxId, @onuId, @portNum, @isSwitch, @label)
                        on conflict (channel_id) do update set
                            terminal_box_id = excluded.terminal_box_id,
                            onu_device_id = excluded.onu_device_id,
                            port_number = excluded.port_number,
                            is_switch_port = excluded.is_switch_port,
                            port_label = excluded.port_label,
                            updated_at = now()",
                        new { channelId, boxId = request.TerminalBoxId, onuId = request.OnuDeviceId, portNum, isSwitch, label }, ct);
                }
            }

            foreach (var row in before)
                await AuditAsync(tx, actor, "channel.claim", $"channels/{row.Id()}", $"从录像机认领划拨至单元：{request.UnitId}", ip, ct);

            var changed = before.Any(r => r.Id("unitId") != request.UnitId);
            var users = changed ? await UsersWithInaccessibleResourcesAsync(tx, ct) : [];
            if (changed) await AccessChangedAsync(tx, await AllUserIdsAsync(tx, ct), ct);
            return users;
        }, ct: ct);
        await RevokeUsersAsync(affected);
    }

    public async Task<IReadOnlyList<ChannelDto>> GetUnassignedChannelsAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "channel.read", ct);
        var rows = await db.QueryAsync($@"
            select {AccessService.ChannelColumns}
            from {AccessService.ChannelFrom}
            where c.status <> 'disabled' and d.enabled and c.unit_id is null and ({AccessService.ChannelPredicate})
            order by d.id, c.device_channel", new { userId = actor.UserId }, ct);
        return rows.Select(ToChannel).ToArray();
    }

    public async Task<OrganizationStatsDto> OrganizationStatsAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "area.read", ct);
        var tree = await OrganizationAsync(actor, ct);
        var channels = await access.HasPermissionAsync(actor.UserId, "channel.read", ct);
        var computers = await access.HasPermissionAsync(actor.UserId, "computer.read", ct);
        var boxes = await access.HasPermissionAsync(actor.UserId, "pon.read", ct);
        var counts = await db.OneAsync($@"
            select 
                (select count(*) from {AccessService.ChannelFrom} where @channels and d.enabled and c.status <> 'disabled' and ({AccessService.ChannelPredicate})) as channels_count,
                (select count(*) from {AssetScope.From("computer")} where @computers and ({AssetScope.For("computer")})) as computers_count,
                (select count(*) from {AssetScope.From("terminal-box")} where @boxes and ({AssetScope.For("terminal-box")})) as terminal_boxes_count", new { userId = actor.UserId, channels, computers, boxes }, ct);

        return new OrganizationStatsDto(
            tree.Workshops.Count,
            tree.Areas.Count,
            tree.Units.Count,
            (int)(counts?.Id("channelsCount") ?? 0),
            (int)(counts?.Id("computersCount") ?? 0),
            (int)(counts?.Id("terminalBoxesCount") ?? 0)
        );
    }
}

internal static class OrganizationCodeComparer
{
    public static readonly IComparer<JsonObject> RowInstance = Comparer<JsonObject>.Create((a, b) =>
    {
        var codeA = a.Text("code");
        var codeB = b.Text("code");
        var cmp = CompareCodes(codeA, codeB);
        if (cmp != 0) return cmp;
        var nameCmp = string.Compare(a.Text("name"), b.Text("name"), StringComparison.OrdinalIgnoreCase);
        if (nameCmp != 0) return nameCmp;
        return a.Id().CompareTo(b.Id());
    });

    public static int CompareCodes(string? a, string? b)
    {
        var trimA = (a ?? "").Trim();
        var trimB = (b ?? "").Trim();
        if (trimA.Length > 0 && trimB.Length > 0)
        {
            return AlphanumericCompare(trimA, trimB);
        }
        if (trimA.Length > 0) return -1;
        if (trimB.Length > 0) return 1;
        return 0;
    }

    private static int AlphanumericCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int startA = i;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                int startB = j;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                var segA = a[startA..i];
                var segB = b[startB..j];

                if (long.TryParse(segA, out var numA) && long.TryParse(segB, out var numB))
                {
                    var numCmp = numA.CompareTo(numB);
                    if (numCmp != 0) return numCmp;
                }
                else
                {
                    var strCmp = string.Compare(segA, segB, StringComparison.OrdinalIgnoreCase);
                    if (strCmp != 0) return strCmp;
                }
            }
            else
            {
                var cmp = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (cmp != 0) return cmp;
                i++;
                j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}

