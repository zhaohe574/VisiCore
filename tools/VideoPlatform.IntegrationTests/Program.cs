using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

var apiBase = Environment.GetEnvironmentVariable("TEST_API_URL") ?? "http://127.0.0.1:5082";
var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
var client = new HttpClient { BaseAddress = new Uri(apiBase), Timeout = TimeSpan.FromSeconds(30) };
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("检查失败：" + name);
    Console.WriteLine("通过：" + name);
    passed++;
}
async Task<(HttpStatusCode Status, JsonNode? Json)> Send(string method, string path, object? body = null, HttpClient? http = null)
{
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    if (body is not null) request.Content = JsonContent.Create(body);
    using var response = await (http ?? client).SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    JsonNode? json = string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    return (response.StatusCode, json);
}
async Task<JsonNode> Ok(string method, string path, object? body = null, HttpClient? http = null)
{
    var result = await Send(method, path, body, http);
    Check((int)result.Status is >= 200 and <= 299, $"{method} {path}：{(int)result.Status} {((int)result.Status >= 400 ? result.Json?.ToJsonString() : "")}");
    return result.Json ?? new JsonObject();
}

var adapterBuilder = WebApplication.CreateBuilder();
adapterBuilder.WebHost.UseUrls(Environment.GetEnvironmentVariable("TEST_ADAPTER_URL") ?? "http://127.0.0.1:5092");
adapterBuilder.Logging.ClearProviders();
await using var adapter = adapterBuilder.Build();
var sessions = new ConcurrentDictionary<Guid, JsonObject>();
var ptz = new ConcurrentDictionary<long, bool>();
adapter.MapGet("/health", () => new { status = "ok" });
adapter.MapPut("/internal/devices/{id:long}", (long id) => Results.Ok());
adapter.MapDelete("/internal/devices/{id:long}", (long id) => Results.NoContent());
adapter.MapPost("/internal/devices/{id:long}/sync", (long id) => new { device = new { model = "TEST-CVR", serialNumber = "TEST-" + id }, channels = new[] { new { channel = 1, name = "测试通道一", model = "TEST-PTZ", online = true, ptzCapable = true, codec = "h264" }, new { channel = 2, name = "测试通道二", model = "TEST-FIXED", online = true, ptzCapable = false, codec = "h265" } } });
adapter.MapPost("/internal/devices/{deviceId:long}/live", (long deviceId, JsonObject body) =>
{
    var id = Guid.Parse(body.Text("sessionId"));
    var row = new JsonObject { ["stream"] = $"d{deviceId}_ch{body.Id("channel")}_{body.Id("streamType")}", ["state"] = "playing", ["codec"] = "h264", ["transcoded"] = false, ["streamType"] = body.Id("streamType") };
    sessions[id] = row;
    return Results.Ok(row);
});
adapter.MapDelete("/internal/devices/{deviceId:long}/live/{id:guid}", (long deviceId, Guid id) => { sessions.TryRemove(id, out _); return Results.NoContent(); });
adapter.MapPost("/internal/devices/{deviceId:long}/ptz", (long deviceId, JsonObject body) => { ptz[deviceId] = !body.Flag("stop"); return Results.NoContent(); });
adapter.MapPost("/internal/devices/{deviceId:long}/ptz/preset", (long deviceId) => Results.NoContent());
adapter.MapDelete("/internal/devices/{deviceId:long}/exports/{id:guid}", () => Results.NoContent());
await adapter.StartAsync();
try
{
    var unauth = await Send("GET", "/api/v2/channels");
    Check(unauth.Status == HttpStatusCode.Unauthorized, "匿名读取通道被拒绝");
    var login = await Ok("POST", "/api/v2/auth/login", new { username = "admin", password = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD"), clientType = "desktop", clientVersion = "2.0.0-test" });
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Text("accessToken"));
    var adminId = login["user"].Id();
    // 恢复的资产模块必须通过真实数据库与 HTTP 接口验证，测试数据只进入隔离库。
    foreach (var endpoint in new[] { "/api/v2/computers", "/api/v2/ip-pools", "/api/v2/network-devices", "/api/v2/pon/statistics", "/api/v2/pon/onus", "/api/v2/pon/olts", "/api/v2/pon/health-index", "/api/v2/pon/dashboard/kanban", "/api/v2/pon/dashboard/history", "/api/v2/terminal-boxes", "/api/v2/terminal-boxes/topology-tree", "/api/v2/digital-twin/scenes" })
        await Ok("GET", endpoint);
    var restoredBox = await Ok("POST", "/api/v2/terminal-boxes", new { code = "TEST-RESTORE-" + stamp, name = "恢复测试终端箱" });
    await using var fixtureDb = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("PLATFORM_DATABASE_URL")!);
    await using var fixturePlugin = fixtureDb.CreateCommand("update device_plugins set endpoint_url=@endpoint where id='hikvision'");
    fixturePlugin.Parameters.AddWithValue("endpoint", Environment.GetEnvironmentVariable("TEST_ADAPTER_URL")!);
    await fixturePlugin.ExecuteNonQueryAsync();
    await using var onuFixture = fixtureDb.CreateCommand("insert into onu_devices(ne_dn,onu_id,onu_name,sn,terminal_box_id) values('restore-test','1','RESTORE-ONU','RESTORE-SN',@box) returning id");
    onuFixture.Parameters.AddWithValue("box", restoredBox.Id());
    var restoredOnuId = Convert.ToInt64(await onuFixture.ExecuteScalarAsync());
    var restoredComputer = await Ok("POST", "/api/v2/computers", new { name = "恢复测试电脑", ip = "192.0.2.10", terminalBoxId = restoredBox.Id(), onuDeviceId = restoredOnuId, portNumber = 2 });
    var restoredPool = await Ok("POST", "/api/v2/ip-pools", new { name = "恢复测试地址池", cidr = "192.0.2.0/24", autoScan = false });
    var restoredNetwork = await Ok("POST", "/api/v2/network-devices", new { name = "恢复测试交换机", deviceType = "switch", managementIp = "192.0.2.11" });
    var computers = await Ok("GET", "/api/v2/computers?page=1&pageSize=20&search=恢复测试电脑");
    Check(computers["items"]?.AsArray().Any(c => c.Id() == restoredComputer.Id()) == true && computers.Id("total") == 1, "电脑登记可分页查询且字段与现行契约一致");
    var occupied = await Ok("GET", "/api/v2/ip-pools/check-ip?ip=192.0.2.10");
    Check(occupied.Flag("occupied") && occupied.Text("boundType") == "computer", "登记电脑参与 IP 冲突检测");
    var suggestedIp = await Ok("GET", $"/api/v2/ip-pools/recommend-ip?poolId={restoredPool.Id()}");
    Check(suggestedIp.Text("ip") != "192.0.2.10" && suggestedIp.Id("poolId") == restoredPool.Id(), "推荐地址跳过已登记电脑");
    await Ok("GET", $"/api/v2/ip-pools/{restoredPool.Id()}/detail");
    await Ok("PUT", $"/api/v2/computers/{restoredComputer.Id()}", new { name = "恢复测试电脑已编辑", ip = "192.0.2.10", terminalBoxId = restoredBox.Id(), onuDeviceId = restoredOnuId, portNumber = 2 });
    var boxTopology = await Ok("GET", $"/api/v2/terminal-boxes/{restoredBox.Id()}/topology");
    Check(boxTopology["onus"]?.AsArray().SelectMany(o => o!["ports"]!.AsArray()).Any(p => p.Id("boundComputerId") == restoredComputer.Id() && p.Text("boundComputerName") == "恢复测试电脑已编辑") == true, "终端箱工作舱显示挂接的登记电脑");
    var assetPage = await Ok("GET", "/api/v2/assets/search?type=computer&pageSize=1");
    Check(assetPage.Id("total") == 1 && assetPage["items"]![0]!["ref"].Id() == restoredComputer.Id(), "统一资产查询保留现有电脑编号");
    var assetSummary = await Ok("GET", "/api/v2/assets/summary?type=computer");
    Check(assetSummary.Id("total") == 1 && assetSummary.Id("online") + assetSummary.Id("offline") + assetSummary.Id("unknown") == 1, "统计覆盖完整筛选范围且状态口径一致");
    var assetContext = await Ok("GET", $"/api/v2/assets/computer/{restoredComputer.Id()}/context");
    Check(assetContext["relatedAssets"]!.AsArray().Any(a => a!["ref"].Text("type") == "onu" && a["ref"].Id() == restoredOnuId), "资产上下文关联 ONU");
    Check(assetContext["relatedAssets"]!.AsArray().Any(a => a!["ref"].Text("type") == "ip-pool" && a["ref"].Id() == restoredPool.Id()), "资产上下文关联所在 IP 池");
    await Ok("GET", $"/api/v2/assets/ip-pool/{restoredPool.Id()}/context");
    await Ok("GET", $"/api/v2/assets/network-device/{restoredNetwork.Id()}/context");
    var batchA = await Ok("POST", "/api/v2/computers", new { name = "批量甲", ip = "192.0.2.12" });
    var batchB = await Ok("POST", "/api/v2/computers", new { name = "批量乙", ip = "192.0.2.13" });
    var batchConflict = await Send("POST", "/api/v2/computers/batch-update", new { computerIds = new[] { batchA.Id(), batchB.Id() }, updateNetworkBinding = true, terminalBoxId = restoredBox.Id(), onuDeviceId = restoredOnuId, startPortNumber = 1 });
    Check(batchConflict.Status == HttpStatusCode.Conflict, "批量电脑绑定检测占用端口");
    var rolledBack = await Ok("GET", $"/api/v2/computers/{batchA.Id()}");
    Check(rolledBack["onuDeviceId"] is null, "批量中后续冲突回滚先前写入");
    await Ok("POST", "/api/v2/computers/batch-update", new { computerIds = new[] { batchA.Id(), batchB.Id() }, updateNetworkBinding = true, terminalBoxId = restoredBox.Id(), onuDeviceId = restoredOnuId, startPortNumber = 3 });
    var summaryAfter = await Ok("GET", "/api/v2/assets/summary?type=computer");
    var oneItem = await Ok("GET", "/api/v2/assets/search?type=computer&pageSize=1");
    Check(summaryAfter.Id("total") == 3 && oneItem["items"]!.AsArray().Count == 1 && oneItem.Id("total") == 3, "全量统计不受列表分页影响");
    var concurrentIp = await Task.WhenAll(new[] { "并发甲", "并发乙" }.Select(name => Send("POST", "/api/v2/computers", new { name, ip = "192.0.2.14" })));
    Check(concurrentIp.Count(result => result.Status == HttpStatusCode.Created || result.Status == HttpStatusCode.OK) == 1 && concurrentIp.Count(result => result.Status == HttpStatusCode.Conflict) == 1, "并发登记同一 IP 只允许一次成功");
    var portA = await Ok("POST", $"/api/v2/devices/network_device/{restoredNetwork.Id()}/ports", new { portName = "测试口一" });
    var portB = await Ok("POST", $"/api/v2/devices/network_device/{restoredNetwork.Id()}/ports", new { portName = "测试口二" });
    var portC = await Ok("POST", $"/api/v2/devices/network_device/{restoredNetwork.Id()}/ports", new { portName = "测试口三" });
    await Ok("POST", "/api/v2/device-links/connect", new { localPortId = portA.Id(), peerPortId = portB.Id() });
    Check((await Send("POST", "/api/v2/device-links/connect", new { localPortId = portA.Id(), peerPortId = portC.Id() })).Status == HttpStatusCode.Conflict, "占用物理端口默认拒绝替换");
    await Ok("POST", "/api/v2/device-links/connect", new { localPortId = portA.Id(), peerPortId = portC.Id(), replaceExisting = true });
    await Ok("DELETE", $"/api/v2/computers/{batchA.Id()}");
    await Ok("DELETE", $"/api/v2/computers/{batchB.Id()}");
    await Ok("POST", "/api/v2/pon/bindings/auto-discover");
    foreach (var endpoint in new[] { "/api/v2/computers", "/api/v2/pon/onus", "/api/v2/terminal-boxes", "/api/v2/ip-pools", "/api/v2/network-devices", "/api/v2/assets/search", "/api/v2/assets/summary", $"/api/v2/assets/computer/{restoredComputer.Id()}/context" })
    {
        using var anonymous = new HttpClient { BaseAddress = new Uri(apiBase) };
        Check((await Send("GET", endpoint, http: anonymous)).Status == HttpStatusCode.Unauthorized, "恢复资产接口拒绝匿名访问：" + endpoint);
    }
    await Ok("DELETE", $"/api/v2/computers/{restoredComputer.Id()}");
    await Ok("DELETE", $"/api/v2/network-devices/{restoredNetwork.Id()}");
    await Ok("DELETE", $"/api/v2/ip-pools/{restoredPool.Id()}");
    await Ok("DELETE", $"/api/v2/terminal-boxes/{restoredBox.Id()}");
    foreach (var endpoint in new[] { "/health", "/api/v2/auth/me", "/api/v2/dashboard", "/api/v2/system", "/api/v2/users", "/api/v2/roles", "/api/v2/permissions", "/api/v2/organization", "/api/v2/devices", "/api/v2/channels", "/api/v2/alarms", "/api/v2/exports", "/api/v2/layouts", "/api/v2/favorites", "/api/v2/audit", "/api/v2/sessions", "/api/v2/settings", "/api/v2/releases", "/api/v2/openapi/v2.json" }) await Ok("GET", endpoint);
    var first = await Ok("POST", "/api/v2/devices", new { name = "测试录像机甲" + stamp, host = "test-a-" + stamp + ".example", port = 8000, username = "test", password = "device-test-password", enabled = true });
    var second = await Ok("POST", "/api/v2/devices", new { name = "测试录像机乙" + stamp, host = "test-b-" + stamp + ".example", port = 8000, username = "test", password = "device-test-password", enabled = true });
    await Ok("POST", $"/api/v2/devices/{first.Id()}/sync");
    await Ok("POST", $"/api/v2/devices/{second.Id()}/sync");
    var firstChannels = await Ok("GET", $"/api/v2/channels?deviceId={first.Id()}");
    var secondChannels = await Ok("GET", $"/api/v2/channels?deviceId={second.Id()}");
    var channelA = firstChannels["items"]![0].Id();
    var channelB = secondChannels["items"]![0].Id();
    Check(channelA != channelB && firstChannels["items"]![0].Id("deviceChannel") == secondChannels["items"]![0].Id("deviceChannel"), "两台录像机相同通道号使用不同全局 ID");
    Check(firstChannels["items"]!.AsArray().All(c => c?.AsObject().ContainsKey("password") == false) && !firstChannels.ToJsonString().Contains("device-test-password", StringComparison.Ordinal), "通道响应没有设备密码，只包含是否已配置密码");
    var role = await Ok("POST", "/api/v2/roles", new { name = "测试值班角色" + stamp, code = "test" + stamp, status = "active", permissionCodes = new[] { "device.read", "channel.read", "area.read", "live.view", "playback.view", "ptz.control", "alarm.read", "alarm.ack", "export.create" } });
    var user = await Ok("POST", "/api/v2/users", new { username = "test" + stamp, password = "Test-password-2026!", displayName = "集成测试值班员", phone = "", status = "active", roleIds = new[] { role.Id() } });
    var userClient = new HttpClient { BaseAddress = new Uri(apiBase) };
    var userLogin = await Ok("POST", "/api/v2/auth/login", new { username = "test" + stamp, password = "Test-password-2026!", clientType = "desktop", clientVersion = "2.0.0-test" }, userClient);
    userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userLogin.Text("accessToken"));
    foreach (var endpoint in new[] { "/api/v2/computers", "/api/v2/pon/onus", "/api/v2/terminal-boxes", "/api/v2/ip-pools", "/api/v2/network-devices" })
        Check((await Send("GET", endpoint, http: userClient)).Status == HttpStatusCode.Forbidden, "缺少资产功能权限的账号被拒绝：" + endpoint);
    var deniedChannels = await Ok("GET", "/api/v2/channels", http: userClient);
    Check(deniedChannels.Id("total") == 0, "普通账号未配置范围默认没有通道");
    var deniedAssets = await Ok("GET", "/api/v2/assets/search", http: userClient);
    Check(deniedAssets.Id("total") == 0, "统一聚合不泄露未授权录像机及通道");
    var deniedLive = await Send("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native" }, userClient);
    Check(deniedLive.Status == HttpStatusCode.Forbidden, "无范围账号不能建立媒体会话");
    await Ok("PUT", $"/api/v2/scopes/user/{user.Id()}", new { allChannels = false, scopes = new[] { new { type = "channel", id = channelA } } });
    var allowedChannels = await Ok("GET", "/api/v2/channels", http: userClient);
    Check(allowedChannels.Id("total") == 1 && allowedChannels["items"]![0].Id() == channelA, "账号只读取授权设备的全局通道");
    var scopedAssets = await Ok("GET", "/api/v2/assets/search?type=channel", http: userClient);
    Check(scopedAssets.Id("total") == 1 && scopedAssets["items"]![0]!["ref"].Id() == channelA, "资产聚合逐类应用通道数据权限");
    Check((await Send("GET", $"/api/v2/assets/channel/{channelB}/context", http: userClient)).Status == HttpStatusCode.NotFound, "资产上下文不能越权读取同号不同设备通道");
    var crossDevice = await Send("POST", "/api/v2/live-sessions", new { channelId = channelB, streamType = 2, profile = "native" }, userClient);
    Check(crossDevice.Status == HttpStatusCode.Forbidden, "同号不同设备通道不能越权播放");
    var liveA = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native" }, userClient);
    var liveOther = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native" });
    var consumerA = Guid.NewGuid();
    var consumerB = Guid.NewGuid();
    var slotA = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native", consumerId = consumerA });
    var slotB = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native", consumerId = consumerB });
    Check(sessions.ContainsKey(Guid.Parse(slotA.Text("id"))) && sessions.ContainsKey(Guid.Parse(slotB.Text("id"))), "同一登录的不同播放格位独立建立会话");
    var slotReplacement = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelB, streamType = 2, profile = "native", consumerId = consumerA });
    Check(!sessions.ContainsKey(Guid.Parse(slotA.Text("id"))) && sessions.ContainsKey(Guid.Parse(slotB.Text("id"))), "同格位换台释放旧会话且保留其他窗口");
    await Ok("DELETE", $"/api/v2/live-sessions/{slotReplacement.Text("id")}");
    await Ok("POST", $"/api/v2/live-sessions/{slotB.Text("id")}/renew");
    Check(sessions.ContainsKey(Guid.Parse(slotB.Text("id"))), "关闭一个窗口后另一个窗口可独立续期");
    await Ok("DELETE", $"/api/v2/live-sessions/{slotB.Text("id")}");
    Check(liveA.Text("httpFlvUrl").Split('?')[0] == liveOther.Text("httpFlvUrl").Split('?')[0], "同设备通道共享流路径但使用不同授权令牌");
    Check(liveA.Text("httpFlvUrl") != liveOther.Text("httpFlvUrl"), "每个媒体会话独立令牌");
    await Ok("POST", $"/api/v2/live-sessions/{liveA.Text("id")}/renew", http: userClient);
    await Ok("POST", $"/api/v2/channels/{channelA}/ptz", new { command = "left", speed = 2 }, userClient);
    var busyPtz = await Send("POST", $"/api/v2/channels/{channelA}/ptz", new { command = "right", speed = 2 });
    Check(busyPtz.Status == HttpStatusCode.Conflict, "云台租约阻止第二会话抢占");
    await Ok("PUT", $"/api/v2/scopes/user/{user.Id()}", new { allChannels = false, scopes = Array.Empty<object>() });
    Check(!sessions.ContainsKey(Guid.Parse(liveA.Text("id"))) && sessions.ContainsKey(Guid.Parse(liveOther.Text("id"))), "撤权停止目标媒体且保留其他观看者会话");
    Check(ptz.TryGetValue(first.Id(), out var moving) && !moving, "撤权主动停止云台");
    await Ok("DELETE", $"/api/v2/live-sessions/{liveOther.Text("id")}");
    await Ok("DELETE", $"/api/v2/live-sessions/{liveOther.Text("id")}");
    await Ok("PUT", "/api/v2/favorites", new { channelIds = new[] { channelA, channelB } });
    var favorites = await Ok("GET", "/api/v2/favorites");
    Check(favorites.AsArray().Count == 2, "个人收藏按全局通道保存");
    var layout = await Ok("POST", "/api/v2/layouts", new { name = "测试轮巡" + stamp, kind = "patrol", shared = true, layout = 4, intervalSeconds = 30, channelIds = new[] { channelA, channelB } });
    await Ok("GET", "/api/v2/layouts");
    var invalidLayout = await Send("POST", "/api/v2/layouts", new { name = "非法轮巡", kind = "patrol", shared = false, layout = 4, intervalSeconds = 1, channelIds = new[] { channelA } });
    Check(invalidLayout.Status == HttpStatusCode.BadRequest, "拒绝低于最小间隔的轮巡");
    var workshop = await Ok("POST", "/api/v2/organization/workshops", new { name = "测试车间", code = "w" + stamp, status = "active" });
    var area = await Ok("POST", "/api/v2/organization/areas", new { name = "测试区域", code = "a" + stamp, status = "active", parentId = workshop.Id() });
    var unit = await Ok("POST", "/api/v2/organization/units", new { name = "测试机组", code = "u" + stamp, status = "active", parentId = area.Id() });
    await Ok("PUT", "/api/v2/channels/assignment", new { channelIds = new[] { channelA }, unitId = unit.Id() });
    var referenced = await Send("DELETE", $"/api/v2/organization/units/{unit.Id()}");
    Check(referenced.Status == HttpStatusCode.Conflict, "有通道引用的机组不可删除");
    await using var data = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("PLATFORM_DATABASE_URL")!);
    var db = new Database(data);
    var scopeRole = await Ok("POST", "/api/v2/roles", new { name = "资产范围回归角色" + stamp, code = "asset" + stamp, status = "active",
        permissionCodes = new[] { "area.read", "channel.read", "computer.read", "computer.manage", "pon.read", "pon.manage", "ip.read", "ip.manage", "network_device.read", "network_device.manage", "map.read", "map.manage", "device_port.manage" } });
    var scopeUser = await Ok("POST", "/api/v2/users", new { username = "asset" + stamp, password = "Test-password-2026!", status = "active", roleIds = new[] { scopeRole.Id() } });
    await Ok("PUT", $"/api/v2/scopes/user/{scopeUser.Id()}", new { allChannels = false, scopes = new[] { new { type = "area", id = area.Id() } } });
    using var scopeClient = new HttpClient { BaseAddress = new Uri(apiBase) };
    var scopeLogin = await Ok("POST", "/api/v2/auth/login", new { username = "asset" + stamp, password = "Test-password-2026!", clientType = "desktop", clientVersion = "2.0.0-test" }, scopeClient);
    scopeClient.DefaultRequestHeaders.Authorization = new("Bearer", scopeLogin.Text("accessToken"));
    var localBox = await Ok("POST", "/api/v2/terminal-boxes", new { code = "LOCAL-" + stamp, name = "授权箱", unitId = unit.Id() });
    var foreignBox = await Ok("POST", "/api/v2/terminal-boxes", new { code = "FOREIGN-" + stamp, name = "范围外箱" });
    var localComputer = await Ok("POST", "/api/v2/computers", new { name = "授权电脑", ip = "192.0.2.70", unitId = unit.Id(), owner = "检索口径验证", terminalBoxId = localBox.Id() });
    var foreignComputer = await Ok("POST", "/api/v2/computers", new { name = "范围外电脑", ip = "192.0.2.71", terminalBoxId = foreignBox.Id() });
    var scopedComputers = await Ok("GET", "/api/v2/computers?search=检索口径验证", http: scopeClient);
    var scopedSummary = await Ok("GET", "/api/v2/assets/summary?type=computer&search=检索口径验证", http: scopeClient);
    Check(scopedComputers.Id("total") == 1 && scopedSummary.Id("total") == 1, "电脑检索与完整筛选统计共享负责人搜索和组织口径");
    Check((await Ok("GET", "/api/v2/terminal-boxes", http: scopeClient)).AsArray().All(b => b.Id() != foreignBox.Id()), "旧终端箱台账不泄露范围外记录");
    Check((await Send("GET", $"/api/v2/computers/{foreignComputer.Id()}", http: scopeClient)).Status == HttpStatusCode.NotFound, "旧单资产查询检查数据范围");
    Check((await Send("PUT", $"/api/v2/computers/{localComputer.Id()}", new { name = "越权移动", ip = "192.0.2.70", unitId = unit.Id(), terminalBoxId = foreignBox.Id() }, scopeClient)).Status == HttpStatusCode.Forbidden, "授权电脑不能绑定范围外终端箱");
    Check((await Send("POST", "/api/v2/computers/batch-update", new { computerIds = new[] { localComputer.Id(), foreignComputer.Id() }, updateOrganization = true, unitId = unit.Id() }, scopeClient)).Status == HttpStatusCode.Forbidden, "批量中包含越权电脑时整批拒绝");
    Check((await Ok("GET", $"/api/v2/computers/{localComputer.Id()}")).Text("name") == "授权电脑", "拒绝越权操作后原记录保持完整");
    var redacted = await Ok("GET", "/api/v2/ip-pools/check-ip?ip=192.0.2.71", http: scopeClient);
    Check(redacted.Flag("occupied") && !redacted.ToJsonString().Contains("范围外电脑"), "IP 冲突保留占用事实并脱敏范围外资产");
    Check((await Send("POST", "/api/v2/pon/sync", http: scopeClient)).Status == HttpStatusCode.Forbidden, "受限光网维护账号不能触发全网同步");
    var statsScope = await Ok("GET", "/api/v2/organization/stats", http: scopeClient);
    Check(statsScope.Id("totalComputers") == 1 && statsScope.Id("totalTerminalBoxes") == 1, "组织统计只计授权资产");
    var sceneId = (await db.OneAsync("select id from map_scenes order by id limit 1")).Id();
    await Ok("POST", $"/api/v2/digital-twin/scenes/{sceneId}/coordinates", new { targetType = "computer", targetId = localComputer.Id(), xPct = 0.3, yPct = 0.3 });
    await Ok("POST", $"/api/v2/digital-twin/scenes/{sceneId}/coordinates", new { targetType = "computer", targetId = foreignComputer.Id(), xPct = 0.6, yPct = 0.6 });
    var scopedMap = await Ok("GET", $"/api/v2/digital-twin/scenes/{sceneId}", http: scopeClient);
    Check(scopedMap["nodes"]!.AsArray().All(n => n.Text("name") != "范围外电脑") && scopedMap["stats"].Id("computerCount") == 1, "数字孪生节点及统计逐类应用数据范围");
    Check((await Send("POST", $"/api/v2/digital-twin/scenes/{sceneId}/coordinates/batch", new { items = new[] {
        new { targetType = "computer", targetId = localComputer.Id(), xPct = 0.2, yPct = 0.2 }, new { targetType = "computer", targetId = foreignComputer.Id(), xPct = 0.9, yPct = 0.9 } } }, scopeClient)).Status == HttpStatusCode.Forbidden, "地图批量标定越权整批回滚");
    var mapAfter = await Ok("GET", $"/api/v2/digital-twin/scenes/{sceneId}");
    Check(mapAfter["nodes"]!.AsArray().Single(n => n.Id("targetId") == localComputer.Id() && n.Text("targetType") == "computer")!["xPct"]!.GetValue<decimal>() == 0.3m, "失败地图批量未提交前一节点坐标");
    await Ok("POST", "/api/v2/auth/logout", http: scopeClient);
    var alarm = await db.OneAsync("insert into alarm_events(device_id,channel_id,source_id,event_type,occurred_at) values(@device,@channel,@source,'alarm.motion',now()) returning id", new { device = first.Id(), channel = channelA, source = stamp });
    await Ok("GET", $"/api/v2/alarms/{alarm.Id()}");
    await Ok("POST", $"/api/v2/alarms/{alarm.Id()}/actions", new { action = "claim", note = "开始处理" });
    await Ok("POST", $"/api/v2/alarms/{alarm.Id()}/actions", new { action = "close", note = "测试处理完毕" });
    var closed = await Ok("GET", $"/api/v2/alarms/{alarm.Id()}");
    Check(closed.Text("state") == "closed" && !closed.Flag("recovered"), "人工关闭与设备恢复状态分离");
    await Ok("POST", $"/api/v2/alarms/{alarm.Id()}/actions", new { action = "reopen", note = "复查" });
    var invalidRange = await Send("POST", "/api/v2/exports", new { channelId = channelA, start = DateTimeOffset.UtcNow.AddDays(-2), end = DateTimeOffset.UtcNow });
    Check(invalidRange.Status == HttpStatusCode.BadRequest, "导出拒绝超过24小时范围");
    var export = await Ok("POST", "/api/v2/exports", new { channelId = channelA, start = DateTimeOffset.UtcNow.AddMinutes(-10), end = DateTimeOffset.UtcNow.AddMinutes(-5) });
    await Ok("GET", "/api/v2/exports");
    await Ok("POST", $"/api/v2/exports/{export.Text("id")}/cancel");
    await Ok("POST", $"/api/v2/exports/{export.Text("id")}/retry");
    await Ok("POST", $"/api/v2/exports/{export.Text("id")}/cancel");
    var refreshes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send("POST", "/api/v2/auth/refresh")));
    Check(refreshes.All(r => r.Status == HttpStatusCode.OK && r.Json.Text("accessToken") == login.Text("accessToken")), "并发会话续期不互相撤销");
    var csrfClient = new HttpClient { BaseAddress = new Uri(apiBase) };
    csrfClient.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
    var noCsrf = await Send("POST", "/api/v2/auth/login", new { username = "admin", password = "none", clientType = "web" }, csrfClient);
    Check(noCsrf.Status == HttpStatusCode.BadRequest && noCsrf.Json.Text("code") == "auth.csrf", "Web 登录必须通过请求防伪验证");
    var noHookKey = await Send("POST", "/internal/zlm/on-play", new { app = "live", stream = "none", @params = "token=none" });
    Check(noHookKey.Json.Id("code") == -1, "无内部密钥不能调用播放鉴权钩子");
    Check(Rules.AlarmTransition("closed", "reopen") == "new", "报警状态机支持重新打开");
    Console.WriteLine($"集成测试完成，共 {passed} 项通过。测试设备使用模拟适配器，数据库及 API 为真实实现。");
}
finally { await adapter.StopAsync(); }
