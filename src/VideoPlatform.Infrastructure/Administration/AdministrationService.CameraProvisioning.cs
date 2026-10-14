using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure.Nvr;

namespace VideoPlatform.Infrastructure;

public sealed partial class AdministrationService
{
    public async Task<ChannelDto> AddCameraDirectAsync(
        Actor actor,
        AddCameraRequest request,
        INvrChannelManager nvrManager,
        string? ip,
        CancellationToken ct = default)
    {
        var permission = await access.HasPermissionAsync(actor.UserId, "channel.assign", ct) ? "channel.assign" : "device.manage";
        await access.DemandAsync(actor, permission, ct);
        var alias = Rules.Text(request.Alias ?? request.Name, "通道别名", 128);
        var ipcHost = Rules.Text(request.IpcHost, "摄像头 IP", 253);
        Rules.Require(request.IpcPort is > 0 and <= 65535, "摄像头端口无效");

        var channelRow = await WriteAsync(actor, permission, async tx =>
        {
            // 先锁定并校验本地状态，录像机确认后才写入资产与审计。
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002030)", ct: ct);
            // 确定划拨业务组织单元：若请求显式指定则使用；若未指定但关联了网络终端箱，则自动联动继承终端箱归属业务单元
            var effectiveUnitId = request.UnitId;
            if (!effectiveUnitId.HasValue && request.TerminalBoxId is { } tBoxId && tBoxId > 0)
            {
                var boxRow = await tx.OneAsync("select unit_id from terminal_boxes where id = @tBoxId", new { tBoxId }, ct);
                if (boxRow?.Id("unitId") is { } bUnitId && bUnitId > 0)
                {
                    effectiveUnitId = bUnitId;
                }
            }

            if (effectiveUnitId is { } uId && uId > 0)
            {
                var unit = await RequiredAsync(tx, "units", uId, ct);
                Rules.Require(unit.Text("status") == "active", "目标组织单元已停用", "organization.disabled", 409);
                await DemandNodeScopeAsync(tx, actor.UserId, "units", uId, ct);
            }
            else await DemandFullScopeAsync(tx, actor.UserId, ct);

            await AssetMutationService.ValidateIpAsync(tx, ipcHost, "channel", null, ct);
            await AssetMutationService.ValidateNetworkBindingAsync(tx, "channel", null, request.TerminalBoxId,
                request.OnuDeviceId, request.PortNumber ?? 1, request.IsSwitchPort, ct);
            await AssetScope.DemandBindingAsync(tx, actor, request.TerminalBoxId, request.OnuDeviceId, ct);
            var recorder = await tx.OneAsync("select id from devices where id=@id and enabled", new { id = request.NvrDeviceId }, ct);
            Rules.Require(recorder is not null, "录像机不存在或已停用", "device.not_found", 404);
            if (request.ChannelNumber is { } requestedSlot)
            {
                var assigned = await tx.OneAsync("select id from channels where device_id=@deviceId and device_channel=@requestedSlot", new { deviceId = request.NvrDeviceId, requestedSlot }, ct);
                if (assigned is not null) await DemandChannelsAsync(tx, actor.UserId, [assigned.Id()], false, ct);
            }
            var assignedChannelNum = await nvrManager.AddIpcChannelAsync(request.NvrDeviceId, request.ChannelNumber,
                ipcHost, request.IpcPort, request.IpcUsername, request.IpcPassword, request.ProtocolType, alias, ct);
            var probedName = !string.IsNullOrWhiteSpace(request.Name) && request.Name != request.Alias
                ? request.Name.Trim() : $"通道 {assignedChannelNum}";

            var existingChan = await tx.OneAsync(
                "select id, name from channels where device_id = @deviceId and device_channel = @channelNum",
                new { deviceId = request.NvrDeviceId, channelNum = assignedChannelNum }, ct);

            long createdChannelId;
            if (existingChan is not null)
            {
                createdChannelId = existingChan.Id();
                await DemandChannelsAsync(tx, actor.UserId, [createdChannelId], false, ct);
                var currentName = existingChan.Text("name");
                var resolvedName = string.IsNullOrWhiteSpace(currentName) || currentName.StartsWith("通道 ")
                    ? probedName
                    : currentName;
                await tx.ExecuteAsync(@"
                    update channels set
                        alias = @alias,
                        name = @resolvedName,
                        unit_id = @unitId,
                        ip = @ipcHost,
                        username = @ipcUsername,
                        password = @ipcPassword,
                        status = 'offline',
                        updated_at = now()
                    where id = @createdChannelId",
                    new { createdChannelId, alias, resolvedName, unitId = effectiveUnitId, ipcHost, ipcUsername = request.IpcUsername, ipcPassword = request.IpcPassword }, ct);
            }
            else
            {
                var inserted = await tx.OneAsync(@"
                    insert into channels(
                        device_id, device_channel, name, alias, status, unit_id, ptz_capable, codec,
                        ip, username, password, remark
                    ) values (
                        @deviceId, @channelNum, @probedName, @alias, 'offline', @unitId, false, 'h264',
                        @ipcHost, @ipcUsername, @ipcPassword, '组织与通道直接新增'
                    ) returning id",
                    new { deviceId = request.NvrDeviceId, channelNum = assignedChannelNum, probedName, alias, unitId = effectiveUnitId, ipcHost, ipcUsername = request.IpcUsername, ipcPassword = request.IpcPassword }, ct);
                createdChannelId = inserted!.Id();
            }

            // 建立物理网络承载关系
            var portNum = request.PortNumber ?? 1;
            var isSwitch = request.IsSwitchPort;
            var label = request.PortLabel?.Trim() ?? (isSwitch ? $"SW-P{portNum}" : $"LAN{portNum}");

            await tx.ExecuteAsync(@"
                insert into channel_network_bindings(
                    channel_id, terminal_box_id, onu_device_id, port_number, is_switch_port, port_label
                ) values (
                    @createdChannelId, @terminalBoxId, @onuDeviceId, @portNum, @isSwitch, @label
                ) on conflict (channel_id) do update set
                    terminal_box_id = excluded.terminal_box_id,
                    onu_device_id = excluded.onu_device_id,
                    port_number = excluded.port_number,
                    is_switch_port = excluded.is_switch_port,
                    port_label = excluded.port_label,
                    updated_at = now()",
                new { createdChannelId, terminalBoxId = request.TerminalBoxId, onuDeviceId = request.OnuDeviceId, portNum, isSwitch, label }, ct);

            await AuditAsync(tx, actor, "channel.create_direct", $"channels/{createdChannelId}",
                $"在录像机【{request.NvrDeviceId}】通道 {assignedChannelNum} 新增摄像头：{alias} ({ipcHost})", ip, ct);

            return (Node: createdChannelId, Affected: (IReadOnlyList<long>)[]);
        }, ct: ct);

        // 重新获取包含完整组织与承载网络信息的通道记录
        var row = await db.OneAsync($@"
            select {AccessService.ChannelColumns}
            from {AccessService.ChannelFrom}
            where c.id = @id", new { id = channelRow.Node, userId = actor.UserId }, ct)
            ?? throw new PlatformException(500, "channel.load_failed", "加载新通道记录失败");

        return ToChannel(row);
    }
}

