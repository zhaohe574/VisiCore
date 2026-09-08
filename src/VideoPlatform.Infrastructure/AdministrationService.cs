using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed record AdministrationPage<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);
public sealed record OrganizationNodeDto(long Id, string Name, string Code, string Status, long? ParentId);
public sealed record OrganizationTreeDto(IReadOnlyList<OrganizationNodeDto> Workshops, IReadOnlyList<OrganizationNodeDto> Areas, IReadOnlyList<OrganizationNodeDto> Units);
public sealed record AdministrationRoleDto(long Id, string Name, string Code, string Status, string[] PermissionCodes, long UserCount);
public sealed record PermissionDto(string Code, string Name);
public sealed record AdministrationSessionDto(Guid Id, long UserId, string Username, string ClientType, string ClientVersion, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt);
public sealed record AdministrationAuditDto(long Id, string? Username, string Action, string Resource, string? Summary, string? ClientIp, DateTimeOffset CreatedAt);
public sealed record AdministrationLayoutDto(long Id, string Name, string Kind, bool Shared, int Layout, int IntervalSeconds, long?[] ChannelIds);
public sealed record DashboardDto(long Users, long Roles, long Devices, long OnlineDevices, long Channels, long OnlineChannels, long AlarmsToday, long PendingAlarms, long LiveSessions, long PlaybackSessions, long OnlineSessions);
public sealed record ServiceHealthDto(string Name, string Status, string? Reason = null);
public sealed record MediaStatisticsDto(long LiveSessions, long PlaybackSessions, long Transcodes);
public sealed record SystemStatisticsDto(double? CpuPercent, long? MemoryTotalBytes, long? MemoryUsedBytes, long? DiskTotalBytes, long? DiskFreeBytes, long UptimeSeconds, IReadOnlyList<ServiceHealthDto> Services, MediaStatisticsDto Media, string Version);

public sealed class AdministrationService(Database db, AccessService access, MediaService media, ISettingsStore settings, PlatformOptions options, IDeviceAdapter adapter, IHttpClientFactory httpClients)
{
    // 授权写入共用事务锁，防止两个请求同时撤掉最后管理员或绕过范围校验。
    private const long AdministrationLock = 72002010;
    private const string LayoutColumns = "id,user_id,name,kind,shared,layout,interval_seconds,to_jsonb(channel_ids) as channel_ids,updated_at";
    private const string UserColumns = """
        u.id,u.username,u.display_name,u.phone,u.status,
        array(select ur.role_id from user_roles ur where ur.user_id=u.id order by ur.role_id) as role_ids,
        array(select distinct rp.permission_code from user_roles ur join roles r on r.id=ur.role_id and r.status='active'
          join role_permissions rp on rp.role_id=r.id where ur.user_id=u.id order by rp.permission_code) as permissions
        """;
    private const string RoleColumns = """
        r.id,r.name,r.code,r.status,
        array(select permission_code from role_permissions where role_id=r.id order by permission_code) as permission_codes,
        (select count(*) from user_roles where role_id=r.id) as user_count
        """;
    private const string ScopeCte = """
        with principal as (
          select u.id,(u.all_channels or exists(select 1 from user_roles ur join roles r on r.id=ur.role_id
            where ur.user_id=u.id and r.status='active' and (r.code='admin' or r.all_channels))) as full_access
          from users u where u.id=@userId and u.status='active'
        ), grants as (
          select s.type,s.scope_id from user_scopes s join principal p on p.id=s.user_id
          union select s.type,s.scope_id from role_scopes s join user_roles ur on ur.role_id=s.role_id
            join roles r on r.id=s.role_id and r.status='active' join principal p on p.id=ur.user_id
        ), visible_units as (
          select un.* from units un join areas ar on ar.id=un.parent_id where
            exists(select 1 from principal where full_access)
            or exists(select 1 from grants g where (g.type='unit' and g.scope_id=un.id)
              or (g.type='area' and g.scope_id=un.parent_id) or (g.type='workshop' and g.scope_id=ar.parent_id)
              or (g.type='channel' and exists(select 1 from channels c where c.id=g.scope_id and c.unit_id=un.id)))
        ), visible_areas as (
          select ar.* from areas ar where exists(select 1 from principal where full_access)
            or exists(select 1 from visible_units un where un.parent_id=ar.id)
            or exists(select 1 from grants g where (g.type='area' and g.scope_id=ar.id) or (g.type='workshop' and g.scope_id=ar.parent_id))
        ), visible_workshops as (
          select w.* from workshops w where exists(select 1 from principal where full_access)
            or exists(select 1 from visible_areas ar where ar.parent_id=w.id)
            or exists(select 1 from grants g where g.type='workshop' and g.scope_id=w.id)
        )
        """;

    public async Task<OrganizationTreeDto> OrganizationAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "area.read", ct);
        var rows = await db.QueryAsync(ScopeCte + """
            select 'workshop' as type,id,name,code,status,null::bigint as parent_id from visible_workshops
            union all select 'area',id,name,code,status,parent_id from visible_areas
            union all select 'unit',id,name,code,status,parent_id from visible_units order by name,id
            """, new { userId = actor.UserId }, ct);
        OrganizationNodeDto[] Nodes(string type) => rows.Where(r => r.Text("type") == type).Select(ToNode).ToArray();
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

    public async Task<AdministrationPage<UserDto>> UsersAsync(Actor actor, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "user.read", ct);
        ValidatePage(page, pageSize);
        const string filter = "(@search='' or u.username ilike @pattern or coalesce(u.display_name,'') ilike @pattern or coalesce(u.phone,'') ilike @pattern)";
        var args = PageArgs(page, pageSize, search);
        var total = (await db.OneAsync($"select count(*) as count from users u where {filter}", args, ct)).Id("count");
        var rows = await db.QueryAsync($"select {UserColumns} from users u where {filter} order by u.id limit @size offset @offset", args, ct);
        return new(rows.Select(ToUser).ToArray(), total, page, pageSize);
    }

    public async Task<UserDto> SaveUserAsync(Actor actor, long? id, UserRequest request, string? ip, CancellationToken ct = default)
    {
        var username = Rules.Text(request.Username, "账号", 64);
        Rules.Require(!username.Any(char.IsWhiteSpace), "账号不能包含空白字符");
        ValidateStatus(request.Status, true);
        Rules.Require((request.DisplayName?.Length ?? 0) <= 128 && (request.Phone?.Length ?? 0) <= 32, "姓名或电话号码过长");
        var roleIds = ValidateIds(request.RoleIds, 64);
        var passwordChanged = id is null || !string.IsNullOrEmpty(request.Password);
        if (passwordChanged) Rules.Password(request.Password);
        var hash = passwordChanged ? Passwords.Hash(request.Password!) : null;
        var result = await WriteAsync(actor, "user.manage", async tx =>
        {
            var administrator = await IsAdministratorAsync(tx, actor.UserId, ct);
            JsonObject? previous = null;
            long[] oldRoles = [];
            if (id is { } userId)
            {
                previous = await RequiredAsync(tx, "users", userId, ct);
                oldRoles = (await tx.QueryAsync("select role_id from user_roles where user_id=@userId order by role_id", new { userId }, ct)).Select(r => r.Id("roleId")).ToArray();
                var protectedUser = (await tx.OneAsync("""
                    select exists(select 1 from user_roles ur join roles r on r.id=ur.role_id
                      where ur.user_id=@userId and (r.code='admin' or exists(select 1 from role_permissions p where p.role_id=r.id
                        and p.permission_code in ('user.manage','role.manage','settings.manage','session.manage','area.manage','channel.assign','desktop.release.manage')))) as protected
                    """, new { userId }, ct)).Flag("protected");
                Rules.Require(administrator || !protectedUser, "只有管理员可以修改具备管理权限的账号", "access.administrator", 403);
                Rules.Require(administrator || !passwordChanged, "只有管理员可以重置其他账号的密码", "access.administrator", 403);
            }
            var rolesChanged = !oldRoles.Order().SequenceEqual(roleIds.Order());
            Rules.Require(administrator || !rolesChanged, "只有管理员可以分配或移除角色", "access.administrator", 403);
            if (roleIds.Length > 0)
            {
                var roles = await tx.QueryAsync("select id,status from roles where id=any(@roleIds) for share", new { roleIds }, ct);
                Rules.Require(roles.Count == roleIds.Length, "包含不存在的角色", "role.not_found", 404);
                Rules.Require(roles.Where(r => !oldRoles.Contains(r.Id())).All(r => r.Text("status") == "active"), "不能分配已停用角色", "role.disabled", 409);
            }
            var args = new { id, username, hash, displayName = request.DisplayName?.Trim(), phone = request.Phone?.Trim(), request.Status };
            var row = id is null
                ? await tx.OneAsync("insert into users(username,password_hash,display_name,phone,status) values(@username,@hash,@displayName,@phone,@status) returning id", args, ct)
                : await tx.OneAsync("update users set username=@username,password_hash=coalesce(@hash,password_hash),display_name=@displayName,phone=@phone,status=@status,failed_logins=case when @hash is not null then 0 else failed_logins end,locked_until=case when @hash is not null or @status='active' then null else locked_until end,updated_at=now() where id=@id returning id", args, ct);
            var savedId = row.Id();
            if (rolesChanged)
            {
                await tx.ExecuteAsync("delete from user_roles where user_id=@savedId", new { savedId }, ct);
                foreach (var roleId in roleIds)
                    await tx.ExecuteAsync("insert into user_roles(user_id,role_id) values(@savedId,@roleId)", new { savedId, roleId }, ct);
            }
            await RequireAdministratorRemainsAsync(tx, ct);
            var revoke = previous is not null && (passwordChanged || rolesChanged || previous.Text("status") != request.Status);
            if (previous is not null && (passwordChanged || request.Status != "active"))
                await tx.ExecuteAsync("update sessions set revoked_at=coalesce(revoked_at,now()) where user_id=@savedId and revoked_at is null", new { savedId }, ct);
            if (revoke) await AccessChangedAsync(tx, [savedId], ct);
            await AuditAsync(tx, actor, id is null ? "user.create" : "user.update", $"users/{savedId}", $"账号：{username}，状态：{request.Status}，角色：{string.Join(',', roleIds)}{(id is not null && passwordChanged ? "，已重置密码并撤销登录" : "")}", ip, ct);
            return (User: ToUser((await tx.OneAsync($"select {UserColumns} from users u where u.id=@savedId", new { savedId }, ct))!), Revoke: revoke);
        }, ct: ct);
        if (result.Revoke) await RevokeUsersAsync([result.User.Id]);
        return result.User;
    }

    public async Task<IReadOnlyList<AdministrationRoleDto>> RolesAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "role.read", ct);
        return (await db.QueryAsync($"select {RoleColumns} from roles r order by r.id", ct: ct)).Select(ToRole).ToArray();
    }

    public async Task<IReadOnlyList<PermissionDto>> PermissionsAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "role.read", ct);
        return (await db.QueryAsync("select code,name from permissions order by code", ct: ct)).Select(r => new PermissionDto(r.Text("code"), r.Text("name"))).ToArray();
    }

    public async Task<AdministrationRoleDto> SaveRoleAsync(Actor actor, long? id, RoleRequest request, string? ip, CancellationToken ct = default)
    {
        var name = Rules.Text(request.Name, "角色名称");
        var code = Rules.Text(request.Code, "角色编码", 64);
        ValidateStatus(request.Status);
        var result = await WriteAsync(actor, "role.manage", async tx =>
        {
            var previous = id is null ? null : await RequiredAsync(tx, "roles", id.Value, ct);
            Rules.Require(code != "admin" || previous?.Text("code") == "admin", "系统管理员编码为保留编码", "role.reserved", 409);
            Rules.Require(previous?.Text("code") != "admin" || (code == "admin" && request.Status == "active"), "系统管理员角色不能停用或修改编码", "role.reserved", 409);
            var args = new { id, name, code, request.Status };
            var row = id is null
                ? await tx.OneAsync("insert into roles(name,code,status) values(@name,@code,@status) returning id", args, ct)
                : await tx.OneAsync("update roles set name=@name,code=@code,status=@status where id=@id returning id", args, ct);
            var savedId = row.Id();
            if (request.PermissionCodes is not null) await ReplacePermissionsAsync(tx, savedId, code, request.PermissionCodes, ct);
            await RequireAdministratorRemainsAsync(tx, ct);
            var users = id is null ? [] : await RoleUserIdsAsync(tx, savedId, ct);
            await AccessChangedAsync(tx, users, ct);
            await AuditAsync(tx, actor, id is null ? "role.create" : "role.update", $"roles/{savedId}", $"角色：{name}，编码：{code}，状态：{request.Status}", ip, ct);
            return (Role: ToRole((await tx.OneAsync($"select {RoleColumns} from roles r where r.id=@savedId", new { savedId }, ct))!), Users: users);
        }, administrator: true, ct: ct);
        await RevokeUsersAsync(result.Users);
        return result.Role;
    }

    public async Task<AdministrationRoleDto> SetPermissionsAsync(Actor actor, long id, PermissionRequest request, string? ip, CancellationToken ct = default)
    {
        var result = await WriteAsync(actor, "role.manage", async tx =>
        {
            var role = await RequiredAsync(tx, "roles", id, ct);
            await ReplacePermissionsAsync(tx, id, role.Text("code"), request.Codes, ct);
            await RequireAdministratorRemainsAsync(tx, ct);
            var users = await RoleUserIdsAsync(tx, id, ct);
            await AccessChangedAsync(tx, users, ct);
            await AuditAsync(tx, actor, "role.permissions", $"roles/{id}", $"角色：{role.Text("name")}，权限：{string.Join(',', request.Codes)}", ip, ct);
            return (Role: ToRole((await tx.OneAsync($"select {RoleColumns} from roles r where r.id=@id", new { id }, ct))!), Users: users);
        }, administrator: true, ct: ct);
        await RevokeUsersAsync(result.Users);
        return result.Role;
    }

    public Task DeleteRoleAsync(Actor actor, long id, string? ip, CancellationToken ct = default)
        => WriteAsync(actor, "role.manage", async tx =>
        {
            var role = await RequiredAsync(tx, "roles", id, ct);
            Rules.Require(role.Text("code") != "admin", "系统管理员角色不能删除", "role.reserved", 409);
            Rules.Require((await RoleUserIdsAsync(tx, id, ct)).Length == 0, "角色仍分配给账号，请先调整账号角色", "role.assigned", 409);
            await tx.ExecuteAsync("delete from roles where id=@id", new { id }, ct);
            await RequireAdministratorRemainsAsync(tx, ct);
            await AuditAsync(tx, actor, "role.delete", $"roles/{id}", $"删除角色：{role.Text("name")}", ip, ct);
            return true;
        }, administrator: true, ct: ct);

    public async Task<ScopeRequest> ScopesAsync(Actor actor, string kind, long id, CancellationToken ct = default)
    {
        var (table, scopes, owner) = ScopeTables(kind);
        await access.DemandAsync(actor, kind == "user" ? "user.read" : "role.read", ct);
        var allowed = await access.IsAdministratorAsync(actor.UserId, ct)
            || (kind == "user" ? id == actor.UserId : await db.OneAsync("select 1 as allowed from user_roles where user_id=@userId and role_id=@id", new { userId = actor.UserId, id }, ct) is not null);
        Rules.Require(allowed, "只能查看本人的数据范围或所属角色范围", "scope.denied", 403);
        var row = await db.OneAsync($"select all_channels from {table} where id=@id", new { id }, ct) ?? throw NotFound();
        var values = await db.QueryAsync($"select type,scope_id from {scopes} where {owner}=@id order by type,scope_id", new { id }, ct);
        return new(row.Flag("allChannels"), values.Select(r => new ScopeItem(r.Text("type"), r.Id("scopeId"))).ToArray());
    }

    public async Task<ScopeRequest> SaveScopesAsync(Actor actor, string kind, long id, ScopeRequest request, string? ip, CancellationToken ct = default)
    {
        var (table, scopes, owner) = ScopeTables(kind);
        Rules.Require(request.Scopes is not null && request.Scopes.Length <= 10000, "授权范围不能为空值且最多包含 10000 项");
        Rules.Require(request.Scopes!.All(s => s is not null && s.Id > 0 && s.Type is "workshop" or "area" or "unit" or "channel"), "授权范围类型或编号无效");
        var items = request.Scopes!.Distinct().OrderBy(s => s.Type).ThenBy(s => s.Id).ToArray();
        var users = await WriteAsync(actor, kind == "user" ? "user.manage" : "role.manage", async tx =>
        {
            await RequiredAsync(tx, table, id, ct);
            foreach (var group in items.GroupBy(s => s.Type))
            {
                var target = group.Key switch { "workshop" => "workshops", "area" => "areas", "unit" => "units", _ => "channels" };
                var ids = group.Select(s => s.Id).ToArray();
                var count = (await tx.OneAsync($"select count(*) as count from {target} where id=any(@ids)", new { ids }, ct)).Id("count");
                Rules.Require(count == ids.Length, "授权范围包含不存在的组织或通道", "scope.not_found", 404);
            }
            await tx.ExecuteAsync($"update {table} set all_channels=@all where id=@id", new { id, all = request.AllChannels }, ct);
            await tx.ExecuteAsync($"delete from {scopes} where {owner}=@id", new { id }, ct);
            foreach (var item in items)
                await tx.ExecuteAsync($"insert into {scopes}({owner},type,scope_id) values(@id,@type,@scopeId)", new { id, item.Type, scopeId = item.Id }, ct);
            var affected = kind == "user" ? [id] : await RoleUserIdsAsync(tx, id, ct);
            await AccessChangedAsync(tx, affected, ct);
            await AuditAsync(tx, actor, "scope.update", $"scopes/{kind}/{id}", $"全部通道：{(request.AllChannels ? "是" : "否")}，范围项数：{items.Length}", ip, ct);
            return affected;
        }, administrator: true, ct: ct);
        await RevokeUsersAsync(users);
        return new(request.AllChannels, items);
    }

    public async Task<AdministrationPage<AdministrationSessionDto>> SessionsAsync(Actor actor, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "session.manage", ct);
        ValidatePage(page, pageSize);
        const string filter = "s.revoked_at is null and s.expires_at>now() and u.status='active' and (@search='' or u.username ilike @pattern or s.client_version ilike @pattern or s.client_type ilike @pattern)";
        var args = PageArgs(page, pageSize, search);
        var total = (await db.OneAsync($"select count(*) as count from sessions s join users u on u.id=s.user_id where {filter}", args, ct)).Id("count");
        var rows = await db.QueryAsync($"select s.id,s.user_id,u.username,s.client_type,s.client_version,s.created_at,s.last_seen_at,s.expires_at from sessions s join users u on u.id=s.user_id where {filter} order by s.last_seen_at desc,s.id limit @size offset @offset", args, ct);
        return new(rows.Select(r => new AdministrationSessionDto(Guid.Parse(r.Text("id")), r.Id("userId"), r.Text("username"), r.Text("clientType"), r.Text("clientVersion"), r.Time("createdAt"), r.Time("lastSeenAt"), r.Time("expiresAt"))).ToArray(), total, page, pageSize);
    }

    public async Task RevokeSessionAsync(Actor actor, Guid id, string? ip, CancellationToken ct = default)
    {
        var userId = await WriteAsync(actor, "session.manage", async tx =>
        {
            var row = await tx.OneAsync("select user_id,revoked_at from sessions where id=@id for update", new { id }, ct) ?? throw NotFound();
            var owner = row.Id("userId");
            if (row["revokedAt"] is null)
            {
                await tx.ExecuteAsync("update sessions set revoked_at=now() where id=@id", new { id }, ct);
                await AccessChangedAsync(tx, [owner], ct);
                await AuditAsync(tx, actor, "session.revoke", $"sessions/{id}", $"撤销账号 {owner} 的登录会话", ip, ct);
            }
            return owner;
        }, ct: ct);
        await media.RevokeAsync(userId, id, CancellationToken.None);
    }

    public async Task<AdministrationPage<AdministrationAuditDto>> AuditAsync(Actor actor, int page, int pageSize, string? search, string? action, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "audit.read", ct);
        ValidatePage(page, pageSize);
        Rules.Require(from is null || to is null || from <= to, "审计查询结束时间不能早于开始时间");
        Rules.Require((action?.Length ?? 0) <= 128, "审计动作参数过长");
        const string filter = """
            (@search='' or coalesce(u.username,'') ilike @pattern or a.resource ilike @pattern or coalesce(a.summary,'') ilike @pattern)
            and (@action='' or a.action=@action) and (cast(@from as timestamptz) is null or a.created_at>=@from)
            and (cast(@to as timestamptz) is null or a.created_at<=@to)
            """;
        var args = PageArgs(page, pageSize, search);
        args["action"] = action?.Trim() ?? "";
        args["from"] = from?.ToUniversalTime();
        args["to"] = to?.ToUniversalTime();
        var total = (await db.OneAsync($"select count(*) as count from audit_logs a left join users u on u.id=a.user_id where {filter}", args, ct)).Id("count");
        var rows = await db.QueryAsync($"select a.id,u.username,a.action,a.resource,a.summary,host(a.client_ip) as client_ip,a.created_at from audit_logs a left join users u on u.id=a.user_id where {filter} order by a.created_at desc,a.id desc limit @size offset @offset", args, ct);
        return new(rows.Select(r => new AdministrationAuditDto(r.Id(), r["username"]?.ToString(), r.Text("action"), r.Text("resource"), r["summary"]?.ToString(), r["clientIp"]?.ToString(), r.Time("createdAt"))).ToArray(), total, page, pageSize);
    }

    public async Task<PlatformSettings> SettingsAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "settings.manage", ct);
        return await settings.ReadAsync(ct);
    }

    public Task<PlatformSettings> SaveSettingsAsync(Actor actor, PlatformSettings request, string? ip, CancellationToken ct = default)
    {
        ValidateSettings(request);
        return WriteAsync(actor, "settings.manage", async tx =>
        {
            var value = JsonSerializer.Serialize(request, JsonDefaults.Options);
            await tx.ExecuteAsync("insert into settings(id,value) values(1,cast(@value as jsonb)) on conflict(id) do update set value=excluded.value,updated_at=now()", new { value }, ct);
            await AuditAsync(tx, actor, "settings.update", "settings/1", $"更新系统配额和保留策略：{value}", ip, ct);
            return request;
        }, administrator: true, ct: ct);
    }

    public async Task<IReadOnlyList<ChannelDto>> FavoritesAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "channel.read", ct);
        var rows = await db.QueryAsync($"select {AccessService.ChannelColumns} from favorites f join channels c on c.id=f.channel_id join devices d on d.id=c.device_id left join units un on un.id=c.unit_id left join areas ar on ar.id=un.parent_id where f.user_id=@userId and d.enabled and c.status<>'disabled' and ({AccessService.ChannelPredicate}) order by f.sort_order,c.id", new { userId = actor.UserId }, ct);
        return rows.Select(ToChannel).ToArray();
    }

    public async Task<IReadOnlyList<ChannelDto>> SaveFavoritesAsync(Actor actor, FavoritesRequest request, string? ip, CancellationToken ct = default)
    {
        var ids = ValidateIds(request.ChannelIds, 1000);
        await WriteAsync(actor, "channel.read", async tx =>
        {
            await DemandChannelsAsync(tx, actor.UserId, ids, true, ct);
            await tx.ExecuteAsync("delete from favorites where user_id=@userId", new { userId = actor.UserId }, ct);
            for (var i = 0; i < ids.Length; i++)
                await tx.ExecuteAsync("insert into favorites(user_id,channel_id,sort_order) values(@userId,@channelId,@sortOrder)", new { userId = actor.UserId, channelId = ids[i], sortOrder = i }, ct);
            await AuditAsync(tx, actor, "favorites.update", $"users/{actor.UserId}/favorites", $"收藏通道数：{ids.Length}", ip, ct);
            return true;
        }, ct: ct);
        return await FavoritesAsync(actor, ct);
    }

    public async Task<IReadOnlyList<AdministrationLayoutDto>> LayoutsAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "channel.read", ct);
        // 普通布局保留空窗口与失权窗口的位置；轮巡过滤失权项后继续播放可见通道。
        var rows = await db.QueryAsync($"""
            select l.id,l.name,l.kind,l.shared,l.layout,l.interval_seconds,filtered.channel_ids
            from layouts l cross join lateral (
              select coalesce(jsonb_agg(case when granted.allowed then slot.channel_id else null end order by slot.position),'[]'::jsonb) as channel_ids
              from unnest(l.channel_ids) with ordinality as slot(channel_id,position)
              cross join lateral (select exists(select 1 from {AccessService.ChannelFrom}
                where c.id=slot.channel_id and d.enabled and c.status<>'disabled' and ({AccessService.ChannelPredicate})) as allowed) granted
              where l.kind='layout' or granted.allowed
            ) filtered where l.user_id=@userId or l.shared order by l.shared desc,l.updated_at desc,l.id
            """, new { userId = actor.UserId }, ct);
        return rows.Select(ToLayout).ToArray();
    }

    public async Task<AdministrationLayoutDto> SaveLayoutAsync(Actor actor, long? id, LayoutRequest request, string? ip, CancellationToken ct = default)
    {
        var name = Rules.Text(request.Name, "布局名称");
        Rules.Require(request.Kind is "layout" or "patrol", "布局类型无效");
        Rules.Require(request.Layout is 1 or 4 or 9 or 16, "仅支持 1、4、9、16 分屏");
        Rules.Require(request.IntervalSeconds is >= 10 and <= 300, "轮巡间隔必须为 10～300 秒");
        Rules.Require(request.ChannelIds is not null && request.ChannelIds.Length <= (request.Kind == "layout" ? request.Layout : 1000) && request.ChannelIds.All(i => i is null or > 0), "布局通道编号或数量无效");
        Rules.Require(request.ChannelIds!.Any(i => i is > 0), "布局至少需要一个通道");
        if (request.Kind == "patrol") Rules.Require(request.ChannelIds!.All(i => i.HasValue), "轮巡方案不能包含空通道");
        var ids = request.ChannelIds!.ToArray();
        return await WriteAsync(actor, "channel.read", async tx =>
        {
            JsonObject? previous = null;
            if (id is { } layoutId)
            {
                previous = await RequiredAsync(tx, "layouts", layoutId, ct);
                Rules.Require(previous.Id("userId") == actor.UserId || (previous.Flag("shared") && await IsAdministratorAsync(tx, actor.UserId, ct)), "不能修改他人的个人布局", "layout.denied", 403);
            }
            if (request.Shared || previous.Flag("shared"))
            {
                await DemandTransactionAsync(tx, actor, "layout.share", true, ct);
            }
            if (request.Kind == "patrol") await DemandTransactionAsync(tx, actor, "live.view", false, ct);
            await DemandChannelsAsync(tx, actor.UserId, ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToArray(), true, ct);
            var args = new { id, userId = actor.UserId, name, request.Kind, request.Shared, request.Layout, request.IntervalSeconds, ids };
            var row = id is null
                ? await tx.OneAsync($"insert into layouts(user_id,name,kind,shared,layout,interval_seconds,channel_ids) values(@userId,@name,@kind,@shared,@layout,@intervalSeconds,@ids) returning {LayoutColumns}", args, ct)
                : await tx.OneAsync($"update layouts set name=@name,kind=@kind,shared=@shared,layout=@layout,interval_seconds=@intervalSeconds,channel_ids=@ids,updated_at=now() where id=@id returning {LayoutColumns}", args, ct);
            await AuditAsync(tx, actor, id is null ? "layout.create" : "layout.update", $"layouts/{row.Id()}", $"名称：{name}，类型：{request.Kind}，共享：{(request.Shared ? "是" : "否")}", ip, ct);
            return ToLayout(row!);
        }, ct: ct);
    }

    public Task DeleteLayoutAsync(Actor actor, long id, string? ip, CancellationToken ct = default)
        => WriteAsync(actor, "channel.read", async tx =>
        {
            var row = await RequiredAsync(tx, "layouts", id, ct);
            Rules.Require(row.Id("userId") == actor.UserId || (row.Flag("shared") && await IsAdministratorAsync(tx, actor.UserId, ct)), "不能删除他人的个人布局", "layout.denied", 403);
            if (row.Flag("shared")) await DemandTransactionAsync(tx, actor, "layout.share", true, ct);
            await tx.ExecuteAsync("delete from layouts where id=@id", new { id }, ct);
            await AuditAsync(tx, actor, "layout.delete", $"layouts/{id}", $"删除布局：{row.Text("name")}", ip, ct);
            return true;
        }, ct: ct);

    public async Task<DashboardDto> DashboardAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "statistics.read", ct);
        var administrator = await access.IsAdministratorAsync(actor.UserId, ct);
        var readUsers = await access.HasPermissionAsync(actor.UserId, "user.read", ct);
        var readRoles = await access.HasPermissionAsync(actor.UserId, "role.read", ct);
        var manageSessions = await access.HasPermissionAsync(actor.UserId, "session.manage", ct);
        var row = await db.OneAsync($"""
            with visible as (select c.id,c.device_id,c.status from {AccessService.ChannelFrom} where ({AccessService.ChannelPredicate})),
            active_media as (select * from media_sessions m where m.closed_at is null and m.expires_at>now()
              and m.state not in ('stopped','failed','completed') and (m.user_id=@userId or @administrator))
            select (select count(*) from users where @readUsers or id=@userId) as users,
              (select count(*) from roles r where @readRoles or exists(select 1 from user_roles ur where ur.role_id=r.id and ur.user_id=@userId)) as roles,
              (select count(*) from devices d where @administrator or exists(select 1 from visible c where c.device_id=d.id)) as devices,
              (select count(*) from devices d where d.enabled and d.status='online' and (@administrator or exists(select 1 from visible c where c.device_id=d.id))) as online_devices,
              (select count(*) from visible) as channels,
              (select count(*) from visible c join devices d on d.id=c.device_id where c.status='online' and d.enabled) as online_channels,
              (select count(*) from alarm_events a where a.occurred_at>=(date_trunc('day',now() at time zone 'Asia/Shanghai') at time zone 'Asia/Shanghai')
                and (@administrator or a.channel_id in (select id from visible))) as alarms_today,
              (select count(*) from alarm_events a where a.state<>'closed' and (@administrator or a.channel_id in (select id from visible))) as pending_alarms,
              (select count(*) from active_media where kind='live') as live_sessions,
              (select count(*) from active_media where kind='playback') as playback_sessions,
              (select count(*) from sessions s join users u on u.id=s.user_id where s.revoked_at is null and s.expires_at>now()
                and u.status='active' and (@manageSessions or s.user_id=@userId)) as online_sessions
            """, new { userId = actor.UserId, administrator, readUsers, readRoles, manageSessions }, ct);
        return new(row.Id("users"), row.Id("roles"), row.Id("devices"), row.Id("onlineDevices"), row.Id("channels"), row.Id("onlineChannels"), row.Id("alarmsToday"), row.Id("pendingAlarms"), row.Id("liveSessions"), row.Id("playbackSessions"), row.Id("onlineSessions"));
    }

    public async Task<SystemStatisticsDto> SystemAsync(Actor actor, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, "statistics.read", ct);
        var cpu = await AdministrationHostMetrics.CpuPercentAsync(ct);
        var (memoryTotal, memoryUsed) = AdministrationHostMetrics.Memory();
        var (diskTotal, diskFree) = AdministrationHostMetrics.Disk(options.DataPath);
        var services = new List<ServiceHealthDto> { new("平台 API", "online") };
        var row = await db.OneAsync("""
            select count(*) filter(where kind='live') as live_sessions,count(*) filter(where kind='playback') as playback_sessions,
              count(distinct (device_id,stream)) filter(where transcoded and stream is not null) as transcodes
            from media_sessions where closed_at is null and expires_at>now() and state not in ('stopped','failed','completed')
            """, ct: ct);
        services.Add(new("PostgreSQL", "online"));
        var heartbeats = await db.QueryAsync("select name,checked_at,details,(checked_at>now()-interval '90 seconds') as healthy from service_heartbeats order by checked_at desc,name", ct: ct);
        var latestWorker = heartbeats.FirstOrDefault(h => h.Text("name").StartsWith("worker", StringComparison.OrdinalIgnoreCase));
        services.Add(WorkerHealth(latestWorker));
        services.AddRange(heartbeats.Where(h => !h.Text("name").StartsWith("worker", StringComparison.OrdinalIgnoreCase))
            .Select(h => new ServiceHealthDto(h.Text("name"), h.Flag("healthy") ? "online" : "offline")));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var adapterHealth = ProbeAdapterAsync(timeout.Token);
        var zlmHealth = ProbeZlmAsync(timeout.Token);
        services.Add(new("海康适配器", await adapterHealth));
        services.Add(new("ZLMediaKit", await zlmHealth));
        ct.ThrowIfCancellationRequested();
        var uptime = (long)Math.Max(0, (DateTimeOffset.UtcNow - new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime())).TotalSeconds);
        return new(cpu, memoryTotal, memoryUsed, diskTotal, diskFree, uptime, services,
            new(row.Id("liveSessions"), row.Id("playbackSessions"), row.Id("transcodes")), "2.0.0");
    }

    private async Task<string> ProbeAdapterAsync(CancellationToken ct)
    {
        try
        {
            var result = await adapter.SendAsync(HttpMethod.Get, "/health", ct: ct);
            if (result is null) return "unknown";
            return result.Text("status").ToLowerInvariant() is "offline" or "unhealthy" or "failed" or "error" || result["healthy"]?.ToString().Equals("false", StringComparison.OrdinalIgnoreCase) == true ? "offline" : "online";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or PlatformException) { return "offline"; }
    }

    private static ServiceHealthDto WorkerHealth(JsonObject? heartbeat)
    {
        if (heartbeat is null) return new("后台任务", "unknown", "尚未收到后台任务心跳。");
        var jobs = heartbeat["details"]?["jobs"] as JsonObject;
        var failed = jobs?.Where(job => job.Value.Text("state") is "degraded" or "failed" or "unhealthy").ToArray() ?? [];
        static string JobName(string key) => key switch
        {
            "alarms" => "报警入库", "devices" => "设备同步", "media" => "媒体维护", "exports" => "录像导出",
            "retention" => "保留策略", "reconciliation" => "资源核对", _ => "后台子任务"
        };
        var reasons = failed.Select(job => $"{JobName(job.Key)}：{job.Value.Text("message", "运行异常")}").ToList();
        if (!heartbeat.Flag("healthy")) reasons.Insert(0, $"心跳超时，最近心跳：{heartbeat.Time("checkedAt"):O}");
        return new("后台任务", !heartbeat.Flag("healthy") ? "offline" : failed.Length > 0 ? "degraded" : "online",
            reasons.Count == 0 ? null : string.Join("；", reasons));
    }

    private async Task<string> ProbeZlmAsync(CancellationToken ct)
    {
        try
        {
            using var client = httpClients.CreateClient("zlm");
            using var response = await client.GetAsync($"{options.ZlmUrl.TrimEnd('/')}/index/api/getServerConfig?secret={Uri.EscapeDataString(options.ZlmSecret)}", ct);
            if (!response.IsSuccessStatusCode) return "offline";
            var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            return payload?["code"] is not null && payload.Id("code") == 0 ? "online" : "offline";
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { return "offline"; }
    }

    private async Task<T> WriteAsync<T>(Actor actor, string permission, Func<DbSession, Task<T>> operation, bool administrator = false, CancellationToken ct = default)
    {
        await access.DemandAsync(actor, permission, ct);
        try
        {
            return await db.TransactionAsync(async tx =>
            {
                await tx.ExecuteAsync("select pg_advisory_xact_lock(@key)", new { key = AdministrationLock }, ct);
                await DemandTransactionAsync(tx, actor, permission, administrator, ct);
                return await operation(tx);
            }, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new PlatformException(409, "resource.duplicate", "账号、编码或名称已存在，请使用其他值"); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        { throw new PlatformException(409, "resource.referenced", "资源仍被引用或关联资源已变化，请刷新后重试"); }
    }

    private static async Task DemandTransactionAsync(DbSession tx, Actor actor, string permission, bool administrator, CancellationToken ct)
    {
        var state = await tx.OneAsync("""
            select exists(select 1 from sessions s join users u on u.id=s.user_id where s.id=@sessionId and s.user_id=@userId
              and s.revoked_at is null and s.expires_at>now() and u.status='active') as authenticated,
              exists(select 1 from user_roles ur join roles r on r.id=ur.role_id and r.status='active'
                join role_permissions p on p.role_id=r.id where ur.user_id=@userId and p.permission_code=@permission) as allowed
            """, new { actor.SessionId, userId = actor.UserId, permission }, ct);
        Rules.Require(state.Flag("authenticated"), "登录会话已失效", "auth.expired", 401);
        Rules.Require(state.Flag("allowed"), "当前账号没有此操作权限", "access.denied", 403);
        if (administrator) Rules.Require(await IsAdministratorAsync(tx, actor.UserId, ct), "此授权操作仅限管理员执行", "access.administrator", 403);
    }

    private static async Task<bool> IsAdministratorAsync(DbSession tx, long userId, CancellationToken ct)
        => (await tx.OneAsync("select exists(select 1 from users u join user_roles ur on ur.user_id=u.id join roles r on r.id=ur.role_id where u.id=@userId and u.status='active' and r.code='admin' and r.status='active') as allowed", new { userId }, ct)).Flag("allowed");

    private static async Task RequireAdministratorRemainsAsync(DbSession tx, CancellationToken ct)
    {
        var exists = await tx.OneAsync("""
            select exists(select 1 from users u join user_roles ur on ur.user_id=u.id join roles r on r.id=ur.role_id
              where u.status='active' and r.code='admin' and r.status='active'
              and not exists(select 1 from permissions p where not exists(select 1 from role_permissions rp where rp.role_id=r.id and rp.permission_code=p.code))) as allowed
            """, ct: ct);
        Rules.Require(exists.Flag("allowed"), "必须保留至少一个启用且权限完整的管理员账号", "administrator.last", 409);
    }

    private static async Task ReplacePermissionsAsync(DbSession tx, long id, string roleCode, string[] codes, CancellationToken ct)
    {
        Rules.Require(codes is not null && codes.Length <= 256 && codes.All(c => !string.IsNullOrWhiteSpace(c) && c.Length <= 64), "权限代码无效");
        var normalized = codes!.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var available = (await tx.QueryAsync("select code from permissions order by code", ct: ct)).Select(r => r.Text("code")).ToArray();
        Rules.Require(normalized.All(c => available.Contains(c, StringComparer.Ordinal)), "包含不存在的权限代码", "permission.unknown");
        Rules.Require(roleCode != "admin" || normalized.ToHashSet(StringComparer.Ordinal).SetEquals(available), "系统管理员必须保留全部权限", "administrator.permissions", 409);
        await tx.ExecuteAsync("delete from role_permissions where role_id=@id", new { id }, ct);
        foreach (var code in normalized)
            await tx.ExecuteAsync("insert into role_permissions(role_id,permission_code) values(@id,@code)", new { id, code }, ct);
    }

    private static async Task DemandChannelsAsync(DbSession tx, long userId, long[] ids, bool requireAvailable, CancellationToken ct)
    {
        if (ids.Length == 0) return;
        var count = (await tx.OneAsync($"select count(*) as count from {AccessService.ChannelFrom} where c.id=any(@ids) and ({AccessService.ChannelPredicate}) {(requireAvailable ? "and d.enabled and c.status<>'disabled'" : "")}", new { userId, ids }, ct)).Id("count");
        Rules.Require(count == ids.Distinct().Count(), "包含不存在、不可用或未授权的通道", "channel.denied", 403);
    }

    private static async Task DemandFullScopeAsync(DbSession tx, long userId, CancellationToken ct)
    {
        var row = await tx.OneAsync(ScopeCte + "select exists(select 1 from principal where full_access) as allowed", new { userId }, ct);
        Rules.Require(row.Flag("allowed"), "此操作需要全部通道范围授权", "scope.denied", 403);
    }

    private static async Task DemandNodeScopeAsync(DbSession tx, long userId, string kind, long id, CancellationToken ct)
    {
        var predicate = kind switch
        {
            "workshops" => "exists(select 1 from grants where type='workshop' and scope_id=@id)",
            "areas" => "exists(select 1 from grants g join areas ar on ar.id=@id where (g.type='area' and g.scope_id=ar.id) or (g.type='workshop' and g.scope_id=ar.parent_id))",
            "units" => "exists(select 1 from grants g join units un on un.id=@id join areas ar on ar.id=un.parent_id where (g.type='unit' and g.scope_id=un.id) or (g.type='area' and g.scope_id=ar.id) or (g.type='workshop' and g.scope_id=ar.parent_id))",
            _ => throw NotFound()
        };
        var row = await tx.OneAsync(ScopeCte + $"select exists(select 1 from principal where full_access) or ({predicate}) as allowed", new { userId, id }, ct);
        Rules.Require(row.Flag("allowed"), "组织不在完整授权范围内，不能修改", "organization.denied", 403);
    }

    private static Task<JsonObject> RequiredAsync(DbSession tx, string table, long id, CancellationToken ct)
    {
        Rules.Require(id > 0, "资源编号必须为正整数");
        return Read();
        async Task<JsonObject> Read() => await tx.OneAsync($"select {(table == "layouts" ? LayoutColumns : "*")} from {table} where id=@id for update", new { id }, ct) ?? throw NotFound();
    }

    private static async Task<long[]> RoleUserIdsAsync(DbSession tx, long id, CancellationToken ct)
        => (await tx.QueryAsync("select user_id from user_roles where role_id=@id order by user_id", new { id }, ct)).Select(r => r.Id("userId")).ToArray();

    private static async Task<long[]> AllUserIdsAsync(DbSession tx, CancellationToken ct)
        => (await tx.QueryAsync("select id from users order by id", ct: ct)).Select(r => r.Id()).ToArray();

    private static async Task<long[]> UsersWithInaccessibleResourcesAsync(DbSession tx, CancellationToken ct)
    {
        // 组织移动只终止实际失去资源范围的账号，合法订阅者继续复用上游。
        var predicate = AccessService.ChannelPredicate.Replace("@userId", "resource.user_id", StringComparison.Ordinal);
        var rows = await tx.QueryAsync($"""
            with resources as (
              select user_id,channel_id from media_sessions where closed_at is null
              union select user_id,channel_id from ptz_leases
              union select user_id,channel_id from export_jobs where state in ('queued','running')
            ) select distinct resource.user_id from resources resource
              where not exists(select 1 from {AccessService.ChannelFrom} where c.id=resource.channel_id
                and d.enabled and c.status<>'disabled' and ({predicate})) order by resource.user_id
            """, ct: ct);
        return rows.Select(r => r.Id("userId")).ToArray();
    }

    private static async Task AccessChangedAsync(DbSession tx, IEnumerable<long> userIds, CancellationToken ct)
    {
        foreach (var userId in userIds.Distinct())
            await tx.ExecuteAsync("insert into outbox(kind,resource_id,user_id,version) values('access.changed',cast(@userId as text),@userId,nextval(pg_get_serial_sequence('outbox','id')))", new { userId }, ct);
    }

    private async Task RevokeUsersAsync(IEnumerable<long> userIds)
    {
        List<Exception>? failures = null;
        // 提交后的撤权不能随 HTTP 断开取消，且单个设备失败不能阻止其他账号撤权。
        foreach (var userId in userIds.Distinct())
        {
            try { await media.RevokeAsync(userId, null, CancellationToken.None); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is not null) throw new PlatformException(503, "access.revoke_pending", "授权已保存，但部分媒体资源释放失败；撤权事件已记录，请检查后台服务并刷新状态");
    }

    private static Task<int> AuditAsync(DbSession tx, Actor actor, string action, string resource, string summary, string? ip, CancellationToken ct)
        => tx.ExecuteAsync("insert into audit_logs(user_id,action,resource,summary,client_ip) values(@userId,@action,@resource,@summary,cast(@ip as inet))", new { userId = actor.UserId, action, resource, summary, ip }, ct);

    private static string OrganizationTable(string kind) => kind switch
    {
        "workshops" => "workshops", "areas" => "areas", "units" => "units", _ => throw NotFound()
    };

    private static (string Table, string Scopes, string Owner) ScopeTables(string kind) => kind switch
    {
        "user" => ("users", "user_scopes", "user_id"), "role" => ("roles", "role_scopes", "role_id"), _ => throw NotFound()
    };

    private static PlatformException NotFound() => new(404, "resource.not_found", "资源不存在");
    private static void ValidateStatus(string status, bool user = false) => Rules.Require(status is "active" or "disabled" || (user && status == "locked"), "状态无效");
    private static long[] ValidateIds(long[]? ids, int maximum, bool allowEmpty = true)
    {
        Rules.Require(ids is not null && ids.Length <= maximum && (allowEmpty || ids.Length > 0) && ids.All(i => i > 0), $"编号列表无效，最多允许 {maximum} 项");
        return ids!.Distinct().ToArray();
    }
    private static void ValidatePage(int page, int size) => Rules.Require(page is >= 1 and <= 100000 && size is >= 1 and <= 200, "分页参数无效");
    private static Dictionary<string, object?> PageArgs(int page, int size, string? search)
    {
        var text = search?.Trim() ?? "";
        Rules.Require(text.Length <= 256, "搜索条件过长");
        return new() { ["search"] = text, ["pattern"] = $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%", ["size"] = size, ["offset"] = (page - 1) * size };
    }
    private static void ValidateSettings(PlatformSettings value)
    {
        Rules.Require(value.LivePerUser is >= 1 and <= 64 && value.PlaybackPerUser is >= 1 and <= 16 && value.PlaybackPerDevice is >= 1 and <= 128 && value.PlaybackGlobal is >= 1 and <= 256 && value.TranscodeGlobal is >= 0 and <= 32, "媒体配额超出允许范围");
        Rules.Require(value.ExportGlobal is >= 1 and <= 16 && value.ExportPerDevice is >= 1 and <= 4 && value.ExportRetentionDays is >= 1 and <= 365 && value.ExportQuotaGb is >= 1 and <= 10000 && value.AlarmRetentionDays is >= 1 and <= 3650 && value.AuditRetentionDays is >= 1 and <= 3650, "任务或保留配置超出允许范围");
    }
    private static long? NullableId(JsonObject row, string name) => row[name] is null ? null : row.Id(name);
    private static T[] Array<T>(JsonObject row, string name) => row[name]?.Deserialize<T[]>(JsonDefaults.Options) ?? [];
    private static OrganizationNodeDto ToNode(JsonObject row) => new(row.Id(), row.Text("name"), row.Text("code"), row.Text("status"), NullableId(row, "parentId"));
    private static UserDto ToUser(JsonObject row) => new(row.Id(), row.Text("username"), row["displayName"]?.ToString(), row["phone"]?.ToString(), row.Text("status"), Array<string>(row, "permissions"), Array<long>(row, "roleIds"));
    private static AdministrationRoleDto ToRole(JsonObject row) => new(row.Id(), row.Text("name"), row.Text("code"), row.Text("status"), Array<string>(row, "permissionCodes"), row.Id("userCount"));
    private static ChannelDto ToChannel(JsonObject row) => new(row.Id(), row.Id("deviceId"), row.Text("deviceName"), (int)row.Id("deviceChannel"), row.Text("name"), row["model"]?.ToString(), row.Text("status"), NullableId(row, "unitId"), row.Flag("ptzCapable"), row["codec"]?.ToString());
    private static AdministrationLayoutDto ToLayout(JsonObject row) => new(row.Id(), row.Text("name"), row.Text("kind"), row.Flag("shared"), (int)row.Id("layout"), (int)row.Id("intervalSeconds"), Array<long?>(row, "channelIds"));
}

public static class AdministrationHostMetrics
{
    public static async Task<double?> CpuPercentAsync(CancellationToken ct = default)
    {
        try
        {
            var first = ReadCpu();
            if (first is null) return null;
            await Task.Delay(150, ct);
            var second = ReadCpu();
            if (second is null || second.Value.Total <= first.Value.Total) return null;
            var total = second.Value.Total - first.Value.Total;
            var idle = second.Value.Idle - first.Value.Idle;
            return Math.Round(Math.Clamp(100d * (total - idle) / total, 0, 100), 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException) { return null; }
    }

    public static (long? Total, long? Used) Memory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var state = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref state)) return ((long)state.TotalPhysical, (long)(state.TotalPhysical - state.AvailablePhysical));
            }
            if (OperatingSystem.IsLinux())
            {
                var values = File.ReadLines("/proc/meminfo").Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => long.Parse(parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], CultureInfo.InvariantCulture) * 1024);
                if (values.TryGetValue("MemTotal", out var total) && values.TryGetValue("MemAvailable", out var available)) return (total, total - available);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException) { }
        return (null, null);
    }

    public static (long? Total, long? Free) Disk(string path)
    {
        try
        {
            var resolved = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var drive = DriveInfo.GetDrives().Where(d => d.IsReady && (resolved.Equals(d.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar), comparison)
                || resolved.StartsWith(Path.EndsInDirectorySeparator(d.RootDirectory.FullName) ? d.RootDirectory.FullName : d.RootDirectory.FullName + Path.DirectorySeparatorChar, comparison)))
                .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
            return drive is null ? (null, null) : (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return (null, null); }
    }

    private static (ulong Idle, ulong Total)? ReadCpu()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            return (idle.Value, kernel.Value + user.Value);
        }
        if (OperatingSystem.IsLinux())
        {
            var fields = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var values = fields.Skip(1).Take(8).Select(v => ulong.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (values.Length < 4) return null;
            return (values[3] + (values.Length > 4 ? values[4] : 0), values.Aggregate(0UL, (sum, value) => sum + value));
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
