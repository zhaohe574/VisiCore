using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Jobs;
using Xunit;

namespace VideoPlatform.Worker.Tests;

public sealed class DatabaseTests
{
    private static JsonObject Alarm(string id, long device, int? channel, bool recovered = false, DateTimeOffset? time = null)
        => new() { ["id"] = id, ["deviceId"] = device, ["channel"] = channel, ["eventType"] = "alarm.motion", ["occurredAt"] = time ?? DateTimeOffset.UtcNow, ["recovered"] = recovered, ["payload"] = new JsonObject { ["channels"] = new JsonArray(1, 2), ["payloadBase64"] = "AQID" } };

    [DatabaseFact]
    public async Task AlarmCommitPrecedesAckAndReplayIsIdempotent()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var first = new JsonArray(Alarm("same-source", 1, 1), Alarm("second-channel", 1, 2));
        var page = new JsonObject { ["items"] = first, ["nextCursor"] = "1:100" };
        var ackCount = 0;
        h.Adapter.Handler = async (method, path, body, ct) =>
        {
            if (method == HttpMethod.Get) return page.DeepClone();
            var checkpoint = await h.Db.OneAsync("select cursor from adapter_checkpoints where device_id=1", ct: ct);
            Assert.Equal(body.Text("cursor"), checkpoint.Text("cursor"));
            Assert.Equal(2, (await h.Db.OneAsync("select count(*) as count from alarm_history", ct: ct)).Id("count"));
            if (Interlocked.Increment(ref ackCount) == 1) throw new PlatformException(503, "test.ack", "模拟提交后的确认断线。");
            return null;
        };
        var job = h.Services.GetRequiredService<AlarmIngestionJob>();
        await Assert.ThrowsAsync<PlatformException>(() => job.IngestDeviceAsync(1, default));
        page["nextCursor"] = "1:200";
        await job.IngestDeviceAsync(1, default);
        Assert.Equal(2, (await h.Db.OneAsync("select count(*) as count from alarm_events")).Id("count"));
        Assert.Equal(2, (await h.Db.OneAsync("select count(*) as count from outbox where kind='alarm.changed'")).Id("count"));
        Assert.Equal(new long[] { 11, 12 }, (await h.Db.QueryAsync("select channel_id from alarm_events order by channel_id")).Select(r => r.Id("channelId")));
    }

    [DatabaseFact]
    public async Task AlarmFailureRollsBackCheckpointHistoryAndOutboxWithoutAck()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.Db.ExecuteAsync("create function reject_test_outbox() returns trigger language plpgsql as $$ begin raise exception '测试事务回滚'; end $$; create trigger reject_test_outbox before insert on outbox for each row execute function reject_test_outbox()");
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(new JsonObject { ["items"] = new JsonArray(Alarm("source", 1, 1)), ["nextCursor"] = "1:100" });
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Services.GetRequiredService<AlarmIngestionJob>().IngestDeviceAsync(1, default));
        foreach (var table in new[] { "adapter_checkpoints", "alarm_events", "alarm_history", "outbox" })
            Assert.Equal(0, (await h.Db.OneAsync($"select count(*) as count from {table}")).Id("count"));
        Assert.DoesNotContain(h.Adapter.Calls, c => c.Path.EndsWith("/ack"));
    }

    [DatabaseFact]
    public async Task RecoveryMatchesDeviceAndChannelWithoutClosingHumanState()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var occurred = DateTimeOffset.UtcNow.AddMinutes(-1);
        var job = h.Services.GetRequiredService<AlarmIngestionJob>();
        JsonObject page = new() { ["items"] = new JsonArray(Alarm("original", 1, 1, time: occurred), Alarm("other-channel", 1, 2, time: occurred)), ["nextCursor"] = "1:100" };
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult(method == HttpMethod.Get ? (JsonNode?)page.DeepClone() : null);
        await job.IngestDeviceAsync(1, default);
        page = new() { ["items"] = new JsonArray(Alarm("original", 2, 1, time: occurred)), ["nextCursor"] = "1:100" };
        await job.IngestDeviceAsync(2, default);
        await h.Db.ExecuteAsync("update alarm_events set state='processing' where device_id=1 and channel_id=11");
        page = new() { ["items"] = new JsonArray(Alarm("recovery", 1, 1, true)), ["nextCursor"] = "1:200" };
        await job.IngestDeviceAsync(1, default);
        var original = await h.Db.OneAsync("select * from alarm_events where device_id=1 and source_id='original'");
        Assert.True(original.Flag("recovered"));
        Assert.Equal("processing", original.Text("state"));
        Assert.Equal(2, original.Id("version"));
        Assert.Equal(original.Id(), (await h.Db.OneAsync("select recovery_of from alarm_events where device_id=1 and source_id='recovery'")).Id("recoveryOf"));
        Assert.Equal("new", (await h.Db.OneAsync("select state from alarm_events where device_id=1 and source_id='recovery'")).Text("state"));
        Assert.Equal(2, (await h.Db.OneAsync("select count(*) as count from alarm_events where not recovered")).Id("count"));
    }

    [DatabaseFact]
    public async Task ConcurrentClaimsRespectGlobalTwoAndDeviceOne()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.AddExportAsync(); await h.AddExportAsync(); await h.AddExportAsync(2, 21); await h.AddExportAsync(2, 21);
        var worker = h.Services.GetRequiredService<ExportProcessingJob>();
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => worker.ClaimQueuedAsync(new PlatformSettings(), default)));
        Assert.Equal(2, claims.Sum(c => c.Count));
        Assert.All(await h.Db.QueryAsync("select device_id,count(*) as count from export_jobs where state='running' group by device_id"), row => Assert.Equal(1, row.Id("count")));
    }

    [DatabaseFact]
    public async Task ExportChecksScopeAndFunctionPermissionAtClaimAndDuringRun()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync();
        await h.Db.ExecuteAsync("delete from role_permissions where permission_code='export.create'");
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        Assert.Equal("cancelled", (await h.Db.OneAsync("select state from export_jobs where id=@id", new { id })).Text("state"));
        Assert.DoesNotContain(h.Adapter.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/exports"));
    }

    [DatabaseFact]
    public async Task WorkerRestartPollsExistingExportWithoutResubmitting()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(new JsonObject { ["state"] = "running", ["progress"] = 40 });
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal(h.Options.InstanceId, row.Text("workerId"));
        Assert.Equal(40, row.Id("progress"));
        Assert.DoesNotContain(h.Adapter.Calls, c => c.Method == HttpMethod.Post);
    }

    [DatabaseFact]
    public async Task LostSubmitResponseIsRecoveredByPollingSameJob()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync();
        var submitted = false;
        h.Adapter.Handler = (method, path, body, ct) =>
        {
            if (method == HttpMethod.Post)
            {
                submitted = true;
                Assert.Equal(id.ToString(), body.Text("jobId"));
                Assert.Equal(h.Services.GetRequiredService<ExportFiles>().JobDirectory(id), body.Text("outputDirectory"));
                throw new PlatformException(504, "test.timeout", "模拟提交成功后的响应丢失。");
            }
            if (!submitted) throw new PlatformException(404, "test.missing", "模拟任务未创建。");
            return Task.FromResult<JsonNode?>(new JsonObject { ["state"] = "running", ["progress"] = 50 });
        };
        var worker = h.Services.GetRequiredService<ExportProcessingJob>();
        await worker.RunAsync(default);
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        await worker.RunAsync(default);
        Assert.Single(h.Adapter.Calls, c => c.Method == HttpMethod.Post);
        Assert.Equal(50, (await h.Db.OneAsync("select progress from export_jobs where id=@id", new { id })).Id("progress"));
    }

    [DatabaseFact]
    public async Task CompletedExportValidatesPathAndExpiresAfterSevenDays()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        var path = Path.Combine(h.Services.GetRequiredService<ExportFiles>().Prepare(id), "result.mp4");
        File.WriteAllBytes(path, [1, 2, 3]);
        h.Adapter.Handler = (method, route, body, ct) => Task.FromResult<JsonNode?>(new JsonObject { ["state"] = "completed", ["progress"] = 100, ["path"] = path });
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal("completed", row.Text("state"));
        Assert.Equal(3, row.Id("fileSize"));
        Assert.Null(row?["workerId"]);
        Assert.InRange(row.Time("expiresAt"), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));
    }

    [DatabaseFact]
    public async Task ExportTimeoutCancelsAndRetainsFailureForRetry()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        await h.Db.ExecuteAsync("update export_jobs set started_at=now()-interval '7 hours' where id=@id", new { id });
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal("failed", row.Text("state"));
        Assert.Null(row?["workerId"]);
        Assert.Contains(h.Adapter.Calls, c => c.Method == HttpMethod.Delete && c.Path.EndsWith(id.ToString()));
    }

    [DatabaseFact]
    public async Task ExpiredAuthenticationReleasesMediaAndPtz()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddMediaAsync();
        await h.Db.ExecuteAsync("insert into ptz_leases(channel_id,user_id,auth_session_id,command,speed,expires_at) values(11,@userId,@authId,'up',4,now()+interval '10 seconds'); update sessions set revoked_at=now() where id=@authId", new { userId = h.UserId, authId = h.AuthId });
        await h.Services.GetRequiredService<MediaMaintenanceJob>().RunAsync(default);
        Assert.Equal("stopped", (await h.Db.OneAsync("select state from media_sessions where id=@id", new { id })).Text("state"));
        Assert.Equal(0, (await h.Db.OneAsync("select count(*) as count from ptz_leases")).Id("count"));
        Assert.Contains(h.Adapter.Calls, c => c.Path == "/internal/devices/1/ptz" && c.Body.Flag("stop"));
    }

    [DatabaseFact]
    public async Task MediaRecoveryRetainsValidSiblingAndStopsOnlyInvalidChannel()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var bad = await h.AddMediaAsync();
        var good = await h.AddMediaAsync(2, 21);
        await h.Db.ExecuteAsync("update devices set enabled=false where id=1");
        await h.Services.GetRequiredService<MediaMaintenanceJob>().RunAsync(default);
        Assert.Equal("stopped", (await h.Db.OneAsync("select state from media_sessions where id=@id", new { id = bad })).Text("state"));
        Assert.Equal("playing", (await h.Db.OneAsync("select state from media_sessions where id=@id", new { id = good })).Text("state"));
        Assert.DoesNotContain(h.Adapter.Calls, c => c.Path.Contains("/devices/2/"));
    }

    [DatabaseFact]
    public async Task StartupReconciliationStopsOrphansButKeepsValidSessions()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var valid = await h.AddMediaAsync();
        var orphan = Guid.NewGuid();
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(method == HttpMethod.Get
            ? new JsonObject
            {
                ["bootId"] = h.Adapter.BootId, ["devices"] = new JsonArray(), ["playback"] = new JsonArray(), ["exports"] = new JsonArray(),
                ["live"] = new JsonArray(new JsonObject { ["sessionId"] = valid, ["deviceId"] = 1 }, new JsonObject { ["sessionId"] = orphan, ["deviceId"] = 2 })
            } : new JsonObject());
        await h.Services.GetRequiredService<MediaMaintenanceJob>().ReconcileAsync(default);
        Assert.Contains(h.Adapter.Calls, c => c.Method == HttpMethod.Delete && c.Path == $"/internal/devices/2/live/{orphan}");
        Assert.DoesNotContain(h.Adapter.Calls, c => c.Method == HttpMethod.Delete && c.Path.EndsWith(valid.ToString()));
    }

    [DatabaseFact]
    public async Task RetentionPreservesForeignKeysAndNeverDeletesSiblingDirectory()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.Db.ExecuteAsync("""
            insert into alarm_events(id,device_id,source_id,event_type,occurred_at,recovered) values(100,1,'old','alarm.motion',now()-interval '181 days',true);
            insert into alarm_events(id,device_id,source_id,event_type,occurred_at,recovered,recovery_of) values(101,1,'recent-recovery','alarm.motion',now(),true,100);
            insert into alarm_events(id,device_id,source_id,event_type,occurred_at) values(102,1,'expired','alarm.motion',now()-interval '181 days');
            insert into alarm_history(alarm_id,action) values(102,'received');
            insert into audit_logs(action,resource,created_at) values('test','expired',now()-interval '181 days'),('test','recent',now());
            """);
        var expired = await h.AddExportAsync(state: "completed");
        var safe = await h.AddExportAsync(state: "completed");
        await h.Db.ExecuteAsync("update export_jobs set expires_at=now()-interval '1 minute' where id=@id", new { id = expired });
        var files = h.Services.GetRequiredService<ExportFiles>();
        var expiredPath = files.Prepare(expired);
        var safePath = files.Prepare(safe);
        File.WriteAllBytes(Path.Combine(expiredPath, "result.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(safePath, "result.mp4"), [2]);
        await h.Services.GetRequiredService<RetentionJob>().RunAsync(default);
        Assert.False(Directory.Exists(expiredPath));
        Assert.True(Directory.Exists(safePath));
        Assert.Equal(2, (await h.Db.OneAsync("select count(*) as count from alarm_events")).Id("count"));
        Assert.Equal(0, (await h.Db.OneAsync("select count(*) as count from alarm_history")).Id("count"));
        Assert.Equal(1, (await h.Db.OneAsync("select count(*) as count from audit_logs")).Id("count"));
    }

    [DatabaseFact]
    public async Task HeartbeatIsPersistedWithVersionAndJobState()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var health = h.Services.GetRequiredService<JobHealth>();
        health.Set("alarms", "degraded", "模拟读取故障。");
        await health.BeatAsync(default);
        var row = await h.Db.OneAsync("select * from service_heartbeats where name=@name", new { name = "worker:" + h.Options.InstanceId });
        Assert.Equal("2.0.0", row?["details"].Text("version"));
        Assert.Equal("degraded", row?["details"]?["jobs"]?["alarms"].Text("state"));
    }
}
