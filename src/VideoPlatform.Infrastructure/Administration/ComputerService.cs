using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed class ComputerService(Database db)
{
    internal const string SearchPredicate = "(c.name ilike @search or c.id::text ilike @search or c.ip ilike @search or coalesce(c.mac,'') ilike @search or coalesce(c.owner,'') ilike @search or coalesce(c.department,'') ilike @search or coalesce(c.location,'') ilike @search or coalesce(tb.name,'') ilike @search or coalesce(tb.code,'') ilike @search or coalesce(onu.onu_name,'') ilike @search or coalesce(onu.alias,'') ilike @search or coalesce(onu.sn,'') ilike @search)";
    internal static string SearchArgument(string? value) => "%" + value?.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    private const string BaseColumns = @"
        c.id, c.name, c.ip, c.mac, c.owner, c.department, c.os, c.location,
        coalesce(c.workshop_id,a.parent_id) as workshop_id, w.name as workshop_name,
        coalesce(c.area_id,u.parent_id) as area_id, a.name as area_name,
        c.unit_id, u.name as unit_name,
        c.terminal_box_id, tb.name as terminal_box_name, tb.code as terminal_box_code,
        c.onu_device_id, onu.onu_name, onu.alias as onu_alias,
        c.port_number, c.is_switch_port, c.port_label,
        c.status, c.response_time_ms, c.last_seen_at, c.last_probed_at,
        c.remarks, c.created_at, c.updated_at";

    private const string FromSql = @"
        computers c
        left join units u on c.unit_id = u.id
        left join areas a on coalesce(c.area_id,u.parent_id) = a.id
        left join workshops w on coalesce(c.workshop_id,a.parent_id) = w.id
        left join terminal_boxes tb on c.terminal_box_id = tb.id
        left join onu_devices onu on c.onu_device_id = onu.id";

    public async Task<PagedResponse<ComputerDto>> ListAsync(int page, int size, string? search, long? workshopId, long? areaId, long? unitId, string? status, CancellationToken ct = default, Actor? actor = null)
    {
        var offset = Math.Max(0, (page - 1) * size);
        var where = "1=1";
        if (actor is not null) where += " and (" + AssetScope.Predicate("c.unit_id", "coalesce(c.area_id,u.parent_id)", "coalesce(c.workshop_id,a.parent_id)") + ")";
        if (!string.IsNullOrWhiteSpace(search))
            where += " and " + SearchPredicate;
        if (workshopId.HasValue && workshopId.Value > 0)
            where += " and coalesce(c.workshop_id,a.parent_id) = @workshopId";
        if (areaId.HasValue && areaId.Value > 0)
            where += " and coalesce(c.area_id,u.parent_id) = @areaId";
        if (unitId.HasValue && unitId.Value > 0)
            where += " and c.unit_id = @unitId";
        if (!string.IsNullOrWhiteSpace(status))
            where += " and c.status = @status";

        var args = new { search = SearchArgument(search), workshopId, areaId, unitId, status, size, offset, userId = actor?.UserId };
        var rows = await db.QueryAsync($"select {BaseColumns} from {FromSql} where {where} order by c.id desc limit @size offset @offset", args, ct);
        var count = await db.OneAsync($"select count(*) as count from {FromSql} where {where}", args, ct);

        return new PagedResponse<ComputerDto>(rows.Select(ToDto).ToArray(), count?.Id("count") ?? 0, page, size);
    }

    public async Task<ComputerDto> GetAsync(long id, CancellationToken ct = default, Actor? actor = null)
    {
        var scope = actor is null ? "" : " and (" + AssetScope.Predicate("c.unit_id", "coalesce(c.area_id,u.parent_id)", "coalesce(c.workshop_id,a.parent_id)") + ")";
        var row = await db.OneAsync($"select {BaseColumns} from {FromSql} where c.id = @id{scope}", new { id, userId = actor?.UserId }, ct)
            ?? throw new PlatformException(404, "computer.not_found", "电脑登记记录不存在");
        return ToDto(row);
    }

    public async Task<ComputerDto> CreateAsync(ComputerRequest request, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default, Actor? actor = null)
    {
        Validate(request);
        var createdId = await new AssetMutationService(db).WriteAsync(actor, "computer.manage", async tx =>
        {
        request = await AssetMutationService.ValidateComputerAsync(tx, request, null, actor, ct);
        var row = await tx.OneAsync(@"
            insert into computers(
                name, ip, mac, owner, department, os, location,
                workshop_id, area_id, unit_id,
                terminal_box_id, onu_device_id, port_number, is_switch_port, port_label,
                remarks
            ) values (
                @name, @ip, @mac, @owner, @department, @os, @location,
                @workshopId, @areaId, @unitId,
                @terminalBoxId, @onuDeviceId, @portNumber, @isSwitchPort, @portLabel,
                @remarks
            ) returning id", new
        {
            name = request.Name.Trim(),
            ip = request.Ip.Trim(),
            mac = request.Mac?.Trim(),
            owner = request.Owner?.Trim(),
            department = request.Department?.Trim(),
            os = request.Os?.Trim(),
            location = request.Location?.Trim(),
            workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null,
            areaId = request.AreaId is > 0 ? request.AreaId : null,
            unitId = request.UnitId is > 0 ? request.UnitId : null,
            terminalBoxId = request.TerminalBoxId is > 0 ? request.TerminalBoxId : null,
            onuDeviceId = request.OnuDeviceId is > 0 ? request.OnuDeviceId : null,
            request.PortNumber,
            request.IsSwitchPort,
            portLabel = request.PortLabel?.Trim(),
            remarks = request.Remarks?.Trim()
        }, ct);

        var id = row!.Id("id");
        if (actorUserId != null)
            await AssetMutationService.AuditAsync(tx, long.TryParse(actorUserId, out var uid) ? uid : null, "computer.create", id.ToString(), $"添加电脑登记：{request.Name} ({request.Ip})", clientIp, ct);
        return id;
        }, ct);
        return await GetAsync(createdId, ct);
    }

    public async Task<ComputerDto> UpdateAsync(long id, ComputerRequest request, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default, Actor? actor = null)
    {
        Validate(request);
        await new AssetMutationService(db).WriteAsync(actor, "computer.manage", async tx =>
        {
        var existing = await tx.OneAsync("select * from computers where id = @id for update", new { id }, ct)
            ?? throw new PlatformException(404, "computer.not_found", "电脑登记记录不存在");
        if (actor is not null) await AssetScope.DemandComputerAsync(tx, actor, existing, ct);
        request = await AssetMutationService.ValidateComputerAsync(tx, request, id, actor, ct);
        await tx.ExecuteAsync(@"
            update computers set
                name = @name,
                ip = @ip,
                mac = @mac,
                owner = @owner,
                department = @department,
                os = @os,
                location = @location,
                workshop_id = @workshopId,
                area_id = @areaId,
                unit_id = @unitId,
                terminal_box_id = @terminalBoxId,
                onu_device_id = @onuDeviceId,
                port_number = @portNumber,
                is_switch_port = @isSwitchPort,
                port_label = @portLabel,
                remarks = @remarks,
                updated_at = now()
            where id = @id", new
        {
            id,
            name = request.Name.Trim(),
            ip = request.Ip.Trim(),
            mac = request.Mac?.Trim(),
            owner = request.Owner?.Trim(),
            department = request.Department?.Trim(),
            os = request.Os?.Trim(),
            location = request.Location?.Trim(),
            workshopId = request.WorkshopId is > 0 ? request.WorkshopId : null,
            areaId = request.AreaId is > 0 ? request.AreaId : null,
            unitId = request.UnitId is > 0 ? request.UnitId : null,
            terminalBoxId = request.TerminalBoxId is > 0 ? request.TerminalBoxId : null,
            onuDeviceId = request.OnuDeviceId is > 0 ? request.OnuDeviceId : null,
            request.PortNumber,
            request.IsSwitchPort,
            portLabel = request.PortLabel?.Trim(),
            remarks = request.Remarks?.Trim()
        }, ct);

        if (actorUserId != null)
            await AssetMutationService.AuditAsync(tx, long.TryParse(actorUserId, out var uid) ? uid : null, "computer.update", id.ToString(), $"修改电脑登记：{request.Name} ({request.Ip})", clientIp, ct);
        return true;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, string? actorUserId = null, string? clientIp = null, CancellationToken ct = default, Actor? actor = null)
    {
        await new AssetMutationService(db).WriteAsync(actor, "computer.manage", async tx =>
        {
        var existing = await tx.OneAsync("select * from computers where id = @id for update", new { id }, ct)
            ?? throw new PlatformException(404, "computer.not_found", "电脑登记记录不存在");
        if (actor is not null) await AssetScope.DemandComputerAsync(tx, actor, existing, ct);
        await tx.ExecuteAsync("delete from computers where id = @id", new { id }, ct);
        if (actorUserId != null)
            await AssetMutationService.AuditAsync(tx, long.TryParse(actorUserId, out var uid) ? uid : null, "computer.delete", id.ToString(), $"删除电脑登记：{existing.Text("name")}（{existing.Text("ip")}）", clientIp, ct);
        return true;
        }, ct);
    }

    public async Task<ComputerProbeResultDto> ProbeAsync(long id, CancellationToken ct = default, Actor? actor = null)
    {
        if (actor is not null) await GetAsync(id, ct, actor);
        var comp = await db.OneAsync("select id, ip from computers where id = @id", new { id }, ct)
            ?? throw new PlatformException(404, "computer.not_found", "电脑登记记录不存在");

        var ip = comp.Text("ip");
        var result = await PingIpAsync(ip, 500, ct);

        var now = DateTimeOffset.UtcNow;
        if (result.Success)
        {
            await db.ExecuteAsync(@"
                update computers set
                    status = 'online',
                    response_time_ms = @rtt,
                    last_seen_at = @now,
                    last_probed_at = @now,
                    updated_at = now()
                where id = @id", new { id, rtt = result.RoundtripTime, now }, ct);
        }
        else
        {
            await db.ExecuteAsync(@"
                update computers set
                    status = 'offline',
                    response_time_ms = null,
                    last_probed_at = @now,
                    updated_at = now()
                where id = @id", new { id, now }, ct);
        }

        return new ComputerProbeResultDto(id, ip, result.Success ? "online" : "offline", result.RoundtripTime, now);
    }

    public async Task<IReadOnlyList<ComputerProbeResultDto>> ProbeAllAsync(CancellationToken ct = default, Actor? actor = null)
    {
        var scope = actor is null ? "" : " where (" + AssetScope.Predicate("c.unit_id", "c.area_id", "c.workshop_id") + ")";
        var rows = await db.QueryAsync($"select c.id,c.ip from computers c{scope} order by c.id", new { userId = actor?.UserId }, ct);
        using var semaphore = new SemaphoreSlim(32);
        var now = DateTimeOffset.UtcNow;

        var tasks = rows.Select(async r =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var id = r.Id("id");
                var ip = r.Text("ip");
                var probe = await PingIpAsync(ip, 500, ct);
                return new ComputerProbeResultDto(id, ip, probe.Success ? "online" : "offline", probe.RoundtripTime, now);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var completed = await Task.WhenAll(tasks);

        // 批量更新数据库，避免在并发 Ping 循环中多线程占用数据库连接
        var onlineList = completed.Where(x => x.Status == "online").ToList();
        var offlineList = completed.Where(x => x.Status != "online").ToList();

        if (onlineList.Count > 0)
        {
            var ids = onlineList.Select(x => x.Id).ToArray();
            var rtts = onlineList.Select(x => x.ResponseTimeMs).ToArray();
            await db.ExecuteAsync(@"
                update computers c set
                    status = 'online',
                    response_time_ms = u.rtt,
                    last_seen_at = @now,
                    last_probed_at = @now,
                    updated_at = now()
                from (select unnest(cast(@ids as bigint[])) as id, unnest(cast(@rtts as integer[])) as rtt) u
                where c.id = u.id", new { ids, rtts, now }, ct);
        }

        if (offlineList.Count > 0)
        {
            var ids = offlineList.Select(x => x.Id).ToArray();
            await db.ExecuteAsync(@"
                update computers set
                    status = 'offline',
                    response_time_ms = null,
                    last_probed_at = @now,
                    updated_at = now()
                where id = any(@ids)", new { ids, now }, ct);
        }

        return completed;
    }

    public static async Task<(bool Success, int? RoundtripTime)> PingIpAsync(string ip, int timeoutMs = 500, CancellationToken ct = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            if (reply.Status == IPStatus.Success)
                return (true, (int)reply.RoundtripTime);
        }
        catch
        {
            // Ignore ping exceptions, consider unreachable
        }
        return (false, null);
    }

    private static void Validate(ComputerRequest req)
    {
        Rules.Text(req.Name, "计算机名", 128);
        Rules.Require(!string.IsNullOrWhiteSpace(req.Ip), "IP 地址不能为空");
        Rules.Require(IPAddress.TryParse(req.Ip.Trim(), out _), "请输入有效的 IPv4 地址格式");
        if (!string.IsNullOrWhiteSpace(req.Mac))
            Rules.Require(req.Mac.Trim().Length <= 64, "MAC 地址长度不能超过 64 字符");
    }

    private static ComputerDto ToDto(JsonObject r) => new(
        r.Id("id"),
        r.Text("name"),
        r.Text("ip"),
        r.Text("mac"),
        r.Text("owner"),
        r.Text("department"),
        r.Text("os"),
        r.Text("location"),
        NullableId(r, "workshopId"),
        r.Text("workshopName"),
        NullableId(r, "areaId"),
        r.Text("areaName"),
        NullableId(r, "unitId"),
        r.Text("unitName"),
        NullableId(r, "terminalBoxId"),
        r.Text("terminalBoxName"),
        r.Text("terminalBoxCode"),
        NullableId(r, "onuDeviceId"),
        r.Text("onuName"),
        r.Text("onuAlias"),
        (int)r.Id("portNumber"),
        r.Flag("isSwitchPort"),
        r.Text("portLabel"),
        r.Text("status") ?? "unknown",
        r.TryGetPropertyValue("responseTimeMs", out var rtt) && rtt != null ? (int?)r.Id("responseTimeMs") : null,
        DateTime(r, "lastSeenAt"),
        DateTime(r, "lastProbedAt"),
        r.Text("remarks"),
        DateTime(r, "createdAt") ?? DateTimeOffset.UtcNow,
        DateTime(r, "updatedAt") ?? DateTimeOffset.UtcNow
    );

    private static long? NullableId(JsonObject r, string prop)
        => r.TryGetPropertyValue(prop, out var v) && v != null && v.GetValue<long?>() is { } id && id > 0 ? id : null;

    private static DateTimeOffset? DateTime(JsonObject r, string prop)
        => r.TryGetPropertyValue(prop, out var v) && v != null && System.DateTimeOffset.TryParse(v.ToString(), out var dt) ? dt : null;
}
