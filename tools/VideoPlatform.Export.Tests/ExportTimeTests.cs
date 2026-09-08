using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using VideoPlatform.Api;
using VideoPlatform.Api.Modules;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Worker.Tests;
using Xunit;

namespace VideoPlatform.Export.Tests;

public sealed class ExportTimeTests
{
    private static DateTimeOffset Time(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    [DatabaseFact]
    public async Task CreateAndListPreserveInstantsAcrossOffsets()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await using var api = await ExportApi.StartAsync(h);
        var ranges = new[]
        {
            ("2026-09-07T00:15:00.123456+08:00", "2026-09-07T01:15:00.654321+08:00"),
            ("2026-09-06T16:15:00.123456Z", "2026-09-06T17:15:00.654321Z"),
            ("2026-09-06T11:15:00.123456-05:00", "2026-09-06T12:15:00.654321-05:00"),
            ("2026-09-07T01:45:00+09:30", "2026-09-06T13:45:00-03:30"),
            ("2026-09-06T23:30:00+08:00", "2026-09-07T23:30:00+08:00")
        };
        foreach (var (startText, endText) in ranges)
        {
            var request = new RecordingRequest(11, Time(startText), Time(endText));
            var before = DateTimeOffset.UtcNow.AddSeconds(-1);
            var id = await api.CreateAsync(request);
            var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
            AssertUtc(row!, "startAt", request.Start);
            AssertUtc(row!, "endAt", request.End);
            Assert.Equal(request.End - request.Start, row.Time("endAt") - row.Time("startAt"));
            Assert.InRange(row.Time("createdAt"), before, DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.Equal(TimeSpan.Zero, row.Time("createdAt").Offset);
            Assert.Null(row!["startedAt"]);
            Assert.Null(row["expiresAt"]);
            Assert.Null(row["leaseUntil"]);

            using var listResponse = await api.Http.GetAsync("/api/v2/exports");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = (await listResponse.Content.ReadFromJsonAsync<JsonObject>())!;
            var listed = list["items"]!.AsArray().Single(item => item.Text("id") == id.ToString())!;
            AssertUtc(listed, "start", request.Start);
            AssertUtc(listed, "end", request.End);
            AssertUtc(listed, "createdAt", row.Time("createdAt"));
            Assert.Null(listed["expiresAt"]);
        }
    }

    [DatabaseFact]
    public async Task InvalidRangesAreRejectedBeforeInsertingJobs()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await using var api = await ExportApi.StartAsync(h);
        var start = Time("2026-09-07T08:00:00+08:00");
        // 使用不同偏移表达相同或更早时刻，防止按本地钟面比较时间。
        foreach (var end in new[] { start.ToOffset(TimeSpan.Zero), start.AddSeconds(-1).ToOffset(TimeSpan.FromHours(9)), start.AddDays(1).AddSeconds(1) })
        {
            using var response = await api.Http.PostAsJsonAsync("/api/v2/exports", new RecordingRequest(11, start, end));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation.failed", (await response.Content.ReadFromJsonAsync<JsonObject>()).Text("code"));
        }
        Assert.Equal(0, (await h.Db.OneAsync("select count(*) as count from export_jobs")).Id("count"));
        Assert.Equal(0, (await h.Db.OneAsync("select count(*) as count from audit_logs where action='export.create'")).Id("count"));
    }

    [DatabaseFact]
    public async Task RetryPreservesRecordingTimesAndChecksWorkerOwnership()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await using var api = await ExportApi.StartAsync(h);
        var request = new RecordingRequest(11, Time("2026-09-07T00:15:00+08:00"), Time("2026-09-07T00:45:00+08:00"));
        foreach (var state in new[] { "failed", "cancelled" })
        {
            var id = await api.CreateAsync(request);
            await h.Db.ExecuteAsync("""
                update export_jobs set state=@state,error='测试失败',progress=50,
                  started_at=now()-interval '2 hours',expires_at=now()-interval '1 hour',
                  lease_until=now()-interval '1 minute',worker_id='test-worker' where id=@id
                """, new { id, state });
            using var blocked = await api.Http.PostAsync($"/api/v2/exports/{id}/retry", null);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("export.stopping", (await blocked.Content.ReadFromJsonAsync<JsonObject>()).Text("code"));
            await h.Db.ExecuteAsync("update export_jobs set worker_id=null where id=@id", new { id });
            var before = (await h.Db.OneAsync("select * from export_jobs where id=@id", new { id }))!;
            using var response = await api.Http.PostAsync($"/api/v2/exports/{id}/retry", null);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var after = (await h.Db.OneAsync("select * from export_jobs where id=@id", new { id }))!;
            Assert.Equal("queued", after.Text("state"));
            Assert.Equal(0, after.Id("progress"));
            Assert.Null(after["error"]);
            Assert.Null(after["workerId"]);
            Assert.Null(after["leaseUntil"]);
            AssertUtc(after, "startAt", request.Start);
            AssertUtc(after, "endAt", request.End);
            // 重试只重新排队；执行时间和过期时间由 Worker 领取任务时刷新。
            foreach (var field in new[] { "createdAt", "startedAt", "expiresAt" })
                AssertUtc(after, field, before.Time(field));
            using var duplicate = await api.Http.PostAsync($"/api/v2/exports/{id}/retry", null);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }
    }

    [DatabaseFact]
    public async Task DownloadUsesExpiryInstantAndListReturnsUtcExpiry()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await using var api = await ExportApi.StartAsync(h);
        var id = await api.CreateAsync(new(11, Time("2026-09-07T00:15:00+08:00"), Time("2026-09-07T00:45:00+08:00")));
        var path = Path.Combine(h.Services.GetRequiredService<PlatformOptions>().ExportsPath, $"{id}.mp4");
        byte[] bytes = [0, 1, 2, 3];
        await File.WriteAllBytesAsync(path, bytes);
        foreach (var expired in new[] { false, true })
        {
            var expiry = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1).ToOffset(TimeSpan.FromHours(8));
            // PostgreSQL 为微秒精度，去除低于微秒的部分后比较完整时刻。
            expiry = expiry.AddTicks(-(expiry.Ticks % 10));
            await h.Db.ExecuteAsync("update export_jobs set state='completed',path=@path,file_name='test.mp4',expires_at=@expiry where id=@id",
                new { id, path, expiry = expiry.ToUniversalTime() });
            using var listResponse = await api.Http.GetAsync("/api/v2/exports");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var list = (await listResponse.Content.ReadFromJsonAsync<JsonObject>())!;
            AssertUtc(list["items"]!.AsArray().Single()!, "expiresAt", expiry);
            using var response = await api.Http.GetAsync($"/api/v2/exports/{id}/download");
            Assert.Equal(expired ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
            if (expired) Assert.Equal("export.unavailable", (await response.Content.ReadFromJsonAsync<JsonObject>()).Text("code"));
            else Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
        }
    }

    private static void AssertUtc(JsonNode row, string field, DateTimeOffset expected)
    {
        var actual = row.Time(field);
        Assert.Equal(expected, actual);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    private sealed class ExportApi(WebApplication app, HttpClient http) : IAsyncDisposable
    {
        public HttpClient Http { get; } = http;

        public static async Task<ExportApi> StartAsync(DatabaseHarness h)
        {
            // 框架预置令牌仅供 Worker 测试；HTTP 认证需要至少 32 字符的令牌。
            var token = Passwords.Token();
            await h.Db.ExecuteAsync("update sessions set token_hash=@hash where id=@id", new { id = h.AuthId, hash = Passwords.TokenHash(token) });
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(h.Db);
            builder.Services.AddSingleton(h.Services.GetRequiredService<AccessService>());
            builder.Services.AddSingleton(h.Services.GetRequiredService<SessionStore>());
            builder.Services.AddSingleton(h.Services.GetRequiredService<AuditStore>());
            builder.Services.AddSingleton(h.Services.GetRequiredService<PlatformOptions>());
            builder.Services.AddSingleton(h.Services.GetRequiredService<ISettingsStore>());
            builder.Services.AddSingleton<IDeviceAdapter>(h.Adapter);
            builder.Services.AddAuthentication("session").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("session", _ => { });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            try
            {
                app.Use(async (context, next) =>
                {
                    try { await next(context); }
                    catch (PlatformException error)
                    {
                        context.Response.StatusCode = error.Status;
                        await context.Response.WriteAsJsonAsync(new ErrorResponse(error.Code, error.Message, context.TraceIdentifier));
                    }
                });
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapExportEndpoints();
                await app.StartAsync();
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
                // 复用现有 PostgreSQL 框架预置的真实会话，只有设备适配器使用模拟实现。
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return new ExportApi(app, http);
            }
            catch { await app.DisposeAsync(); throw; }
        }

        public async Task<Guid> CreateAsync(RecordingRequest request)
        {
            using var response = await Http.PostAsJsonAsync("/api/v2/exports", request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            var id = Guid.Parse(body.Text("id"));
            Assert.Equal("queued", body.Text("state"));
            Assert.Equal(0, body.Id("progress"));
            Assert.Equal($"/api/v2/exports/{id}", response.Headers.Location?.OriginalString);
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            try { await app.StopAsync(); }
            finally { await app.DisposeAsync(); }
        }
    }
}
