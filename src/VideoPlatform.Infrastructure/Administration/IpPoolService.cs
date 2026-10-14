using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure;

public sealed class IpPoolService(Database db, AuditStore audit, AssetQueryService assets)
{
    private const string BaseColumns = @"
        p.id, p.name, p.cidr, p.start_ip, p.end_ip, p.gateway, p.netmask, p.vlan_id, p.description,
        p.workshop_id, w.name as workshop_name,
        p.area_id, a.name as area_name,
        p.auto_scan, p.last_scanned_at, p.created_at, p.updated_at";

    private const string FromSql = @"
        ip_pools p
        left join workshops w on p.workshop_id = w.id
        left join areas a on p.area_id = a.id";

    public async Task<IReadOnlyList<IpPoolDto>> ListAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var pools = await db.QueryAsync($"select {BaseColumns} from {FromSql} where {(actor is null ? "true" : $"p.id in ({AssetScope.AllowedIds("ip-pool")})")} order by p.cidr::inet, p.id", new { userId = actor?.UserId }, ct);
        var result = new List<IpPoolDto>();
        foreach (var p in pools)
        {
            var summary = await CalculatePoolSummaryAsync(p, ct);
            result.Add(summary);
        }
        return result;
    }

    public async Task<IpPoolDto> GetAsync(long id, CancellationToken ct = default)
    {
        var row = await db.OneAsync($"select {BaseColumns} from {FromSql} where p.id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "ip_pool.not_found", "指定的 IP 号段池不存在");
        return await CalculatePoolSummaryAsync(row, ct);
    }

    public async Task<IpPoolDto> CreateAsync(IpPoolRequest request, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default)
    {
        var (cidr, startIp, endIp, netmask, gateway) = ParseAndNormalizeCidr(request);
        var name = Rules.Text(request.Name, "号段名称", 128);

        var existing = await db.OneAsync("select id from ip_pools where cidr = @cidr", new { cidr }, ct);
        Rules.Require(existing is null, $"已存在相同 CIDR 的号段：{cidr}", "ip_pool.exists", 409);

        var row = await db.OneAsync(@"
            insert into ip_pools(
                name, cidr, start_ip, end_ip, gateway, netmask, vlan_id, description,
                workshop_id, area_id, auto_scan
            ) values (
                @name, @cidr, @startIp, @endIp, @gateway, @netmask, @vlanId, @description,
                @workshopId, @areaId, @autoScan
            ) returning id", new
        {
            name,
            cidr,
            startIp,
            endIp,
            gateway,
            netmask,
            vlanId = request.VlanId,
            description = request.Description?.Trim(),
            workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null,
            areaId = request.AreaId is > 0 ? request.AreaId : null,
            autoScan = request.AutoScan
        }, ct);

        var id = row!.Id("id");
        if (actorUserId != null)
            await audit.WriteAsync(long.TryParse(actorUserId, out var uid) ? uid : 0, "ip_pool.create", id.ToString(), $"添加 IP 号段：{name} ({cidr})", clientIp);

        return await GetAsync(id, ct);
    }

    public async Task<IpPoolDto> UpdateAsync(long id, IpPoolRequest request, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default)
    {
        var (cidr, startIp, endIp, netmask, gateway) = ParseAndNormalizeCidr(request);
        var name = Rules.Text(request.Name, "号段名称", 128);

        var existing = await db.OneAsync("select id from ip_pools where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "ip_pool.not_found", "指定的 IP 号段池不存在");

        var conflict = await db.OneAsync("select id from ip_pools where cidr = @cidr and id <> @id", new { cidr, id }, ct);
        Rules.Require(conflict is null, $"已存在相同 CIDR 的号段：{cidr}", "ip_pool.exists", 409);

        await db.ExecuteAsync(@"
            update ip_pools set
                name = @name,
                cidr = @cidr,
                start_ip = @startIp,
                end_ip = @endIp,
                gateway = @gateway,
                netmask = @netmask,
                vlan_id = @vlanId,
                description = @description,
                workshop_id = @workshopId,
                area_id = @areaId,
                auto_scan = @autoScan,
                updated_at = now()
            where id = @id", new
        {
            id,
            name,
            cidr,
            startIp,
            endIp,
            gateway,
            netmask,
            vlanId = request.VlanId,
            description = request.Description?.Trim(),
            workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null,
            areaId = request.AreaId is > 0 ? request.AreaId : null,
            autoScan = request.AutoScan
        }, ct);

        if (actorUserId != null)
            await audit.WriteAsync(long.TryParse(actorUserId, out var uid) ? uid : 0, "ip_pool.update", id.ToString(), $"修改 IP 号段：{name} ({cidr})", clientIp);

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default)
    {
        var existing = await db.OneAsync("select name, cidr from ip_pools where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "ip_pool.not_found", "指定的 IP 号段池不存在");

        await db.ExecuteAsync("delete from ip_pools where id = @id", new { id }, ct);

        if (actorUserId != null)
            await audit.WriteAsync(long.TryParse(actorUserId, out var uid) ? uid : 0, "ip_pool.delete", id.ToString(), $"删除 IP 号段：{existing.Text("name")} ({existing.Text("cidr")})", clientIp);
    }

    public async Task<IpPoolDetailDto> GetDetailAsync(long id, CancellationToken ct = default, Actor? actor = null)
    {
        var row = await db.OneAsync($"select {BaseColumns} from {FromSql} where p.id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "ip_pool.not_found", "指定的 IP 号段池不存在");

        var cidr = row.Text("cidr");
        var gateway = row.Text("gateway");
        var (network, broadcast, _, _, _, _) = ParseCidr(cidr);

        var startUint = network;
        var endUint = broadcast;
        var totalAddresses = (int)(endUint - startUint + 1);
        Rules.Require(totalAddresses <= 4096, "号段容量过大（超出 4096 个地址限制）", "ip_pool.too_large");

        // 1. 获取全平台已登记设备 IP
        var boundDevicesMap = await QueryAllBoundDevicesAsync(ct);
        await RedactBindingsAsync(boundDevicesMap, actor, ct);

        // 2. 获取该号段最新扫描记录
        var scanRows = await db.QueryAsync("select ip, is_active, response_time_ms, last_probed_at from ip_scan_records where pool_id = @id", new { id }, ct);
        var scanMap = scanRows.ToDictionary(
            r => r.Text("ip"),
            r => (IsActive: r.Flag("isActive"), Rtt: r.TryGetPropertyValue("responseTimeMs", out var rtt) && rtt != null ? (int?)r.Id("responseTimeMs") : null, ProbedAt: DateTime(r, "lastProbedAt"))
        );

        var addresses = new List<IpAddressStatusDto>();
        var slicesSet = new HashSet<string>();

        var availableCount = 0;
        var registeredCount = 0;
        var unregisteredActiveCount = 0;
        var conflictCount = 0;
        var reservedCount = 0;

        for (var u = startUint; u <= endUint; u++)
        {
            var ipStr = UintToIp(u);
            var slice = GetSubnetSlice(u);
            slicesSet.Add(slice);

            var boundList = boundDevicesMap.TryGetValue(ipStr, out var bList) ? bList : (IReadOnlyList<BoundDeviceInfoDto>)[];
            scanMap.TryGetValue(ipStr, out var scan);
            var distinctDeviceCount = boundList.Select(b => (b.DeviceType, b.DeviceId)).Distinct().Count();

            string status;
            if (u == network || u == broadcast || (!string.IsNullOrWhiteSpace(gateway) && ipStr == gateway) || boundList.Any(b => b.DeviceType == "network_device" && b.Role == "gateway"))
            {
                status = "reserved";
                reservedCount++;
            }
            else if (distinctDeviceCount > 1)
            {
                status = "conflict";
                conflictCount++;
            }
            else if (distinctDeviceCount == 1)
            {
                status = "registered";
                registeredCount++;
            }
            else if (scan.IsActive)
            {
                status = "unregistered_active";
                unregisteredActiveCount++;
            }
            else
            {
                status = "available";
                availableCount++;
            }

            addresses.Add(new IpAddressStatusDto(
                ipStr,
                status,
                slice,
                scan.Rtt,
                scan.ProbedAt,
                boundList
            ));
        }

        var poolDto = ToDto(row, totalAddresses, availableCount, registeredCount, unregisteredActiveCount, conflictCount, reservedCount);
        var slicesList = slicesSet.OrderBy(s => s, StringComparer.Ordinal).ToArray();

        return new IpPoolDetailDto(poolDto, slicesList, addresses);
    }

    public async Task<IpPoolScanResultDto> ScanAsync(long id, string? subnetSlice = null, CancellationToken ct = default)
    {
        var row = await db.OneAsync("select id, cidr from ip_pools where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "ip_pool.not_found", "指定的 IP 号段池不存在");

        var cidr = row.Text("cidr");
        var (network, broadcast, _, _, _, _) = ParseCidr(cidr);

        var targetIps = new List<string>();
        for (var u = network; u <= broadcast; u++)
        {
            var ipStr = UintToIp(u);
            if (!string.IsNullOrWhiteSpace(subnetSlice))
            {
                var slice = GetSubnetSlice(u);
                if (slice != subnetSlice.Trim()) continue;
            }
            targetIps.Add(ipStr);
        }

        // 1. 内存中并发 Ping 探测，绝不在此阶段占用任何数据库连接
        using var semaphore = new SemaphoreSlim(32);
        var activeCount = 0;
        var inactiveCount = 0;
        var results = new System.Collections.Concurrent.ConcurrentBag<(string Ip, bool Success, int? Rtt)>();

        var tasks = targetIps.Select(async ip =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var (success, rtt) = await ComputerService.PingIpAsync(ip, 250, ct);
                if (success) Interlocked.Increment(ref activeCount);
                else Interlocked.Increment(ref inactiveCount);

                results.Add((ip, success, success ? rtt : null));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // 2. 批量将探测结果落库（分批 500 条单条 SQL unnest 写入），使用单条数据库连接，绝不会耗尽连接池
        var resultList = results.ToList();
        const int batchSize = 500;
        for (var i = 0; i < resultList.Count; i += batchSize)
        {
            var chunk = resultList.Skip(i).Take(batchSize).ToList();
            var ips = chunk.Select(x => x.Ip).ToArray();
            var actives = chunk.Select(x => x.Success).ToArray();
            var rtts = chunk.Select(x => x.Rtt).ToArray();

            await db.ExecuteAsync(@"
                insert into ip_scan_records(pool_id, ip, is_active, response_time_ms, last_probed_at)
                select @id, unnest(cast(@ips as varchar[])), unnest(cast(@actives as boolean[])), unnest(cast(@rtts as integer[])), now()
                on conflict(pool_id, ip) do update set
                    is_active = excluded.is_active,
                    response_time_ms = excluded.response_time_ms,
                    last_probed_at = now()", new { id, ips, actives, rtts }, ct);
        }

        await db.ExecuteAsync("update ip_pools set last_scanned_at = now() where id = @id", new { id }, ct);

        return new IpPoolScanResultDto(id, targetIps.Count, activeCount, inactiveCount, DateTimeOffset.UtcNow);
    }

    public async Task AutoScanAllAsync(CancellationToken ct = default)
    {
        var pools = await db.QueryAsync("select id from ip_pools where auto_scan = true", ct: ct);
        foreach (var p in pools)
        {
            try
            {
                await ScanAsync(p.Id("id"), null, ct);
            }
            catch
            {
                // Ignore background auto-scan errors
            }
        }
    }

    private async Task<IpPoolDto> CalculatePoolSummaryAsync(JsonObject p, CancellationToken ct)
    {
        var id = p.Id("id");
        var cidr = p.Text("cidr");
        var gateway = p.Text("gateway");
        var (network, broadcast, _, _, _, _) = ParseCidr(cidr);
        var totalAddresses = (int)(broadcast - network + 1);

        var boundDevicesMap = await QueryAllBoundDevicesAsync(ct);
        var scanRows = await db.QueryAsync("select ip, is_active from ip_scan_records where pool_id = @id", new { id }, ct);
        var scanMap = scanRows.ToDictionary(r => r.Text("ip"), r => r.Flag("isActive"));

        var availableCount = 0;
        var registeredCount = 0;
        var unregisteredActiveCount = 0;
        var conflictCount = 0;
        var reservedCount = 0;

        for (var u = network; u <= broadcast; u++)
        {
            var ipStr = UintToIp(u);
            var boundList = boundDevicesMap.TryGetValue(ipStr, out var bList) ? bList : (IReadOnlyList<BoundDeviceInfoDto>)[];
            scanMap.TryGetValue(ipStr, out var isActive);

            var distinctDeviceCount = boundList.Select(b => (b.DeviceType, b.DeviceId)).Distinct().Count();

            if (u == network || u == broadcast || (!string.IsNullOrWhiteSpace(gateway) && ipStr == gateway) || boundList.Any(b => b.DeviceType == "network_device" && b.Role == "gateway"))
                reservedCount++;
            else if (distinctDeviceCount > 1)
                conflictCount++;
            else if (distinctDeviceCount == 1)
                registeredCount++;
            else if (isActive)
                unregisteredActiveCount++;
            else
                availableCount++;
        }

        return ToDto(p, totalAddresses, availableCount, registeredCount, unregisteredActiveCount, conflictCount, reservedCount);
    }

    private async Task<Dictionary<string, List<BoundDeviceInfoDto>>> QueryAllBoundDevicesAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, List<BoundDeviceInfoDto>>(StringComparer.OrdinalIgnoreCase);
        void Add(string? rawIp, BoundDeviceInfoDto info)
        {
            if (string.IsNullOrWhiteSpace(rawIp)) return;
            var parts = rawIp.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var validIps = parts.Where(p => IPAddress.TryParse(p, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (validIps.Count == 0) return;

            var finalInfo = validIps.Count > 1 && (info.RelatedIps == null || info.RelatedIps.Count == 0)
                ? info with { RelatedIps = validIps }
                : info;

            foreach (var ip in validIps)
            {
                if (!map.TryGetValue(ip, out var list))
                {
                    list = [];
                    map[ip] = list;
                }
                list.Add(finalInfo);
            }
        }

        // 1. 通道 (摄像头)
        var channels = await db.QueryAsync("select c.id, c.name, coalesce(c.alias, '') as alias, c.ip, u.name as unit_name from channels c left join units u on c.unit_id = u.id where c.ip is not null and c.ip <> '' and c.status <> 'disabled'", ct: ct);
        foreach (var ch in channels)
            Add(ch.Text("ip"), new BoundDeviceInfoDto("channel", ch.Id("id"), string.IsNullOrWhiteSpace(ch.Text("alias")) ? ch.Text("name") : ch.Text("alias"), "摄像机通道", Location: ch.Text("unitName")));

        // 2. 设备 (NVR / 集中存储)
        var devices = await db.QueryAsync("select id, name, host, model from devices where host is not null and host <> '' and enabled = true", ct: ct);
        foreach (var d in devices)
            Add(d.Text("host"), new BoundDeviceInfoDto("device", d.Id("id"), d.Text("name"), $"录像机 / 存储 ({d.Text("model")})"));

        // 3. ONU 设备
        var onus = await db.QueryAsync(@"
            select o.id, o.onu_name, o.alias, o.sn, o.ip, o.mac, o.terminal_type, tb.name as box_name
            from onu_devices o
            left join terminal_boxes tb on o.terminal_box_id = tb.id
            where o.ip is not null and o.ip <> ''", ct: ct);
        foreach (var o in onus)
        {
            var rawIp = o.Text("ip");
            var name = string.IsNullOrWhiteSpace(o.Text("alias")) ? o.Text("onuName") : o.Text("alias");
            var type = o.Text("terminalType");
            var sn = o.Text("sn");
            var detail = string.IsNullOrWhiteSpace(sn) ? $"全光 ONU ({type})" : $"全光 ONU ({type}) · SN: {sn}";
            Add(rawIp, new BoundDeviceInfoDto("onu", o.Id("id"), name, detail, Mac: o.Text("mac"), Location: o.Text("boxName")));
        }

        // 4. 电脑登记
        var comps = await db.QueryAsync("select c.id, c.name, c.ip, c.mac, c.owner, c.department, c.location from computers c where c.ip is not null and c.ip <> ''", ct: ct);
        foreach (var c in comps)
            Add(c.Text("ip"), new BoundDeviceInfoDto("computer", c.Id("id"), c.Text("name"), $"电脑终端 ({c.Text("department")})", Mac: c.Text("mac"), Location: c.Text("location"), Owner: c.Text("owner")));

        // 5. 网络管理设备 (核心交换机、汇聚交换机、路由器等及多接口)
        var netDevs = await db.QueryAsync("select id, name, device_type, model, management_ip, mac, location, owner from network_devices", ct: ct);
        var netIps = await db.QueryAsync("select id, device_id, ip, vlan_id, interface_name, role, description from network_device_ips", ct: ct);

        // 获取每台网络设备分配的所有关联 IP 列表
        var devRelatedIps = new Dictionary<long, List<string>>();
        foreach (var dev in netDevs)
        {
            var devId = dev.Id("id");
            var mgmt = dev.Text("managementIp");
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(mgmt)) list.Add(mgmt);
            devRelatedIps[devId] = list;
        }
        foreach (var nip in netIps)
        {
            var devId = nip.Id("deviceId");
            var ip = nip.Text("ip");
            if (devRelatedIps.TryGetValue(devId, out var list) && !list.Contains(ip, StringComparer.OrdinalIgnoreCase))
                list.Add(ip);
        }

        var netDevDict = netDevs.ToDictionary(d => d.Id("id"));

        foreach (var nip in netIps)
        {
            var devId = nip.Id("deviceId");
            if (!netDevDict.TryGetValue(devId, out var dev)) continue;
            var related = devRelatedIps.TryGetValue(devId, out var rlist) ? rlist : [];
            var typeDesc = dev.Text("deviceType") switch
            {
                "core_switch" => "核心交换机",
                "agg_switch" => "汇聚交换机",
                "access_switch" => "接入交换机",
                "router" => "路由器",
                "firewall" => "防火墙",
                "ac" => "无线控制器",
                "server" => "服务器",
                _ => "网络设备"
            };
            var vlanDesc = nip.TryGetPropertyValue("vlanId", out var vid) && vid != null ? $"VLAN {vid} " : "";
            var roleDesc = nip.Text("role") switch
            {
                "gateway" => "网关",
                "management" => "管理口",
                "interconnect" => "互联口",
                _ => nip.Text("role")
            };
            Add(nip.Text("ip"), new BoundDeviceInfoDto(
                "network_device",
                devId,
                dev.Text("name"),
                $"{typeDesc} · {nip.Text("interfaceName")} ({vlanDesc}{roleDesc})",
                Mac: dev.Text("mac"),
                Location: dev.Text("location"),
                Owner: dev.Text("owner"),
                InterfaceName: nip.Text("interfaceName"),
                Role: nip.Text("role"),
                RelatedIps: related
            ));
        }

        foreach (var dev in netDevs)
        {
            var mgmtIp = dev.Text("managementIp");
            if (string.IsNullOrWhiteSpace(mgmtIp)) continue;
            var devId = dev.Id("id");
            var related = devRelatedIps.TryGetValue(devId, out var rlist) ? rlist : [];
            var typeDesc = dev.Text("deviceType") switch
            {
                "core_switch" => "核心交换机",
                "agg_switch" => "汇聚交换机",
                _ => "网络设备"
            };
            Add(mgmtIp, new BoundDeviceInfoDto(
                "network_device",
                devId,
                dev.Text("name"),
                $"{typeDesc} · 带外管理口",
                Mac: dev.Text("mac"),
                Location: dev.Text("location"),
                Owner: dev.Text("owner"),
                InterfaceName: "Management",
                Role: "management",
                RelatedIps: related
            ));
        }

        // 6. 虚拟机登记 (从属于物理宿主机)
        var vms = await db.QueryAsync(@"
            select vm.id, vm.host_device_id, vm.vm_name, vm.ip, vm.vlan_id, vm.os, vm.cpu_cores, vm.memory_gb, vm.business_desc,
                   nd.name as host_name, nd.location as host_location
            from virtual_machines vm
            join network_devices nd on vm.host_device_id = nd.id
            where vm.ip is not null and vm.ip <> ''", ct: ct);
        foreach (var vm in vms)
        {
            var hostId = vm.Id("hostDeviceId");
            var related = devRelatedIps.TryGetValue(hostId, out var rlist) ? rlist : [];
            var business = !string.IsNullOrWhiteSpace(vm.Text("businessDesc")) ? $" · {vm.Text("businessDesc")}" : "";
            Add(vm.Text("ip"), new BoundDeviceInfoDto(
                "vm",
                vm.Id("id"),
                vm.Text("vmName"),
                $"虚拟机 (宿主机: {vm.Text("hostName")}{business})",
                Location: vm.Text("hostLocation"),
                Role: "virtual_machine",
                RelatedIps: related
            ));
        }

        return map;
    }

    private async Task RedactBindingsAsync(Dictionary<string, List<BoundDeviceInfoDto>> map, Actor? actor, CancellationToken ct)
    {
        if (actor is null) return;
        var allowed = await assets.AuthorizedRefsAsync(actor, ct);
        foreach (var key in map.Keys.ToArray())
            map[key] = map[key].Select(value => allowed.Contains(new AssetRef(value.DeviceType switch
            {
                "network_device" => "network-device", "onu_device" => "onu", "virtual_machine" => "virtual-machine",
                "camera" => "channel", _ => value.DeviceType
            }, value.DeviceId)) ? value : new BoundDeviceInfoDto("restricted", 0, "授权范围外资产", "地址已登记")).ToList();
    }

    public static (string Cidr, string StartIp, string EndIp, string Netmask, string? Gateway) ParseAndNormalizeCidr(IpPoolRequest req)
    {
        Rules.Require(!string.IsNullOrWhiteSpace(req.Cidr), "CIDR 地址段不能为空");
        var (network, broadcast, mask, prefix, startUint, endUint) = ParseCidr(req.Cidr.Trim());
        var networkIp = UintToIp(network);
        var normCidr = $"{networkIp}/{prefix}";
        var netmask = UintToIp(mask);

        var startIp = string.IsNullOrWhiteSpace(req.StartIp) ? UintToIp(startUint) : req.StartIp.Trim();
        var endIp = string.IsNullOrWhiteSpace(req.EndIp) ? UintToIp(endUint) : req.EndIp.Trim();
        var gateway = string.IsNullOrWhiteSpace(req.Gateway) ? UintToIp(startUint) : req.Gateway.Trim();

        return (normCidr, startIp, endIp, netmask, gateway);
    }

    public static (uint Network, uint Broadcast, uint Netmask, int Prefix, uint StartIp, uint EndIp) ParseCidr(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        Rules.Require(IPAddress.TryParse(parts[0], out var ip), "无效的 IPv4 网络地址");
        var prefix = 24;
        if (parts.Length > 1)
        {
            Rules.Require(int.TryParse(parts[1], out prefix) && prefix is >= 16 and <= 30, "子网前缀必须在 /16 到 /30 之间");
        }

        var ipBytes = ip!.GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(ipBytes);
        var ipUint = BitConverter.ToUInt32(ipBytes, 0);

        var mask = prefix == 0 ? 0 : 0xFFFFFFFF << (32 - prefix);
        var network = ipUint & mask;
        var broadcast = network | ~mask;
        var startIp = network + 1;
        var endIp = broadcast > 0 ? broadcast - 1 : broadcast;

        return (network, broadcast, mask, prefix, startIp, endIp);
    }

    public static string UintToIp(uint val)
    {
        var bytes = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new IPAddress(bytes).ToString();
    }

    public static string GetSubnetSlice(uint ipUint)
    {
        var bytes = BitConverter.GetBytes(ipUint);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.x";
    }

    private static IpPoolDto ToDto(JsonObject p, int totalCapacity, int available, int registered, int unregisteredActive, int conflict, int reserved) => new(
        p.Id("id"),
        p.Text("name"),
        p.Text("cidr"),
        p.Text("startIp"),
        p.Text("endIp"),
        p.Text("gateway"),
        p.Text("netmask") ?? "255.255.255.0",
        p.TryGetPropertyValue("vlanId", out var v) && v != null ? (int?)p.Id("vlanId") : null,
        p.Text("description"),
        NullableId(p, "workshopId"),
        p.Text("workshopName"),
        NullableId(p, "areaId"),
        p.Text("areaName"),
        p.Flag("autoScan"),
        totalCapacity,
        available,
        registered,
        unregisteredActive,
        conflict,
        reserved,
        DateTime(p, "lastScannedAt"),
        DateTime(p, "createdAt") ?? DateTimeOffset.UtcNow,
        DateTime(p, "updatedAt") ?? DateTimeOffset.UtcNow
    );

    private static long? NullableId(JsonObject r, string prop)
        => r.TryGetPropertyValue(prop, out var v) && v != null && v.GetValue<long?>() is { } id && id > 0 ? id : null;

    public async Task<IpConflictCheckResultDto> CheckIpConflictAsync(string ip, string? excludeType = null, long? excludeId = null, CancellationToken ct = default, Actor? actor = null)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip.Trim(), out _))
            return new IpConflictCheckResultDto(false, null, null, null);

        var targetIp = ip.Trim();
        var map = await QueryAllBoundDevicesAsync(ct);
        await RedactBindingsAsync(map, actor, ct);
        if (map.TryGetValue(targetIp, out var boundList) && boundList.Count > 0)
        {
            var activeBounds = boundList.Where(b => !(excludeType != null && excludeId.HasValue &&
                string.Equals(b.DeviceType, excludeType, StringComparison.OrdinalIgnoreCase) &&
                b.DeviceId == excludeId.Value)).ToList();

            if (activeBounds.Count > 0)
            {
                var bound = activeBounds[0];
                return new IpConflictCheckResultDto(true, bound.DeviceType, bound.DeviceName, bound.Detail ?? bound.DeviceName);
            }
        }

        return new IpConflictCheckResultDto(false, null, null, null);
    }

    public async Task<IpRecommendResultDto> RecommendIpAsync(long? workshopId = null, long? poolId = null, CancellationToken ct = default, Actor? actor = null)
    {
        if (actor is not null)
        {
            var row = await db.OneAsync($"select p.* from ip_pools p where p.id in ({AssetScope.AllowedIds("ip-pool")}) and (@poolId::bigint is null or p.id=@poolId) and (@workshopId::bigint is null or p.workshop_id=@workshopId) order by p.id limit 1", new { userId = actor.UserId, poolId, workshopId }, ct)
                ?? throw new PlatformException(404, "ip_pool.none", "当前授权范围尚未配置可用 IP 池");
            poolId = row.Id();
        }
        JsonObject? pool = null;
        if (poolId.HasValue && poolId.Value > 0)
        {
            pool = await db.OneAsync("select * from ip_pools where id = @poolId", new { poolId = poolId.Value }, ct);
        }
        else if (workshopId.HasValue && workshopId.Value > 0)
        {
            pool = await db.OneAsync("select * from ip_pools where workshop_id = @workshopId order by id limit 1", new { workshopId = workshopId.Value }, ct);
        }
        if (pool is null)
        {
            pool = await db.OneAsync("select * from ip_pools order by id limit 1", ct: ct);
        }
        if (pool is null)
        {
            throw new PlatformException(404, "ip_pool.none", "系统尚未配置任何 IP 池");
        }

        var cidr = pool.Text("cidr");
        var (network, broadcast, mask, prefix, startUint, endUint) = ParseCidr(cidr);
        var map = await QueryAllBoundDevicesAsync(ct);

        for (var cur = startUint; cur <= endUint; cur++)
        {
            var ipStr = UintToIp(cur);
            if (string.Equals(ipStr, pool.Text("gateway"), StringComparison.OrdinalIgnoreCase)) continue;
            if (!map.ContainsKey(ipStr))
            {
                return new IpRecommendResultDto(ipStr, pool.Text("name"), pool.Text("gateway"), pool.Text("netmask") ?? "255.255.255.0", pool.Id("id"));
            }
        }

        throw new PlatformException(409, "ip_pool.exhausted", $"号段【{pool.Text("name")}】可用 IP 已耗尽");
    }

    private static DateTimeOffset? DateTime(JsonObject r, string prop)
        => r.TryGetPropertyValue(prop, out var v) && v != null && System.DateTimeOffset.TryParse(v.ToString(), out var dt) ? dt : null;
}
