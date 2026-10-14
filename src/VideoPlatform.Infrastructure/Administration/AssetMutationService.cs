using System.Net;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

/// <summary>资产写入共用的权限、组织、IP 与端口规则，校验和写入使用同一事务。</summary>
public sealed class AssetMutationService(Database db)
{
    public async Task<T> WriteAsync<T>(Actor? actor, string permission, Func<DbSession, Task<T>> operation, CancellationToken ct)
        => await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002010); select pg_advisory_xact_lock(72002030)", ct: ct);
            if (actor is not null) await AdministrationService.DemandTransactionAsync(tx, actor, permission, false, ct);
            return await operation(tx);
        }, ct);

    public static async Task<ComputerRequest> ValidateComputerAsync(DbSession tx, ComputerRequest request,
        long? id, Actor? actor, CancellationToken ct)
    {
        Rules.Require(IPAddress.TryParse(request.Ip.Trim(), out _), "请输入有效的 IP 地址");
        var unitId = request.UnitId is > 0 ? request.UnitId : null;
        var areaId = request.AreaId is > 0 ? request.AreaId : null;
        var workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null;
        if (unitId is not null)
        {
            var unit = await tx.OneAsync("select un.parent_id as area_id,ar.parent_id as workshop_id from units un join areas ar on ar.id=un.parent_id where un.id=@id", new { id = unitId }, ct)
                ?? throw new PlatformException(400, "asset.organization", "目标单元不存在");
            Rules.Require(areaId is null || areaId == unit.Id("areaId"), "区域与所选单元不一致");
            Rules.Require(workshopId is null || workshopId == unit.Id("workshopId"), "车间与所选单元不一致");
            areaId = unit.Id("areaId"); workshopId = unit.Id("workshopId");
            if (actor is not null) await AdministrationService.DemandNodeScopeAsync(tx, actor.UserId, "units", unitId.Value, ct);
        }
        else if (areaId is not null)
        {
            var area = await tx.OneAsync("select parent_id from areas where id=@id", new { id = areaId }, ct)
                ?? throw new PlatformException(400, "asset.organization", "目标区域不存在");
            Rules.Require(workshopId is null || workshopId == area.Id("parentId"), "车间与所选区域不一致");
            workshopId = area.Id("parentId");
            if (actor is not null) await AdministrationService.DemandNodeScopeAsync(tx, actor.UserId, "areas", areaId.Value, ct);
        }
        else if (workshopId is not null)
        {
            Rules.Require(await tx.OneAsync("select id from workshops where id=@id", new { id = workshopId }, ct) is not null, "目标车间不存在");
            if (actor is not null) await AdministrationService.DemandNodeScopeAsync(tx, actor.UserId, "workshops", workshopId.Value, ct);
        }
        var old = id is null ? null : await tx.OneAsync("select ip from computers where id=@id", new { id }, ct);
        if (old is null || old.Text("ip") != request.Ip.Trim())
            await ValidateIpAsync(tx, request.Ip.Trim(), "computer", id, ct);
        await ValidateNetworkBindingAsync(tx, "computer", id, request.TerminalBoxId, request.OnuDeviceId,
            request.PortNumber, request.IsSwitchPort, ct);
        if (actor is not null)
        {
            await AssetScope.DemandOrganizationAsync(tx, actor, workshopId, areaId, unitId, ct);
            await AssetScope.DemandBindingAsync(tx, actor, request.TerminalBoxId, request.OnuDeviceId, ct);
        }
        return request with { WorkshopId = workshopId, AreaId = areaId, UnitId = unitId, Ip = request.Ip.Trim() };
    }

    public static async Task ValidateIpAsync(DbSession tx, string ip, string type, long? id, CancellationToken ct)
    {
        var occupied = await tx.OneAsync("""
            with addresses as (
              select 'computer' as type,id,ip from computers union all select 'channel',id,ip from channels where status<>'disabled'
              union all select 'device',id,host from devices where enabled union all select 'onu',id,ip from onu_devices
              union all select 'network-device',id,management_ip from network_devices
              union all select 'network-device',device_id,ip from network_device_ips
              union all select 'virtual-machine',id,ip from virtual_machines
            ) select a.type,a.id from addresses a cross join lateral regexp_split_to_table(coalesce(a.ip,''),'[,;\s]+') part
            where part=@ip and not (a.type=@type and a.id=coalesce(@id::bigint,-1)) limit 1
            """, new { ip, type, id }, ct);
        Rules.Require(occupied is null, "IP 地址已被其他资产登记，请重新选择", "asset.ipConflict", 409);
    }

    public static async Task ValidateNetworkBindingAsync(DbSession tx, string type, long? id, long? boxId,
        long? onuId, int? port, bool isSwitchPort, CancellationToken ct)
    {
        Rules.Require(boxId is null or > 0 && onuId is null or > 0, "网络绑定编号无效");
        if (boxId is not null)
            Rules.Require(await tx.OneAsync("select id from terminal_boxes where id=@id", new { id = boxId }, ct) is not null, "所选终端箱不存在");
        if (onuId is null) return;
        var onu = await tx.OneAsync("select terminal_box_id,has_sub_switch,sub_switch_ports from onu_devices where id=@id for update", new { id = onuId }, ct)
            ?? throw new PlatformException(400, "asset.onu", "所选 ONU 不存在");
        Rules.Require(boxId is not null && onu.Id("terminalBoxId") == boxId, "ONU 与所选终端箱不一致");
        Rules.Require(port is > 0, "请选择有效接入端口");
        if (isSwitchPort) Rules.Require(onu.Flag("hasSubSwitch") && port <= onu.Id("subSwitchPorts"), "下挂交换机端口超出配置范围");
        else Rules.Require(port <= 4, "ONU 电口编号应为 1～4");
        var occupied = await tx.OneAsync("""
            select 'computer' as type,id from computers where onu_device_id=@onuId and port_number=@port and is_switch_port=@isSwitchPort
              and not (@type='computer' and id=coalesce(@id::bigint,-1))
            union all select 'channel',channel_id from channel_network_bindings where onu_device_id=@onuId and port_number=@port and is_switch_port=@isSwitchPort
              and not (@type='channel' and channel_id=coalesce(@id::bigint,-1)) limit 1
            """, new { onuId, port, isSwitchPort, type, id }, ct);
        Rules.Require(occupied is null, "接入端口已被其他资产占用，请选择空闲端口", "asset.portConflict", 409);
    }

    public static Task AuditAsync(DbSession tx, long? userId, string action, string resource, string summary, string? ip, CancellationToken ct)
        => tx.ExecuteAsync("insert into audit_logs(user_id,action,resource,summary,client_ip) values(@userId,@action,@resource,@summary,cast(@ip as inet))", new { userId, action, resource, summary, ip }, ct);

    public async Task<AssetBatchResultDto> UpdateComputersAsync(Actor actor, ComputerBatchUpdateRequest request, string? ip, CancellationToken ct)
    {
        var ids = request.ComputerIds?.Distinct().Order().ToArray() ?? [];
        Rules.Require(ids.Length is > 0 and <= 200, "请选择 1～200 台电脑");
        Rules.Require(request.UpdateOrganization || request.UpdateNetworkBinding, "请选择要修改的字段");
        return await WriteAsync(actor, "computer.manage", async tx =>
        {
            var rows = await tx.QueryAsync("select * from computers where id=any(@ids) order by id for update", new { ids }, ct);
            Rules.Require(rows.Count == ids.Length, "部分电脑记录已不存在，请刷新后重试", "asset.batchMissing", 409);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                await AssetScope.DemandComputerAsync(tx, actor, row, ct);
                var input = new ComputerRequest(row.Text("name"), row.Text("ip"),
                    WorkshopId: request.UpdateOrganization ? request.WorkshopId : NullableId(row, "workshopId"),
                    AreaId: request.UpdateOrganization ? request.AreaId : NullableId(row, "areaId"),
                    UnitId: request.UpdateOrganization ? request.UnitId : NullableId(row, "unitId"),
                    TerminalBoxId: request.UpdateNetworkBinding ? request.TerminalBoxId : NullableId(row, "terminalBoxId"),
                    OnuDeviceId: request.UpdateNetworkBinding ? request.OnuDeviceId : NullableId(row, "onuDeviceId"),
                    PortNumber: request.UpdateNetworkBinding ? (request.StartPortNumber ?? 1) + index : (int)row.Id("portNumber"),
                    IsSwitchPort: request.UpdateNetworkBinding ? request.IsSwitchPort : row.Flag("isSwitchPort"));
                input = await ValidateComputerAsync(tx, input, row.Id(), actor, ct);
                await tx.ExecuteAsync("""
                    update computers set workshop_id=@workshopId,area_id=@areaId,unit_id=@unitId,
                    terminal_box_id=@terminalBoxId,onu_device_id=@onuDeviceId,port_number=@portNumber,
                    is_switch_port=@isSwitchPort,updated_at=now() where id=@id
                    """, new { id = row.Id(), input.WorkshopId, input.AreaId, input.UnitId,
                    input.TerminalBoxId, input.OnuDeviceId, input.PortNumber, input.IsSwitchPort }, ct);
                await AuditAsync(tx, actor.UserId, "computer.batchUpdate", row.Id().ToString(), $"批量调整电脑：{row.Text("name")}", ip, ct);
            }
            return new AssetBatchResultDto(ids.Length, ids);
        }, ct);
    }

    private static long? NullableId(JsonObject row, string key) => row[key] is null ? null : row.Id(key);
}
