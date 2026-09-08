namespace VideoPlatform.Infrastructure.Jobs;

public sealed class DeviceSyncJob(Database db, DeviceService devices, JobLocks locks, ResourceRetries retries, WorkerOptions options, JobHealth health)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var rows = await db.QueryAsync("select id,enabled from devices order by id", ct: ct);
        var failures = 0;
        await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = options.DeviceConcurrency, CancellationToken = ct }, async (device, token) =>
        {
            if (!await retries.RunAsync($"sync:{device.Id()}", async bounded =>
            {
                await using var held = await locks.TryAcquireAsync("sync", device.Id().ToString(), bounded);
                if (held is null) return;
                if (device.Flag("enabled")) await devices.SyncAsync(device.Id(), bounded);
                else await devices.RegisterAsync(device.Id(), bounded);
            }, TimeSpan.FromMinutes(2), token)) Interlocked.Increment(ref failures);
        });
        health.Set("devices", failures == 0 ? "healthy" : "degraded", failures == 0 ? null : $"{failures} 台录像机同步失败或等待重试。" );
    }
}
