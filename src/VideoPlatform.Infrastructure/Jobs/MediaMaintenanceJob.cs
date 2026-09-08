using System.Text.Json.Nodes;
using VideoPlatform.Application;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Jobs;

public sealed class MediaMaintenanceJob(Database db, MediaService media, IDeviceAdapter adapter, JobLocks locks,
    ResourceRetries retries, WorkerOptions options, JobHealth health, TimeProvider clock)
{
    private static readonly string MediaAllowed = JobAuthorization.ForRow("m", "case when m.kind='live' then 'live.view' else 'playback.view' end");
    private static readonly string PtzAllowed = JobAuthorization.ForRow("p", "'ptz.control'");
    private const string MediaInvalid = "(m.state='stopping' or (m.closed_at is null and (m.expires_at<=now() or not ({0}))))";
    private Guid? _mediaCursor;
    private long _ptzCursor;

    public async Task RunAsync(CancellationToken ct)
    {
        var sessions = await db.QueryAsync($"select m.id from media_sessions m where (@cursor::uuid is null or m.id>@cursor) and {string.Format(MediaInvalid, MediaAllowed)} order by m.id limit @limit", new { cursor = _mediaCursor, limit = options.BatchSize }, ct);
        if (sessions.Count == 0 && _mediaCursor is not null)
            sessions = await db.QueryAsync($"select m.id from media_sessions m where {string.Format(MediaInvalid, MediaAllowed)} order by m.id limit @limit", new { limit = options.BatchSize }, ct);
        // 游标推进后，持续失败的旧资源不会阻塞后面的撤权和超时资源。
        _mediaCursor = sessions.Count == 0 ? null : Guid.Parse(sessions[^1].Text("id"));
        var failed = 0;
        await Parallel.ForEachAsync(sessions, new ParallelOptions { MaxDegreeOfParallelism = options.MaintenanceConcurrency, CancellationToken = ct }, async (row, token) =>
        {
            if (!await retries.RunAsync($"media:{row.Text("id")}", async bounded =>
            {
                await using var held = await locks.TryAcquireAsync("media", row.Text("id"), bounded);
                if (held is null) return;
                var id = Guid.Parse(row.Text("id"));
                var current = await db.OneAsync($"select m.id,m.expires_at from media_sessions m where m.id=@id and {string.Format(MediaInvalid, MediaAllowed)}", new { id }, bounded);
                if (current is null) return;
                await media.StopInternalAsync(id, bounded, current.Time("expiresAt"));
                if ((await db.OneAsync("select state from media_sessions where id=@id", new { id }, bounded)).Text("state") == "stopping")
                    throw new InvalidOperationException("媒体资源尚未完全停止，保留待清理状态。");
            }, TimeSpan.FromMinutes(2), token)) Interlocked.Increment(ref failed);
        });
        var ptz = await db.QueryAsync($"select p.channel_id from ptz_leases p where p.channel_id>@cursor and (p.expires_at<=now() or not ({PtzAllowed})) order by p.channel_id limit @limit", new { cursor = _ptzCursor, limit = options.BatchSize }, ct);
        if (ptz.Count == 0 && _ptzCursor != 0)
            ptz = await db.QueryAsync($"select p.channel_id from ptz_leases p where p.expires_at<=now() or not ({PtzAllowed}) order by p.channel_id limit @limit", new { limit = options.BatchSize }, ct);
        _ptzCursor = ptz.Count == 0 ? 0 : ptz[^1].Id("channelId");
        await Parallel.ForEachAsync(ptz, new ParallelOptions { MaxDegreeOfParallelism = options.MaintenanceConcurrency, CancellationToken = ct }, async (row, token) =>
        {
            if (!await retries.RunAsync($"ptz:{row.Id("channelId")}", async bounded =>
            {
                var channelId = row.Id("channelId");
                await using var held = await locks.TryAcquireAsync("ptz", channelId.ToString(), bounded);
                if (held is null) return;
                var current = await db.OneAsync($"select p.channel_id,p.expires_at from ptz_leases p where p.channel_id=@channelId and (p.expires_at<=now() or not ({PtzAllowed}))", new { channelId }, bounded);
                if (current is not null) await media.StopPtzInternalAsync(channelId, bounded, current.Time("expiresAt"));
            }, options.RequestTimeout, token)) Interlocked.Increment(ref failed);
        });
        health.Set("media", failed == 0 ? "healthy" : "degraded", failed == 0 ? null : $"{failed} 项媒体或云台资源等待停止。" );
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        await using var held = await locks.TryAcquireAsync("inventory", "all", ct);
        if (held is null) return;
        var snapshotStarted = clock.GetUtcNow();
        var inventory = AdapterInventory.Parse(await adapter.SendAsync(HttpMethod.Get, "/internal/sessions", ct: ct));
        var failures = 0;
        foreach (var resource in inventory.Resources)
        {
            var row = resource.Kind == "exports"
                ? await db.OneAsync("select state,device_id from export_jobs where id=@id", new { id = resource.Id }, ct)
                : await db.OneAsync("select kind,state,device_id,closed_at from media_sessions where id=@id", new { id = resource.Id }, ct);
            var keep = row is not null && row.Id("deviceId") == resource.DeviceId &&
                (resource.Kind == "exports" ? row.Text("state") is "queued" or "running" : row.Text("kind") == resource.Kind && row["closedAt"] is null);
            if (keep || resource.Kind == "exports" && resource.State is "completed" or "failed" or "cancelled") continue;
            if (!await retries.RunAsync($"orphan:{resource.Kind}:{resource.DeviceId}:{resource.Id}", async bounded =>
            {
                try { await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{resource.DeviceId}/{resource.Kind}/{resource.Id}", ct: bounded); }
                catch (PlatformException ex) when (ex.Status == 404) { }
            }, options.RequestTimeout, ct)) failures++;
        }
        var known = inventory.Resources.Where(r => r.Kind != "exports").Select(r => (r.Kind, r.DeviceId, r.Id)).ToHashSet();
        var cutoff = snapshotStarted - options.MediaStartGrace;
        var stored = await db.QueryAsync("select id,kind,device_id from media_sessions where closed_at is null and created_at<@cutoff", new { cutoff }, ct);
        foreach (var row in stored)
        {
            var id = Guid.Parse(row.Text("id"));
            if (known.Contains((row.Text("kind"), row.Id("deviceId"), id))) continue;
            if (!await retries.RunAsync($"missing:{id}", t => media.StopInternalAsync(id, t), TimeSpan.FromMinutes(2), ct)) failures++;
        }
        health.Set("reconciliation", failures == 0 ? "healthy" : "degraded", $"已核对适配器启动标识 {inventory.BootId}，待清理 {failures} 项。" );
    }
}
