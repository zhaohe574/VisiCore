using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public static class AssetScope
{
    public static async Task<bool> IsFullAsync(Database db, Actor actor, CancellationToken ct)
        => (await db.OneAsync($"select ({Predicate("null::bigint", "null::bigint", "null::bigint")}) as allowed", new { userId = actor.UserId }, ct)).Flag("allowed");

    public static string From(string type) => type switch
    {
        "computer" => "computers a left join units u on u.id=a.unit_id left join areas ar on ar.id=coalesce(a.area_id,u.parent_id)",
        "terminal-box" => "terminal_boxes a left join units u on u.id=a.unit_id left join areas ar on ar.id=coalesce(a.area_id,u.parent_id)",
        "onu" => "onu_devices a left join terminal_boxes b on b.id=a.terminal_box_id left join units u on u.id=b.unit_id left join areas ar on ar.id=coalesce(b.area_id,u.parent_id)",
        "network-device" => "network_devices a",
        "ip-pool" => "ip_pools a",
        "virtual-machine" => "virtual_machines a join network_devices host on host.id=a.host_device_id",
        _ => throw new PlatformException(400, "asset.type", "资产类型无效")
    };

    public static string For(string type) => type switch
    {
        "computer" => Predicate("a.unit_id", "coalesce(a.area_id,u.parent_id)", "coalesce(a.workshop_id,ar.parent_id)"),
        "terminal-box" => Predicate("a.unit_id", "coalesce(a.area_id,u.parent_id)", "ar.parent_id"),
        "onu" => Predicate("b.unit_id", "coalesce(b.area_id,u.parent_id)", "ar.parent_id"),
        "network-device" or "ip-pool" => Predicate("null::bigint", "a.area_id", "a.workshop_id"),
        "virtual-machine" => Predicate("null::bigint", "host.area_id", "host.workshop_id"),
        _ => throw new PlatformException(400, "asset.type", "资产类型无效")
    };

    public static string AllowedIds(string type) => $"select a.id from {From(type)} where {For(type)}";

    public static string PortOwnerType(string type) => type switch
    {
        "network_device" => "network-device", "onu_device" => "onu", "computer" => "computer",
        _ => throw new PlatformException(400, "asset.portType", "端口所属资产类型无效")
    };

    public static string PortPredicate(string alias) => $"(({alias}.device_type='computer' and {alias}.device_id in ({AllowedIds("computer")})) or ({alias}.device_type='onu_device' and {alias}.device_id in ({AllowedIds("onu")})) or ({alias}.device_type='network_device' and {alias}.device_id in ({AllowedIds("network-device")})))";

    public static async Task DemandAsync(DbSession tx, Actor actor, string type, long id, CancellationToken ct)
    {
        var row = await tx.OneAsync($"select a.id from {From(type)} where a.id=@id and ({For(type)})", new { id, userId = actor.UserId }, ct);
        Rules.Require(row is not null, "资产不存在或不在当前组织数据范围内", "asset.scope", 403);
    }

    public static async Task DemandBindingAsync(DbSession tx, Actor actor, long? boxId, long? onuId, CancellationToken ct)
    {
        if (boxId is not null) await DemandAsync(tx, actor, "terminal-box", boxId.Value, ct);
        if (onuId is not null) await DemandAsync(tx, actor, "onu", onuId.Value, ct);
    }

    public static async Task DemandOrganizationAsync(DbSession tx, Actor actor, long? workshopId, long? areaId, long? unitId, CancellationToken ct)
    {
        if (unitId is not null) await AdministrationService.DemandNodeScopeAsync(tx, actor.UserId, "units", unitId.Value, ct);
        else if (areaId is not null) await AdministrationService.DemandNodeScopeAsync(tx, actor.UserId, "areas", areaId.Value, ct);
        else if (workshopId is not null) await AdministrationService.DemandNodeScopeAsync(tx, actor.UserId, "workshops", workshopId.Value, ct);
        else
        {
            var row = await tx.OneAsync($"select ({Predicate("null::bigint", "null::bigint", "null::bigint")}) as allowed", new { userId = actor.UserId }, ct);
            Rules.Require(row.Flag("allowed"), "未分配资产仅限完整数据范围账号维护", "asset.scope", 403);
        }
    }

    public static string Predicate(string unit, string area, string workshop) => $$"""
        exists(select 1 from users au where au.id=@userId and au.status='active' and (
          au.all_channels or exists(select 1 from user_roles ur join roles r on r.id=ur.role_id
            where ur.user_id=au.id and r.status='active' and (r.code='admin' or r.all_channels))
          or exists(select 1 from user_scopes s where s.user_id=au.id and
            ((s.type='unit' and s.scope_id={{unit}}) or (s.type='area' and s.scope_id={{area}}) or (s.type='workshop' and s.scope_id={{workshop}})))
          or exists(select 1 from role_scopes s join user_roles ur on ur.role_id=s.role_id join roles r on r.id=s.role_id
            where ur.user_id=au.id and r.status='active' and
            ((s.type='unit' and s.scope_id={{unit}}) or (s.type='area' and s.scope_id={{area}}) or (s.type='workshop' and s.scope_id={{workshop}})))
        ))
        """;

    public static async Task DemandComputerAsync(DbSession tx, Actor actor, JsonObject row, CancellationToken ct)
    {
        var unitId = row["unitId"] is null ? (long?)null : row.Id("unitId");
        var areaId = row["areaId"] is null ? (long?)null : row.Id("areaId");
        var workshopId = row["workshopId"] is null ? (long?)null : row.Id("workshopId");
        var allowed = await tx.OneAsync($"select ({Predicate("@unitId::bigint", "@areaId::bigint", "@workshopId::bigint")}) as allowed",
            new { userId = actor.UserId, unitId, areaId, workshopId }, ct);
        Rules.Require(allowed.Flag("allowed"), "电脑不在当前组织数据范围内", "asset.scope", 403);
    }
}
