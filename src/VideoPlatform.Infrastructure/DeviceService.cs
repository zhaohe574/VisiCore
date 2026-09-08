using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed class DeviceAdapter(HttpClient client, PlatformOptions options, Database? db = null, ILogger<DeviceAdapter>? logger = null) : IDeviceAdapter
{
    private sealed record CachedRoute(string PluginId, string EndpointUrl, string Status, string Name, DateTimeOffset ExpiresAt);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, CachedRoute> _cache = new();

    public static void InvalidateRouteCache(long? deviceId = null)
    {
        if (deviceId.HasValue) _cache.TryRemove(deviceId.Value, out _);
        else _cache.Clear();
    }

    public void InvalidateCache(long? deviceId = null) => InvalidateRouteCache(deviceId);

    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path, @"^/internal/devices/(\d+)");
        if (match.Success && long.TryParse(match.Groups[1].Value, out var deviceId) && db is not null)
        {
            var route = await ResolveRouteAsync(deviceId, ct);
            if (route is not null)
            {
                if (route.Status == "disabled" && method != HttpMethod.Delete)
                {
                    throw new PlatformException(409, "plugin.disabled", $"设备驱动【{route.Name}】已停用，请先在插件管理中启用该驱动");
                }
                return await ForwardAsync(route.EndpointUrl, method, path, body, ct);
            }
        }
        else if (path == "/internal/sessions" && db is not null)
        {
            return await BroadcastSessionsAsync(ct);
        }

        return await ForwardAsync(options.AdapterUrl, method, path, body, ct);
    }

    private async Task<CachedRoute?> ResolveRouteAsync(long deviceId, CancellationToken ct)
    {
        if (_cache.TryGetValue(deviceId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached;

        try
        {
            var row = await db!.OneAsync(@"
                select d.plugin_id, coalesce(p.endpoint_url, @fallbackUrl) as endpoint_url,
                       coalesce(p.status, 'active') as status, coalesce(p.name, '海康威视网络设备驱动') as name
                from devices d
                left join device_plugins p on d.plugin_id = p.id
                where d.id = @deviceId",
                new { deviceId, fallbackUrl = options.AdapterUrl }, ct);

            if (row is not null)
            {
                var route = new CachedRoute(
                    row.Text("pluginId", "hikvision"),
                    row.Text("endpointUrl", options.AdapterUrl),
                    row.Text("status", "active"),
                    row.Text("name", "海康威视网络设备驱动"),
                    DateTimeOffset.UtcNow.AddSeconds(30)
                );
                _cache[deviceId] = route;
                return route;
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "解析设备 {DeviceId} 插件路由失败，回退至默认适配端点", deviceId);
        }
        return null;
    }

    private async Task<JsonNode?> BroadcastSessionsAsync(CancellationToken ct)
    {
        var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var plugins = await db!.QueryAsync("select endpoint_url from device_plugins where status = 'active'", ct: ct);
            foreach (var p in plugins)
            {
                var url = p.Text("endpointUrl");
                if (!string.IsNullOrWhiteSpace(url)) endpoints.Add(url);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "查询活动插件列表失败");
        }
        if (endpoints.Count == 0) endpoints.Add(options.AdapterUrl);

        var allLive = new JsonArray();
        var allPlayback = new JsonArray();

        foreach (var endpoint in endpoints)
        {
            try
            {
                var res = await ForwardAsync(endpoint, HttpMethod.Get, "/internal/sessions", null, ct);
                if (res is JsonObject obj)
                {
                    if (obj["live"] is JsonArray liveArr)
                    {
                        foreach (var item in liveArr)
                            if (item is not null) allLive.Add(item.DeepClone());
                    }
                    if (obj["playback"] is JsonArray pbArr)
                    {
                        foreach (var item in pbArr)
                            if (item is not null) allPlayback.Add(item.DeepClone());
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "向插件端点 {Endpoint} 查询会话失败", endpoint);
            }
        }

        return new JsonObject
        {
            ["live"] = allLive,
            ["playback"] = allPlayback
        };
    }

    private async Task<JsonNode?> ForwardAsync(string baseUrl, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
        request.Headers.Add("X-Adapter-Key", options.AdapterKey);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonDefaults.Options);
        try
        {
            using var response = await client.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                // 适配器错误可能包含设备 URL，不能将上游原文返回给客户端。
                throw new PlatformException(response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 502,
                    "adapter.failed", response.StatusCode == System.Net.HttpStatusCode.NotFound ? "设备或媒体会话不存在" : "设备操作未成功，请检查设备状态和适配服务日志");
            }
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (HttpRequestException) { throw new PlatformException(503, "adapter.unavailable", "设备适配服务暂时不可用"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new PlatformException(504, "adapter.timeout", "设备操作超时，请稍后重试"); }
    }
}

public sealed class DeviceService(Database db, IDeviceAdapter adapter, SecretStore secrets, AuditStore audit, ILogger<DeviceService> logger)
{
    public async Task RegisterAsync(long id, CancellationToken ct = default)
    {
        var device = await db.OneAsync("select * from devices where id=@id", new { id }, ct)
            ?? throw new PlatformException(404, "device.missing", "录像机不存在");
        await adapter.SendAsync(HttpMethod.Put, $"/internal/devices/{id}", new
        {
            host = device.Text("host"), port = (int)device.Id("port"), username = device.Text("username"),
            password = secrets.Unprotect(device.Text("passwordCipher")), enabled = device.Flag("enabled")
        }, ct);
    }

    public async Task<JsonObject> SyncAsync(long id, CancellationToken ct = default)
    {
        try
        {
            await RegisterAsync(id, ct);
            var snapshot = await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{id}/sync", ct: ct) as JsonObject
                ?? throw new PlatformException(502, "device.snapshot", "设备未返回有效通道快照");
            Rules.Require(snapshot["channels"] is JsonArray, "设备返回的通道快照格式无效", "device.snapshot", 502);
            var channels = (JsonArray)snapshot["channels"]!;
            await db.TransactionAsync(async tx =>
            {
                var previous = await tx.OneAsync("select status,model,serial_number from devices where id=@id for update", new { id }, ct);
                var previousChannels = await tx.QueryAsync("select device_channel,name,model,status,ptz_capable,codec from channels where device_id=@id order by device_channel", new { id }, ct);
                var seen = new List<int>();
                foreach (var channel in channels.OfType<JsonObject>())
                {
                    var number = (int)channel.Id("channel");
                    if (number <= 0) continue;
                    seen.Add(number);
                    await tx.ExecuteAsync("""
                        insert into channels(device_id,device_channel,name,model,status,ptz_capable,codec)
                        values(@id,@number,@name,@model,@status,@ptz,@codec)
                        on conflict(device_id,device_channel) do update set name=excluded.name,model=excluded.model,status=excluded.status,ptz_capable=excluded.ptz_capable,codec=excluded.codec,updated_at=now()
                        """, new { id, number, name = channel.Text("name", $"通道 {number}"), model = channel["model"]?.ToString(), status = channel.Flag("online") ? "online" : "offline", ptz = channel.Flag("ptzCapable"), codec = channel["codec"]?.ToString() }, ct);
                }
                await tx.ExecuteAsync("update channels set status='disabled',updated_at=now() where device_id=@id and not(device_channel=any(@seen))", new { id, seen = seen.ToArray() }, ct);
                await tx.ExecuteAsync("update devices set status='online',last_seen_at=now(),sync_error=null,model=@model,serial_number=@serial where id=@id", new { id, model = snapshot["device"]?["model"]?.ToString(), serial = snapshot["device"]?["serialNumber"]?.ToString() }, ct);
                var currentChannels = await tx.QueryAsync("select device_channel,name,model,status,ptz_capable,codec from channels where device_id=@id order by device_channel", new { id }, ct);
                if (previous.Text("status") != "online" || JsonSerializer.Serialize(previousChannels) != JsonSerializer.Serialize(currentChannels))
                    await tx.ExecuteAsync("insert into outbox(kind,resource_id,device_id) values('device.changed',@resource,@id)", new { resource = id.ToString(), id }, ct);
                return true;
            }, ct);
            return snapshot;
        }
        catch (Exception ex) when (ex is PlatformException or JsonException)
        {
            var changed = await db.ExecuteAsync("update devices set status='offline',sync_error=@error where id=@id and status<>'offline'", new { id, error = ex.Message }, ct);
            await db.ExecuteAsync("update channels set status='offline' where device_id=@id and status not in('disabled','offline')", new { id }, ct);
            if (changed > 0) await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id, ct: ct);
            logger.LogWarning("录像机 {DeviceId} 同步失败：{Reason}", id, ex.Message);
            throw;
        }
    }
}
