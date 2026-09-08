using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Npgsql;
using VideoPlatform.Api;
using VideoPlatform.Api.Modules;
using VideoPlatform.Application;
using VideoPlatform.Client.Generated;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using Contracts = VideoPlatform.Contracts;

namespace VideoPlatform.ClientGenerator.Tests;

internal static class DatabaseRegression
{
    public static async Task<int> RunAsync(string databaseUrl)
    {
        var schema = $"sdk_test_{Guid.NewGuid():N}";
        await using var root = NpgsqlDataSource.Create(databaseUrl);
        await using (var command = root.CreateCommand($"create schema {schema}")) await command.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(databaseUrl) { SearchPath = schema };
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PLATFORM_DATABASE_URL"] = connection.ConnectionString,
                ["PLATFORM_ADMIN_USER"] = "admin", ["PLATFORM_ADMIN_PASSWORD"] = "SDK-Test-Password-2026!",
                ["PLATFORM_DATA_PATH"] = Path.GetFullPath("tools/VideoPlatform.ClientGenerator/.runtime/database"),
                ["HIK_ADAPTER_INTERNAL_KEY"] = "本地回归测试密钥", ["ZLM_API_URL"] = "http://127.0.0.1:1"
            });
            builder.Services.AddPlatform(builder.Configuration);
            builder.Services.AddSingleton<IDeviceAdapter, UnusedAdapter>();
            builder.Services.AddAuthentication("session").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("session", _ => { });
            builder.Services.AddAuthorization();
            await using var app = builder.Build();
            await Bootstrap.InitializeAsync(app.Services);
            app.Use(async (context, next) =>
            {
                try { await next(context); }
                catch (PlatformException error)
                {
                    context.Response.StatusCode = error.Status;
                    await context.Response.WriteAsJsonAsync(new Contracts.ErrorResponse(error.Code, error.Message, context.TraceIdentifier));
                }
            });
            app.UseAuthentication(); app.UseAuthorization(); app.MapAdministrationEndpoints();
            await app.StartAsync();
            try
            {
                var db = app.Services.GetRequiredService<Database>();
                var sessions = app.Services.GetRequiredService<SessionStore>();
                var administration = ActivatorUtilities.CreateInstance<AdministrationService>(app.Services);
                var login = await sessions.LoginAsync(new("admin", "SDK-Test-Password-2026!", "desktop"), "127.0.0.1");
                var actor = (await sessions.AuthenticateAsync(login.Token))!;
                var device = (await db.OneAsync("insert into devices(name,host,port,username,password_cipher) values('空槽测试','127.0.0.10',8000,'test','测试密文') returning id")).Id();
                var first = (await db.OneAsync("insert into channels(device_id,device_channel,name,status) values(@device,1,'第一通道','online') returning id", new { device })).Id();
                var second = (await db.OneAsync("insert into channels(device_id,device_channel,name,status) values(@device,2,'第二通道','online') returning id", new { device })).Id();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var http = new HttpClient { BaseAddress = new Uri(address) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
                var client = new VideoPlatformClient(http);
                var passed = 0;
                void Check(bool condition, string name)
                {
                    if (!condition) throw new InvalidOperationException(name);
                    passed++; Console.WriteLine($"通过：{name}");
                }
                static async Task Reject(Func<Task> operation)
                {
                    try { await operation(); }
                    catch (ApiException error) when (error.StatusCode == 400) { return; }
                    throw new InvalidOperationException("无效布局未被拒绝。");
                }
                var create = new LayoutRequest { Name = "空槽四屏", Kind = "layout", Shared = false, Layout = 4, IntervalSeconds = 30, ChannelIds = [null, first, null, second] };
                var layout = await client.CreateLayoutAsync(create);
                Check(layout.ChannelIds.SequenceEqual(new long?[] { null, first, null, second }), "生成 C# 客户端创建布局保留四个窗口索引");
                var persisted = (await client.GetLayoutsAsync()).Single(item => item.Id == layout.Id);
                Check(persisted.ChannelIds.SequenceEqual(create.ChannelIds), "HTTP 读取 PostgreSQL 空槽数组不压缩窗口");
                create.ChannelIds = [second, null, first, null];
                await client.UpdateLayoutAsync(layout.Id, create);
                Check((await client.GetLayoutsAsync()).Single(item => item.Id == layout.Id).ChannelIds.SequenceEqual(create.ChannelIds), "布局更新保留中间和末尾空槽");
                var allNull = new LayoutRequest { Name = "全空", Kind = "layout", Shared = false, Layout = 4, IntervalSeconds = 30, ChannelIds = [null, null, null, null] };
                await Reject(() => client.CreateLayoutAsync(allNull));
                var patrolNull = new LayoutRequest { Name = "轮巡空槽", Kind = "patrol", Shared = false, Layout = 4, IntervalSeconds = 30, ChannelIds = [first, null] };
                await Reject(() => client.CreateLayoutAsync(patrolNull));
                Check(true, "全空布局与包含空槽的轮巡均返回 400");
                var readerRole = (await administration.RolesAsync(actor)).Single(role => role.Code == "operator");
                var reader = await administration.SaveUserAsync(actor, null, new("reader", "SDK-Test-Password-2026!", null, null, "active", [readerRole.Id]), "127.0.0.1");
                await administration.SaveScopesAsync(actor, "user", reader.Id, new(false, [new("channel", first)]), "127.0.0.1");
                create.Shared = true;
                await client.UpdateLayoutAsync(layout.Id, create);
                var readerLogin = await sessions.LoginAsync(new("reader", "SDK-Test-Password-2026!", "desktop"), "127.0.0.1");
                using var readerHttp = new HttpClient { BaseAddress = new Uri(address) };
                readerHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerLogin.Token);
                var readerClient = new VideoPlatformClient(readerHttp);
                Check((await readerClient.GetLayoutsAsync()).Single(item => item.Id == layout.Id).ChannelIds.SequenceEqual(new long?[] { null, null, first, null }), "共享布局失权通道替换为空槽且保留后续窗口位置");
                await administration.SaveScopesAsync(actor, "user", reader.Id, new(false, []), "127.0.0.1");
                Check((await readerClient.GetLayoutsAsync()).Single(item => item.Id == layout.Id).ChannelIds.SequenceEqual(new long?[] { null, null, null, null }), "授权撤销后四个空槽完整保留且不泄露通道编号");
                await client.DeleteLayoutAsync(layout.Id);
                Check(!(await client.GetLayoutsAsync()).Any(item => item.Id == layout.Id), "包含空槽的布局可正常删除");
                await db.ExecuteAsync("insert into service_heartbeats(name,checked_at,details) values('worker:old-1',now()-interval '2 days','{}'),('worker:old-2',now()-interval '1 day','{}'),('worker:current',now(),cast(@details as jsonb))", new { details = "{\"jobs\":{\"exports\":{\"state\":\"degraded\",\"message\":\"导出磁盘空间不足\"}}}" });
                var system = await client.GetSystemStatisticsAsync();
                var worker = system.Services.Single(service => service.Name == "后台任务");
                Check(worker.Status == "degraded" && worker.Reason?.Contains("导出磁盘空间不足") == true && !system.Services.Any(service => service.Name.StartsWith("worker")), "旧 Worker 不显示，最新实例汇总中文服务并保留异常原因");
                await db.ExecuteAsync("update service_heartbeats set checked_at=now()-interval '5 minutes' where name='worker:current'");
                worker = (await client.GetSystemStatisticsAsync()).Services.Single(service => service.Name == "后台任务");
                Check(worker.Status == "offline" && worker.Reason?.Contains("心跳超时") == true && worker.Reason.Contains("导出磁盘空间不足"), "最新 Worker 超时同时保留最近异常原因");
                await db.ExecuteAsync("update service_heartbeats set checked_at=now(),details='{}' where name='worker:current'");
                worker = (await client.GetSystemStatisticsAsync()).Services.Single(service => service.Name == "后台任务");
                Check(worker.Status == "online" && worker.Reason is null, "最新 Worker 恢复后清除过期异常且忽略旧实例");
                return passed;
            }
            finally { await app.StopAsync(); }
        }
        finally { await using var cleanup = root.CreateCommand($"drop schema {schema} cascade"); await cleanup.ExecuteNonQueryAsync(); }
    }

    private sealed class UnusedAdapter : IDeviceAdapter
    {
        public Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
            => Task.FromResult<JsonNode?>(new JsonObject { ["status"] = "ok" });
    }
}
