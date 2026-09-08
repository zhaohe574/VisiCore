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
    Check(!firstChannels.ToJsonString().Contains("password", StringComparison.OrdinalIgnoreCase), "通道响应没有设备密码");
    var role = await Ok("POST", "/api/v2/roles", new { name = "测试值班角色" + stamp, code = "test" + stamp, status = "active", permissionCodes = new[] { "device.read", "channel.read", "area.read", "live.view", "playback.view", "ptz.control", "alarm.read", "alarm.ack", "export.create" } });
    var user = await Ok("POST", "/api/v2/users", new { username = "test" + stamp, password = "Test-password-2026!", displayName = "集成测试值班员", phone = "", status = "active", roleIds = new[] { role.Id() } });
    var userClient = new HttpClient { BaseAddress = new Uri(apiBase) };
    var userLogin = await Ok("POST", "/api/v2/auth/login", new { username = "test" + stamp, password = "Test-password-2026!", clientType = "desktop", clientVersion = "2.0.0-test" }, userClient);
    userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userLogin.Text("accessToken"));
    var deniedChannels = await Ok("GET", "/api/v2/channels", http: userClient);
    Check(deniedChannels.Id("total") == 0, "普通账号未配置范围默认没有通道");
    var deniedLive = await Send("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native" }, userClient);
    Check(deniedLive.Status == HttpStatusCode.Forbidden, "无范围账号不能建立媒体会话");
    await Ok("PUT", $"/api/v2/scopes/user/{user.Id()}", new { allChannels = false, scopes = new[] { new { type = "channel", id = channelA } } });
    var allowedChannels = await Ok("GET", "/api/v2/channels", http: userClient);
    Check(allowedChannels.Id("total") == 1 && allowedChannels["items"]![0].Id() == channelA, "账号只读取授权设备的全局通道");
    var crossDevice = await Send("POST", "/api/v2/live-sessions", new { channelId = channelB, streamType = 2, profile = "native" }, userClient);
    Check(crossDevice.Status == HttpStatusCode.Forbidden, "同号不同设备通道不能越权播放");
    var liveA = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native" }, userClient);
    var liveOther = await Ok("POST", "/api/v2/live-sessions", new { channelId = channelA, streamType = 2, profile = "native" });
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
