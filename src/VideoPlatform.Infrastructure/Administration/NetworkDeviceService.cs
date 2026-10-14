using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure;

public sealed class NetworkDeviceService(Database db, AuditStore audit, DevicePortService portService, VirtualMachineService vmService)
{
    private const string BaseColumns = @"
        nd.id, nd.name, nd.device_type, nd.vendor, nd.model,
        nd.management_ip, nd.mac, nd.location,
        nd.workshop_id, w.name as workshop_name,
        nd.area_id, a.name as area_name,
        nd.owner, nd.status, nd.response_time_ms, nd.last_probed_at,
        nd.remarks, nd.created_at, nd.updated_at";

    private const string FromSql = @"
        network_devices nd
        left join workshops w on nd.workshop_id = w.id
        left join areas a on nd.area_id = a.id";

    public async Task<PagedResponse<NetworkDeviceDto>> ListAsync(int page, int size, string? search, string? deviceType, string? status, CancellationToken ct = default, Actor? actor = null)
    {
        var offset = Math.Max(0, (page - 1) * size);
        var where = "1=1";
        if (actor is not null) where += $" and nd.id in ({AssetScope.AllowedIds("network-device")})";
        if (!string.IsNullOrWhiteSpace(search))
            where += " and (nd.name ilike @search or coalesce(nd.management_ip,'') ilike @search or coalesce(nd.model,'') ilike @search or coalesce(nd.vendor,'') ilike @search or coalesce(nd.location,'') ilike @search or coalesce(nd.owner,'') ilike @search)";
        if (!string.IsNullOrWhiteSpace(deviceType))
            where += " and nd.device_type = @deviceType";
        if (!string.IsNullOrWhiteSpace(status))
            where += " and nd.status = @status";

        var args = new { search = $"%{search?.Trim()}%", deviceType, status, size, offset, userId = actor?.UserId };
        var rows = await db.QueryAsync($"select {BaseColumns} from {FromSql} where {where} order by nd.id desc limit @size offset @offset", args, ct);
        var count = await db.OneAsync($"select count(*) as count from {FromSql} where {where}", args, ct);

        if (rows.Count == 0)
            return new PagedResponse<NetworkDeviceDto>([], count?.Id("count") ?? 0, page, size);

        var deviceIds = rows.Select(r => r.Id("id")).ToArray();
        var allIps = await QueryIpsForDevicesAsync(deviceIds, ct);

        var portStatsRows = await db.QueryAsync(@"
            select dp.device_id, count(dp.id) as port_count, count(dl.id) as linked_count
            from device_ports dp
            left join device_links dl on (dl.local_port_id = dp.id or dl.peer_port_id = dp.id)
            where dp.device_type = 'network_device' and dp.device_id = any(@deviceIds)
            group by dp.device_id", new { deviceIds }, ct);
        var portStats = portStatsRows.ToDictionary(r => r.Id("deviceId"), r => ((int)r.Id("portCount"), (int)r.Id("linkedCount")));

        var vmStatsRows = await db.QueryAsync(@"
            select host_device_id, count(*) as vm_count
            from virtual_machines
            where host_device_id = any(@deviceIds)
            group by host_device_id", new { deviceIds }, ct);
        var vmStats = vmStatsRows.ToDictionary(r => r.Id("hostDeviceId"), r => (int)r.Id("vmCount"));

        var items = rows.Select(r =>
        {
            var id = r.Id("id");
            var ips = allIps.TryGetValue(id, out var list) ? list : (IReadOnlyList<NetworkDeviceIpDto>)[];
            var (portCount, linkedCount) = portStats.TryGetValue(id, out var ps) ? ps : (0, 0);
            var vmCount = vmStats.TryGetValue(id, out var vc) ? vc : 0;
            return ToDto(r, ips, portCount, linkedCount, vmCount);
        }).ToArray();

        return new PagedResponse<NetworkDeviceDto>(items, count?.Id("count") ?? 0, page, size);
    }

    public async Task<NetworkDeviceDto> GetAsync(long id, CancellationToken ct = default, Actor? actor = null)
    {
        var row = await db.OneAsync($"select {BaseColumns} from {FromSql} where nd.id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "network_device.not_found", "网络设备不存在");

        var allIps = await QueryIpsForDevicesAsync([id], ct);
        var ips = allIps.TryGetValue(id, out var list) ? list : (IReadOnlyList<NetworkDeviceIpDto>)[];
        var ports = await portService.ListPortsAsync("network_device", id, ct, actor);
        var vms = await vmService.ListAsync(id, ct);
        var linkedCount = ports.Count(p => p.Link != null);

        return ToDto(row, ips, ports.Count, linkedCount, vms.Count, ports, vms);
    }

    public async Task<NetworkDeviceDto> CreateAsync(NetworkDeviceRequest request, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default)
    {
        var name = Rules.Text(request.Name, "设备名称", 128);
        var deviceType = Rules.Text(request.DeviceType, "设备类型", 64);

        if (!string.IsNullOrWhiteSpace(request.ManagementIp))
        {
            Rules.Require(IPAddress.TryParse(request.ManagementIp.Trim(), out _), "管理 IP 地址格式无效");
        }

        var row = await db.TransactionAsync(async tx =>
        {
            var inserted = await tx.OneAsync(@"
                insert into network_devices (
                    name, device_type, vendor, model, management_ip, mac, location,
                    workshop_id, area_id, owner, status, remarks
                ) values (
                    @name, @deviceType, @vendor, @model, @managementIp, @mac, @location,
                    @workshopId, @areaId, @owner, @status, @remarks
                ) returning id", new
            {
                name,
                deviceType,
                vendor = request.Vendor?.Trim(),
                model = request.Model?.Trim(),
                managementIp = string.IsNullOrWhiteSpace(request.ManagementIp) ? null : request.ManagementIp.Trim(),
                mac = request.Mac?.Trim(),
                location = request.Location?.Trim(),
                workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null,
                areaId = request.AreaId is > 0 ? request.AreaId : null,
                owner = request.Owner?.Trim(),
                status = string.IsNullOrWhiteSpace(request.Status) ? "online" : request.Status.Trim(),
                remarks = request.Remarks?.Trim()
            }, ct);

            var newId = inserted!.Id("id");

            if (request.AssignedIps is { Count: > 0 })
            {
                foreach (var ipReq in request.AssignedIps)
                {
                    if (string.IsNullOrWhiteSpace(ipReq.Ip)) continue;
                    var ip = ipReq.Ip.Trim();
                    if (!IPAddress.TryParse(ip, out _)) continue;

                    await tx.ExecuteAsync(@"
                        insert into network_device_ips (
                            device_id, ip, vlan_id, interface_name, role, description
                        ) values (
                            @deviceId, @ip, @vlanId, @interfaceName, @role, @description
                        ) on conflict (device_id, ip) do update set
                            vlan_id = excluded.vlan_id,
                            interface_name = excluded.interface_name,
                            role = excluded.role,
                            description = excluded.description,
                            updated_at = now()", new
                    {
                        deviceId = newId,
                        ip,
                        vlanId = ipReq.VlanId,
                        interfaceName = string.IsNullOrWhiteSpace(ipReq.InterfaceName) ? "Vlanif" : ipReq.InterfaceName.Trim(),
                        role = string.IsNullOrWhiteSpace(ipReq.Role) ? "gateway" : ipReq.Role.Trim(),
                        description = ipReq.Description?.Trim()
                    }, ct);
                }
            }

            return inserted;
        }, ct);

        var id = row!.Id("id");
        if (actorUserId != null)
            await audit.WriteAsync(long.TryParse(actorUserId, out var uid) ? uid : 0, "network_device.create", id.ToString(), $"登记网络设备：{name} ({deviceType})", clientIp);

        return await GetAsync(id, ct);
    }

    public async Task<NetworkDeviceDto> UpdateAsync(long id, NetworkDeviceRequest request, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default)
    {
        var name = Rules.Text(request.Name, "设备名称", 128);
        var deviceType = Rules.Text(request.DeviceType, "设备类型", 64);

        if (!string.IsNullOrWhiteSpace(request.ManagementIp))
        {
            Rules.Require(IPAddress.TryParse(request.ManagementIp.Trim(), out _), "管理 IP 地址格式无效");
        }

        var existing = await db.OneAsync("select id, name from network_devices where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "network_device.not_found", "网络设备不存在");

        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync(@"
                update network_devices set
                    name = @name,
                    device_type = @deviceType,
                    vendor = @vendor,
                    model = @model,
                    management_ip = @managementIp,
                    mac = @mac,
                    location = @location,
                    workshop_id = @workshopId,
                    area_id = @areaId,
                    owner = @owner,
                    status = @status,
                    remarks = @remarks,
                    updated_at = now()
                where id = @id", new
            {
                id,
                name,
                deviceType,
                vendor = request.Vendor?.Trim(),
                model = request.Model?.Trim(),
                managementIp = string.IsNullOrWhiteSpace(request.ManagementIp) ? null : request.ManagementIp.Trim(),
                mac = request.Mac?.Trim(),
                location = request.Location?.Trim(),
                workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null,
                areaId = request.AreaId is > 0 ? request.AreaId : null,
                owner = request.Owner?.Trim(),
                status = string.IsNullOrWhiteSpace(request.Status) ? "online" : request.Status.Trim(),
                remarks = request.Remarks?.Trim()
            }, ct);

            // 全量更新接口 IP 列表
            await tx.ExecuteAsync("delete from network_device_ips where device_id = @id", new { id }, ct);

            if (request.AssignedIps is { Count: > 0 })
            {
                foreach (var ipReq in request.AssignedIps)
                {
                    if (string.IsNullOrWhiteSpace(ipReq.Ip)) continue;
                    var ip = ipReq.Ip.Trim();
                    if (!IPAddress.TryParse(ip, out _)) continue;

                    await tx.ExecuteAsync(@"
                        insert into network_device_ips (
                            device_id, ip, vlan_id, interface_name, role, description
                        ) values (
                            @deviceId, @ip, @vlanId, @interfaceName, @role, @description
                        ) on conflict (device_id, ip) do update set
                            vlan_id = excluded.vlan_id,
                            interface_name = excluded.interface_name,
                            role = excluded.role,
                            description = excluded.description,
                            updated_at = now()", new
                    {
                        deviceId = id,
                        ip,
                        vlanId = ipReq.VlanId,
                        interfaceName = string.IsNullOrWhiteSpace(ipReq.InterfaceName) ? "Vlanif" : ipReq.InterfaceName.Trim(),
                        role = string.IsNullOrWhiteSpace(ipReq.Role) ? "gateway" : ipReq.Role.Trim(),
                        description = ipReq.Description?.Trim()
                    }, ct);
                }
            }

            return true;
        }, ct);

        if (actorUserId != null)
            await audit.WriteAsync(long.TryParse(actorUserId, out var uid) ? uid : 0, "network_device.update", id.ToString(), $"更新网络设备：{name} ({deviceType})", clientIp);

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default)
    {
        var existing = await db.OneAsync("select name from network_devices where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "network_device.not_found", "网络设备不存在");

        await db.ExecuteAsync("delete from network_devices where id = @id", new { id }, ct);

        if (actorUserId != null)
            await audit.WriteAsync(long.TryParse(actorUserId, out var uid) ? uid : 0, "network_device.delete", id.ToString(), $"删除网络设备：{existing.Text("name")}", clientIp);
    }

    public async Task<NetworkDeviceProbeResultDto> ProbeAsync(long id, CancellationToken ct = default)
    {
        var row = await db.OneAsync("select id, name, management_ip from network_devices where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "network_device.not_found", "网络设备不存在");

        var targetIp = row.Text("managementIp");
        if (string.IsNullOrWhiteSpace(targetIp))
        {
            var firstIpRow = await db.OneAsync("select ip from network_device_ips where device_id = @id order by id asc limit 1", new { id }, ct);
            targetIp = firstIpRow?.Text("ip");
        }

        if (string.IsNullOrWhiteSpace(targetIp))
            return new NetworkDeviceProbeResultDto(id, row.Text("name"), null, "unknown", null, DateTimeOffset.UtcNow);

        var (online, rtt) = await PingIpAsync(targetIp, ct);
        var status = online ? "online" : "offline";
        var probedAt = DateTimeOffset.UtcNow;

        await db.ExecuteAsync(@"
            update network_devices set
                status = @status,
                response_time_ms = @rtt,
                last_probed_at = @probedAt,
                updated_at = now()
            where id = @id", new { id, status, rtt, probedAt }, ct);

        return new NetworkDeviceProbeResultDto(id, row.Text("name"), targetIp, status, rtt, probedAt);
    }

    public async Task<IReadOnlyList<NetworkDeviceProbeResultDto>> ProbeAllAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var devices = await db.QueryAsync($@"
            select d.id, d.name, d.management_ip,
                   (select ip from network_device_ips where device_id = d.id order by id asc limit 1) as first_ip
            from network_devices d
            where {(actor is null ? "true" : $"d.id in ({AssetScope.AllowedIds("network-device")})")}
            order by d.id", new { userId = actor?.UserId }, ct);

        using var semaphore = new SemaphoreSlim(16);

        var tasks = devices.Select(async r =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var id = r.Id("id");
                var name = r.Text("name");
                var targetIp = r.Text("managementIp");
                if (string.IsNullOrWhiteSpace(targetIp))
                    targetIp = r.Text("firstIp");

                if (string.IsNullOrWhiteSpace(targetIp))
                    return new NetworkDeviceProbeResultDto(id, name, null, "unknown", null, DateTimeOffset.UtcNow);

                var (online, rtt) = await PingIpAsync(targetIp, ct);
                var status = online ? "online" : "offline";
                var probedAt = DateTimeOffset.UtcNow;

                return new NetworkDeviceProbeResultDto(id, name, targetIp, status, rtt, probedAt);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        // 批量更新数据库，避免并发抢占数据库连接
        var validResults = results.Where(x => x.ManagementIp != null).ToList();
        if (validResults.Count > 0)
        {
            var ids = validResults.Select(x => x.Id).ToArray();
            var statuses = validResults.Select(x => x.Status).ToArray();
            var rtts = validResults.Select(x => x.ResponseTimeMs).ToArray();

            await db.ExecuteAsync(@"
                update network_devices d set
                    status = u.status,
                    response_time_ms = u.rtt,
                    last_probed_at = now(),
                    updated_at = now()
                from (select unnest(cast(@ids as bigint[])) as id, unnest(cast(@statuses as varchar[])) as status, unnest(cast(@rtts as integer[])) as rtt) u
                where d.id = u.id", new { ids, statuses, rtts }, ct);
        }

        return results;
    }

    private static async Task<(bool Online, int? Rtt)> PingIpAsync(string ip, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1200);
            if (reply.Status == IPStatus.Success)
                return (true, (int)reply.RoundtripTime);
            return (false, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private async Task<Dictionary<long, List<NetworkDeviceIpDto>>> QueryIpsForDevicesAsync(long[] deviceIds, CancellationToken ct)
    {
        if (deviceIds.Length == 0) return [];
        var rows = await db.QueryAsync(@"
            select id, device_id, ip, vlan_id, interface_name, role, description
            from network_device_ips
            where device_id = any(@deviceIds)
            order by id asc", new { deviceIds }, ct);

        var dict = new Dictionary<long, List<NetworkDeviceIpDto>>();
        foreach (var r in rows)
        {
            var did = r.Id("deviceId");
            if (!dict.TryGetValue(did, out var list))
            {
                list = [];
                dict[did] = list;
            }
            list.Add(new NetworkDeviceIpDto(
                r.Id("id"),
                did,
                r.Text("ip"),
                r.TryGetPropertyValue("vlanId", out var vid) && vid != null ? (int?)r.Id("vlanId") : null,
                r.Text("interfaceName"),
                r.Text("role"),
                r.Text("description")
            ));
        }
        return dict;
    }

    private static NetworkDeviceDto ToDto(
        JsonObject r,
        IReadOnlyList<NetworkDeviceIpDto> assignedIps,
        int portCount = 0,
        int linkedPortCount = 0,
        int vmCount = 0,
        IReadOnlyList<DevicePortDto>? ports = null,
        IReadOnlyList<VirtualMachineDto>? vms = null)
    {
        return new NetworkDeviceDto(
            r.Id("id"),
            r.Text("name"),
            r.Text("deviceType"),
            r.Text("vendor"),
            r.Text("model"),
            r.Text("managementIp"),
            r.Text("mac"),
            r.Text("location"),
            r.TryGetPropertyValue("workshopId", out var wid) && wid != null ? (long?)r.Id("workshopId") : null,
            r.Text("workshopName"),
            r.TryGetPropertyValue("areaId", out var aid) && aid != null ? (long?)r.Id("areaId") : null,
            r.Text("areaName"),
            r.Text("owner"),
            r.Text("status"),
            r.TryGetPropertyValue("responseTimeMs", out var rtt) && rtt != null ? (int?)r.Id("responseTimeMs") : null,
            DateTime(r, "lastProbedAt"),
            r.Text("remarks"),
            DateTime(r, "createdAt") ?? DateTimeOffset.UtcNow,
            DateTime(r, "updatedAt") ?? DateTimeOffset.UtcNow,
            assignedIps,
            portCount,
            linkedPortCount,
            vmCount,
            ports,
            vms
        );
    }

    private static DateTimeOffset? DateTime(JsonObject row, string property)
    {
        if (row.TryGetPropertyValue(property, out var node) && node != null &&
            DateTimeOffset.TryParse(node.ToString(), out var dt))
        {
            return dt;
        }
        return null;
    }
}
