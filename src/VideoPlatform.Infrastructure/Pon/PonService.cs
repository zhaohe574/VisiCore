using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure.Pon;

public sealed class PonService(Database db, EsightPonClient client, ILogger<PonService>? logger = null)
{
    public async Task<PonStatisticsDto> GetStatisticsAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var scope = actor is null ? "true" : $"id in ({AssetScope.AllowedIds("onu")})";
        var counts = await db.OneAsync($@"
            select 
                count(*) as total_count,
                count(*) filter (where status in ('1', 'online') or run_status = '1') as online_count,
                count(*) filter (where not (status in ('1', 'online') or run_status = '1')) as offline_count
            from onu_devices where {scope}", new { userId = actor?.UserId }, ct);

        var modelRows = await db.QueryAsync($@"
            select terminal_type as name, count(*) as count 
            from onu_devices where {scope}
            group by terminal_type 
            order by count desc", new { userId = actor?.UserId }, ct);

        var models = modelRows.Select(r => new NameCountDto(r.Text("name", "未知型号"), r.Id("count"))).ToArray();
        return new PonStatisticsDto(
            counts.Id("totalCount"),
            counts.Id("onlineCount"),
            counts.Id("offlineCount"),
            models
        );
    }

    public async Task<PagedResponse<OnuDeviceDto>> ListOnusAsync(
        string? search = null,
        string? status = null,
        long? terminalBoxId = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default, Actor? actor = null)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var offset = (pageNumber - 1) * pageSize;

        var where = "1=1";
        if (actor is not null) where += $" and o.id in ({AssetScope.AllowedIds("onu")})";
        long? searchId = long.TryParse(search?.Trim(), out var parsedId) ? parsedId : null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " and (o.onu_name ilike @search or coalesce(o.alias, '') ilike @search or o.sn ilike @search or coalesce(o.mac, '') ilike @search or o.terminal_type ilike @search or coalesce(b.name, '') ilike @search or coalesce(o.description, '') ilike @search or coalesce(o.group_name, '') ilike @search or coalesce(o.ip, '') ilike @search or coalesce(o.if_desc, '') ilike @search";
            if (searchId.HasValue)
            {
                where += " or o.id = @searchId";
            }
            where += ")";
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status == "online")
                where += " and (o.status in ('1', 'online') or o.run_status = '1')";
            else if (status == "offline")
                where += " and not (o.status in ('1', 'online') or o.run_status = '1')";
            else
                where += " and o.health_status = @status";
        }
        if (terminalBoxId.HasValue)
            where += " and o.terminal_box_id = @terminalBoxId";

        var args = new
        {
            search = $"%{search?.Trim()}%",
            searchId,
            status,
            terminalBoxId,
            size = pageSize,
            offset, userId = actor?.UserId
        };

        var rows = await db.QueryAsync($@"
            select 
                o.*,
                b.name as terminal_box_name,
                b.code as terminal_box_code,
                (select count(*) from channel_network_bindings where onu_device_id = o.id) as bound_channel_count
            from onu_devices o
            left join terminal_boxes b on o.terminal_box_id = b.id
            where {where}
            order by (o.status in ('1', 'online') or o.run_status = '1') desc, o.port_id, o.onu_name
            limit @size offset @offset", args, ct);

        var totalRow = await db.OneAsync($"select count(*) as count from onu_devices o left join terminal_boxes b on o.terminal_box_id = b.id where {where}", args, ct);

        var items = rows.Select(ToOnuDto).ToArray();
        return new PagedResponse<OnuDeviceDto>(items, totalRow.Id("count"), pageNumber, pageSize);
    }

    public async Task<OnuDeviceDto> GetOnuAsync(long id, CancellationToken ct = default)
    {
        var row = await db.OneAsync(@"
            select 
                o.*,
                b.name as terminal_box_name,
                b.code as terminal_box_code,
                (select count(*) from channel_network_bindings where onu_device_id = o.id) as bound_channel_count
            from onu_devices o
            left join terminal_boxes b on o.terminal_box_id = b.id
            where o.id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "onu.not_found", "指定的 ONU 设备不存在");

        return ToOnuDto(row);
    }

    public async Task<OnuDeviceDto> UpdateOnuAsync(long id, OnuUpdateRequest request, CancellationToken ct = default)
    {
        var existing = await db.OneAsync("select * from onu_devices where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "onu.not_found", "指定的 ONU 设备不存在");

        var alias = request.Alias?.Trim();
        var terminalBoxId = request.TerminalBoxId;
        if (terminalBoxId.HasValue && terminalBoxId.Value > 0)
        {
            var box = await db.OneAsync("select id from terminal_boxes where id = @terminalBoxId", new { terminalBoxId }, ct)
                ?? throw new PlatformException(404, "terminal_box.not_found", "关联的终端箱不存在");
        }

        var hasSubSwitch = request.HasSubSwitch ?? existing.Flag("hasSubSwitch");
        var subSwitchModel = request.SubSwitchModel?.Trim() ?? existing.Text("subSwitchModel");
        var subSwitchPorts = request.SubSwitchPorts ?? (int)existing.Id("subSwitchPorts");
        var rawPortsConfig = request.PortsConfig?.Trim() ?? existing.Text("portsConfig", "[]");
        string jsonbPortsConfig;
        if (string.IsNullOrWhiteSpace(rawPortsConfig))
        {
            jsonbPortsConfig = "[]";
        }
        else if ((rawPortsConfig.StartsWith('{') && rawPortsConfig.EndsWith('}')) || (rawPortsConfig.StartsWith('[') && rawPortsConfig.EndsWith(']')))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawPortsConfig);
                jsonbPortsConfig = rawPortsConfig;
            }
            catch
            {
                jsonbPortsConfig = JsonSerializer.Serialize(rawPortsConfig);
            }
        }
        else
        {
            jsonbPortsConfig = JsonSerializer.Serialize(rawPortsConfig);
        }

        await db.ExecuteAsync(@"
            update onu_devices set
                alias = @alias,
                terminal_box_id = @terminalBoxId,
                has_sub_switch = @hasSubSwitch,
                sub_switch_model = @subSwitchModel,
                sub_switch_ports = @subSwitchPorts,
                ports_config = cast(@jsonbPortsConfig as jsonb),
                updated_at = now()
            where id = @id",
            new { id, alias, terminalBoxId, hasSubSwitch, subSwitchModel, subSwitchPorts, jsonbPortsConfig }, ct);

        string? syncWarning = null;
        var oldAlias = existing.Text("alias");
        var oldDesc = existing.Text("description");
        var newAlias = alias ?? "";
        var needSyncEsight = !string.IsNullOrWhiteSpace(newAlias) && (newAlias != oldAlias || newAlias != oldDesc);
        if (needSyncEsight)
        {
            var sn = existing.Text("sn");
            try
            {
                if (!string.IsNullOrWhiteSpace(sn))
                {
                    var neDn = existing.Text("neDn");
                    var onuName = existing.Text("onuName");
                    int? onuId = int.TryParse(existing.Text("onuId"), out var oid) ? oid : null;
                    long? ifIndex = long.TryParse(existing.Text("ifIndex"), out var ifidx) ? ifidx : null;
                    var onuDesc = existing.Text("description");

                    // 100% 官方北向 OpenAPI modifyaliasname 带场景模板联动 (shouldCreateSceneTask: true)
                    await client.ModifyOnuAliasAsync(sn, newAlias, neDn, onuId, onuName, ifIndex, onuDesc, ct);
                    logger?.LogInformation("已通过官方北向 OpenAPI 成功同步 ONU {Id} (SN: {Sn}, Alias: {Alias}) 别名至网管并触发场景模板联动", id, sn, newAlias);
                    await db.ExecuteAsync("update onu_devices set description = @newAlias where id = @id", new { newAlias, id }, ct);
                }
                else
                {
                    throw new PlatformException(400, "onu.missing_identifier", "该 ONU 缺少 SN 码，无法推送到网管");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "同步 ONU {Id} 别名至网管受限或失败: {Message}", id, ex.Message);
                syncWarning = $"本地资产与拓扑配置已保存成功。别名未同步至华为网管：{ex.Message}";
            }
        }

        var result = await GetOnuAsync(id, ct);
        return !string.IsNullOrWhiteSpace(syncWarning) ? result with { SyncWarning = syncWarning } : result;
    }

    public async Task<long?> SyncOltAsync(CancellationToken ct = default)
    {
        try
        {
            var oltArr = await client.QueryOltDevicesAsync(ct);
            if (oltArr.Count == 0) return null;

            long? firstOltId = null;
            foreach (var node in oltArr)
            {
                if (node is not JsonObject item) continue;
                var nedn = item["nedn"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(nedn)) continue;

                var name = item["name"]?.ToString() ?? "OLT";
                var ip = item["neIp"]?.ToString() ?? "10.37.5.3";
                var model = item["neType"]?.ToString() ?? "EA5801E-GP08-H3";
                var vendor = item["neVendorName"]?.ToString() ?? "Huawei";
                var osVersion = item["neOsVersion"]?.ToString();
                var patchVersion = item["nePatchVersion"]?.ToString();
                var status = item["neState"]?.ToString() == "1" ? "online" : "offline";

                var row = await db.OneAsync(@"
                    insert into olt_devices (nedn, name, ip, model, vendor, os_version, patch_version, status, synced_at)
                    values (@nedn, @name, @ip, @model, @vendor, @osVersion, @patchVersion, @status, now())
                    on conflict (nedn) do update set
                        name = excluded.name,
                        ip = excluded.ip,
                        model = excluded.model,
                        vendor = excluded.vendor,
                        os_version = excluded.os_version,
                        patch_version = excluded.patch_version,
                        status = excluded.status,
                        synced_at = now(),
                        updated_at = now()
                    returning id", new { nedn, name, ip, model, vendor, osVersion, patchVersion, status }, ct);

                if (row is not null && firstOltId is null)
                {
                    firstOltId = row.Id();
                }
            }
            return firstOltId;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "从华为 OpenAPI 同步 OLT 网元失败");
            return null;
        }
    }

    public async Task<int> SyncFromEsightAsync(CancellationToken ct = default)
    {
        logger?.LogInformation("开始通过华为 NeoSight 官方北向 OpenAPI 网关 (32102) 同步全网资产...");

        // 1. 同步 OLT 核心网元
        var defaultOltId = await SyncOltAsync(ct);
        if (defaultOltId is null)
        {
            var existingOlt = await db.OneAsync("select id from olt_devices limit 1", ct: ct);
            defaultOltId = existingOlt?.Id();
        }

        // 2. 查询先前的状态字典以跟踪状态变迁
        var previousStatusMap = (await db.QueryAsync("select id, ne_dn, frame_id, slot_id, port_id, onu_id, status, run_status, terminal_box_id, alias, onu_name, sn, health_status from onu_devices", ct: ct))
            .ToDictionary(r => (r.Text("neDn"), (int)r.Id("frameId"), (int)r.Id("slotId"), (int)r.Id("portId"), r.Text("onuId")));

        var openApiArr = await client.QueryOnuDevicesAsync(ct);
        var totalSynced = 0;

        foreach (var node in openApiArr)
        {
            if (node is not JsonObject item) continue;
            totalSynced += await UpsertSingleOnuAsync(item, defaultOltId, previousStatusMap, ct);
        }

        logger?.LogInformation("通过北向 OpenAPI 网关成功同步 {Count} 台 ONU 设备", totalSynced);

        // 3. 智能故障根因分析判定引擎 (Root Cause Engine)
        await EvaluateRootCausesAsync(ct);

        // 4. 采集全网光链路健康指数快照
        await RecordHealthSnapshotAsync(ct);

        return totalSynced;
    }

    private async Task<int> UpsertSingleOnuAsync(
        JsonObject item,
        long? oltId,
        Dictionary<(string, int, int, int, string), JsonObject> previousStatusMap,
        CancellationToken ct)
    {
        var neDn = item["neDn"]?.ToString() ?? "";
        var onuId = item["onuid"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(neDn) || string.IsNullOrWhiteSpace(onuId)) return 0;

        var frameId = 0;
        var slotId = 1;
        var portId = 0;
        if (int.TryParse(item["frameId"]?.ToString(), out var f)) frameId = f;
        if (int.TryParse(item["slotId"]?.ToString(), out var s)) slotId = s;
        if (int.TryParse(item["portId"]?.ToString(), out var p)) portId = p;

        var oltName = item["oltName"]?.ToString() ?? "OLT";
        var oltIp = item["oltIP"]?.ToString() ?? "10.37.5.3";
        var onuName = item["onuName"]?.ToString() ?? $"OLT/{frameId}/{slotId}/{portId}/{onuId}";

        if (onuName.Contains('/'))
        {
            var parts = onuName.Split('/');
            if (parts.Length >= 5)
            {
                if (int.TryParse(parts[1], out var pf)) frameId = pf;
                if (int.TryParse(parts[2], out var ps)) slotId = ps;
                if (int.TryParse(parts[3], out var pp)) portId = pp;
            }
        }

        var ifIndex = item["ifIndex"]?.ToString();
        var ifDesc = item["ifDesc"]?.ToString() ?? $"GPON {frameId}/{slotId}/{portId}";
        var alias = item["onualias"]?.ToString();
        var sn = item["sn"]?.ToString() ?? "";
        var mac = item["ontmac"]?.ToString();
        var terminalType = item["terminalType"]?.ToString() ?? "EG8040C-10";
        var runStatus = item["runStatus"]?.ToString() ?? "";
        var status = runStatus == "1" ? "online" : "offline";
        var lastDownCause = item["lastDownCause"]?.ToString();
        var description = item["description"]?.ToString();
        var groupName = item["groupMgrName"]?.ToString();
        var fullDn = item["fullDn"]?.ToString();
        var ip = item["ip"]?.ToString();

        var lastUpTime = ParseEsightTime(item["lastUpTime"]?.ToString());
        var lastDownTime = ParseEsightTime(item["lastDownTime"]?.ToString());
        var lastDyingGaspTime = ParseEsightTime(item["lastDyingGaspTime"]?.ToString());

        await db.ExecuteAsync(@"
            insert into onu_devices(
                ne_dn, olt_id, olt_name, olt_ip, frame_id, slot_id, port_id, onu_id,
                onu_name, alias, sn, mac, terminal_type, description, group_name,
                status, run_status, last_up_time, last_down_time, last_down_cause,
                last_dying_gasp_time, if_desc, full_dn, ip, if_index, last_synced_at
            ) values (
                @neDn, @oltId, @oltName, @oltIp, @frameId, @slotId, @portId, @onuId,
                @onuName, @alias, @sn, @mac, @terminalType, @description, @groupName,
                @status, @runStatus, @lastUpTime, @lastDownTime, @lastDownCause,
                @lastDyingGaspTime, @ifDesc, @fullDn, @ip, @ifIndex, now()
            )
            on conflict (ne_dn, frame_id, slot_id, port_id, onu_id) do update set
                olt_id = coalesce(excluded.olt_id, onu_devices.olt_id),
                olt_name = excluded.olt_name,
                olt_ip = excluded.olt_ip,
                onu_name = excluded.onu_name,
                alias = case 
                    when length(coalesce(onu_devices.alias, '')) > 0 then onu_devices.alias
                    when length(coalesce(excluded.alias, '')) > 0 then excluded.alias
                    when length(coalesce(excluded.description, '')) > 0 then excluded.description
                    else onu_devices.alias end,
                sn = case when length(excluded.sn) > 0 then excluded.sn else onu_devices.sn end,
                mac = coalesce(excluded.mac, onu_devices.mac),
                terminal_type = excluded.terminal_type,
                description = coalesce(excluded.description, onu_devices.description),
                group_name = coalesce(excluded.group_name, onu_devices.group_name),
                status = excluded.status,
                run_status = excluded.run_status,
                last_up_time = excluded.last_up_time,
                last_down_time = excluded.last_down_time,
                last_down_cause = excluded.last_down_cause,
                last_dying_gasp_time = excluded.last_dying_gasp_time,
                if_desc = excluded.if_desc,
                full_dn = coalesce(excluded.full_dn, onu_devices.full_dn),
                ip = coalesce(excluded.ip, onu_devices.ip),
                if_index = coalesce(excluded.if_index, onu_devices.if_index),
                last_synced_at = now()",
            new { neDn, oltId, oltName, oltIp, frameId, slotId, portId, onuId, onuName, alias, sn, mac, terminalType, description, groupName, status, runStatus, lastUpTime, lastDownTime, lastDownCause, lastDyingGaspTime, ifDesc, fullDn, ip, ifIndex }, ct);

        // 状态变迁记账（掉线与恢复历史跟踪）
        var isNowOnline = runStatus == "1";
        if (previousStatusMap.TryGetValue((neDn, frameId, slotId, portId, onuId), out var prev))
        {
            var wasOnline = prev.Text("status") is "1" or "online" || prev.Text("runStatus") is "1";
            var onuDbId = prev.Id();
            var prevBoxId = prev.Id("terminalBoxId") == 0 ? (long?)null : prev.Id("terminalBoxId");

            if (wasOnline && !isNowOnline)
            {
                var devName = !string.IsNullOrWhiteSpace(alias) ? alias : (!string.IsNullOrWhiteSpace(prev.Text("alias")) ? prev.Text("alias") : onuName);
                var downAt = lastDownTime ?? DateTimeOffset.UtcNow;
                var downReason = !string.IsNullOrWhiteSpace(lastDownCause) ? lastDownCause : "光网离线";
                await db.ExecuteAsync(@"
                    insert into device_offline_history(
                        device_type, terminal_box_id, onu_device_id, device_name, device_identifier,
                        event_type, down_time, reason
                    )
                    select 'onu', @prevBoxId, @onuDbId, @devName, @sn, 'offline', @downAt, @downReason
                    where not exists (
                        select 1 from device_offline_history
                        where onu_device_id = @onuDbId and up_time is null
                    )", new { prevBoxId, onuDbId, devName, sn, downAt, downReason }, ct);
            }
            else if (!wasOnline && isNowOnline)
            {
                var upAt = lastUpTime ?? DateTimeOffset.UtcNow;
                await db.ExecuteAsync(@"
                    update device_offline_history set
                        up_time = @upAt,
                        duration_seconds = greatest(0, extract(epoch from (@upAt - down_time))::int)
                    where onu_device_id = @onuDbId and up_time is null", new { upAt, onuDbId }, ct);
            }
        }

        return 1;
    }

    /// <summary>
    /// 全维度智能故障根因分析判定引擎与 30 秒告警防抖
    /// </summary>
    public async Task EvaluateRootCausesAsync(CancellationToken ct = default)
    {
        var onus = await db.QueryAsync(@"
            select id, port_id, if_desc, status, run_status, last_down_cause,
                   last_down_time, last_dying_gasp_time, rx_power, health_status,
                   last_state_change_time, terminal_box_id
            from onu_devices", ct: ct);

        // 1. 统计各 PON 口下的总设备数、在线数、离线数
        var portStats = onus
            .GroupBy(o => {
                var d = o.Text("ifDesc");
                return !string.IsNullOrWhiteSpace(d) ? d : $"GPON 0/1/{o.Id("portId")}";
            })
            .ToDictionary(
                g => g.Key,
                g => new {
                    Total = g.Count(),
                    Online = g.Count(o => o.Text("status") is "1" or "online" || o.Text("runStatus") is "1"),
                    Offline = g.Count(o => !(o.Text("status") is "1" or "online" || o.Text("runStatus") is "1"))
                }
            );

        // 2. 统计同终端箱（terminal_box_id）收到 Dying Gasp 掉电遗言的离线设备数
        var boxDyingGaspCounts = onus
            .Where(o => !(o.Text("status") is "1" or "online" || o.Text("runStatus") is "1"))
            .Where(o => {
                var gt = o.NullableTime("lastDyingGaspTime");
                var dt = o.NullableTime("lastDownTime");
                return gt.HasValue && dt.HasValue && Math.Abs((dt.Value - gt.Value).TotalSeconds) <= 15;
            })
            .Where(o => o.Id("terminalBoxId") > 0)
            .GroupBy(o => o.Id("terminalBoxId"))
            .ToDictionary(g => g.Key, g => g.Count());

        // 3. 统计同 PON 口下【未收到掉电遗言】的离线设备列表（用于 3 分钟并发失联判定）
        var offlineNoGaspByPort = onus
            .Where(o => !(o.Text("status") is "1" or "online" || o.Text("runStatus") is "1"))
            .Where(o => {
                var gt = o.NullableTime("lastDyingGaspTime");
                var dt = o.NullableTime("lastDownTime");
                return !(gt.HasValue && dt.HasValue && Math.Abs((dt.Value - gt.Value).TotalSeconds) <= 15);
            })
            .GroupBy(o => {
                var d = o.Text("ifDesc");
                return !string.IsNullOrWhiteSpace(d) ? d : $"GPON 0/1/{o.Id("portId")}";
            })
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var onu in onus)
        {
            var id = onu.Id();
            var isOnline = onu.Text("status") is "1" or "online" || onu.Text("runStatus") is "1";
            var ifDescRaw = onu.Text("ifDesc");
            var ifDesc = !string.IsNullOrWhiteSpace(ifDescRaw) ? ifDescRaw : $"GPON 0/1/{onu.Id("portId")}";
            var currentHealth = onu.Text("healthStatus", "healthy");
            var downTime = onu.NullableTime("lastDownTime");
            var gaspTime = onu.NullableTime("lastDyingGaspTime");
            var cause = onu.Text("lastDownCause");
            var rxPower = onu.Decimal("rxPower");
            var boxId = onu.Id("terminalBoxId");

            string newHealth;
            string? newRootCause;

            if (!isOnline)
            {
                var isGasp = gaspTime.HasValue && downTime.HasValue && Math.Abs((downTime.Value - gaspTime.Value).TotalSeconds) <= 15;

                // 1. 优先定界：收到 Dying Gasp 掉电遗言 -> 供电中断
                if (isGasp)
                {
                    newHealth = "power_off";
                    var boxGaspCount = boxId > 0 && boxDyingGaspCounts.TryGetValue(boxId, out var bc) ? bc : 1;
                    if (boxGaspCount >= 2)
                    {
                        newRootCause = "终端箱掉电 / 总供电中断 ⚡ (同箱多台收到 Dying Gasp 掉电遗言)";
                    }
                    else
                    {
                        newRootCause = "设备掉电 / 供电中断 ⚡ (收到网管 Dying Gasp 掉电遗言)";
                    }
                }
                // 2. 主干光纤全灭：仅当同 PON 端口全部设备失联（在线数 = 0 且纳管总数 >= 2）时才判定为主干中断
                else if (portStats.TryGetValue(ifDesc, out var pStat) && pStat.Total >= 2 && pStat.Online == 0)
                {
                    newHealth = "trunk_broken";
                    newRootCause = $"主干光缆中断 / PON口故障 🚨 (同 PON 口 {ifDesc} 下全量 {pStat.Total} 台设备全部失联)";
                }
                // 3. 多台并发失联：同 PON 口无掉电遗言的多台在 3 分钟内并发失联 -> 分光器 / 支干光缆中断
                else
                {
                    var concurrentCount = 0;
                    if (downTime.HasValue && offlineNoGaspByPort.TryGetValue(ifDesc, out var portNoGaspList))
                    {
                        concurrentCount = portNoGaspList.Count(other =>
                        {
                            var otherDt = other.NullableTime("lastDownTime");
                            return otherDt.HasValue && Math.Abs((downTime.Value - otherDt.Value).TotalSeconds) <= 180;
                        });
                    }

                    if (concurrentCount >= 2)
                    {
                        newHealth = "splitter_broken";
                        newRootCause = $"分光器/支干光缆中断 💥 (同 PON 口 {ifDesc} 下 3 分钟内并发失联 {concurrentCount} 台)";
                    }
                    else
                    {
                        // 4. 单台离线设备细分定界
                        if (cause == "2")
                        {
                            newHealth = "fiber_cut";
                            newRootCause = "分支光纤断纤 / 尾纤松脱 🔴 (网管上报 LOSi 物理失光)";
                        }
                        else
                        {
                            newHealth = "fiber_cut";
                            newRootCause = $"分支链路中断 / 离线待排查 🔴 (Cause: {(!string.IsNullOrWhiteSpace(cause) ? cause : "13")})";
                        }
                    }
                }
            }
            else
            {
                // 在线设备：接收光功率处于劣化区间 (-25dBm ~ -27dBm) -> 弱光亚健康预警
                if (rxPower.HasValue && rxPower.Value < -25.0m)
                {
                    newHealth = "sub_health_weak_light";
                    newRootCause = $"弱光亚健康 🟡 (接收光功率: {rxPower.Value:F2} dBm，随时面临断流风险)";
                }
                else
                {
                    newHealth = "healthy";
                    newRootCause = null;
                }
            }

            // 30 秒状态防抖机制 (ApplyStateDebounce)
            var lastStateChange = onu.NullableTime("lastStateChangeTime") ?? DateTimeOffset.MinValue;
            var elapsed = lastStateChange != DateTimeOffset.MinValue ? DateTimeOffset.UtcNow - lastStateChange : TimeSpan.FromSeconds(999);
            var healthChanged = currentHealth != newHealth;

            if (healthChanged)
            {
                if (currentHealth == "jitter_buffering")
                {
                    // 已经在抖动缓冲中
                    if (elapsed >= TimeSpan.FromSeconds(30))
                    {
                        // 缓冲已满 30 秒，确认新状态并定界
                        await db.ExecuteAsync(@"
                            update onu_devices set
                                health_status = @newHealth,
                                root_cause = @newRootCause,
                                last_state_change_time = now(),
                                updated_at = now()
                            where id = @id",
                            new { id, newHealth, newRootCause }, ct);
                    }
                    else
                    {
                        // 仍在 30 秒缓冲期内，更新预期根因说明并保持缓冲
                        var bufferingCause = $"30s 闪断防抖缓冲中 ({newRootCause ?? "状态变迁中"})";
                        await db.ExecuteAsync(@"
                            update onu_devices set
                                root_cause = @bufferingCause,
                                updated_at = now()
                            where id = @id and coalesce(root_cause, '') != @bufferingCause",
                            new { id, bufferingCause }, ct);
                    }
                }
                else
                {
                    // 从非缓冲状态跃迁至新状态：
                    // 若从稳定状态突变至故障状态，且属于突发跳变（30s 内），先置入 30 秒防抖缓冲
                    if (newHealth != "healthy" && elapsed < TimeSpan.FromSeconds(30))
                    {
                        var bufferingCause = $"30s 闪断防抖缓冲中 ({newRootCause ?? "状态变迁中"})";
                        await db.ExecuteAsync(@"
                            update onu_devices set
                                health_status = 'jitter_buffering',
                                root_cause = @bufferingCause,
                                last_state_change_time = now(),
                                updated_at = now()
                            where id = @id",
                            new { id, bufferingCause }, ct);
                    }
                    else
                    {
                        // 正常跃迁生效
                        await db.ExecuteAsync(@"
                            update onu_devices set
                                health_status = @newHealth,
                                root_cause = @newRootCause,
                                last_state_change_time = now(),
                                updated_at = now()
                            where id = @id",
                            new { id, newHealth, newRootCause }, ct);
                    }
                }
            }
            else
            {
                await db.ExecuteAsync(@"
                    update onu_devices set
                        root_cause = @newRootCause
                    where id = @id and coalesce(root_cause, '') != coalesce(@newRootCause, '')",
                    new { id, newRootCause }, ct);
            }
        }
    }

    public async Task RecordHealthSnapshotAsync(CancellationToken ct = default)
    {
        await db.ExecuteAsync(@"
            insert into optical_health_snapshots (
                score, total_onus, online_onus, offline_onus, healthy_onus,
                sub_health_onus, power_off_onus, fiber_cut_onus, trunk_broken_onus, splitter_broken_onus,
                avg_rx_power, recorded_at
            )
            select
                greatest(0, 100 
                    - (count(*) filter (where not (status in ('1', 'online') or run_status = '1')) * 1) 
                    - (count(*) filter (where (status in ('1', 'online') or run_status = '1') and (health_status = 'sub_health_weak_light' or (rx_power is not null and rx_power < -25.0))) * 1)
                    - (case when exists (select 1 from onu_devices where health_status = 'trunk_broken') then 5 else 0 end)
                )::int,
                count(*)::int,
                count(*) filter (where status in ('1', 'online') or run_status = '1')::int,
                count(*) filter (where not (status in ('1', 'online') or run_status = '1'))::int,
                count(*) filter (where (status in ('1', 'online') or run_status = '1') and health_status = 'healthy')::int,
                count(*) filter (where health_status = 'sub_health_weak_light')::int,
                count(*) filter (where health_status = 'power_off')::int,
                count(*) filter (where health_status = 'fiber_cut')::int,
                count(*) filter (where health_status = 'trunk_broken')::int,
                count(*) filter (where health_status = 'splitter_broken')::int,
                round(avg(rx_power) filter (where rx_power is not null), 2),
                now()
            from onu_devices", ct: ct);
    }

    public async Task<IReadOnlyList<OltDeviceDto>> GetOltsWithPortsAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var olts = await db.QueryAsync("select * from olt_devices order by id", ct: ct);
        if (olts.Count == 0)
        {
            // 兜底虚拟 OLT
            olts = [new JsonObject
            {
                ["id"] = 1,
                ["nedn"] = "a26a9d2a-ac90-4a80-8051-4288d3eb07e1",
                ["name"] = "OLT",
                ["ip"] = "10.37.5.3",
                ["model"] = "EA5801E-GP08-H3",
                ["vendor"] = "Huawei",
                ["osVersion"] = "EA5801EV100R025C10",
                ["patchVersion"] = "SPC200",
                ["status"] = "online",
                ["syncedAt"] = DateTimeOffset.UtcNow
            }];
        }

        var onuRows = await db.QueryAsync($"select id, port_id, if_desc, status, run_status, health_status, root_cause, olt_id from onu_devices where {(actor is null ? "true" : $"id in ({AssetScope.AllowedIds("onu")})")}", new { userId = actor?.UserId }, ct);
        if (actor is not null && !await AssetScope.IsFullAsync(db, actor, ct))
            olts = olts.Where(olt => onuRows.Any(onu => onu.Id("oltId") == olt.Id())).ToList();

        var result = new List<OltDeviceDto>();
        foreach (var olt in olts)
        {
            var oltId = olt.Id();
            var matchingOnus = onuRows.Where(o => o.Id("oltId") == 0 || o.Id("oltId") == oltId).ToList();

            var ports = new List<PonPortSummaryDto>();
            for (var p = 0; p < 8; p++)
            {
                var pOnus = matchingOnus.Where(o => (int)o.Id("portId") == p).ToList();
                var total = pOnus.Count;
                var online = pOnus.Count(o => o.Text("status") is "1" or "online" || o.Text("runStatus") is "1");
                var offline = total - online;
                var subHealth = pOnus.Count(o => o.Text("healthStatus") == "sub_health_weak_light");
                var splitterBroken = pOnus.Count(o => o.Text("healthStatus") == "splitter_broken");
                var powerOff = pOnus.Count(o => o.Text("healthStatus") == "power_off");

                string portStatus;
                var causes = new List<string>();

                // 仅当端口总设备数 >= 2 且全部离线 (在线数 = 0) 时，才定界为主干中断
                if (total >= 2 && online == 0)
                {
                    portStatus = "trunk_broken";
                    causes.Add($"同端口 {total} 台设备全灭，主干光缆中断");
                }
                else if (subHealth > 0 || offline > 0)
                {
                    portStatus = "warning";
                    if (splitterBroken > 0) causes.Add($"{splitterBroken} 台疑似支干/分光中断");
                    if (powerOff > 0) causes.Add($"{powerOff} 台掉电");
                    var otherOffline = offline - splitterBroken - powerOff;
                    if (otherOffline > 0) causes.Add($"{otherOffline} 台分支中断/离线");
                    if (subHealth > 0) causes.Add($"{subHealth} 台设备弱光亚健康");
                }
                else
                {
                    portStatus = "healthy";
                }

                ports.Add(new PonPortSummaryDto(
                    p,
                    $"GPON 0/1/{p}",
                    total,
                    online,
                    offline,
                    subHealth,
                    portStatus,
                    causes
                ));
            }

            result.Add(new OltDeviceDto(
                oltId,
                olt.Text("nedn"),
                olt.Text("name", "OLT"),
                olt.Text("ip", "10.37.5.3"),
                olt.Text("model", "EA5801E-GP08-H3"),
                olt.Text("vendor", "Huawei"),
                olt.Text("osVersion"),
                olt.Text("patchVersion"),
                olt.Text("status", "online"),
                olt.Time("syncedAt"),
                8,
                ports
            ));
        }

        return result;
    }

    public async Task<OpticalHealthIndexDto> GetHealthIndexAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var counts = await db.OneAsync($@"
            select 
                count(*)::int as total,
                count(*) filter (where status in ('1', 'online') or run_status = '1')::int as online,
                count(*) filter (where not (status in ('1', 'online') or run_status = '1'))::int as offline,
                count(*) filter (where (status in ('1', 'online') or run_status = '1') and health_status = 'healthy')::int as healthy,
                count(*) filter (where health_status = 'sub_health_weak_light')::int as sub_health,
                count(*) filter (where health_status = 'power_off')::int as power_off,
                count(*) filter (where health_status = 'fiber_cut')::int as fiber_cut,
                count(*) filter (where health_status = 'trunk_broken')::int as trunk_broken,
                count(*) filter (where health_status = 'splitter_broken')::int as splitter_broken,
                round(avg(rx_power) filter (where rx_power is not null), 2) as avg_rx_power
            from onu_devices where {(actor is null ? "true" : $"id in ({AssetScope.AllowedIds("onu")})")}", new { userId = actor?.UserId }, ct) ?? new JsonObject();

        int total = (int)counts.Id("total");
        int online = (int)counts.Id("online");
        int offline = (int)counts.Id("offline");
        int healthy = (int)counts.Id("healthy");
        int subHealth = (int)counts.Id("subHealth");
        int powerOff = (int)counts.Id("powerOff");
        int fiberCut = (int)counts.Id("fiberCut");
        int trunkBroken = (int)counts.Id("trunkBroken");
        int splitterBroken = (int)counts.Id("splitterBroken");
        decimal? avgRxPower = counts["avgRxPower"] is not null ? counts.Decimal("avgRxPower") : null;

        var availabilityRate = total > 0 ? Math.Round((decimal)online / total * 100m, 1) : 100m;
        var score = Math.Clamp(100 - (offline * 1) - (subHealth * 1) - (trunkBroken > 0 ? 5 : 0), 0, 100);
        var grade = score >= 90 ? "优秀" : (score >= 80 ? "良好" : (score >= 60 ? "预警" : "高危"));

        // 获取最近 24 次健康趋势快照
        var snapshots = actor is null || await AssetScope.IsFullAsync(db, actor, ct) ? await db.QueryAsync(@"
            select score, total_onus, online_onus, offline_onus, sub_health_onus, recorded_at
            from optical_health_snapshots
            order by recorded_at desc
            limit 24", ct: ct) : [];

        var trend = new List<OpticalHealthTrendItemDto>();
        if (snapshots.Count > 0)
        {
            foreach (var s in snapshots.OrderBy(s => s.Time("recordedAt")))
            {
                var sTotal = (int)s.Id("totalOnus");
                var sOnline = (int)s.Id("onlineOnus");
                var sAvail = sTotal > 0 ? Math.Round((decimal)sOnline / sTotal * 100m, 1) : 100m;
                trend.Add(new OpticalHealthTrendItemDto(
                    s.Time("recordedAt"),
                    (int)s.Id("score"),
                    sAvail,
                    (int)s.Id("offlineOnus"),
                    (int)s.Id("subHealthOnus")
                ));
            }
        }
        else
        {
            trend.Add(new OpticalHealthTrendItemDto(DateTimeOffset.UtcNow, score, availabilityRate, offline, subHealth));
        }

        return new OpticalHealthIndexDto(
            score,
            grade,
            total,
            online,
            offline,
            healthy,
            subHealth,
            powerOff,
            fiberCut,
            trunkBroken,
            availabilityRate,
            avgRxPower,
            DateTimeOffset.UtcNow,
            trend,
            splitterBroken
        );
    }

    public async Task<AutoTopologyDiscoverResultDto> AutoDiscoverBindingsAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var channels = await db.QueryAsync($@"
            select c.id, c.name, c.alias, c.ip, c.unit_id, c.remark,
                   u.parent_id as channel_area_id,
                   b.onu_device_id, b.terminal_box_id, b.port_number, b.port_label,
                   o.onu_name as current_onu_name
            from channels c
            left join units u on u.id = c.unit_id
            left join channel_network_bindings b on b.channel_id = c.id
            left join onu_devices o on b.onu_device_id = o.id
            where {(actor is null ? "true" : $"c.id in (select c.id from {AccessService.ChannelFrom} where {AccessService.ChannelPredicate})")}
            order by c.id", new { userId = actor?.UserId }, ct);

        var onus = await db.QueryAsync($@"
            select o.id, o.onu_name, o.alias, o.description, o.group_name, o.ip, o.mac,
                   o.terminal_box_id, tb.code as box_code, tb.name as box_name,
                   tb.area_id as box_area_id, tb.unit_id as box_unit_id
            from onu_devices o
            left join terminal_boxes tb on tb.id = o.terminal_box_id where {(actor is null ? "true" : $"o.id in ({AssetScope.AllowedIds("onu")})")}", new { userId = actor?.UserId }, ct);

        // 跟踪每个 ONU 已被占用的物理端口，避免所有推导通道扎堆抢占端口 1
        var usedPortsByOnu = channels
            .Where(c => c.Id("onuDeviceId") > 0 && c.Id("portNumber") > 0)
            .GroupBy(c => c.Id("onuDeviceId"))
            .ToDictionary(g => g.Key, g => g.Select(c => (int)c.Id("portNumber")).ToHashSet());

        var suggestions = new List<AutoTopologyBindingSuggestionDto>();

        foreach (var ch in channels)
        {
            var chId = ch.Id();
            var chName = ch.Text("name");
            var chAlias = ch.Text("alias");
            var chIp = ch.Text("ip");
            var chRemark = ch.Text("remark");
            var chUnitId = ch.Id("unitId") == 0 ? (long?)null : ch.Id("unitId");
            var currentOnuId = ch.Id("onuDeviceId") == 0 ? (long?)null : ch.Id("onuDeviceId");
            var currentOnuName = ch.Text("currentOnuName");
            var currentPortNumber = (int)ch.Id("portNumber");

            var chTokens = ExtractKeywords(chAlias, chName, chRemark);

            var bestScore = 0;
            JsonObject? bestOnu = null;
            var matchReasons = new List<string>();

            foreach (var onu in onus)
            {
                var score = 0;
                var reasons = new List<string>();

                var onuIp = onu.Text("ip");
                var onuAlias = onu.Text("alias");
                var onuDesc = onu.Text("description");
                var onuName = onu.Text("onuName");
                var onuGroup = onu.Text("groupName");
                var boxCode = onu.Text("boxCode");
                var boxName = onu.Text("boxName");
                var onuBoxAreaId = onu.Id("boxAreaId") == 0 ? (long?)null : onu.Id("boxAreaId");
                var onuBoxUnitId = onu.Id("boxUnitId") == 0 ? (long?)null : onu.Id("boxUnitId");
                var chAreaId = ch.Id("channelAreaId") == 0 ? (long?)null : ch.Id("channelAreaId");

                // 1. IP 地址关联判定（同 /24 网段或精确匹配）
                if (!string.IsNullOrWhiteSpace(chIp) && !string.IsNullOrWhiteSpace(onuIp))
                {
                    if (chIp == onuIp)
                    {
                        score += 90;
                        reasons.Add("IP 地址精确匹配");
                    }
                    else
                    {
                        var chParts = chIp.Split('.');
                        var onuParts = onuIp.Split('.');
                        if (chParts.Length == 4 && onuParts.Length == 4 &&
                            chParts[0] == onuParts[0] && chParts[1] == onuParts[1] && chParts[2] == onuParts[2])
                        {
                            score += 55;
                            reasons.Add($"同一 /24 子网网段 ({chParts[0]}.{chParts[1]}.{chParts[2]}.0/24)");
                        }
                    }
                }

                // 2. 空间划拨区域/单元匹配
                if (chAreaId.HasValue && onuBoxAreaId.HasValue && chAreaId.Value == onuBoxAreaId.Value)
                {
                    score += 25;
                    reasons.Add("所属业务区域一致");
                }
                else if (chUnitId.HasValue && onuBoxUnitId.HasValue && chUnitId.Value == onuBoxUnitId.Value)
                {
                    score += 25;
                    reasons.Add("所属组织单元一致");
                }

                // 3. 终端箱编号契合（例如 HG3-W2, LX1-W1）
                if (!string.IsNullOrWhiteSpace(boxCode) && boxCode.Length >= 3)
                {
                    var chCombined = $"{chName} {chAlias} {chRemark}";
                    if (chCombined.Contains(boxCode, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 35;
                        reasons.Add($"终端箱编号契合 ({boxCode})");
                    }
                }

                // 4. 中文与工业机组关键词特征多维契合
                var onuTokens = ExtractKeywords(onuAlias, onuDesc, onuName, onuGroup, boxCode, boxName);
                var common = chTokens.Intersect(onuTokens, StringComparer.OrdinalIgnoreCase).ToList();
                if (common.Count > 0)
                {
                    var highValCount = common.Count(k => DomainKeywords.Contains(k) || k.Length >= 3);
                    var kwScore = Math.Min(50, common.Count * 12 + highValCount * 15);
                    score += kwScore;
                    reasons.Add($"特征关键词匹配 ({string.Join(",", common.Take(3))})");
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOnu = onu;
                    matchReasons = reasons;
                }
            }

            if (bestOnu is not null && bestScore >= 40)
            {
                var suggestedOnuId = bestOnu.Id();
                var suggestedBoxId = bestOnu.Id("terminalBoxId") == 0 ? (long?)null : bestOnu.Id("terminalBoxId");
                var needsCorrection = currentOnuId != suggestedOnuId;

                // 自动分配不冲突端口 (1..4)
                if (!usedPortsByOnu.TryGetValue(suggestedOnuId, out var usedPorts))
                {
                    usedPorts = new HashSet<int>();
                    usedPortsByOnu[suggestedOnuId] = usedPorts;
                }

                int suggestedPortNumber = 1;
                if (currentOnuId == suggestedOnuId && currentPortNumber > 0)
                {
                    suggestedPortNumber = currentPortNumber;
                }
                else
                {
                    for (var p = 1; p <= 8; p++)
                    {
                        if (!usedPorts.Contains(p))
                        {
                            suggestedPortNumber = p;
                            break;
                        }
                    }
                }
                usedPorts.Add(suggestedPortNumber);

                suggestions.Add(new AutoTopologyBindingSuggestionDto(
                    chId,
                    chName,
                    chAlias,
                    chIp,
                    currentOnuId,
                    currentOnuName,
                    suggestedOnuId,
                    bestOnu.Text("onuName"),
                    bestOnu.Text("alias"),
                    suggestedPortNumber,
                    suggestedBoxId,
                    bestOnu.Text("boxName"),
                    bestScore,
                    string.Join("；", matchReasons),
                    needsCorrection
                ));
            }
        }

        return new AutoTopologyDiscoverResultDto(
            channels.Count,
            suggestions.Count,
            suggestions
        );
    }

    private static readonly HashSet<string> DomainKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "办公楼", "一楼", "二楼", "三楼", "四楼", "五楼", "1楼", "2楼", "3楼", "4楼", "5楼",
        "外围", "保卫", "车间", "仓库", "门卫", "大门", "西门", "东门", "南门", "北门",
        "地磅", "供应磅", "活套", "原料", "机组", "供应", "螺旋", "制管", "轧钢", "高线",
        "冷轧", "酸洗", "热轧", "热处理", "精整", "连铸", "炼钢", "165", "219", "114",
        "成型", "定径", "销售", "财务", "行政", "品管", "会议室", "餐厅", "宿舍"
    };

    private static List<string> ExtractKeywords(params string?[] texts)
    {
        var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var delimiters = new[] { ' ', '-', '_', '/', '(', ')', '（', '）', '[', ']', ':', '：', ',', '，' };

        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;

            // 1. 标点符号分词
            var parts = t.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var clean = p.Trim();
                if (clean.Length >= 2 && !int.TryParse(clean, out _))
                {
                    list.Add(clean);
                }
            }

            // 2. 字母数字特征串（例如 LX1, HG3, 165, 219, ZG）
            var matches = System.Text.RegularExpressions.Regex.Matches(t, @"[A-Za-z]+[0-9]*|[0-9]{3,}");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (m.Value.Length >= 2)
                {
                    list.Add(m.Value.ToUpperInvariant());
                }
            }

            // 3. 工业与厂区领域词特征提取
            foreach (var dk in DomainKeywords)
            {
                if (t.Contains(dk, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(dk);
                }
            }

            // 4. 中文字符 2-gram 与 3-gram 切分
            var cleanText = System.Text.RegularExpressions.Regex.Replace(t, @"[^\u4e00-\u9fa5]", "");
            for (var len = 2; len <= 3; len++)
            {
                for (var i = 0; i <= cleanText.Length - len; i++)
                {
                    list.Add(cleanText.Substring(i, len));
                }
            }
        }

        return list.ToList();
    }

    public async Task<int> ApplyTopologyCorrectionsAsync(ApplyTopologyCorrectionRequest request, CancellationToken ct = default)
    {
        if (request.Bindings.Count == 0) return 0;

        await db.TransactionAsync(async tx =>
        {
            foreach (var b in request.Bindings)
            {
                await tx.ExecuteAsync(@"
                    insert into channel_network_bindings (
                        channel_id, terminal_box_id, onu_device_id, port_number, is_switch_port, port_label, updated_at
                    ) values (
                        @channelId, @terminalBoxId, @onuDeviceId, @portNumber, @isSwitchPort, @portLabel, now()
                    )
                    on conflict (channel_id) do update set
                        terminal_box_id = excluded.terminal_box_id,
                        onu_device_id = excluded.onu_device_id,
                        port_number = excluded.port_number,
                        is_switch_port = excluded.is_switch_port,
                        port_label = excluded.port_label,
                        updated_at = now()",
                    new
                    {
                        channelId = b.ChannelId,
                        terminalBoxId = b.TerminalBoxId,
                        onuDeviceId = b.OnuDeviceId,
                        portNumber = b.PortNumber,
                        isSwitchPort = b.IsSwitchPort,
                        portLabel = b.PortLabel ?? (b.IsSwitchPort ? $"SW-P{b.PortNumber}" : $"LAN{b.PortNumber}")
                    }, ct);
            }
            return true;
        }, ct);

        return request.Bindings.Count;
    }

    public async Task<OnuDiagnosisDto> DiagnoseOnuAsync(long id, CancellationToken ct = default)
    {
        var onu = await db.OneAsync(@"
            select o.*, b.name as terminal_box_name
            from onu_devices o
            left join terminal_boxes b on o.terminal_box_id = b.id
            where o.id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "onu.not_found", "指定的 ONU 设备不存在");

        var isOnline = onu.Text("status") is "1" or "online" || onu.Text("runStatus") is "1";
        var status = isOnline ? "online" : "offline";
        var rxPower = onu.Decimal("rxPower");
        var txPower = onu.Decimal("txPower");
        var healthStatus = onu.Text("healthStatus", "healthy");
        var rootCause = onu.Text("rootCause");
        var lastDownTime = onu.Text("lastDownTime");
        var lastUpTime = onu.Text("lastUpTime");
        var lastDownCause = onu.Text("lastDownCause");
        var dyingGasp = onu.Text("lastDyingGaspTime");

        var items = new List<OnuDiagnosisItemDto>();

        // 1. 接收光功率指标检测
        if (rxPower.HasValue)
        {
            var pVal = rxPower.Value;
            if (pVal >= -24.0m && pVal <= -8.0m)
            {
                items.Add(new OnuDiagnosisItemDto("接收光功率 (RxPower)", $"{pVal:F2} dBm", 0, "光功率处于安全门限区间 (-8dBm ~ -24dBm)，光信号优良"));
            }
            else if (pVal < -24.0m && pVal >= -27.0m)
            {
                items.Add(new OnuDiagnosisItemDto("接收光功率 (RxPower)", $"{pVal:F2} dBm [弱光预警]", 1, "处于弱光临界区间 (-25dBm ~ -27dBm)，建议清洁法兰盘或检查尾纤弯曲半径"));
            }
            else
            {
                items.Add(new OnuDiagnosisItemDto("接收光功率 (RxPower)", $"{pVal:F2} dBm [严重超标]", 2, "接收光衰严重超标，存在突发脱网丢包风险"));
            }
        }
        else
        {
            if (isOnline)
            {
                items.Add(new OnuDiagnosisItemDto("接收光功率 (RxPower)", "正常 (-8~-24 dBm)", 0, "光功率正常"));
            }
            else if (healthStatus == "power_off")
            {
                items.Add(new OnuDiagnosisItemDto("接收光功率 (RxPower)", "设备停电未发光", 1, "ONU 设备已掉电，未发光收光（物理光纤完好）"));
            }
            else
            {
                items.Add(new OnuDiagnosisItemDto("接收光功率 (RxPower)", "无光信号 (脱网)", 2, "设备脱网无信号，光纤链路中断"));
            }
        }

        // 2. 智能链路根因定界检测
        if (isOnline)
        {
            items.Add(new OnuDiagnosisItemDto("链路健康状态", healthStatus == "sub_health_weak_light" ? "弱光亚健康" : "正常连通", healthStatus == "sub_health_weak_light" ? 1 : 0, rootCause ?? "设备在线且光路健康"));
        }
        else
        {
            var itemStatus = healthStatus == "power_off" ? 1 : 2;
            var shortTitle = healthStatus switch
            {
                "power_off" => "终端箱掉电 / 市电中断 ⚡",
                "splitter_broken" => "同分光器支干光纤中断 ✂️",
                "trunk_broken" => "PON口主干光纤中断 🚨",
                "fiber_cut" => "单台分支尾纤中断 🔍",
                _ => rootCause ?? "光网离线"
            };
            items.Add(new OnuDiagnosisItemDto("故障根因诊断", shortTitle, itemStatus, rootCause ?? "设备离线"));
        }

        // 3. 供电遗言 (Dying Gasp) 判定
        if (!string.IsNullOrWhiteSpace(dyingGasp))
        {
            items.Add(new OnuDiagnosisItemDto("Dying Gasp 供电遗言", dyingGasp, 0, "断电瞬间已成功上报 Dying Gasp 告警帧，确认为外部供电切断"));
        }
        else if (!isOnline && healthStatus != "power_off")
        {
            items.Add(new OnuDiagnosisItemDto("Dying Gasp 供电遗言", "未收到遗言帧", 1, "离线时未收到断电遗言，符合突发断纤或光路中断特征"));
        }

        // 4. ONU 本地 4 个物理以太网端口与绑定的下行业务终端
        var boundCameras = await db.QueryAsync(@"
            select b.port_number, c.name as channel_name, c.alias as channel_alias, c.ip as channel_ip, c.status as channel_status, c.model as channel_model
            from channel_network_bindings b
            join channels c on b.channel_id = c.id
            where b.onu_device_id = @id and coalesce(b.is_switch_port, false) = false
            order by b.port_number", new { id }, ct);

        var boundComputers = await db.QueryAsync(@"
            select port_number, name, ip, mac, status
            from computers
            where onu_device_id = @id and coalesce(is_switch_port, false) = false
            order by port_number", new { id }, ct);

        var cameraByPort = boundCameras.GroupBy(r => (int)r.Id("portNumber")).ToDictionary(g => g.Key, g => g.First());
        var computerByPort = boundComputers.GroupBy(r => (int)r.Id("portNumber")).ToDictionary(g => g.Key, g => g.First());

        var ports = new List<OnuPortStatusDto>();
        for (var i = 1; i <= 4; i++)
        {
            string? devName = null;
            string? devIp = null;
            string? devType = null;
            string? devStatus = null;
            string? mac = null;

            if (cameraByPort.TryGetValue(i, out var cam))
            {
                devName = cam.Text("channelAlias") is { Length: > 0 } alias ? alias : cam.Text("channelName");
                devIp = cam.Text("channelIp");
                devType = "camera";
                devStatus = cam.Text("channelStatus");
            }
            else if (computerByPort.TryGetValue(i, out var comp))
            {
                devName = comp.Text("name");
                devIp = comp.Text("ip");
                devType = "computer";
                devStatus = comp.Text("status");
                mac = comp.Text("mac");
            }

            var portStatus = isOnline && (devName != null || i == 1) ? "up" : "down";
            ports.Add(new OnuPortStatusDto(
                i,
                $"LAN{i}",
                portStatus,
                portStatus == "up" ? "1000M" : "--",
                portStatus == "up" ? "FullDuplex" : "--",
                mac,
                devName,
                devIp,
                devType,
                devStatus
            ));
        }

        string opticalStatus;
        if (rxPower.HasValue)
        {
            opticalStatus = $"{rxPower.Value:F2} dBm";
        }
        else if (isOnline)
        {
            opticalStatus = "正常 (-8~-24 dBm)";
        }
        else if (healthStatus == "power_off")
        {
            opticalStatus = "设备断电无信号";
        }
        else
        {
            opticalStatus = "光路中断无信号";
        }

        string fiberStatus;
        if (isOnline)
        {
            fiberStatus = healthStatus == "sub_health_weak_light" ? "弱光预警 (Degraded)" : "链路连通 (Up)";
        }
        else
        {
            fiberStatus = healthStatus switch
            {
                "power_off" => "未激活 (终端断电)",
                "trunk_broken" => "主干光缆中断 (Trunk Cut)",
                "splitter_broken" => "分光器支干中断 (Branch Cut)",
                "fiber_cut" => "单台分支尾纤中断 (LOSi)",
                _ => "光纤物理中断"
            };
        }

        return new OnuDiagnosisDto(
            onu.Text("neDn"),
            onu.Text("onuId"),
            status,
            rxPower,
            txPower,
            lastDownCause,
            lastUpTime,
            lastDownTime,
            ports,
            opticalStatus,
            fiberStatus,
            items,
            healthStatus,
            rootCause
        );
    }

    public async Task RebootOnuAsync(long id, CancellationToken ct = default)
    {
        var onu = await db.OneAsync("select * from onu_devices where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "onu.not_found", "指定的 ONU 设备不存在");

        var sn = onu.Text("sn");
        var onuName = onu.Text("onuName");
        logger?.LogInformation("收到 ONU 远程维护/重启请求：{Id} ({Name}, SN: {Sn})", id, onuName, sn);
    }

    public async Task<PonKanbanResponseDto> GetKanbanDashboardAsync(CancellationToken ct = default, Actor? actor = null)
    {
        // 1. 终端箱列表
        var boxRows = await db.QueryAsync($@"
            select 
                b.*,
                a.name as area_name,
                w.name as workshop_name,
                u.name as unit_name
            from terminal_boxes b
            left join areas a on b.area_id = a.id
            left join workshops w on a.parent_id = w.id
            left join units u on b.unit_id = u.id
            where {(actor is null ? "true" : $"b.id in ({AssetScope.AllowedIds("terminal-box")})")}
            order by b.code, b.id", new { userId = actor?.UserId }, ct);

        // 2. 全部 ONU 设备
        var onuRows = await db.QueryAsync($@"
            select o.*, b.name as terminal_box_name, b.code as terminal_box_code
            from onu_devices o
            left join terminal_boxes b on o.terminal_box_id = b.id
            where {(actor is null ? "true" : $"o.id in ({AssetScope.AllowedIds("onu")})")}
            order by (o.status in ('1', 'online') or o.run_status = '1') asc, o.port_id, o.onu_name", new { userId = actor?.UserId }, ct);

        // 3. 通道绑定与通道状态
        var bindingRows = await db.QueryAsync($@"
            select 
                b.channel_id,
                b.terminal_box_id,
                b.onu_device_id,
                b.port_number,
                b.is_switch_port,
                b.port_label,
                c.device_channel,
                c.name as channel_name,
                c.alias as channel_alias,
                c.ip as channel_ip,
                c.status as channel_status,
                c.model as channel_model
            from channel_network_bindings b
            join channels c on b.channel_id = c.id
            where {(actor is null ? "true" : $"c.id in (select c.id from {AccessService.ChannelFrom} where {AccessService.ChannelPredicate})")}
            order by b.port_number", new { userId = actor?.UserId }, ct);

        // 4. 电脑终端列表
        var computerRows = await db.QueryAsync($@"
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
                c.port_label,
                c.response_time_ms
            from computers c
            where {(actor is null ? "true" : $"c.id in ({AssetScope.AllowedIds("computer")})")}
            order by c.port_number, c.id", new { userId = actor?.UserId }, ct);

        // 4.1 录像机/接入设备列表
        var deviceRows = await db.QueryAsync($@"
            select 
                d.id,
                d.name,
                d.host,
                d.port,
                d.status,
                d.model,
                d.terminal_box_id,
                d.onu_device_id,
                d.port_number,
                d.is_switch_port,
                d.port_label,
                (select count(*) from channels where device_id=d.id and status<>'disabled') as channel_count,
                (select count(*) from channels where device_id=d.id and status='online') as online_channels
            from devices d
            where d.terminal_box_id is not null or d.onu_device_id is not null
            order by d.port_number, d.id", ct: ct);

        // 5. 掉线事件统计
        var historyStats = await db.OneAsync($@"
            select 
                count(*) filter (where up_time is null) as ongoing_count,
                count(*) filter (where down_time >= date_trunc('day', now() at time zone 'Asia/Shanghai') at time zone 'Asia/Shanghai') as today_count
            from device_offline_history h where {(actor is null ? "true" : $"(h.channel_id is not null and exists(select 1 from {AccessService.ChannelFrom} where c.id=h.channel_id and ({AccessService.ChannelPredicate}))) or (h.channel_id is null and h.onu_device_id in ({AssetScope.AllowedIds("onu")})) or (h.channel_id is null and h.onu_device_id is null and h.terminal_box_id in ({AssetScope.AllowedIds("terminal-box")}))")}", new { userId = actor?.UserId }, ct);

        // 构建映射
        var channelByOnu = bindingRows
            .Where(r => r.Id("onuDeviceId") > 0)
            .GroupBy(r => r.Id("onuDeviceId"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PonKanbanCameraDto(
                    r.Id("channelId"),
                    (int)r.Id("deviceChannel"),
                    r.Text("channelName"),
                    r.Text("channelAlias"),
                    r.Text("channelIp"),
                    r.Text("channelStatus"),
                    (int)r.Id("portNumber"),
                    r.Flag("isSwitchPort"),
                    r.Text("portLabel", r.Flag("isSwitchPort") ? $"SW-P{r.Id("portNumber")}" : $"LAN{r.Id("portNumber")}"),
                    r.Text("channelModel")
                )).ToList()
            );

        var directChannelByBox = bindingRows
            .Where(r => r.Id("terminalBoxId") > 0 && r.Id("onuDeviceId") == 0)
            .GroupBy(r => r.Id("terminalBoxId"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PonKanbanCameraDto(
                    r.Id("channelId"),
                    (int)r.Id("deviceChannel"),
                    r.Text("channelName"),
                    r.Text("channelAlias"),
                    r.Text("channelIp"),
                    r.Text("channelStatus"),
                    (int)r.Id("portNumber"),
                    r.Flag("isSwitchPort"),
                    r.Text("portLabel", $"Port{r.Id("portNumber")}"),
                    r.Text("channelModel")
                )).ToList()
            );

        var computerByOnu = computerRows
            .Where(r => r.Id("onuDeviceId") > 0)
            .GroupBy(r => r.Id("onuDeviceId"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PonKanbanComputerDto(
                    r.Id(),
                    r.Text("name"),
                    r.Text("ip"),
                    r.Text("mac"),
                    r.Text("owner"),
                    r.Text("department"),
                    r.Text("status", "unknown"),
                    (int)r.Id("portNumber"),
                    r.Flag("isSwitchPort"),
                    r.Text("portLabel", r.Flag("isSwitchPort") ? $"SW-P{r.Id("portNumber")}" : $"LAN{r.Id("portNumber")}"),
                    r.NullableInt("responseTimeMs")
                )).ToList()
            );

        var directComputerByBox = computerRows
            .Where(r => r.Id("terminalBoxId") > 0 && r.Id("onuDeviceId") == 0)
            .GroupBy(r => r.Id("terminalBoxId"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PonKanbanComputerDto(
                    r.Id(),
                    r.Text("name"),
                    r.Text("ip"),
                    r.Text("mac"),
                    r.Text("owner"),
                    r.Text("department"),
                    r.Text("status", "unknown"),
                    (int)r.Id("portNumber"),
                    r.Flag("isSwitchPort"),
                    r.Text("portLabel", $"Port{r.Id("portNumber")}"),
                    r.NullableInt("responseTimeMs")
                )).ToList()
            );

        var deviceByOnu = deviceRows
            .Where(r => r.Id("onuDeviceId") > 0)
            .GroupBy(r => r.Id("onuDeviceId"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PonKanbanDeviceDto(
                    r.Id(),
                    r.Text("name"),
                    r.Text("host"),
                    (int)r.Id("port"),
                    r.Text("status", "unknown"),
                    (int)r.Id("portNumber"),
                    r.Flag("isSwitchPort"),
                    r.Text("portLabel", r.Flag("isSwitchPort") ? $"SW-P{r.Id("portNumber")}" : $"LAN{r.Id("portNumber")}"),
                    r.Text("model"),
                    r.Id("channelCount"),
                    r.Id("onlineChannels")
                )).ToList()
            );

        var directDeviceByBox = deviceRows
            .Where(r => r.Id("terminalBoxId") > 0 && r.Id("onuDeviceId") == 0)
            .GroupBy(r => r.Id("terminalBoxId"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new PonKanbanDeviceDto(
                    r.Id(),
                    r.Text("name"),
                    r.Text("host"),
                    (int)r.Id("port"),
                    r.Text("status", "unknown"),
                    (int)r.Id("portNumber"),
                    r.Flag("isSwitchPort"),
                    r.Text("portLabel", $"Port{r.Id("portNumber")}"),
                    r.Text("model"),
                    r.Id("channelCount"),
                    r.Id("onlineChannels")
                )).ToList()
            );

        var onusByBox = onuRows
            .Where(r => r.Id("terminalBoxId") > 0)
            .GroupBy(r => r.Id("terminalBoxId"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var boxDtos = new List<PonKanbanBoxDto>();
        int totalBoxes = boxRows.Count;
        int healthyBoxes = 0, warningBoxes = 0, dangerBoxes = 0;
        int totalOnus = onuRows.Count;
        int onlineOnus = 0, offlineOnus = 0;
        int totalCameras = bindingRows.Count;
        int onlineCameras = 0, offlineCameras = 0;
        int totalComputers = computerRows.Count;
        int onlineComputers = 0, offlineComputers = 0;

        foreach (var b in boxRows)
        {
            var boxId = b.Id();
            onusByBox.TryGetValue(boxId, out var bOnuRows);
            bOnuRows ??= [];

            directChannelByBox.TryGetValue(boxId, out var bDirectCameras);
            bDirectCameras ??= [];

            directComputerByBox.TryGetValue(boxId, out var bDirectComputers);
            bDirectComputers ??= [];

            directDeviceByBox.TryGetValue(boxId, out var bDirectDevices);
            bDirectDevices ??= [];

            var boxOnuDtos = new List<PonKanbanOnuDto>();
            foreach (var o in bOnuRows)
            {
                var oId = o.Id();
                channelByOnu.TryGetValue(oId, out var oCameras);
                oCameras ??= [];
                computerByOnu.TryGetValue(oId, out var oComputers);
                oComputers ??= [];
                deviceByOnu.TryGetValue(oId, out var oDevices);
                oDevices ??= [];

                var isOnline = (o.Text("status", "offline") is "1" or "online" || o.Text("runStatus") is "1");
                boxOnuDtos.Add(new PonKanbanOnuDto(
                    oId,
                    o.Text("onuName"),
                    o.Text("alias"),
                    o.Text("sn"),
                    o.Text("terminalType", "EG8040C-10"),
                    isOnline ? "online" : "offline",
                    o.Text("runStatus"),
                    o.NullableTime("lastDownTime"),
                    o.Text("lastDownCause"),
                    o.NullableTime("lastUpTime"),
                    o.Decimal("rxPower"),
                    o.Flag("hasSubSwitch"),
                    o.Text("subSwitchModel"),
                    (int)o.Id("subSwitchPorts"),
                    oCameras,
                    oComputers,
                    oDevices
                ));
            }

            int bTotalOnus = boxOnuDtos.Count;
            int bOnlineOnus = boxOnuDtos.Count(x => x.Status == "online");
            int bOfflineOnus = boxOnuDtos.Count(x => x.Status != "online");

            var allBoxCameras = boxOnuDtos.SelectMany(x => x.Cameras).Concat(bDirectCameras).ToList();
            int bTotalCameras = allBoxCameras.Count;
            int bOnlineCameras = allBoxCameras.Count(x => x.Status == "online");
            int bOfflineCameras = allBoxCameras.Count(x => x.Status != "online");

            var allBoxComputers = boxOnuDtos.SelectMany(x => x.Computers ?? []).Concat(bDirectComputers).ToList();
            int bTotalComputers = allBoxComputers.Count;
            int bOnlineComputers = allBoxComputers.Count(x => x.Status == "online");
            int bOfflineComputers = allBoxComputers.Count(x => x.Status != "online");

            string healthState;
            if (bOfflineOnus > 0)
            {
                healthState = "danger";
                dangerBoxes++;
            }
            else if (bOfflineCameras > 0 || bOfflineComputers > 0)
            {
                healthState = "warning";
                warningBoxes++;
            }
            else if (bTotalOnus > 0 || bTotalCameras > 0 || bTotalComputers > 0)
            {
                healthState = "healthy";
                healthyBoxes++;
            }
            else
            {
                healthState = "empty";
            }

            var areaName = b.Text("areaName");
            var wsName = b.Text("workshopName");
            var fullAreaName = !string.IsNullOrEmpty(wsName) && !string.IsNullOrEmpty(areaName)
                ? $"{wsName} / {areaName}"
                : areaName;

            boxDtos.Add(new PonKanbanBoxDto(
                boxId,
                b.Text("code"),
                b.Text("name"),
                b.Text("location"),
                b.Id("areaId") == 0 ? null : b.Id("areaId"),
                fullAreaName,
                healthState,
                bTotalOnus,
                bOnlineOnus,
                bOfflineOnus,
                bTotalCameras,
                bOnlineCameras,
                bOfflineCameras,
                boxOnuDtos,
                bDirectCameras,
                b.Id("unitId") == 0 ? null : b.Id("unitId"),
                b.Text("unitName") ?? fullAreaName,
                bTotalComputers,
                bOnlineComputers,
                bOfflineComputers,
                bDirectComputers,
                bDirectDevices
            ));
        }

        foreach (var o in onuRows)
        {
            if (o.Text("status", "offline") is "1" or "online" || o.Text("runStatus") is "1") onlineOnus++;
            else offlineOnus++;
        }
        foreach (var c in bindingRows)
        {
            if (c.Text("channelStatus") == "online") onlineCameras++;
            else offlineCameras++;
        }
        foreach (var c in computerRows)
        {
            if (c.Text("status") == "online") onlineComputers++;
            else offlineComputers++;
        }

        var unassignedOnuRows = onuRows.Where(r => r.Id("terminalBoxId") == 0).ToList();
        var unassignedOnus = new List<PonKanbanOnuDto>();
        foreach (var o in unassignedOnuRows)
        {
            var oId = o.Id();
            channelByOnu.TryGetValue(oId, out var oCameras);
            oCameras ??= [];
            computerByOnu.TryGetValue(oId, out var oComputers);
            oComputers ??= [];

            var isOnline = (o.Text("status", "offline") is "1" or "online" || o.Text("runStatus") is "1");
            unassignedOnus.Add(new PonKanbanOnuDto(
                oId,
                o.Text("onuName"),
                o.Text("alias"),
                o.Text("sn"),
                o.Text("terminalType", "EG8040C-10"),
                isOnline ? "online" : "offline",
                o.Text("runStatus"),
                o.NullableTime("lastDownTime"),
                o.Text("lastDownCause"),
                o.NullableTime("lastUpTime"),
                o.Decimal("rxPower"),
                o.Flag("hasSubSwitch"),
                o.Text("subSwitchModel"),
                (int)o.Id("subSwitchPorts"),
                oCameras,
                oComputers
            ));
        }

        var unassignedCameras = bindingRows
            .Where(r => r.Id("terminalBoxId") == 0 && r.Id("onuDeviceId") == 0)
            .Select(r => new PonKanbanCameraDto(
                r.Id("channelId"),
                (int)r.Id("deviceChannel"),
                r.Text("channelName"),
                r.Text("channelAlias"),
                r.Text("channelIp"),
                r.Text("channelStatus"),
                (int)r.Id("portNumber"),
                r.Flag("isSwitchPort"),
                r.Text("portLabel", $"Port{r.Id("portNumber")}"),
                r.Text("channelModel")
            )).ToList();

        var unassignedComputers = computerRows
            .Where(r => r.Id("terminalBoxId") == 0 && r.Id("onuDeviceId") == 0)
            .Select(r => new PonKanbanComputerDto(
                r.Id(),
                r.Text("name"),
                r.Text("ip"),
                r.Text("mac"),
                r.Text("owner"),
                r.Text("department"),
                r.Text("status", "unknown"),
                (int)r.Id("portNumber"),
                r.Flag("isSwitchPort"),
                r.Text("portLabel", $"Port{r.Id("portNumber")}"),
                r.NullableInt("responseTimeMs")
            )).ToList();

        var summary = new PonKanbanSummaryDto(
            totalBoxes,
            healthyBoxes,
            warningBoxes,
            dangerBoxes,
            totalOnus,
            onlineOnus,
            offlineOnus,
            totalCameras,
            onlineCameras,
            offlineCameras,
            (int)(historyStats?.Id("ongoingCount") ?? 0),
            (int)(historyStats?.Id("todayCount") ?? 0),
            totalComputers,
            onlineComputers,
            offlineComputers
        );

        return new PonKanbanResponseDto(summary, boxDtos, unassignedOnus, unassignedCameras, unassignedComputers);
    }

    public async Task<PagedResponse<DeviceOfflineHistoryDto>> GetOfflineHistoryAsync(
        long? terminalBoxId = null,
        long? onuDeviceId = null,
        long? channelId = null,
        string? deviceType = null,
        string? status = null,
        string? search = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken ct = default, Actor? actor = null)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var offset = (pageNumber - 1) * pageSize;

        var where = "1=1";
        if (actor is not null) where += $" and ((h.channel_id is not null and exists(select 1 from {AccessService.ChannelFrom} where c.id=h.channel_id and ({AccessService.ChannelPredicate}))) or (h.channel_id is null and h.onu_device_id in ({AssetScope.AllowedIds("onu")})) or (h.channel_id is null and h.onu_device_id is null and h.terminal_box_id in ({AssetScope.AllowedIds("terminal-box")})))";
        if (terminalBoxId.HasValue && terminalBoxId.Value > 0)
            where += " and h.terminal_box_id = @terminalBoxId";
        if (onuDeviceId.HasValue && onuDeviceId.Value > 0)
            where += " and h.onu_device_id = @onuDeviceId";
        if (channelId.HasValue && channelId.Value > 0)
            where += " and h.channel_id = @channelId";
        if (!string.IsNullOrWhiteSpace(deviceType) && deviceType != "all")
            where += " and h.device_type = @deviceType";
        if (status == "ongoing")
            where += " and h.up_time is null";
        else if (status == "recovered")
            where += " and h.up_time is not null";
        if (!string.IsNullOrWhiteSpace(search))
            where += " and (h.device_name ilike @search or coalesce(h.device_identifier, '') ilike @search or coalesce(h.reason, '') ilike @search or coalesce(b.name, '') ilike @search or coalesce(b.code, '') ilike @search)";
        if (from.HasValue)
            where += " and h.down_time >= @from";
        if (to.HasValue)
            where += " and h.down_time <= @to";

        var args = new
        {
            terminalBoxId,
            onuDeviceId,
            channelId,
            deviceType,
            search = $"%{search?.Trim()}%",
            from,
            to,
            size = pageSize,
            offset, userId = actor?.UserId
        };

        var rows = await db.QueryAsync($@"
            select 
                h.*,
                b.name as terminal_box_name,
                b.code as terminal_box_code
            from device_offline_history h
            left join terminal_boxes b on h.terminal_box_id = b.id
            where {where}
            order by (h.up_time is null) desc, h.down_time desc, h.id desc
            limit @size offset @offset", args, ct);

        var totalRow = await db.OneAsync($@"
            select count(*) as count
            from device_offline_history h
            left join terminal_boxes b on h.terminal_box_id = b.id
            where {where}", args, ct);

        var items = rows.Select(r => new DeviceOfflineHistoryDto(
            r.Id(),
            r.Text("deviceType"),
            r.Id("terminalBoxId") == 0 ? null : r.Id("terminalBoxId"),
            r.Text("terminalBoxCode"),
            r.Text("terminalBoxName"),
            r.Id("onuDeviceId") == 0 ? null : r.Id("onuDeviceId"),
            r.Id("channelId") == 0 ? null : r.Id("channelId"),
            r.Text("deviceName"),
            r.Text("deviceIdentifier"),
            r.Text("eventType", "offline"),
            r.Time("downTime"),
            r.NullableTime("upTime"),
            r.Id("durationSeconds") == 0 && r.NullableTime("upTime") is null ? null : (int)r.Id("durationSeconds"),
            r.Text("reason"),
            r.Time("createdAt")
        )).ToArray();

        return new PagedResponse<DeviceOfflineHistoryDto>(items, totalRow.Id("count"), pageNumber, pageSize);
    }

    private static OnuDeviceDto ToOnuDto(JsonObject r) => new(
        r.Id("id"),
        r.Text("neDn"),
        r.Text("oltName", "OLT"),
        r.Text("oltIp", "10.37.5.3"),
        r.Text("onuId"),
        r.Text("onuName"),
        r.Text("alias"),
        r.Text("sn"),
        r.Text("mac"),
        r.Text("terminalType", "EG8040C-10"),
        (r.Text("status", "offline") is "1" or "online" || r.Text("runStatus") is "1") ? "online" : "offline",
        r.Text("runStatus"),
        r.NullableTime("lastUpTime"),
        r.NullableTime("lastDownTime"),
        r.Text("lastDownCause"),
        r.Decimal("rxPower"),
        r.Decimal("txPower"),
        r.Id("terminalBoxId") == 0 ? null : r.Id("terminalBoxId"),
        r.Text("terminalBoxName"),
        r.Text("terminalBoxCode"),
        r.Flag("hasSubSwitch"),
        r.Text("subSwitchModel"),
        (int)r.Id("subSwitchPorts"),
        r.Text("portsConfig", "[]"),
        r.NullableTime("lastSyncedAt"),
        r.Id("boundChannelCount"),
        r.Text("description"),
        r.Text("groupName"),
        r.Text("ip"),
        null,
        r.Id("oltId") == 0 ? null : r.Id("oltId"),
        (int)r.Id("portId"),
        r.Text("ifDesc"),
        r.NullableTime("lastDyingGaspTime"),
        r.Text("healthStatus", "healthy"),
        r.Text("rootCause"),
        r.NullableTime("lastStateChangeTime")
    );

    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public static DateTimeOffset? ParseEsightTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}(:?\d{2})?$"))
            {
                return dto.ToUniversalTime();
            }
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return new DateTimeOffset(dt, ChinaOffset).ToUniversalTime();
        }

        return null;
    }
}
