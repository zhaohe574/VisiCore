using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using VideoPlatform.Api;
using VideoPlatform.Api.Modules;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

var databaseUrl = Environment.GetEnvironmentVariable("ADMIN_TEST_DATABASE_URL");
if (string.IsNullOrWhiteSpace(databaseUrl))
{
    Console.Error.WriteLine("缺少 ADMIN_TEST_DATABASE_URL。请指定测试 PostgreSQL 连接串；测试在随机独立 schema 中运行并清理，不使用现有业务表。");
    return 2;
}

var schema = $"admin_test_{Guid.NewGuid():N}";
await using var root = NpgsqlDataSource.Create(databaseUrl);
await using (var create = root.CreateCommand($"create schema {schema}")) await create.ExecuteNonQueryAsync();
try
{
    var connection = new NpgsqlConnectionStringBuilder(databaseUrl) { SearchPath = schema };
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1073741824);
    builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 1073741824);
    builder.Services.AddAntiforgery();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PLATFORM_DATABASE_URL"] = connection.ConnectionString,
        ["HIK_ADAPTER_INTERNAL_KEY"] = "管理集成测试适配器密钥",
        ["PLATFORM_ADMIN_USER"] = "admin",
        ["PLATFORM_ADMIN_PASSWORD"] = "Admin-Test-Password-2026!",
        ["PLATFORM_DATA_PATH"] = Path.GetFullPath(Environment.GetEnvironmentVariable("ADMIN_TEST_DATA_PATH") ?? Path.Combine("tools", "VideoPlatform.Admin.Tests", ".runtime", "data")),
        ["HIK_ADAPTER_API_URL"] = "http://127.0.0.1:1",
        ["ZLM_API_URL"] = "http://127.0.0.1:1"
    });
    builder.Services.AddSingleton(new PlatformOptions(builder.Configuration));
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connection.ConnectionString));
    builder.Services.AddSingleton<Database>();
    builder.Services.AddSingleton<AccessService>();
    builder.Services.AddSingleton<SessionStore>();
    builder.Services.AddSingleton<AuditStore>();
    builder.Services.AddSingleton<ISettingsStore, SettingsStore>();
    builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    builder.Services.AddSingleton<SecretStore>();
    builder.Services.AddSingleton<FakeAdapter>();
    builder.Services.AddSingleton<IDeviceAdapter>(provider => provider.GetRequiredService<FakeAdapter>());
    builder.Services.AddSingleton<MediaService>();
    builder.Services.AddSingleton<AdministrationService>();
    builder.Services.AddHttpClient("zlm").ConfigurePrimaryHttpMessageHandler(() => new FakeZlm());
    builder.Services.AddAuthentication("session").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("session", _ => { });
    builder.Services.AddAuthorization();
    await using var app = builder.Build();
    await Bootstrap.InitializeAsync(app.Services);
    Directory.CreateDirectory(app.Services.GetRequiredService<PlatformOptions>().ReleasesPath);
    app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (PlatformException ex)
        {
            context.Response.StatusCode = ex.Status;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(ex.Code, ex.Message, context.TraceIdentifier));
        }
        // 与正式 API 的唯一约束冲突映射一致，保留真实 Npgsql 失败路径。
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ErrorResponse("data.duplicate", "名称、编号或记录已存在", context.TraceIdentifier));
        }
    });
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapAdministrationEndpoints();
    app.MapReleaseEndpoints();
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        var releases = new ReleaseRegression(app.Services, http);
        await releases.RunAsync();
        var tests = new AdministrationTests(app.Services, http);
        await tests.RunAsync();
        Console.WriteLine($"管理模块集成测试通过：{tests.Passed + releases.Passed} 项。数据库、HTTP、权限与撤权均执行了真实路径；设备适配器和媒体 HTTP 为明确标记的模拟端。");
    }
    finally { await app.StopAsync(); }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"管理模块集成测试失败：{ex}");
    return 1;
}
finally
{
    await using var cleanup = root.CreateCommand($"drop schema {schema} cascade");
    await cleanup.ExecuteNonQueryAsync();
}

internal sealed class AdministrationTests(IServiceProvider services, HttpClient http)
{
    private readonly Database db = services.GetRequiredService<Database>();
    private readonly AdministrationService admin = services.GetRequiredService<AdministrationService>();
    private readonly AccessService access = services.GetRequiredService<AccessService>();
    private readonly SessionStore sessions = services.GetRequiredService<SessionStore>();
    private readonly FakeAdapter adapter = services.GetRequiredService<FakeAdapter>();
    private const string Password = "Admin-Test-Password-2026!";
    private const string Ip = "127.0.0.1";
    public int Passed { get; private set; }

    public async Task RunAsync()
    {
        var (actor, token) = await LoginAsync("admin");
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var adminRole = (await admin.RolesAsync(actor)).Single(r => r.Code == "admin");
        var operatorRole = (await admin.RolesAsync(actor)).Single(r => r.Code == "operator");
        var w1 = await admin.SaveOrganizationAsync(actor, "workshops", null, new("一车间", "w1"), Ip);
        var w2 = await admin.SaveOrganizationAsync(actor, "workshops", null, new("二车间", "w2"), Ip);
        var a1 = await admin.SaveOrganizationAsync(actor, "areas", null, new("一区", "a1", ParentId: w1.Id), Ip);
        var a2 = await admin.SaveOrganizationAsync(actor, "areas", null, new("二区", "a2", ParentId: w2.Id), Ip);
        var u1 = await admin.SaveOrganizationAsync(actor, "units", null, new("一单元", "u1", ParentId: a1.Id), Ip);
        var u2 = await admin.SaveOrganizationAsync(actor, "units", null, new("二单元", "u2", ParentId: a2.Id), Ip);
        var d1 = (await db.OneAsync("insert into devices(name,host,port,username,password_cipher,status) values('测试设备一','127.0.0.11',8000,'test','模拟密文','online') returning id")).Id();
        var d2 = (await db.OneAsync("insert into devices(name,host,port,username,password_cipher,status) values('测试设备二','127.0.0.12',8000,'test','模拟密文','offline') returning id")).Id();
        var c1 = (await db.OneAsync("insert into channels(device_id,device_channel,name,status,unit_id,ptz_capable) values(@d1,1,'一号通道','online',@unit,true) returning id", new { d1, unit = u1.Id })).Id();
        var c2 = (await db.OneAsync("insert into channels(device_id,device_channel,name,status,unit_id) values(@d2,1,'同号异设备通道','offline',@unit) returning id", new { d2, unit = u2.Id })).Id();
        var operatorUser = await admin.SaveUserAsync(actor, null, new("operator1", Password, "值守员", "", "active", [operatorRole.Id]), Ip);
        var (operatorActor, operatorToken) = await LoginAsync(operatorUser.Username);

        await CheckAsync("未配置范围默认拒绝，area.read 仅返回授权树", async () =>
        {
            Assert(!await access.CanChannelAsync(operatorActor.UserId, c1), "未配置范围却可读取通道");
            Assert((await admin.OrganizationAsync(operatorActor)).Workshops.Count == 0, "未配置范围泄露组织树");
            await admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, [new("channel", c1)]), Ip);
            var tree = await admin.OrganizationAsync(operatorActor);
            Assert(tree.Workshops.Select(w => w.Id).SequenceEqual([w1.Id]) && tree.Areas.Select(a => a.Id).SequenceEqual([a1.Id]) && tree.Units.Select(u => u.Id).SequenceEqual([u1.Id]), "单通道范围未正确裁剪父级树");
            Assert(await access.CanChannelAsync(operatorActor.UserId, c1) && !await access.CanChannelAsync(operatorActor.UserId, c2), "同号不同设备通道发生串权");
        });

        await CheckAsync("只有 area.read 仍可看到显式授权的空组织", async () =>
        {
            var role = await admin.SaveRoleAsync(actor, null, new("组织查看", "area-only", PermissionCodes: ["area.read"]), Ip);
            var user = await admin.SaveUserAsync(actor, null, new("areaonly", Password, null, null, "active", [role.Id]), Ip);
            var (reader, _) = await LoginAsync(user.Username);
            var empty = await admin.SaveOrganizationAsync(actor, "workshops", null, new("空车间", "empty"), Ip);
            await admin.SaveScopesAsync(actor, "role", role.Id, new(false, [new("workshop", empty.Id)]), Ip);
            var tree = await admin.OrganizationAsync(reader);
            Assert(tree.Workshops.Single().Id == empty.Id && tree.Units.Count == 0, "空节点显式范围未显示");
            await ExpectAsync(403, () => admin.FavoritesAsync(reader));
            await ExpectAsync(409, () => admin.DeleteOrganizationAsync(actor, "workshops", empty.Id, Ip));
            await admin.SaveScopesAsync(actor, "role", role.Id, new(false, []), Ip);
            await admin.DeleteOrganizationAsync(actor, "workshops", empty.Id, Ip);
        });

        await CheckAsync("组织 CRUD、父级检查和引用保护", async () =>
        {
            await ExpectAsync(409, () => admin.DeleteOrganizationAsync(actor, "workshops", w1.Id, Ip));
            await ExpectAsync(409, () => admin.DeleteOrganizationAsync(actor, "units", u1.Id, Ip));
            await ExpectAsync(404, () => admin.SaveOrganizationAsync(actor, "areas", null, new("不存在父级", "bad", ParentId: 999999), Ip));
            await ExpectAsync(409, () => admin.SaveOrganizationAsync(actor, "workshops", null, new("重复编码", "w1"), Ip));
            var updated = await admin.SaveOrganizationAsync(actor, "units", u1.Id, new("已更名单元", "u1", ParentId: a1.Id), Ip);
            Assert(updated.Name == "已更名单元", "组织更新未生效");
        });

        await CheckAsync("批量通道移动原子性、审计及越权拒绝", async () =>
        {
            await ExpectAsync(403, () => admin.AssignChannelsAsync(actor, new([c1, 999999], u2.Id), Ip));
            Assert((await db.OneAsync("select unit_id from channels where id=@c1", new { c1 })).Id("unitId") == u1.Id, "失败批量移动产生部分写入");
            await admin.AssignChannelsAsync(actor, new([c1, c2], null), Ip);
            Assert((await db.OneAsync("select count(*) as count from channels where unit_id is null")).Id("count") == 2, "批量取消分配失败");
            var logs = await admin.AuditAsync(actor, 1, 50, "", "channel.assign", null, null);
            Assert(logs.Total == 2 && logs.Items.All(i => i.ClientIp == Ip), "批量移动未逐条事务审计");
            await admin.AssignChannelsAsync(actor, new([c1], u1.Id), Ip);
            await admin.AssignChannelsAsync(actor, new([c2], u2.Id), Ip);
            await ExpectAsync(403, () => admin.AssignChannelsAsync(operatorActor, new([c2], u1.Id), Ip));
        });

        await CheckAsync("多角色权限合并、停用角色失效、显式全部通道", async () =>
        {
            var role = await admin.SaveRoleAsync(actor, null, new("统计员", "stats", PermissionCodes: ["statistics.read"]), Ip);
            var user = await admin.SaveUserAsync(actor, null, new("multirole", Password, "多角色", null, "active", [operatorRole.Id, role.Id]), Ip);
            Assert(user.RoleIds.Length == 2 && user.Permissions.Contains("statistics.read") && user.Permissions.Contains("live.view"), "多角色功能权限未合并");
            await admin.SaveScopesAsync(actor, "role", role.Id, new(true, []), Ip);
            Assert(await access.CanChannelAsync(user.Id, c2), "角色全部通道未生效");
            await admin.SaveRoleAsync(actor, role.Id, new("统计员", "stats", "disabled"), Ip);
            Assert(!await access.CanChannelAsync(user.Id, c2) && !await access.HasPermissionAsync(user.Id, "statistics.read"), "停用角色仍授予范围或功能");
            await admin.SaveScopesAsync(actor, "user", user.Id, new(true, []), Ip);
            Assert(await access.CanChannelAsync(user.Id, c2), "用户全部通道未生效");
            await admin.SaveScopesAsync(actor, "user", user.Id, new(false, []), Ip);
            Assert(!await access.CanChannelAsync(user.Id, c2), "全部范围撤销后仍有权限");
        });

        await CheckAsync("局部组织维护允许本节点修改，拒绝修改父级或越权移动", async () =>
        {
            var role = await admin.SaveRoleAsync(actor, null, new("局部组织维护", "local-area-manager", PermissionCodes: ["area.read", "area.manage", "channel.assign"]), Ip);
            var user = await admin.SaveUserAsync(actor, null, new("local-area-manager", Password, null, null, "active", [role.Id]), Ip);
            var (manager, _) = await LoginAsync(user.Username);
            await admin.SaveScopesAsync(actor, "user", user.Id, new(false, [new("unit", u1.Id)]), Ip);
            var updated = await admin.SaveOrganizationAsync(manager, "units", u1.Id, new("授权单元更名", "u1", ParentId: a1.Id), Ip);
            Assert(updated.Name == "授权单元更名", "授权单元的正常资料修改被错误拒绝");
            await ExpectAsync(403, () => admin.SaveOrganizationAsync(manager, "workshops", w1.Id, new("越权修改车间", "w1"), Ip));
            await ExpectAsync(403, () => admin.SaveOrganizationAsync(manager, "units", u1.Id, new("越权移动", "u1", ParentId: a2.Id), Ip));
            await ExpectAsync(403, () => admin.SaveOrganizationAsync(manager, "units", null, new("越权新增同级", "forged", ParentId: a1.Id), Ip));
            await ExpectAsync(403, () => admin.AssignChannelsAsync(manager, new([c1], u2.Id), Ip));
            await ExpectAsync(403, () => admin.AssignChannelsAsync(manager, new([c2], u1.Id), Ip));
            await ExpectAsync(403, () => admin.SaveOrganizationAsync(manager, "workshops", null, new("越权新增车间", "forged"), Ip));
            Assert((await db.OneAsync("select parent_id from units where id=@id", new { id = u1.Id })).Id("parentId") == a1.Id, "越权移动改变组织关系");
        });

        await CheckAsync("非管理员不能借 user.manage 自授管理员或重置管理账号", async () =>
        {
            var role = await admin.SaveRoleAsync(actor, null, new("账号维护", "user-manager", PermissionCodes: ["user.read", "user.manage", "role.read", "role.manage", "settings.manage"]), Ip);
            var user = await admin.SaveUserAsync(actor, null, new("user-manager", Password, "维护员", null, "active", [role.Id]), Ip);
            var (manager, _) = await LoginAsync(user.Username);
            await ExpectAsync(403, () => admin.SaveUserAsync(manager, user.Id, new(user.Username, null, null, null, "active", [role.Id, adminRole.Id]), Ip));
            await ExpectAsync(403, () => admin.SaveUserAsync(manager, actor.UserId, new(actor.Username, "Replacement-Password-2026", null, null, "active", [adminRole.Id]), Ip));
            await ExpectAsync(403, () => admin.SaveUserAsync(manager, null, new("forged-admin", Password, null, null, "active", [adminRole.Id]), Ip));
            await ExpectAsync(403, () => admin.SetPermissionsAsync(manager, role.Id, new(Rules.Permissions.Keys.ToArray()), Ip));
            await ExpectAsync(403, () => admin.SaveRoleAsync(manager, null, new("非法强权角色", "forged-power", PermissionCodes: Rules.Permissions.Keys.ToArray()), Ip));
            await ExpectAsync(403, () => admin.SaveRoleAsync(manager, role.Id, new("修改自有角色", role.Code, PermissionCodes: Rules.Permissions.Keys.ToArray()), Ip));
            await ExpectAsync(403, () => admin.DeleteRoleAsync(manager, role.Id, Ip));
            await ExpectAsync(403, () => admin.SaveScopesAsync(manager, "user", user.Id, new(true, []), Ip));
            await ExpectAsync(403, () => admin.SaveSettingsAsync(manager, new(), Ip));
            Assert(!await access.IsAdministratorAsync(user.Id), "拒绝后仍获得管理员角色");
        });

        await CheckAsync("最后管理员、保留角色及未知权限保护", async () =>
        {
            await ExpectAsync(409, () => admin.SaveUserAsync(actor, actor.UserId, new(actor.Username, null, null, null, "disabled", [adminRole.Id]), Ip));
            await ExpectAsync(409, () => admin.SaveUserAsync(actor, actor.UserId, new(actor.Username, null, null, null, "locked", [adminRole.Id]), Ip));
            await ExpectAsync(409, () => admin.SaveUserAsync(actor, actor.UserId, new(actor.Username, null, null, null, "active", []), Ip));
            await ExpectAsync(409, () => admin.SetPermissionsAsync(actor, adminRole.Id, new(["user.read"]), Ip));
            await ExpectAsync(409, () => admin.DeleteRoleAsync(actor, adminRole.Id, Ip));
            await ExpectAsync(409, () => admin.SaveRoleAsync(actor, adminRole.Id, new("管理员", "ordinary"), Ip));
            await ExpectAsync(409, () => admin.SaveRoleAsync(actor, adminRole.Id, new("管理员", "admin", "disabled"), Ip));
            await ExpectAsync(400, () => admin.SaveRoleAsync(actor, null, new("未知权限", "bad-role", PermissionCodes: ["invented.permission"]), Ip));
            Assert(await access.IsAdministratorAsync(actor.UserId), "失败修改未回滚管理员权限");
        });

        await CheckAsync("角色 CRUD、范围引用检查和关联账号撤权事件", async () =>
        {
            var role = await admin.SaveRoleAsync(actor, null, new("可删除角色", "delete-me", PermissionCodes: ["area.read"]), Ip);
            var user = await admin.SaveUserAsync(actor, null, new("role-member", Password, null, null, "active", [role.Id]), Ip);
            await ExpectAsync(409, () => admin.DeleteRoleAsync(actor, role.Id, Ip));
            await ExpectAsync(404, () => admin.SaveScopesAsync(actor, "role", role.Id, new(false, [new("unit", 999999)]), Ip));
            await admin.SaveScopesAsync(actor, "role", role.Id, new(false, [new("area", a1.Id)]), Ip);
            await admin.SetPermissionsAsync(actor, role.Id, new(["area.read", "channel.read"]), Ip);
            Assert((await db.OneAsync("select count(*) as count from outbox where kind='access.changed' and user_id=@id", new { id = user.Id })).Id("count") > 0, "角色变更未通知关联账号");
            await admin.SaveUserAsync(actor, user.Id, new(user.Username, null, null, null, "active", []), Ip);
            await admin.DeleteRoleAsync(actor, role.Id, Ip);
            Assert(!(await admin.RolesAsync(actor)).Any(r => r.Id == role.Id), "角色未删除");
        });

        await CheckAsync("收藏排序去重、越权回滚和撤权后裁剪", async () =>
        {
            var favorites = await admin.SaveFavoritesAsync(operatorActor, new([c1, c1]), Ip);
            Assert(favorites.Count == 1 && favorites[0].Id == c1, "收藏未去重");
            await ExpectAsync(403, () => admin.SaveFavoritesAsync(operatorActor, new([c1, c2]), Ip));
            Assert((await admin.FavoritesAsync(operatorActor)).Single().Id == c1, "越权收藏破坏原收藏");
            await admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, []), Ip);
            Assert((await admin.FavoritesAsync(operatorActor)).Count == 0, "撤权后收藏仍泄露通道");
            await admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, [new("channel", c1)]), Ip);
        });

        await CheckAsync("个人布局与共享轮巡 CRUD、授权过滤和发布权限", async () =>
        {
            var shared = await admin.SaveLayoutAsync(actor, null, new("共享轮巡", "patrol", true, 4, 30, [c1, c2]), Ip);
            var visible = (await admin.LayoutsAsync(operatorActor)).Single(l => l.Id == shared.Id);
            Assert(visible.ChannelIds.SequenceEqual([c1]), "共享轮巡泄露越权通道");
            await ExpectAsync(403, () => admin.SaveLayoutAsync(operatorActor, null, new("非法共享", "patrol", true, 4, 30, [c1]), Ip));
            await ExpectAsync(403, () => admin.SaveLayoutAsync(operatorActor, null, new("越权布局", "layout", false, 4, 30, [c2]), Ip));
            await ExpectAsync(400, () => admin.SaveLayoutAsync(operatorActor, null, new("过短间隔", "patrol", false, 4, 9, [c1]), Ip));
            var personal = await admin.SaveLayoutAsync(operatorActor, null, new("个人四屏", "layout", false, 4, 30, [c1]), Ip);
            Assert(!(await admin.LayoutsAsync(actor)).Any(l => l.Id == personal.Id), "管理员读取了他人个人布局");
            await ExpectAsync(403, () => admin.DeleteLayoutAsync(actor, personal.Id, Ip));
            var updated = await admin.SaveLayoutAsync(operatorActor, personal.Id, new("个人单屏", "layout", false, 1, 30, [c1]), Ip);
            Assert(updated.Layout == 1, "布局更新未生效");
            await admin.DeleteLayoutAsync(operatorActor, personal.Id, Ip);
            await admin.DeleteLayoutAsync(actor, shared.Id, Ip);
        });

        await CheckAsync("范围撤销主动停止媒体、PTZ、导出且不影响其他用户共享流", async () =>
        {
            var own = await SeedMediaAsync(operatorActor, c1, d1);
            var other = await SeedMediaAsync(actor, c1, d1);
            var job = Guid.NewGuid();
            await db.ExecuteAsync("insert into ptz_leases(channel_id,user_id,auth_session_id,command,speed,expires_at) values(@c1,@userId,@sessionId,'up',4,now()+interval '10 seconds')", new { c1, operatorActor.UserId, operatorActor.SessionId });
            await db.ExecuteAsync("insert into export_jobs(id,user_id,auth_session_id,channel_id,device_id,start_at,end_at) values(@job,@userId,@sessionId,@c1,@d1,now()-interval '1 hour',now())", new { job, operatorActor.UserId, operatorActor.SessionId, c1, d1 });
            await admin.SaveScopesAsync(actor, "user", operatorActor.UserId, new(false, []), Ip);
            Assert((await db.OneAsync("select state from media_sessions where id=@own", new { own })).Text("state") == "stopped", "撤权没有停止媒体会话");
            Assert((await db.OneAsync("select state from media_sessions where id=@other", new { other })).Text("state") == "playing", "撤权停止了其他用户共享流");
            Assert(await db.OneAsync("select channel_id from ptz_leases where channel_id=@c1", new { c1 }) is null, "撤权未释放 PTZ");
            Assert((await db.OneAsync("select state from export_jobs where id=@job", new { job })).Text("state") == "cancelled", "撤权未取消导出");
            Assert(adapter.Calls.Any(c => c.Method == HttpMethod.Delete && c.Path.EndsWith(own.ToString())), "未调用设备侧媒体停止");
            await admin.SaveScopesAsync(actor, "user", operatorActor.UserId, new(false, [new("channel", c1)]), Ip);
        });

        await CheckAsync("密码重置撤销全部会话，单会话撤销幂等且保留其他登录", async () =>
        {
            var first = await LoginAsync(operatorUser.Username);
            var second = await LoginAsync(operatorUser.Username);
            await admin.RevokeSessionAsync(actor, first.Actor.SessionId, Ip);
            await admin.RevokeSessionAsync(actor, first.Actor.SessionId, Ip);
            Assert(await sessions.AuthenticateAsync(first.Token) is null, "会话撤销未生效");
            Assert(await sessions.AuthenticateAsync(second.Token) is not null, "单会话撤销误伤其他登录");
            await admin.SaveUserAsync(actor, operatorUser.Id, new(operatorUser.Username, "Changed-Password-2026!", "值守员", "", "active", [operatorRole.Id]), Ip);
            Assert(await sessions.AuthenticateAsync(second.Token) is null && await sessions.AuthenticateAsync(operatorToken) is null, "密码重置未撤销全部会话");
            var (_, fresh) = await LoginAsync(operatorUser.Username, "Changed-Password-2026!");
            Assert(await sessions.AuthenticateAsync(fresh) is not null, "新密码无法登录");
        });

        await CheckAsync("组织移动仅撤销失去范围的订阅者，保留合法共享流", async () =>
        {
            var scoped = await admin.SaveUserAsync(actor, null, new("unit-viewer", Password, null, null, "active", [operatorRole.Id]), Ip);
            var (viewer, _) = await LoginAsync(scoped.Username);
            await admin.SaveScopesAsync(actor, "user", viewer.UserId, new(false, [new("unit", u1.Id)]), Ip);
            var own = await SeedMediaAsync(viewer, c1, d1);
            var lawful = await SeedMediaAsync(actor, c1, d1);
            await admin.AssignChannelsAsync(actor, new([c1], u2.Id), Ip);
            Assert((await db.OneAsync("select state from media_sessions where id=@own", new { own })).Text("state") == "stopped", "组织移动未撤销失权订阅者");
            Assert((await db.OneAsync("select state from media_sessions where id=@lawful", new { lawful })).Text("state") == "playing", "组织移动停止了合法共享流");
            await admin.AssignChannelsAsync(actor, new([c1], u1.Id), Ip);
        });

        await CheckAsync("设置边界及事务审计", async () =>
        {
            var original = await admin.SettingsAsync(actor);
            await ExpectAsync(400, () => admin.SaveSettingsAsync(actor, original with { PlaybackGlobal = 0 }, Ip));
            Assert((await admin.SettingsAsync(actor)).PlaybackGlobal == original.PlaybackGlobal, "失败配置改变设置");
            var changed = await admin.SaveSettingsAsync(actor, original with { TranscodeGlobal = 0, ExportRetentionDays = 8 }, Ip);
            Assert(changed.TranscodeGlobal == 0 && (await admin.SettingsAsync(actor)).ExportRetentionDays == 8, "配置未持久化");
            Assert((await admin.AuditAsync(actor, 1, 50, "", "settings.update", null, null)).Total == 1, "配置审计不准确");
        });

        await CheckAsync("管理请求字段空值、长度、状态、编号和未知权限验证", async () =>
        {
            var baseline = (await admin.UsersAsync(actor, 1, 200, "")).Total;
            foreach (var request in new UserRequest[]
            {
                new(" ", Password, null, null, "active", []),
                new(new string('u', 65), Password, null, null, "active", []),
                new("has space", Password, null, null, "active", []),
                new("bad-password", "short", null, null, "active", []),
                new("bad-display", Password, new string('名', 129), null, "active", []),
                new("bad-phone", Password, null, new string('1', 33), "active", []),
                new("bad-status", Password, null, null, "deleted", []),
                new("bad-roles", Password, null, null, "active", null!),
                new("bad-role-id", Password, null, null, "active", [-1]),
                new("bad-many-roles", Password, null, null, "active", Enumerable.Range(1, 65).Select(i => (long)i).ToArray())
            }) await ExpectAsync(400, () => admin.SaveUserAsync(actor, null, request, Ip));
            await ExpectAsync(404, () => admin.SaveUserAsync(actor, null, new("missing-role", Password, null, null, "active", [999999]), Ip));
            Assert((await admin.UsersAsync(actor, 1, 200, "")).Total == baseline, "字段验证失败仍创建账号");
            await ExpectAsync(400, () => admin.SaveOrganizationAsync(actor, "workshops", null, new("错误父级", "bad", ParentId: w1.Id), Ip));
            await ExpectAsync(400, () => admin.SaveOrganizationAsync(actor, "units", null, new("无父级", "bad"), Ip));
            await ExpectAsync(400, () => admin.SaveOrganizationAsync(actor, "areas", null, new("无效状态", "bad", "locked", w1.Id), Ip));
            await ExpectAsync(404, () => admin.SaveOrganizationAsync(actor, "workshops;delete", null, new("未知类型", "bad"), Ip));
            await ExpectAsync(400, () => admin.AssignChannelsAsync(actor, new([], u1.Id), Ip));
            await ExpectAsync(400, () => admin.AssignChannelsAsync(actor, new([c1], -1), Ip));
            await ExpectAsync(400, () => admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, null!), Ip));
            await ExpectAsync(400, () => admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, [new("device", d1)]), Ip));
            await ExpectAsync(400, () => admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, [new("channel", 0)]), Ip));
            await ExpectAsync(400, () => admin.SaveScopesAsync(actor, "user", operatorUser.Id, new(false, [null!]), Ip));
            await ExpectAsync(400, () => admin.SetPermissionsAsync(actor, operatorRole.Id, new(null!), Ip));
            await ExpectAsync(400, () => admin.SetPermissionsAsync(actor, operatorRole.Id, new(["unknown.permission"]), Ip));
            await ExpectAsync(400, () => admin.SaveRoleAsync(actor, null, new("", "empty-name"), Ip));
            await ExpectAsync(400, () => admin.SaveRoleAsync(actor, null, new("过长编码", new string('r', 65)), Ip));
            await ExpectAsync(400, () => admin.SaveFavoritesAsync(actor, new(null!), Ip));
            await ExpectAsync(400, () => admin.SaveFavoritesAsync(actor, new([0]), Ip));
            foreach (var layout in new LayoutRequest[]
            {
                new("", "layout", false, 4, 30, [c1]),
                new("类型错误", "wall", false, 4, 30, [c1]),
                new("分屏错误", "layout", false, 3, 30, [c1]),
                new("间隔过大", "patrol", false, 4, 301, [c1]),
                new("空轮巡", "patrol", false, 4, 30, []),
                new("窗口过多", "layout", false, 1, 30, [c1, c2]),
                new("非法编号", "layout", false, 4, 30, [-1]),
                new("空值", "layout", false, 4, 30, null!)
            }) await ExpectAsync(400, () => admin.SaveLayoutAsync(actor, null, layout, Ip));
            await ExpectAsync(400, () => admin.UsersAsync(actor, 0, 50, ""));
            await ExpectAsync(400, () => admin.SessionsAsync(actor, 1, 201, ""));
            await ExpectAsync(400, () => admin.AuditAsync(actor, 1, 50, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)));
            await ExpectAsync(400, () => admin.UsersAsync(actor, 1, 50, new string('x', 257)));
            Assert((await admin.UsersAsync(actor, 1, 50, "%' OR 1=1 --")).Total == 0, "搜索输入未按字面参数处理");
            Assert((await admin.UsersAsync(actor, 1, 50, "%")).Total == 0, "搜索百分号被错误当作通配符");
        });

        await CheckAsync("各项系统配置独立边界拒绝且不改变持久化配置", async () =>
        {
            var original = await admin.SettingsAsync(actor);
            foreach (var invalid in new PlatformSettings[]
            {
                original with { LivePerUser = 65 }, original with { PlaybackPerUser = 17 },
                original with { PlaybackPerDevice = 129 }, original with { PlaybackGlobal = 257 },
                original with { TranscodeGlobal = -1 }, original with { ExportGlobal = 17 },
                original with { ExportPerDevice = 5 }, original with { ExportRetentionDays = 0 },
                original with { ExportQuotaGb = 10001 }, original with { AlarmRetentionDays = 0 },
                original with { AuditRetentionDays = 3651 }
            }) await ExpectAsync(400, () => admin.SaveSettingsAsync(actor, invalid, Ip));
            Assert(await admin.SettingsAsync(actor) == original, "无效设置导致部分写入");
        });

        await CheckAsync("真实仪表盘、宿主资源和服务探测", async () =>
        {
            await db.ExecuteAsync("insert into alarm_events(device_id,channel_id,source_id,event_type,occurred_at,state) values(@d1,@c1,'test-alarm','测试报警',now(),'new')", new { d1, c1 });
            await db.ExecuteAsync("insert into service_heartbeats(name,checked_at) values('worker',now())");
            var dash = await admin.DashboardAsync(actor);
            Assert(dash.Devices == 2 && dash.OnlineDevices == 1 && dash.Channels == 2 && dash.OnlineChannels == 1 && dash.PendingAlarms == 1 && dash.AlarmsToday == 1, "仪表盘不是实际数据");
            var system = await admin.SystemAsync(actor);
            Assert(system.MemoryTotalBytes is > 0 && system.MemoryUsedBytes > 0 && system.MemoryUsedBytes <= system.MemoryTotalBytes, "宿主内存未真实采集");
            Assert(system.DiskTotalBytes is > 0 && system.DiskFreeBytes >= 0 && system.DiskFreeBytes <= system.DiskTotalBytes, "宿主磁盘未真实采集");
            Assert(system.CpuPercent is >= 0 and <= 100, "宿主 CPU 未真实采集");
            Assert(system.Services.Single(s => s.Name == "后台任务").Status == "online", "后台心跳状态错误");
            await db.ExecuteAsync("update service_heartbeats set details=cast(@details as jsonb) where name='worker'", new { details = "{\"jobs\":{\"exports\":{\"state\":\"degraded\"}}}" });
            Assert((await admin.SystemAsync(actor)).Services.Single(s => s.Name == "后台任务").Status == "degraded", "降级的后台任务被错误标成正常");
            adapter.Healthy = false;
            Assert((await admin.SystemAsync(actor)).Services.Any(s => s.Name == "海康适配器" && s.Status == "offline"), "异常适配器被错误标成在线");
            adapter.Healthy = true;
        });

        await CheckAsync("HTTP 管理路由、分页契约、中文错误及禁止用户删除", async () =>
        {
            foreach (var path in new[] { "organization", "users", "roles", "permissions", $"scopes/user/{actor.UserId}", $"scopes/role/{adminRole.Id}", "sessions", "audit", "settings", "dashboard", "system", "favorites", "layouts" })
            {
                using var response = await http.GetAsync($"/api/v2/{path}");
                Assert(response.IsSuccessStatusCode, $"GET {path} 返回 {(int)response.StatusCode}：{await response.Content.ReadAsStringAsync()}");
            }
            var page = await http.GetFromJsonAsync<JsonObject>("/api/v2/users?page=1&pageSize=2&search=operator");
            Assert(page?["items"] is JsonArray && page.Id("page") == 1 && page.Id("pageSize") == 2 && page.Id("total") == 1, "分页契约不一致");
            using var badDate = await http.GetAsync("/api/v2/audit?from=bad-date");
            var error = await badDate.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert(badDate.StatusCode == HttpStatusCode.BadRequest && error?.TraceId.Length > 0 && error.Message.Contains("有效时间"), "时间验证缺少中文标准错误");
            using var delete = await http.DeleteAsync($"/api/v2/users/{operatorUser.Id}");
            Assert(delete.StatusCode == HttpStatusCode.MethodNotAllowed, "错误暴露用户删除接口");
            using var create = await http.PostAsJsonAsync("/api/v2/organization/workshops", new OrganizationRequest("HTTP 车间", "http-workshop"));
            Assert(create.StatusCode == HttpStatusCode.Created, "组织创建 HTTP 状态不符合契约");
            var created = await create.Content.ReadFromJsonAsync<OrganizationNodeDto>();
            using var update = await http.PutAsJsonAsync($"/api/v2/organization/workshops/{created!.Id}", new OrganizationRequest("HTTP 更名", "http-workshop"));
            Assert(update.StatusCode == HttpStatusCode.OK, "组织更新 HTTP 失败");
            using var removed = await http.DeleteAsync($"/api/v2/organization/workshops/{created.Id}");
            Assert(removed.StatusCode == HttpStatusCode.NoContent, "组织删除 HTTP 失败");
        });

        await CheckAsync("并发降权只能成功一次，至少保留一个管理员", async () =>
        {
            var secondUser = await admin.SaveUserAsync(actor, null, new("second-admin", Password, null, null, "active", [adminRole.Id]), Ip);
            var (second, _) = await LoginAsync(secondUser.Username);
            async Task<bool> Disable(Actor target)
            {
                try { await admin.SaveUserAsync(target, target.UserId, new(target.Username, null, null, null, "disabled", [adminRole.Id]), Ip); return true; }
                catch (PlatformException ex) when (ex.Status is 409 or 401 or 403) { return false; }
            }
            var results = await Task.WhenAll(Disable(actor), Disable(second));
            Assert(results.Count(ok => ok) == 1, "并发降权未阻止最后管理员被移除");
            Assert((await access.IsAdministratorAsync(actor.UserId) ? 1 : 0) + (await access.IsAdministratorAsync(second.UserId) ? 1 : 0) == 1, "最终管理员数量错误");
        });
    }

    private async Task<Guid> SeedMediaAsync(Actor actor, long channelId, long deviceId)
    {
        var id = Guid.NewGuid();
        var token = Passwords.Token();
        await db.ExecuteAsync("""
            insert into media_sessions(id,user_id,auth_session_id,device_id,channel_id,kind,profile,stream,token_hash,token_cipher,state,expires_at)
            values(@id,@userId,@sessionId,@deviceId,@channelId,'live','native','shared-test-stream',@hash,'测试不解密','playing',now()+interval '3 minutes')
            """, new { id, actor.UserId, actor.SessionId, deviceId, channelId, hash = Passwords.TokenHash(token) });
        return id;
    }

    private async Task<(Actor Actor, string Token)> LoginAsync(string username, string password = Password)
    {
        var response = await sessions.LoginAsync(new(username, password, "desktop", "2.0.0"), Ip);
        return ((await sessions.AuthenticateAsync(response.Token))!, response.Token);
    }
    private async Task CheckAsync(string name, Func<Task> check)
    {
        await check();
        Passed++;
        Console.WriteLine($"通过：{name}");
    }
    private static async Task ExpectAsync(int status, Func<Task> action)
    {
        try { await action(); }
        catch (PlatformException ex) when (ex.Status == status) { return; }
        throw new InvalidOperationException($"预期返回 HTTP {status}，实际操作未按预期拒绝");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class FakeAdapter : IDeviceAdapter
{
    public bool Healthy { get; set; } = true;
    public ConcurrentQueue<(HttpMethod Method, string Path)> Calls { get; } = new();
    public Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls.Enqueue((method, path));
        return Task.FromResult<JsonNode?>(path == "/health" ? new JsonObject { ["status"] = Healthy ? "online" : "offline" } : new JsonObject { ["state"] = "stopped" });
    }
}

internal sealed class FakeZlm : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { code = 0, data = new { } }) });
}
