using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Jobs;

public sealed class RetentionJob(Database db, ISettingsStore settings, IDeviceAdapter adapter, ExportFiles files,
    JobLocks locks, ResourceRetries retries, WorkerOptions options, JobHealth health, ILogger<RetentionJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var held = await locks.TryAcquireAsync("retention", "all", ct);
        if (held is null) return;
        var configuration = await settings.ReadAsync(ct);
        await db.TransactionAsync(async tx =>
        {
            // 先清理旧恢复记录，再清理没有外部引用的旧报警；仍被保留期内恢复记录引用的报警继续保留。
            await tx.ExecuteAsync("""
                delete from alarm_events a where a.id in (
                  select old.id from alarm_events old where old.occurred_at<now()-@retention
                    and not exists(select 1 from alarm_events recovery where recovery.recovery_of=old.id)
                  order by old.occurred_at,old.id limit @limit for update of old skip locked)
                """, new { retention = TimeSpan.FromDays(configuration.AlarmRetentionDays), limit = options.BatchSize }, ct);
            await tx.ExecuteAsync("""
                delete from audit_logs where id in (select id from audit_logs where created_at<now()-@retention order by id limit @limit for update skip locked)
                """, new { retention = TimeSpan.FromDays(configuration.AuditRetentionDays), limit = options.BatchSize }, ct);
            return true;
        }, ct);
        var jobs = await db.QueryAsync("""
            select id from export_jobs where state in('completed','failed','cancelled') and worker_id is null
              and (expires_at<=now() or (expires_at is null and created_at<now()-@retention))
            order by created_at limit @limit
            """, new { retention = TimeSpan.FromDays(configuration.ExportRetentionDays), limit = options.BatchSize }, ct);
        var failures = 0;
        foreach (var row in jobs)
        {
            var id = Guid.Parse(row.Text("id"));
            if (!await retries.RunAsync($"retention:{id}", async token =>
            {
                await using var jobLock = await locks.TryAcquireAsync("export", id.ToString(), token);
                if (jobLock is null) return;
                // 行锁阻止 API 在文件删除过程中将同一任务重新排队。
                await db.TransactionAsync(async tx =>
                {
                    var job = await tx.OneAsync("""
                        select * from export_jobs where id=@id and state in('completed','failed','cancelled') and worker_id is null
                          and (expires_at<=now() or (expires_at is null and created_at<now()-@retention)) for update
                        """, new { id, retention = TimeSpan.FromDays(configuration.ExportRetentionDays) }, token);
                    if (job is null) return false;
                    try
                    {
                        var status = AdapterExportStatus.Parse(await adapter.SendAsync(HttpMethod.Get, $"/internal/devices/{job.Id("deviceId")}/exports/{id}", ct: token));
                        if (status.State is "queued" or "running")
                        {
                            await adapter.SendAsync(HttpMethod.Delete, $"/internal/devices/{job.Id("deviceId")}/exports/{id}", ct: token);
                            throw new InvalidOperationException("导出任务仍在适配器运行，等待下一轮确认后清理。");
                        }
                    }
                    catch (PlatformException ex) when (ex.Status == 404) { }
                    files.DeleteJobDirectory(id);
                    await tx.ExecuteAsync("delete from export_jobs where id=@id", new { id }, token);
                    await JobOutbox.ChangedAsync(tx, job, token);
                    return true;
                }, token);
            }, options.RequestTimeout, ct)) failures++;
        }
        if (failures > 0) logger.LogWarning("{Count} 项导出文件未通过安全清理检查，已保留目录和数据库记录。", failures);
        health.Set("retention", failures == 0 ? "healthy" : "degraded", failures == 0 ? null : $"{failures} 项导出等待安全清理。" );
    }
}
