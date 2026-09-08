using System.Text.Json.Nodes;
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
            await access.DemandAsync(actor, "device.read");
            var rows = await db.QueryAsync(@"
                select p.id, p.name, p.vendor, p.version, p.description, p.status, 
                       p.endpoint_url, p.capabilities, p.config_schema, p.created_at, p.updated_at,
                       (select count(*) from devices where plugin_id = p.id and enabled = true) as device_count
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

        group.MapPut("/plugins/{id}/status", async (string id, PluginStatusRequest request, HttpContext context, Database db, AccessService access, IDeviceAdapter adapter, AuditStore audit) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "device.manage");
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
            await access.DemandAsync(actor, "device.read");

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
    }
}
