using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class PluginEndpoints
{
    public static void MapPluginEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("设备驱动插件");

        group.MapGet("/plugins", async (HttpContext context, Database db, AccessService access, IHttpClientFactory httpClients, PlatformOptions options) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.read");
            var rows = await db.QueryAsync(@"
                select p.id, p.name, p.vendor, p.version, p.description, p.status, 
                       p.endpoint_url, p.capabilities, p.config_schema, p.created_at, p.updated_at,
                       (select count(*) from devices where plugin_id = p.id) as device_count
                from device_plugins p
                order by p.created_at
            ");

            var client = httpClients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);

            var items = new List<object>();
            foreach (var row in rows)
            {
                var endpoint = row.Text("endpointUrl");
                var status = row.Text("status");
                string healthStatus = "unknown";
                if (status == "active" && !string.IsNullOrWhiteSpace(endpoint))
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint.TrimEnd('/') + "/health");
                        req.Headers.Add("X-Adapter-Key", options.AdapterKey);
                        using var res = await client.SendAsync(req, context.RequestAborted);
                        healthStatus = res.IsSuccessStatusCode ? "online" : "offline";
                    }
                    catch
                    {
                        healthStatus = "offline";
                    }
                }
                else if (status == "disabled")
                {
                    healthStatus = "disabled";
                }

                items.Add(new
                {
                    id = row.Text("id"),
                    name = row.Text("name"),
                    vendor = row.Text("vendor"),
                    version = row.Text("version"),
                    description = row.Text("description"),
                    status = row.Text("status"),
                    endpointUrl = row.Text("endpointUrl"),
                    capabilities = row["capabilities"],
                    configSchema = row["configSchema"],
                    deviceCount = row.Id("deviceCount"),
                    healthStatus,
                    createdAt = row.Time("createdAt"),
                    updatedAt = row.Time("updatedAt")
                });
            }
            return Results.Ok(items);
        }).WithName("ListPlugins");

        group.MapPut("/plugins/{id}/status", async (string id, PluginStatusRequest request, HttpContext context, Database db, AccessService access, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.manage");
            var status = request.Status?.Trim().ToLowerInvariant();
            if (status is not ("active" or "disabled"))
                throw new PlatformException(400, "plugin.status_invalid", "插件状态仅支持 active 或 disabled");

            var row = await db.OneAsync("select id, name from device_plugins where id = @id", new { id })
                ?? throw new PlatformException(404, "plugin.not_found", "指定的插件不存在");

            await db.ExecuteAsync("update device_plugins set status = @status, updated_at = now() where id = @id", new { id, status });
            DeviceAdapter.InvalidateRouteCache();

            await audit.WriteAsync(actor.UserId, "plugin.status", id, $"变更驱动插件状态：{row.Text("name")} -> {status}", ApiSupport.Ip(context));
            await audit.NotifyAsync("device.changed", id);

            return Results.Ok(new { id, status });
        }).WithName("UpdatePluginStatus");

        group.MapGet("/plugins/{id}/health", async (string id, HttpContext context, Database db, AccessService access, IHttpClientFactory httpClients, PlatformOptions options) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.read");

            var row = await db.OneAsync("select id, name, endpoint_url, status from device_plugins where id = @id", new { id })
                ?? throw new PlatformException(404, "plugin.not_found", "指定的插件不存在");

            var endpoint = row.Text("endpointUrl");
            var client = httpClients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint.TrimEnd('/') + "/health");
                req.Headers.Add("X-Adapter-Key", options.AdapterKey);
                using var res = await client.SendAsync(req, context.RequestAborted);
                var body = await res.Content.ReadAsStringAsync(context.RequestAborted);
                return Results.Content(body, "application/json", statusCode: (int)res.StatusCode);
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "offline", error = ex.Message }, statusCode: 503);
            }
        }).WithName("GetPluginHealth");

        group.MapPost("/plugins/install", async (HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.manage");
            Rules.Require(context.Request.HasFormContentType, "请上传插件安装包文件");

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            Rules.Require(file is not null && file.Length is > 0 and <= 524288000, "插件安装包不能为空且不能超过 500 MB");

            var fileName = Path.GetFileName(file!.FileName);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            Rules.Require(extension == ".zip", "插件安装包仅支持 .zip 压缩格式");

            using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith("/plugin.json", StringComparison.OrdinalIgnoreCase))
                ?? throw new PlatformException(400, "plugin.manifest_missing", "安装包内未找到 plugin.json 驱动清单文件");

            using var manifestStream = manifestEntry.Open();
            var jsonNode = await JsonNode.ParseAsync(manifestStream, cancellationToken: context.RequestAborted) as JsonObject
                ?? throw new PlatformException(400, "plugin.manifest_invalid", "plugin.json 格式无效，必须为有效 JSON 对象");

            var pluginId = jsonNode["id"]?.GetValue<string>()?.Trim();
            Rules.Require(!string.IsNullOrWhiteSpace(pluginId), "plugin.json 中缺少 id 标识");
            Rules.Require(Regex.IsMatch(pluginId!, "^[a-zA-Z0-9_-]+$"), "插件 id 仅允许英文字母、数字、下划线及短横线");

            var name = jsonNode["name"]?.GetValue<string>()?.Trim();
            Rules.Require(!string.IsNullOrWhiteSpace(name), "plugin.json 中缺少 name 驱动名称");

            var vendor = jsonNode["vendor"]?.GetValue<string>()?.Trim() ?? "Unknown";
            var version = jsonNode["version"]?.GetValue<string>()?.Trim() ?? "1.0.0";
            var description = jsonNode["description"]?.GetValue<string>()?.Trim();

            var endpointUrl = form["endpointUrl"].ToString().Trim();
            if (string.IsNullOrEmpty(endpointUrl))
                endpointUrl = jsonNode["endpointUrl"]?.GetValue<string>()?.Trim() ?? "";
            if (string.IsNullOrEmpty(endpointUrl) && jsonNode["defaultPort"] != null && int.TryParse(jsonNode["defaultPort"]!.ToString(), out var defaultPort) && defaultPort > 0)
                endpointUrl = $"http://127.0.0.1:{defaultPort}";
            if (string.IsNullOrEmpty(endpointUrl))
                endpointUrl = "http://127.0.0.1:5092";

            var capabilities = jsonNode["capabilities"] is JsonArray capArr
                ? capArr.Select(c => c?.ToString() ?? "").Where(c => !string.IsNullOrEmpty(c)).ToArray()
                : ["live", "playback", "recordings", "ptz", "alarms", "presets"];
            var configSchema = jsonNode["configSchema"]?.ToJsonString() ?? "{}";

            var pluginDir = Path.Combine(options.PluginsPath, pluginId!);
            Directory.CreateDirectory(pluginDir);

            string rootPrefix = "";
            if (manifestEntry.FullName.Contains('/'))
            {
                var prefix = manifestEntry.FullName[..(manifestEntry.FullName.LastIndexOf('/') + 1)];
                if (archive.Entries.All(e => e.FullName.StartsWith(prefix) || string.IsNullOrEmpty(e.Name)))
                    rootPrefix = prefix;
            }

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var relName = entry.FullName;
                if (!string.IsNullOrEmpty(rootPrefix) && relName.StartsWith(rootPrefix))
                    relName = relName[rootPrefix.Length..];

                relName = relName.Replace('\\', '/').TrimStart('/');
                if (relName.Contains("..") || Path.IsPathRooted(relName))
                    throw new PlatformException(400, "plugin.zip_slip", $"安装包包含非法文件路径：{entry.FullName}");

                var destPath = Path.GetFullPath(Path.Combine(pluginDir, relName));
                if (!destPath.StartsWith(Path.GetFullPath(pluginDir), StringComparison.OrdinalIgnoreCase))
                    throw new PlatformException(400, "plugin.zip_slip", $"安装包试图解压至插件根目录以外：{entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }

            var manifestFile = Path.Combine(pluginDir, "plugin.json");
            jsonNode["endpointUrl"] = endpointUrl;
            await File.WriteAllTextAsync(manifestFile, jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), context.RequestAborted);

            var row = await db.OneAsync(@"
                insert into device_plugins (id, name, vendor, version, description, status, endpoint_url, capabilities, config_schema, updated_at)
                values (@pluginId, @name, @vendor, @version, @description, 'active', @endpointUrl, @capabilities::jsonb, @configSchema::jsonb, now())
                on conflict (id) do update set
                    name = excluded.name,
                    vendor = excluded.vendor,
                    version = excluded.version,
                    description = excluded.description,
                    endpoint_url = excluded.endpoint_url,
                    capabilities = excluded.capabilities,
                    config_schema = excluded.config_schema,
                    updated_at = now()
                returning *
            ", new { pluginId, name, vendor, version, description, endpointUrl, capabilities = JsonSerializer.Serialize(capabilities), configSchema })
                ?? throw new PlatformException(500, "plugin.save_failed", "驱动插件保存失败");

            DeviceAdapter.InvalidateRouteCache();
            await audit.WriteAsync(actor.UserId, "plugin.install", pluginId!, $"安装驱动插件：{name} ({version})", ApiSupport.Ip(context));
            await audit.NotifyAsync("device.changed", pluginId!);

            return Results.Created($"/api/v2/plugins/{pluginId}", new
            {
                id = row.Text("id"),
                name = row.Text("name"),
                vendor = row.Text("vendor"),
                version = row.Text("version"),
                description = row.Text("description"),
                status = row.Text("status"),
                endpointUrl = row.Text("endpointUrl"),
                capabilities = row["capabilities"],
                configSchema = row["configSchema"],
                deviceCount = 0,
                healthStatus = "online",
                createdAt = row.Time("createdAt"),
                updatedAt = row.Time("updatedAt")
            });
        }).DisableAntiforgery().WithName("InstallPluginPackage");

        group.MapPost("/plugins", async (PluginCreateRequest request, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.manage");

            var pluginId = request.Id?.Trim();
            Rules.Require(!string.IsNullOrWhiteSpace(pluginId), "插件 ID 不能为空");
            Rules.Require(Regex.IsMatch(pluginId!, "^[a-zA-Z0-9_-]+$"), "插件 ID 仅允许英文字母、数字、下划线及短横线");

            var name = request.Name?.Trim();
            Rules.Require(!string.IsNullOrWhiteSpace(name), "驱动名称不能为空");

            var endpointUrl = request.EndpointUrl?.Trim();
            Rules.Require(!string.IsNullOrWhiteSpace(endpointUrl), "驱动服务地址不能为空");

            var vendor = string.IsNullOrWhiteSpace(request.Vendor) ? "Generic" : request.Vendor.Trim();
            var version = string.IsNullOrWhiteSpace(request.Version) ? "1.0.0" : request.Version.Trim();
            var description = request.Description?.Trim();
            var capabilities = request.Capabilities is { Length: > 0 } ? request.Capabilities : ["live", "playback", "recordings", "ptz", "alarms", "presets"];
            var configSchema = string.IsNullOrWhiteSpace(request.ConfigSchema) ? "{}" : request.ConfigSchema.Trim();

            var pluginDir = Path.Combine(options.PluginsPath, pluginId!);
            Directory.CreateDirectory(pluginDir);

            var manifestObj = new JsonObject
            {
                ["id"] = pluginId,
                ["name"] = name,
                ["vendor"] = vendor,
                ["version"] = version,
                ["description"] = description,
                ["endpointUrl"] = endpointUrl,
                ["capabilities"] = JsonNode.Parse(JsonSerializer.Serialize(capabilities)),
                ["configSchema"] = JsonNode.Parse(configSchema)
            };
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifestObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), context.RequestAborted);

            var row = await db.OneAsync(@"
                insert into device_plugins (id, name, vendor, version, description, status, endpoint_url, capabilities, config_schema, updated_at)
                values (@pluginId, @name, @vendor, @version, @description, 'active', @endpointUrl, @capabilities::jsonb, @configSchema::jsonb, now())
                on conflict (id) do update set
                    name = excluded.name,
                    vendor = excluded.vendor,
                    version = excluded.version,
                    description = excluded.description,
                    endpoint_url = excluded.endpoint_url,
                    capabilities = excluded.capabilities,
                    config_schema = excluded.config_schema,
                    updated_at = now()
                returning *
            ", new { pluginId, name, vendor, version, description, endpointUrl, capabilities = JsonSerializer.Serialize(capabilities), configSchema })
                ?? throw new PlatformException(500, "plugin.save_failed", "驱动插件保存失败");

            DeviceAdapter.InvalidateRouteCache();
            await audit.WriteAsync(actor.UserId, "plugin.register", pluginId!, $"注册驱动插件：{name} ({version})", ApiSupport.Ip(context));
            await audit.NotifyAsync("device.changed", pluginId!);

            return Results.Created($"/api/v2/plugins/{pluginId}", new
            {
                id = row.Text("id"),
                name = row.Text("name"),
                vendor = row.Text("vendor"),
                version = row.Text("version"),
                description = row.Text("description"),
                status = row.Text("status"),
                endpointUrl = row.Text("endpointUrl"),
                capabilities = row["capabilities"],
                configSchema = row["configSchema"],
                deviceCount = 0,
                healthStatus = "online",
                createdAt = row.Time("createdAt"),
                updatedAt = row.Time("updatedAt")
            });
        }).WithName("RegisterPlugin");

        group.MapPost("/plugins/probe", async (PluginProbeRequest request, HttpContext context, AccessService access, IHttpClientFactory httpClients, PlatformOptions options) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.manage");

            var endpoint = request.EndpointUrl?.Trim().TrimEnd('/');
            Rules.Require(!string.IsNullOrWhiteSpace(endpoint), "请输入待探测的服务端点地址");

            var client = httpClients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint + "/manifest");
                req.Headers.Add("X-Adapter-Key", options.AdapterKey);
                using var res = await client.SendAsync(req, context.RequestAborted);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(context.RequestAborted);
                    return Results.Content(body, "application/json");
                }
            }
            catch { /* fallback to health probe */ }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint + "/health");
                req.Headers.Add("X-Adapter-Key", options.AdapterKey);
                using var res = await client.SendAsync(req, context.RequestAborted);
                if (res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync(context.RequestAborted);
                    return Results.Content(body, "application/json");
                }
            }
            catch (Exception ex)
            {
                throw new PlatformException(503, "plugin.probe_failed", $"连接端点失败：{ex.Message}");
            }

            throw new PlatformException(502, "plugin.probe_failed", "目标服务未响应有效的 manifest 或 health 接口");
        }).WithName("ProbePlugin");

        group.MapGet("/plugins/{id}/export", async (string id, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.manage");

            var row = await db.OneAsync("select * from device_plugins where id = @id", new { id })
                ?? throw new PlatformException(404, "plugin.not_found", "指定的插件不存在");

            var mem = new MemoryStream();
            using (var archive = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
            {
                var pluginDir = Path.Combine(options.PluginsPath, id);
                var hasWrittenManifest = false;

                if (Directory.Exists(pluginDir))
                {
                    foreach (var filePath in Directory.GetFiles(pluginDir, "*", SearchOption.AllDirectories))
                    {
                        var relPath = Path.GetRelativePath(pluginDir, filePath).Replace('\\', '/');
                        archive.CreateEntryFromFile(filePath, relPath);
                        if (relPath.Equals("plugin.json", StringComparison.OrdinalIgnoreCase))
                            hasWrittenManifest = true;
                    }
                }

                if (!hasWrittenManifest)
                {
                    var manifestObj = new JsonObject
                    {
                        ["id"] = row.Text("id"),
                        ["name"] = row.Text("name"),
                        ["vendor"] = row.Text("vendor"),
                        ["version"] = row.Text("version"),
                        ["description"] = row.Text("description"),
                        ["endpointUrl"] = row.Text("endpointUrl"),
                        ["capabilities"] = JsonNode.Parse(row["capabilities"]?.ToString() ?? "[]"),
                        ["configSchema"] = JsonNode.Parse(row["configSchema"]?.ToString() ?? "{}")
                    };
                    var entry = archive.CreateEntry("plugin.json");
                    using var writer = new StreamWriter(entry.Open());
                    await writer.WriteAsync(manifestObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
            }

            mem.Position = 0;
            await audit.WriteAsync(actor.UserId, "plugin.export", id, $"导出驱动插件：{row.Text("name")}", ApiSupport.Ip(context));

            var downloadFileName = $"{id}-{row.Text("version")}.zip";
            return Results.File(mem, "application/zip", downloadFileName);
        }).WithName("ExportPlugin");

        group.MapDelete("/plugins/{id}", async (string id, HttpContext context, Database db, AccessService access, PlatformOptions options, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "plugin.manage");

            var row = await db.OneAsync("select id, name from device_plugins where id = @id", new { id })
                ?? throw new PlatformException(404, "plugin.not_found", "指定的插件不存在");

            var devCount = (await db.OneAsync("select count(*) as count from devices where plugin_id = @id", new { id })).Id("count");
            if (devCount > 0)
            {
                throw new PlatformException(409, "plugin.referenced", $"驱动插件“{row.Text("name")}”当前正被 {devCount} 台设备使用，无法删除。请先在设备管理中将相关设备分配给其他驱动或删除相关设备。");
            }

            await db.ExecuteAsync("delete from device_plugins where id = @id", new { id });

            var pluginDir = Path.Combine(options.PluginsPath, id);
            if (Directory.Exists(pluginDir))
            {
                try
                {
                    Directory.Delete(pluginDir, true);
                }
                catch { /* ignore cleanup errors */ }
            }

            DeviceAdapter.InvalidateRouteCache();
            await audit.WriteAsync(actor.UserId, "plugin.delete", id, $"删除驱动插件：{row.Text("name")}", ApiSupport.Ip(context));
            await audit.NotifyAsync("device.changed", id);

            return Results.NoContent();
        }).WithName("DeletePlugin");
    }
}
