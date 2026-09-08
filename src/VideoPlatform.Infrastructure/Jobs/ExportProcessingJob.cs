using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Jobs;

public sealed class ExportProcessingJob(Database db, IDeviceAdapter adapter, ISettingsStore settings, JobLocks locks,
    ExportFiles files, WorkerOptions options, TimeProvider clock, JobHealth health, ILogger<ExportProcessingJob> logger)
{
    public const long SchedulingLock = 72002010;
    private static readonly string Allowed = JobAuthorization.ForRow("j", "'export.create'");
    private const string Retention = "(coalesce((select (value->>'exportRetentionDays')::int from settings where id=1),7)*interval '1 day')";
    private sealed record Retry(int Count, DateTimeOffset Next);
    private readonly ConcurrentDictionary<Guid, Retry> _retries = new();

    public async Task RunAsync(CancellationToken ct)
    {
        var configuration = await settings.ReadAsync(ct);
        await CancelUnauthorizedAsync(ct);
        var pending = await ClaimExistingAsync(ct);
        var failures = 0;
        await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = options.MaintenanceConcurrency, CancellationToken = ct }, async (job, token) =>
        {
            if (!await ProcessAsync(job, false, configuration, token)) Interlocked.Increment(ref failures);
        });
        string? rejection;
        try { rejection = files.Measure().Rejection(configuration.ExportQuotaGb); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rejection = "导出存储校验失败，已暂停新任务。";
            logger.LogError("导出存储校验失败。异常类型：{ErrorType}", ex.GetType().Name);
        }
        if (rejection is null)
        {
            var claimed = await ClaimQueuedAsync(configuration, ct);
            await Parallel.ForEachAsync(claimed, new ParallelOptions { MaxDegreeOfParallelism = options.MaintenanceConcurrency, CancellationToken = ct }, async (job, token) =>
            {
                if (!await ProcessAsync(job, true, configuration, token)) Interlocked.Increment(ref failures);
            });
        }
        health.Set("exports", failures == 0 && rejection is null ? "healthy" : "degraded", rejection ?? (failures > 0 ? $"{failures} 项导出正在等待适配器恢复或取消确认。" : null));
    }

    private Task CancelUnauthorizedAsync(CancellationToken ct)
        => db.TransactionAsync(async tx =>
        {
            var changed = await tx.QueryAsync($"""
                update export_jobs j set state='cancelled',error='登录会话、导出权限或通道授权已失效',
                  expires_at=coalesce(expires_at,now()+{Retention}),worker_id=case when state='queued' then null else worker_id end
                where state in('queued','running') and not ({Allowed}) returning j.*
                """, ct: ct);
            foreach (var job in changed) await JobOutbox.ChangedAsync(tx, job, ct);
            return true;
        }, ct);

    public Task<List<JsonObject>> ClaimQueuedAsync(PlatformSettings configuration, CancellationToken ct)
        => db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(@key)", new { key = SchedulingLock }, ct);
            // 未完成取消确认的任务也占用名额，防止设备端旧任务与新任务重叠。
            var counts = await tx.QueryAsync("select device_id,count(*) as count from export_jobs where state='running' or worker_id is not null group by device_id", ct: ct);
            var deviceCounts = counts.ToDictionary(r => r.Id("deviceId"), r => (int)r.Id("count"));
            var globalLimit = Math.Min(2, Math.Max(1, configuration.ExportGlobal));
            var perDeviceLimit = Math.Min(1, Math.Max(1, configuration.ExportPerDevice));
            var remaining = globalLimit - deviceCounts.Values.Sum();
            var claimed = new List<JsonObject>();
            if (remaining <= 0) return claimed;
            var candidates = await tx.QueryAsync($"""
                select j.* from export_jobs j where j.state='queued' and j.worker_id is null and ({Allowed})
                  and (select count(*) from export_jobs active where active.device_id=j.device_id and (active.state='running' or active.worker_id is not null))<@perDeviceLimit
                order by j.created_at,j.id limit @limit for update of j skip locked
                """, new { perDeviceLimit, limit = options.BatchSize }, ct);
            foreach (var candidate in candidates)
            {
                if (remaining <= 0) break;
                var deviceId = candidate.Id("deviceId");
                if (deviceCounts.GetValueOrDefault(deviceId) >= perDeviceLimit) continue;
                var job = await tx.OneAsync("""
                    update export_jobs set state='running',started_at=now(),worker_id=@workerId,lease_until=now()+@lease,
                      progress=0,error=null,expires_at=null,path=null,file_name=null,file_size=0 where id=@id returning *
                    """, new { id = Guid.Parse(candidate.Text("id")), workerId = options.InstanceId, lease = options.ExportLease }, ct);
                claimed.Add(job!);
                await JobOutbox.ChangedAsync(tx, job!, ct);
                deviceCounts[deviceId] = deviceCounts.GetValueOrDefault(deviceId) + 1;
                remaining--;
            }
            return claimed;
        }, ct);

    private Task<List<JsonObject>> ClaimExistingAsync(CancellationToken ct)
        => db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(@key)", new { key = SchedulingLock }, ct);
            var jobs = await tx.QueryAsync("""
                select * from export_jobs where (state='running' or worker_id is not null)
                  and (worker_id=@workerId or lease_until is null or lease_until<=now())
                order by lease_until nulls first,created_at limit @limit for update skip locked
                """, new { workerId = options.InstanceId, limit = options.BatchSize }, ct);
            foreach (var job in jobs)
                await tx.ExecuteAsync("update export_jobs set worker_id=@workerId,lease_until=now()+@lease where id=@id", new { id = Guid.Parse(job.Text("id")), workerId = options.InstanceId, lease = options.ExportLease }, ct);
            return jobs;
        }, ct);

    private async Task<bool> ProcessAsync(JsonObject claimed, bool fresh, PlatformSettings configuration, CancellationToken ct)
    {
        var id = Guid.Parse(claimed.Text("id"));
        if (_retries.TryGetValue(id, out var retry) && retry.Next > clock.GetUtcNow()) return false;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(100));
        try
        {
            await using var held = await locks.TryAcquireAsync("export", id.ToString(), bounded.Token);
            if (held is null) return true;
            var job = await ReadOwnedAsync(id, claimed, bounded.Token);
            if (job is null) { _retries.TryRemove(id, out _); return true; }
            if (job.Text("state") is "cancelled" or "failed")
            {
                await ConfirmStoppedAsync(job, bounded.Token);
                _retries.TryRemove(id, out _);
                return true;
            }
            if (job.Text("state") != "running") return true;
            if (job["startedAt"] is null || job.Time("startedAt") + options.ExportTimeout <= clock.GetUtcNow())
            {
                await FailAndStopAsync(job, "导出超过允许执行时长，已请求停止。", bounded.Token);
                return false;
            }
            var status = await GetAsync(job, bounded.Token);
            if (status is null || fresh && status.State is "failed" or "cancelled")
            {
                if (!await StillAllowedAsync(id, job, bounded.Token))
                {
                    await CancelUnauthorizedAsync(bounded.Token);
                    await ConfirmStoppedAsync(job, bounded.Token);
                    return true;
                }
                var rejection = files.Measure().Rejection(configuration.ExportQuotaGb);
                if (rejection is not null) { await FailAndStopAsync(job, rejection, bounded.Token); return false; }
                var directory = files.Prepare(id);
                var channel = await db.OneAsync("select device_channel from channels where id=@channelId and device_id=@deviceId", new { channelId = job.Id("channelId"), deviceId = job.Id("deviceId") }, bounded.Token);
                if (channel is null) throw new InvalidDataException("导出通道已不存在。");
                // GET 后仅对不存在的任务或明确用户重试进行 POST；jobId 在断线重试中保持不变。
                status = AdapterExportStatus.Parse(await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{job.Id("deviceId")}/exports", new
                {
                    jobId = id, channel = (int)channel.Id("deviceChannel"), start = job.Time("startAt"), end = job.Time("endAt"), outputDirectory = directory
                }, bounded.Token));
            }
            var latest = await ReadOwnedAsync(id, claimed, bounded.Token);
            if (latest is null || latest.Text("state") != "running" || !await StillAllowedAsync(id, job, bounded.Token))
            {
                await CancelUnauthorizedAsync(bounded.Token);
                if (latest is not null) await ConfirmStoppedAsync(latest, bounded.Token);
                return true;
            }
            await ApplyStatusAsync(latest, status, configuration, bounded.Token);
            _retries.TryRemove(id, out _);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var count = Math.Min((retry?.Count ?? 0) + 1, options.ExportFailureLimit);
            _retries[id] = new(count, clock.GetUtcNow().AddSeconds(Math.Min(60, 2 * (1 << count))));
            logger.LogWarning("导出任务 {JobId} 处理失败，第 {Count} 次。异常类型：{ErrorType}", id, count, ex.GetType().Name);
            if (count >= options.ExportFailureLimit || ex is InvalidDataException or UnauthorizedAccessException)
            {
                using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cleanup.CancelAfter(options.RequestTimeout);
                var job = await ReadOwnedAsync(id, claimed, cleanup.Token);
                if (job?.Text("state") == "running")
                    await MarkTerminalAsync(job, "failed", "导出处理连续失败或文件校验未通过，已请求取消，请检查后台日志后手动重试。", cleanup.Token);
            }
            foreach (var stale in _retries.Where(p => p.Value.Next < clock.GetUtcNow().AddHours(-1)).Select(p => p.Key)) _retries.TryRemove(stale, out _);
            return false;
        }
    }

    private Task<JsonObject?> ReadOwnedAsync(Guid id, JsonObject expected, CancellationToken ct)
        => db.OneAsync("select * from export_jobs where id=@id and worker_id=@workerId and started_at is not distinct from @startedAt::timestamptz",
            new { id, workerId = options.InstanceId, startedAt = StartedAt(expected) }, ct);

    private static DateTimeOffset? StartedAt(JsonObject job) => job["startedAt"] is null ? null : job.Time("startedAt");

    private async Task<bool> StillAllowedAsync(Guid id, JsonObject expected, CancellationToken ct)
        => await db.OneAsync($"select j.id from export_jobs j where j.id=@id and j.state='running' and j.worker_id=@workerId and j.started_at=@startedAt and ({Allowed})",
            new { id, workerId = options.InstanceId, startedAt = StartedAt(expected) }, ct) is not null;

    private async Task<AdapterExportStatus?> GetAsync(JsonObject job, CancellationToken ct)
    {
        try { return AdapterExportStatus.Parse(await adapter.SendAsync(HttpMethod.Get, $"/internal/devices/{job.Id("deviceId")}/exports/{job.Text("id")}", ct: ct)); }
        catch (PlatformException ex) when (ex.Status == 404) { return null; }
    }

    private async Task ApplyStatusAsync(JsonObject job, AdapterExportStatus status, PlatformSettings configuration, CancellationToken ct)
    {
        if (status.State is "failed" or "cancelled")
        {
            await MarkTerminalAsync(job, status.State, status.State == "failed" ? "适配器导出失败，请检查适配器日志后重试。" : "设备导出已取消。", ct);
            await ConfirmStoppedAsync(job, ct);
            return;
        }
        if (status.State == "completed")
        {
            var file = files.ValidateOutput(Guid.Parse(job.Text("id")), status.Path);
            await db.TransactionAsync(async tx =>
            {
                var updated = await tx.OneAsync($"""
                    update export_jobs j set state='completed',progress=100,error=null,path=@path,file_name=@name,file_size=@size,
                      expires_at=now()+@retention,worker_id=null,lease_until=null
                    where j.id=@id and j.state='running' and j.worker_id=@workerId and j.started_at=@startedAt and ({Allowed}) returning j.*
                    """, new { id = Guid.Parse(job.Text("id")), workerId = options.InstanceId, startedAt = StartedAt(job), path = file.FullName, name = file.Name, size = file.Length, retention = TimeSpan.FromDays(configuration.ExportRetentionDays) }, ct);
                if (updated is not null) await JobOutbox.ChangedAsync(tx, updated, ct);
                return true;
            }, ct);
            return;
        }
        var rejection = files.Measure().Rejection(configuration.ExportQuotaGb);
        if (rejection is not null) { await FailAndStopAsync(job, rejection, ct); return; }
        await db.TransactionAsync(async tx =>
        {
            var updated = await tx.OneAsync("""
                update export_jobs set progress=@progress where id=@id and state='running' and worker_id=@workerId and started_at=@startedAt and progress<>@progress returning *
                """, new { id = Guid.Parse(job.Text("id")), workerId = options.InstanceId, startedAt = StartedAt(job), progress = status.Progress }, ct);
            if (updated is not null) await JobOutbox.ChangedAsync(tx, updated, ct);
            return true;
        }, ct);
    }

    private async Task FailAndStopAsync(JsonObject job, string reason, CancellationToken ct)
    {
        await MarkTerminalAsync(job, "failed", reason, ct);
        await ConfirmStoppedAsync(job, ct);
    }

    private Task MarkTerminalAsync(JsonObject job, string state, string reason, CancellationToken ct)
        => db.TransactionAsync(async tx =>
        {
            var updated = await tx.OneAsync($"""
                update export_jobs set state=@state,error=@reason,expires_at=now()+{Retention}
                where id=@id and state='running' and worker_id=@workerId and started_at=@startedAt returning *
                """, new { id = Guid.Parse(job.Text("id")), workerId = options.InstanceId, startedAt = StartedAt(job), state, reason }, ct);
            if (updated is not null) await JobOutbox.ChangedAsync(tx, updated, ct);
            return true;
        }, ct);

    private async Task ConfirmStoppedAsync(JsonObject job, CancellationToken ct)
    {
        var current = await ReadOwnedAsync(Guid.Parse(job.Text("id")), job, ct);
        if (current is null || current.Text("state") is not ("failed" or "cancelled")) return;
        try
        {
            var response = AdapterExportStatus.Parse(await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{job.Id("deviceId")}/exports/{job.Text("id")}", ct: ct));
            if (response.State is "queued" or "running") throw new InvalidOperationException("适配器尚未确认导出取消。");
        }
        catch (PlatformException ex) when (ex.Status == 404) { }
        await db.ExecuteAsync($"""
            update export_jobs set worker_id=null,lease_until=null,expires_at=coalesce(expires_at,now()+{Retention})
            where id=@id and worker_id=@workerId and started_at is not distinct from @startedAt::timestamptz and state in('failed','cancelled')
            """, new { id = Guid.Parse(job.Text("id")), workerId = options.InstanceId, startedAt = StartedAt(job) }, ct);
    }
}
