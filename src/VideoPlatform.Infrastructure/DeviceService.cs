using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure.Adapter;

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
                if (PluginIds.IsInProcess(route.PluginId))
                {
                    if (path.Contains("/events"))
                    {
                        return new JsonObject
                        {
                            ["items"] = new JsonArray(),
                            ["nextCursor"] = "0:0"
                        };
                    }
                    return new JsonObject();
                }
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
            var plugins = await db!.QueryAsync($"select endpoint_url from device_plugins where status = 'active' and id <> '{PluginIds.EsightPon}'", ct: ct);
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

        string? bootId = null;
        var allDevices = new JsonArray();
        var allLive = new JsonArray();
        var allPlayback = new JsonArray();
        var allExports = new JsonArray();

        var seenDevices = new HashSet<long>();
        var seenLive = new HashSet<(long DeviceId, string SessionId)>();
        var seenPlayback = new HashSet<(long DeviceId, string SessionId)>();
        var seenExports = new HashSet<(long DeviceId, string JobId)>();

        foreach (var endpoint in endpoints)
        {
            try
            {
                var res = await ForwardAsync(endpoint, HttpMethod.Get, "/internal/sessions", null, ct);
                if (res is JsonObject obj)
                {
                    if (bootId is null && !string.IsNullOrWhiteSpace(obj.Text("bootId")))
                        bootId = obj.Text("bootId");
                    if (obj["devices"] is JsonArray devArr)
                    {
                        foreach (var item in devArr)
                        {
                            if (item is JsonObject d)
                            {
                                var devId = d.Id("deviceId") > 0 ? d.Id("deviceId") : d.Id("id");
                                if (devId > 0 && seenDevices.Add(devId))
                                    allDevices.Add(item.DeepClone());
                            }
                        }
                    }
                    if (obj["live"] is JsonArray liveArr)
                    {
                        foreach (var item in liveArr)
                        {
                            if (item is JsonObject l)
                            {
                                var devId = l.Id("deviceId");
                                var sid = l.Text("sessionId");
                                if (string.IsNullOrWhiteSpace(sid)) sid = l.Text("id");
                                if (devId > 0 && !string.IsNullOrWhiteSpace(sid) && seenLive.Add((devId, sid)))
                                    allLive.Add(item.DeepClone());
                            }
                        }
                    }
                    if (obj["playback"] is JsonArray pbArr)
                    {
                        foreach (var item in pbArr)
                        {
                            if (item is JsonObject p)
                            {
                                var devId = p.Id("deviceId");
                                var sid = p.Text("sessionId");
                                if (string.IsNullOrWhiteSpace(sid)) sid = p.Text("id");
                                if (devId > 0 && !string.IsNullOrWhiteSpace(sid) && seenPlayback.Add((devId, sid)))
                                    allPlayback.Add(item.DeepClone());
                            }
                        }
                    }
                    if (obj["exports"] is JsonArray expArr)
                    {
                        foreach (var item in expArr)
                        {
                            if (item is JsonObject e)
                            {
                                var devId = e.Id("deviceId");
                                var jid = e.Text("jobId");
                                if (string.IsNullOrWhiteSpace(jid)) jid = e.Text("id");
                                if (devId > 0 && !string.IsNullOrWhiteSpace(jid) && seenExports.Add((devId, jid)))
                                    allExports.Add(item.DeepClone());
                            }
                        }
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
            ["bootId"] = bootId ?? Guid.NewGuid().ToString(),
            ["devices"] = allDevices,
            ["live"] = allLive,
            ["playback"] = allPlayback,
            ["exports"] = allExports
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
                var msg = response.StatusCode == System.Net.HttpStatusCode.NotFound ? "设备或媒体会话不存在" : "设备操作未成功，请检查设备状态和适配服务日志";
                var code = "adapter.failed";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        var errObj = JsonNode.Parse(text);
                        var serverMsg = errObj?["message"]?.ToString();
                        var serverCode = errObj?["code"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(serverCode)) code = serverCode;
                        if (!string.IsNullOrWhiteSpace(serverMsg) && !serverMsg.Contains("http://") && !serverMsg.Contains("rtsp://"))
                        {
                            msg = serverMsg;
                        }
                    }
                    catch { }
                }
                var status = response.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500) ? (int)response.StatusCode : 502;
                throw new PlatformException(status, code, msg);
            }
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (HttpRequestException) { throw new PlatformException(503, "adapter.unavailable", "设备适配服务暂时不可用"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new PlatformException(504, "adapter.timeout", "设备操作超时，请稍后重试"); }
    }
}

public sealed class DeviceService(
    Database db,
    IDeviceAdapter adapter,
    ICameraAdapter camera,
    SecretStore secrets,
    AuditStore audit,
    ILogger<DeviceService> logger,
    Pon.EsightPonClient? ponClient = null,
    Pon.PonService? ponService = null)
{
    public async Task RegisterAsync(long id, CancellationToken ct = default)
    {
        var device = await db.OneAsync("select * from devices where id=@id", new { id }, ct)
            ?? throw new PlatformException(404, "device.missing", "设备不存在");

        if (PluginIds.IsInProcess(device.Text("pluginId", device.Text("plugin_id"))))
        {
            if (ponClient is not null)
            {
                var host = device.Text("host");
                var port = (int)device.Id("port");
                var user = device.Text("username");
                var pwd = secrets.Unprotect(device.Text("passwordCipher"));
                ponClient.Configure($"https://{host}:{port}", user, pwd);
            }
            return;
        }

        await camera.RegisterAsync(id, new AdapterDeviceSpec(
            device.Text("host"),
            (int)device.Id("port"),
            device.Text("username"),
            secrets.Unprotect(device.Text("passwordCipher")),
            device.Flag("enabled")), ct);
    }

    public async Task<JsonObject> SyncAsync(long id, CancellationToken ct = default)
    {
        var device = await db.OneAsync("select * from devices where id=@id", new { id }, ct)
            ?? throw new PlatformException(404, "device.missing", "设备不存在");

        var pluginId = device.Text("pluginId", device.Text("plugin_id"));
        if (PluginIds.IsInProcess(pluginId))
        {
            try
            {
                var host = device.Text("host");
                var port = (int)device.Id("port");
                var user = device.Text("username");
                var pwd = secrets.Unprotect(device.Text("passwordCipher"));

                if (ponClient is not null)
                {
                    ponClient.Configure($"https://{host}:{port}", user, pwd);
                }

                var (ok, msg) = ponClient is not null
                    ? await ponClient.CheckHealthAsync(ct)
                    : (false, "未配置华为网管客户端");

                if (!ok)
                {
                    await db.ExecuteAsync("update devices set status='offline',sync_error=@msg where id=@id", new { id, msg }, ct);
                    await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id, ct: ct);
                    throw new PlatformException(503, "pon.offline", msg);
                }

                int onuCount = 0;
                if (ponService is not null)
                {
                    try
                    {
                        onuCount = await ponService.SyncFromEsightAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("华为网管连通正常，但同步 ONU 列表失败：{Reason}", ex.Message);
                    }
                }

                var serial = onuCount > 0 ? $"已同步 {onuCount} 台 ONU" : "全光网管通信正常";
                await db.ExecuteAsync(@"
                    update devices set
                        status='online',
                        last_seen_at=now(),
                        sync_error=null,
                        model='Huawei eSight/NeoSight POL',
                        serial_number=@serial
                    where id=@id", new { id, serial }, ct);

                await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id, ct: ct);

                return new JsonObject
                {
                    ["device"] = new JsonObject
                    {
                        ["model"] = "Huawei eSight/NeoSight POL",
                        ["serialNumber"] = serial,
                        ["status"] = "online"
                    },
                    ["channels"] = new JsonArray()
                };
            }
            catch (Exception ex) when (ex is not PlatformException)
            {
                logger.LogWarning("华为网管设备 {DeviceId} 同步异常：{Reason}", id, ex.Message);
                await db.ExecuteAsync("update devices set status='offline',sync_error=@error where id=@id", new { id, error = ex.Message }, ct);
                await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id, ct: ct);
                throw;
            }
        }

        try
        {
            await RegisterAsync(id, ct);
            var raw = await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{id}/sync", ct: ct) as JsonObject
                ?? throw new PlatformException(502, "device.snapshot", "设备未返回有效通道快照");
            // raw 原样返回给客户端（保持响应形状不变）；snapshot 是同一响应的类型化视图，
            // 仅供落库使用。Parse 在 channels 非数组时抛既有的 502/device.snapshot，
            // 与原先的 Rules.Require 完全等效。
            var snapshot = DeviceSnapshot.Parse(raw);
            await db.TransactionAsync(async tx =>
            {
                var previous = await tx.OneAsync("select status,model,serial_number from devices where id=@id for update", new { id }, ct);
                var previousChannels = await tx.QueryAsync("select device_channel,name,model,status,ptz_capable,codec from channels where device_id=@id order by device_channel", new { id }, ct);
                var seen = new List<int>();
                foreach (var channel in snapshot.Channels)
                {
                    var number = channel.Channel;
                    if (number <= 0) continue;
                    seen.Add(number);
                    // 沿用 Text("ip") 的空串语义：缺失时为 ""，不得变成 null 而改变落库值。
                    var ip = channel.Ip ?? "";
                    await tx.ExecuteAsync("""
                        insert into channels(device_id,device_channel,name,model,status,ptz_capable,codec,ip)
                        values(@id,@number,@name,@model,@status,@ptz,@codec,@ip)
                        on conflict(device_id,device_channel) do update set
                            name=case when coalesce(nullif(excluded.name, ''), '') <> '' and excluded.name not like '通道 %' then excluded.name else channels.name end,
                            model=coalesce(nullif(excluded.model, ''), channels.model),
                            status=excluded.status,
                            ptz_capable=excluded.ptz_capable,
                            codec=coalesce(nullif(excluded.codec, ''), channels.codec),
                            ip=coalesce(nullif(excluded.ip, ''), channels.ip),
                            updated_at=now()
                        """, new { id, number, name = channel.Name ?? $"通道 {number}", model = channel.Model, status = channel.Online ? "online" : "offline", ptz = channel.PtzCapable, codec = channel.Codec, ip }, ct);
                }
                await tx.ExecuteAsync("update channels set status='disabled',updated_at=now() where device_id=@id and not(device_channel=any(@seen))", new { id, seen = seen.ToArray() }, ct);
                await tx.ExecuteAsync("update devices set status='online',last_seen_at=now(),sync_error=null,model=@model,serial_number=@serial where id=@id", new { id, model = snapshot.Device.Model, serial = snapshot.Device.SerialNumber }, ct);
                var currentChannels = await tx.QueryAsync("select device_channel,name,model,status,ptz_capable,codec from channels where device_id=@id order by device_channel", new { id }, ct);

                // 记录摄像头离线与恢复变迁历史
                var prevStatusMap = previousChannels.ToDictionary(c => (int)c.Id("deviceChannel"), c => c.Text("status"));
                var channelIdsAndBindings = (await tx.QueryAsync(@"
                    select c.id as channel_id, c.device_channel, c.name, c.alias, c.ip, b.terminal_box_id, b.onu_device_id
                    from channels c
                    left join channel_network_bindings b on c.id = b.channel_id
                    where c.device_id = @id", new { id }, ct))
                    .ToDictionary(c => (int)c.Id("deviceChannel"));

                foreach (var curr in currentChannels)
                {
                    var chNum = (int)curr.Id("deviceChannel");
                    var currStatus = curr.Text("status");
                    prevStatusMap.TryGetValue(chNum, out var prevStatus);

                    if (channelIdsAndBindings.TryGetValue(chNum, out var meta))
                    {
                        var channelId = meta.Id("channelId");
                        var boxId = meta.Id("terminalBoxId") == 0 ? (long?)null : meta.Id("terminalBoxId");
                        var onuId = meta.Id("onuDeviceId") == 0 ? (long?)null : meta.Id("onuDeviceId");
                        var devName = !string.IsNullOrWhiteSpace(meta.Text("alias")) ? meta.Text("alias") : meta.Text("name");
                        var ip = meta.Text("ip");

                        if (prevStatus == "online" && currStatus == "offline")
                        {
                            await tx.ExecuteAsync(@"
                                insert into device_offline_history(
                                    device_type, terminal_box_id, onu_device_id, channel_id,
                                    device_name, device_identifier, event_type, down_time, reason
                                ) values (
                                    'camera', @boxId, @onuId, @channelId,
                                    @devName, @ip, 'offline', now(), '摄像头离线/通道推流中断'
                                )", new { boxId, onuId, channelId, devName, ip }, ct);
                        }
                        else if (prevStatus == "offline" && currStatus == "online")
                        {
                            await tx.ExecuteAsync(@"
                                update device_offline_history set
                                    up_time = now(),
                                    duration_seconds = greatest(0, extract(epoch from (now() - down_time))::int)
                                where channel_id = @channelId and up_time is null", new { channelId }, ct);
                        }
                    }
                }

                if (previous.Text("status") != "online" || JsonSerializer.Serialize(previousChannels) != JsonSerializer.Serialize(currentChannels))
                    await tx.ExecuteAsync("insert into outbox(kind,resource_id,device_id) values('device.changed',@resource,@id)", new { resource = id.ToString(), id }, ct);
                return true;
            }, ct);
            return raw;
        }
        catch (Exception ex) when (ex is PlatformException or JsonException)
        {
            var changed = await db.ExecuteAsync("update devices set status='offline',sync_error=@error where id=@id and (status<>'offline' or coalesce(sync_error,'')<>coalesce(@error,''))", new { id, error = ex.Message }, ct);
            await db.ExecuteAsync("update channels set status='offline' where device_id=@id and status not in('disabled','offline')", new { id }, ct);
            if (changed > 0) await audit.NotifyAsync("device.changed", id.ToString(), deviceId: id, ct: ct);
            logger.LogWarning("录像机 {DeviceId} 同步失败：{Reason}", id, ex.Message);
            throw;
        }
    }
}
