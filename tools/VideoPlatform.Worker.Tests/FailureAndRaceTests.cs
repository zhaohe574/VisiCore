using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Jobs;
using Xunit;

namespace VideoPlatform.Worker.Tests;

public sealed class FailureAndRaceTests
{
    [DatabaseFact]
    public async Task ExportClaimSkipsRowLockedByAnotherTransaction()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var lockedId = await h.AddExportAsync();
        await h.AddExportAsync();
        await h.AddExportAsync(2, 21);
        await using var connection = await h.Services.GetRequiredService<Npgsql.NpgsqlDataSource>().OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new Npgsql.NpgsqlCommand("select id from export_jobs where id=@id for update", connection, transaction);
        command.Parameters.AddWithValue("id", lockedId);
        await command.ExecuteScalarAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var claimed = await h.Services.GetRequiredService<ExportProcessingJob>().ClaimQueuedAsync(new VideoPlatform.Contracts.PlatformSettings(), limit.Token);
        Assert.Equal(2, claimed.Count);
        Assert.DoesNotContain(claimed, row => row.Text("id") == lockedId.ToString());
        await transaction.RollbackAsync();
    }

    [DatabaseFact]
    public async Task ScopeRevocationDuringExportPollCancelsAdapterTask()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        h.Adapter.Handler = async (method, path, body, ct) =>
        {
            if (method == HttpMethod.Get)
            {
                await h.Db.ExecuteAsync("update roles set code='restricted',all_channels=false where code='admin'", ct: ct);
                return new JsonObject { ["state"] = "running", ["progress"] = 50 };
            }
            return new JsonObject { ["state"] = "cancelled", ["progress"] = 50 };
        };
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal("cancelled", row.Text("state"));
        Assert.Null(row?["workerId"]);
        Assert.Contains(h.Adapter.Calls, c => c.Method == HttpMethod.Delete);
    }

    [DatabaseFact]
    public async Task DeviceLevelRecoveryCannotMatchUnknownChannelAlarm()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.Db.ExecuteAsync("insert into alarm_events(device_id,source_id,event_type,occurred_at,payload) values(1,'unknown-source','alarm.motion',now()-interval '1 minute','{\"channel\":999}'::jsonb)");
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(method == HttpMethod.Get
            ? JsonNode.Parse("""{"items":[{"id":"device-recovery","deviceId":1,"channel":null,"eventType":"alarm.motion","occurredAt":"2099-09-07T10:00:00Z","recovered":true,"payload":{}}],"nextCursor":"1:100"}""") : null);
        await h.Services.GetRequiredService<AlarmIngestionJob>().IngestDeviceAsync(1, default);
        Assert.False((await h.Db.OneAsync("select recovered from alarm_events where source_id='unknown-source'")).Flag("recovered"));
        Assert.Null((await h.Db.OneAsync("select recovery_of from alarm_events where source_id='device-recovery'"))?["recoveryOf"]);
    }

    [DatabaseFact]
    public async Task FailedFirstMediaBatchDoesNotStarveLaterExpiredSessions()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.AddMediaAsync();
        await h.AddMediaAsync(2, 21);
        await h.Db.ExecuteAsync("update media_sessions set expires_at=now()-interval '1 second'");
        h.Adapter.Handler = (method, path, body, ct) => throw new PlatformException(503, "test.down", "模拟设备适配器失败。");
        var job = new MediaMaintenanceJob(h.Db, h.Services.GetRequiredService<MediaService>(), h.Adapter,
            h.Services.GetRequiredService<JobLocks>(), h.Services.GetRequiredService<ResourceRetries>(), new WorkerOptions { BatchSize = 1 },
            h.Services.GetRequiredService<JobHealth>(), h.Clock);
        await job.RunAsync(default);
        await job.RunAsync(default);
        Assert.Equal(2, h.Adapter.Calls.Select(c => c.Path).Distinct().Count());
        Assert.Equal(2, (await h.Db.OneAsync("select count(*) as count from media_sessions where state='stopping'")).Id("count"));
    }

    [DatabaseFact]
    public async Task UnregisteredChannelKeepsSourceAndCannotRecoverDeviceLevelAlarm()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.Db.ExecuteAsync("insert into alarm_events(device_id,source_id,event_type,occurred_at) values(1,'device-level','alarm.motion',now()-interval '1 minute')");
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(method == HttpMethod.Get
            ? JsonNode.Parse("""{"items":[{"id":"unknown-channel","deviceId":1,"channel":999,"eventType":"alarm.motion","occurredAt":"2026-09-07T10:00:00Z","recovered":true,"payload":{"payloadBase64":"AQID"}}],"nextCursor":"1:100"}""") : null);
        await h.Services.GetRequiredService<AlarmIngestionJob>().IngestDeviceAsync(1, default);
        var received = await h.Db.OneAsync("select * from alarm_events where source_id='unknown-channel'");
        Assert.Null(received?["channelId"]);
        Assert.Equal(999, received?["payload"].Id("channel"));
        Assert.Null(received?["recoveryOf"]);
        Assert.Equal("new", received.Text("state"));
        Assert.False((await h.Db.OneAsync("select recovered from alarm_events where source_id='device-level'")).Flag("recovered"));
    }

    [DatabaseFact]
    public async Task EmptyAlarmPageRetriesCommittedAcknowledgement()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.Db.ExecuteAsync("insert into adapter_checkpoints(device_id,cursor) values(1,'1:100')");
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(method == HttpMethod.Get
            ? new JsonObject { ["items"] = new JsonArray(), ["nextCursor"] = "1:100" } : null);
        await h.Services.GetRequiredService<AlarmIngestionJob>().IngestDeviceAsync(1, default);
        Assert.Contains(h.Adapter.Calls, c => c.Method == HttpMethod.Post && c.Body.Text("cursor") == "1:100");
    }

    [DatabaseFact]
    public async Task DeviceSyncBoundsConcurrencyAndOneOfflineDeviceDoesNotBlockOthers()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var current = 0;
        var maximum = 0;
        h.Adapter.Handler = async (method, path, body, ct) =>
        {
            if (!path.EndsWith("/sync")) return new JsonObject();
            var active = Interlocked.Increment(ref current);
            maximum = Math.Max(maximum, active);
            try
            {
                await Task.Delay(10, ct);
                if (path.Contains("/devices/1/")) throw new PlatformException(503, "test.offline", "模拟设备离线。");
                return JsonNode.Parse("""{"device":{"model":"模拟设备","serialNumber":"two"},"channels":[{"channel":1,"name":"同步后通道","online":true,"ptzCapable":true}]}""");
            }
            finally { Interlocked.Decrement(ref current); }
        };
        var job = new DeviceSyncJob(h.Db, h.Services.GetRequiredService<DeviceService>(), h.Services.GetRequiredService<JobLocks>(),
            h.Services.GetRequiredService<ResourceRetries>(), new WorkerOptions { DeviceConcurrency = 1 }, h.Services.GetRequiredService<JobHealth>());
        await job.RunAsync(default);
        Assert.Equal(1, maximum);
        Assert.Equal("offline", (await h.Db.OneAsync("select status from devices where id=1")).Text("status"));
        Assert.Equal("同步后通道", (await h.Db.OneAsync("select name from channels where id=21")).Text("name"));
    }

    [DatabaseFact]
    public async Task MediaSweepHandlesExpiryScopePermissionAndStoppedRetries()
    {
        foreach (var change in new[]
        {
            "update sessions set expires_at=now()-interval '1 second'",
            "update media_sessions set expires_at=now()-interval '1 second'",
            "delete from role_permissions where permission_code='live.view'",
            "update roles set code='restricted',all_channels=false where code='admin'",
            "update users set status='disabled'",
            "update media_sessions set state='stopping',closed_at=now()"
        })
        {
            await using var h = await DatabaseHarness.CreateAsync();
            var id = await h.AddMediaAsync();
            await h.Db.ExecuteAsync(change);
            await h.Services.GetRequiredService<MediaMaintenanceJob>().RunAsync(default);
            Assert.Equal("stopped", (await h.Db.OneAsync("select state from media_sessions where id=@id", new { id })).Text("state"));
        }
    }

    [DatabaseFact]
    public async Task FailedMediaStopIsRetainedAndRetriedWithBackoff()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddMediaAsync();
        await h.Db.ExecuteAsync("update media_sessions set expires_at=now()-interval '1 second'");
        var fail = true;
        h.Adapter.Handler = (method, path, body, ct) =>
        {
            if (fail) throw new PlatformException(503, "test.down", "模拟适配器不可用。");
            return Task.FromResult<JsonNode?>(new JsonObject());
        };
        var job = h.Services.GetRequiredService<MediaMaintenanceJob>();
        await job.RunAsync(default);
        Assert.Equal("stopping", (await h.Db.OneAsync("select state from media_sessions where id=@id", new { id })).Text("state"));
        await job.RunAsync(default);
        Assert.Single(h.Adapter.Calls);
        fail = false;
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        await job.RunAsync(default);
        Assert.Equal("stopped", (await h.Db.OneAsync("select state from media_sessions where id=@id", new { id })).Text("state"));
    }

    [DatabaseFact]
    public async Task PtzTimeoutStopsButValidLeaseSurvives()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        await h.Db.ExecuteAsync("""
            insert into ptz_leases(channel_id,user_id,auth_session_id,command,speed,expires_at)
            values(11,@userId,@authId,'up',4,now()-interval '1 second'),(21,@userId,@authId,'up',4,now()+interval '10 seconds')
            """, new { userId = h.UserId, authId = h.AuthId });
        await h.Services.GetRequiredService<MediaMaintenanceJob>().RunAsync(default);
        Assert.Equal(21, (await h.Db.OneAsync("select channel_id from ptz_leases")).Id("channelId"));
        Assert.Single(h.Adapter.Calls);
    }

    [DatabaseFact]
    public async Task MissingAdapterSessionStopsOnlyAfterStartGrace()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var recent = await h.AddMediaAsync();
        var old = await h.AddMediaAsync(2, 21);
        await h.Db.ExecuteAsync("update media_sessions set created_at=now()-interval '3 minutes' where id=@old", new { old });
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(method == HttpMethod.Get
            ? new JsonObject { ["bootId"] = h.Adapter.BootId, ["devices"] = new JsonArray(), ["live"] = new JsonArray(), ["playback"] = new JsonArray(), ["exports"] = new JsonArray() } : new JsonObject());
        await h.Services.GetRequiredService<MediaMaintenanceJob>().ReconcileAsync(default);
        Assert.Equal("stopped", (await h.Db.OneAsync("select state from media_sessions where id=@old", new { old })).Text("state"));
        Assert.Equal("playing", (await h.Db.OneAsync("select state from media_sessions where id=@recent", new { recent })).Text("state"));
    }

    [DatabaseFact]
    public async Task CancellationDuringSubmitCannotPublishCompletion()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync();
        var postCount = 0;
        h.Adapter.Handler = async (method, path, body, ct) =>
        {
            if (method == HttpMethod.Get) throw new PlatformException(404, "test.missing", "模拟任务尚未创建。");
            if (method == HttpMethod.Post)
            {
                postCount++;
                await h.Db.ExecuteAsync("update export_jobs set state='cancelled',error='用户取消' where id=@id", new { id }, ct);
                return new JsonObject { ["state"] = "completed", ["progress"] = 100, ["path"] = "invalid" };
            }
            return new JsonObject { ["state"] = "cancelled", ["progress"] = 0 };
        };
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        Assert.Equal(1, postCount);
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal("cancelled", row.Text("state"));
        Assert.Null(row?["path"]);
        Assert.Null(row?["workerId"]);
        Assert.Contains(h.Adapter.Calls, c => c.Method == HttpMethod.Delete);
    }

    [DatabaseFact]
    public async Task PendingCancellationKeepsDeviceSlotUntilAdapterResponds()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var cancelled = await h.AddExportAsync(state: "cancelled", workerId: "old-worker");
        var waiting = await h.AddExportAsync();
        h.Adapter.Handler = (method, path, body, ct) => throw new PlatformException(503, "test.down", "模拟取消失败。");
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        Assert.Equal("queued", (await h.Db.OneAsync("select state from export_jobs where id=@waiting", new { waiting })).Text("state"));
        Assert.NotNull((await h.Db.OneAsync("select worker_id from export_jobs where id=@cancelled", new { cancelled }))?["workerId"]);
        Assert.DoesNotContain(h.Adapter.Calls, c => c.Method == HttpMethod.Post);
    }

    [DatabaseFact]
    public async Task OldExportExecutionCannotOverwriteRequeuedGeneration()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        h.Adapter.Handler = async (method, path, body, ct) =>
        {
            await h.Db.ExecuteAsync("update export_jobs set started_at=now()+interval '1 second',progress=0 where id=@id", new { id }, ct);
            return new JsonObject { ["state"] = "running", ["progress"] = 99 };
        };
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        Assert.Equal(0, (await h.Db.OneAsync("select progress from export_jobs where id=@id", new { id })).Id("progress"));
    }

    [DatabaseFact]
    public async Task ExportInvalidOutputIsFailedAndNeverPublished()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        h.Adapter.Handler = (method, path, body, ct) => Task.FromResult<JsonNode?>(new JsonObject
        {
            ["state"] = "completed", ["progress"] = 100, ["path"] = Path.Combine(h.Services.GetRequiredService<ExportFiles>().Root, "outside.mp4")
        });
        await h.Services.GetRequiredService<ExportProcessingJob>().RunAsync(default);
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal("failed", row.Text("state"));
        Assert.Null(row?["path"]);
        Assert.NotNull(row?["workerId"]);
    }

    [DatabaseFact]
    public async Task ExportAutomaticFailuresStopAtLimitAndRetainRecord()
    {
        await using var h = await DatabaseHarness.CreateAsync();
        var id = await h.AddExportAsync(state: "running", workerId: "old-worker");
        h.Adapter.Handler = (method, path, body, ct) => throw new PlatformException(503, "test.down", "模拟连续失败。");
        var worker = h.Services.GetRequiredService<ExportProcessingJob>();
        for (var attempt = 0; attempt < h.Options.ExportFailureLimit; attempt++)
        {
            await worker.RunAsync(default);
            h.Clock.Advance(TimeSpan.FromSeconds(70));
        }
        var row = await h.Db.OneAsync("select * from export_jobs where id=@id", new { id });
        Assert.Equal("failed", row.Text("state"));
        Assert.NotNull(row?["workerId"]);
        Assert.True(row.Time("expiresAt") > DateTimeOffset.UtcNow.AddDays(6));
        await worker.RunAsync(default);
        Assert.Equal(h.Options.ExportFailureLimit, h.Adapter.Calls.Count(c => c.Method == HttpMethod.Get));
    }
}
