using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

/// <summary>旧资产写入入口的应用边界，将授权、占用校验、审计与事务事件一起提交。</summary>
public sealed class AssetOperations(Database db)
{
    public Task<T> ExecuteAsync<T>(Actor actor, string permission, string? type, long? id, object? input,
        Func<Task<T>> operation, CancellationToken ct)
        => db.InApplicationTransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002010); select pg_advisory_xact_lock(72002030)", ct: ct);
            await AdministrationService.DemandTransactionAsync(tx, actor, permission, false, ct);
            if (type is not null && id is not null) await DemandAsync(tx, actor, type, id.Value, ct);
            if (type is "port" or "network-device" or "onu" or "computer" && id is not null)
            {
                var ports = await tx.QueryAsync(type == "port" ? "select id from device_ports where id=@id"
                    : "select id from device_ports where device_id=@id and device_type=@deviceType", new { id, deviceType = type switch { "network-device" => "network_device", "onu" => "onu_device", _ => type } }, ct);
                foreach (var port in ports)
                {
                    var peers = await tx.QueryAsync("select case when local_port_id=@id then peer_port_id else local_port_id end as id from device_links where local_port_id=@id or peer_port_id=@id", new { id = port.Id() }, ct);
                    foreach (var peer in peers) await DemandAsync(tx, actor, "port", peer.Id(), ct);
                }
            }
            switch (input)
            {
                case TerminalBoxRequest box:
                    await AssetScope.DemandOrganizationAsync(tx, actor, null, box.AreaId, box.UnitId, ct);
                    if (box.AreaId is not null && box.UnitId is not null)
                    {
                        var unit = await tx.OneAsync("select parent_id from units where id=@id", new { id = box.UnitId }, ct);
                        Rules.Require(unit?.Id("parentId") == box.AreaId, "终端箱区域与单元不一致");
                    }
                    break;
                case OnuUpdateRequest onu:
                    await AssetScope.DemandBindingAsync(tx, actor, onu.TerminalBoxId, null, ct);
                    if (onu.TerminalBoxId == 0) await AssetScope.DemandOrganizationAsync(tx, actor, null, null, null, ct);
                    break;
                case IpPoolRequest pool:
                    await AssetScope.DemandOrganizationAsync(tx, actor, pool.WorkshopId, pool.AreaId, null, ct);
                    break;
                case NetworkDeviceRequest network:
                    await AssetScope.DemandOrganizationAsync(tx, actor, network.WorkshopId, network.AreaId, null, ct);
                    if (!string.IsNullOrWhiteSpace(network.ManagementIp)) await ValidateChangedIpAsync(tx, "network-device", id, network.ManagementIp, ct);
                    foreach (var address in network.AssignedIps ?? [])
                        if (!string.IsNullOrWhiteSpace(address.Ip)) await ValidateChangedIpAsync(tx, "network-device", id, address.Ip, ct);
                    break;
                case VirtualMachineRequest vm:
                    await ValidateChangedIpAsync(tx, "virtual-machine", type == "virtual-machine" ? id : null, vm.Ip, ct);
                    break;
                case ConnectLinkRequest link:
                    await DemandAsync(tx, actor, "port", link.LocalPortId, ct);
                    await DemandAsync(tx, actor, "port", link.PeerPortId, ct);
                    break;
                case ApplyTopologyCorrectionRequest topology:
                    Rules.Require(topology.Bindings.Count is > 0 and <= 1000, "请选择 1～1000 条绑定记录");
                    Rules.Require(topology.Bindings.Select(b => (b.OnuDeviceId, b.PortNumber, b.IsSwitchPort)).Distinct().Count() == topology.Bindings.Count, "批量绑定包含重复接入端口", "asset.portConflict", 409);
                    foreach (var binding in topology.Bindings)
                    {
                        var channel = await tx.OneAsync($"select c.id from {AccessService.ChannelFrom} where c.id=@id and ({AccessService.ChannelPredicate})", new { id = binding.ChannelId, userId = actor.UserId }, ct);
                        Rules.Require(channel is not null, "包含范围外通道，整批操作已取消", "asset.scope", 403);
                        await AssetScope.DemandBindingAsync(tx, actor, binding.TerminalBoxId, binding.OnuDeviceId, ct);
                        await AssetMutationService.ValidateNetworkBindingAsync(tx, "channel", binding.ChannelId, binding.TerminalBoxId, binding.OnuDeviceId, binding.PortNumber, binding.IsSwitchPort, ct);
                    }
                    break;
            }
            if (input is SaveCoordinateRequest coordinate)
                await DemandMapTargetAsync(tx, actor, coordinate.TargetType, coordinate.TargetId, ct);
            if (input is BatchSaveCoordinatesRequest coordinates)
            {
                Rules.Require(coordinates.Items.Count is > 0 and <= 1000, "请选择 1～1000 个标定节点");
                foreach (var item in coordinates.Items) await DemandMapTargetAsync(tx, actor, item.TargetType, item.TargetId, ct);
            }
            if (input is SaveCablePathRequest cable)
            {
                if (cable.FromDeviceId is not null) await DemandMapTargetAsync(tx, actor, cable.FromDeviceType, cable.FromDeviceId.Value, ct);
                if (cable.ToDeviceId is not null) await DemandMapTargetAsync(tx, actor, cable.ToDeviceType, cable.ToDeviceId.Value, ct);
                if (cable.FromDeviceId is null && cable.ToDeviceId is null) await AssetScope.DemandOrganizationAsync(tx, actor, null, null, null, ct);
            }
            return await operation();
        }, ct);

    private static async Task ValidateChangedIpAsync(DbSession tx, string type, long? id, string ip, CancellationToken ct)
    {
        Rules.Require(System.Net.IPAddress.TryParse(ip.Trim(), out _), "请输入有效的 IP 地址");
        if (id is not null)
        {
            var old = await tx.OneAsync(type == "network-device"
                ? "select id from network_devices where id=@id and management_ip=@ip union all select device_id from network_device_ips where device_id=@id and ip=@ip"
                : "select id from virtual_machines where id=@id and ip=@ip", new { id, ip = ip.Trim() }, ct);
            if (old is not null) return;
        }
        await AssetMutationService.ValidateIpAsync(tx, ip.Trim(), type, id, ct);
    }

    public Task DemandAsync(Actor actor, string type, long id, CancellationToken ct)
        => db.TransactionAsync(async tx => { await DemandAsync(tx, actor, type, id, ct); return true; }, ct);

    private static async Task DemandAsync(DbSession tx, Actor actor, string type, long id, CancellationToken ct)
    {
        if (type == "map-scene")
        {
            await AssetScope.DemandOrganizationAsync(tx, actor, null, null, null, ct);
            return;
        }
        if (type == "map-coordinate")
        {
            var coordinate = await tx.OneAsync("select target_type,target_id from map_coordinates where id=@id", new { id }, ct)
                ?? throw new PlatformException(404, "map.not_found", "地图标定不存在");
            await DemandMapTargetAsync(tx, actor, coordinate.Text("targetType"), coordinate.Id("targetId"), ct);
            return;
        }
        if (type == "map-cable")
        {
            var cable = await tx.OneAsync("select from_device_type,from_device_id,to_device_type,to_device_id from map_cable_paths where id=@id", new { id }, ct)
                ?? throw new PlatformException(404, "map.not_found", "地图光缆不存在");
            if (cable["fromDeviceId"] is not null) await DemandMapTargetAsync(tx, actor, cable.Text("fromDeviceType"), cable.Id("fromDeviceId"), ct);
            if (cable["toDeviceId"] is not null) await DemandMapTargetAsync(tx, actor, cable.Text("toDeviceType"), cable.Id("toDeviceId"), ct);
            if (cable["fromDeviceId"] is null && cable["toDeviceId"] is null) await AssetScope.DemandOrganizationAsync(tx, actor, null, null, null, ct);
            return;
        }
        if (type == "port")
        {
            var port = await tx.OneAsync("select device_type,device_id from device_ports where id=@id", new { id }, ct)
                ?? throw new PlatformException(404, "port.not_found", "端口不存在");
            await AssetScope.DemandAsync(tx, actor, AssetScope.PortOwnerType(port.Text("deviceType")), port.Id("deviceId"), ct);
        }
        else if (type == "link")
        {
            var link = await tx.OneAsync("select local_port_id,peer_port_id from device_links where id=@id", new { id }, ct)
                ?? throw new PlatformException(404, "link.not_found", "连接不存在");
            await DemandAsync(tx, actor, "port", link.Id("localPortId"), ct);
            await DemandAsync(tx, actor, "port", link.Id("peerPortId"), ct);
        }
        else await AssetScope.DemandAsync(tx, actor, type, id, ct);
    }

    private static async Task DemandMapTargetAsync(DbSession tx, Actor actor, string? type, long id, CancellationToken ct)
    {
        var normalized = DigitalTwinMapService.AssetType(type);
        Rules.Require(normalized is "channel" or "computer" or "terminal-box" or "network-device" or "onu", "地图资产类型无效");
        if (normalized == "channel")
        {
            var row = await tx.OneAsync($"select c.id from {AccessService.ChannelFrom} where c.id=@id and ({AccessService.ChannelPredicate})", new { id, userId = actor.UserId }, ct);
            Rules.Require(row is not null, "通道不在当前数据范围内", "asset.scope", 403);
        }
        else await AssetScope.DemandAsync(tx, actor, normalized, id, ct);
    }
}
