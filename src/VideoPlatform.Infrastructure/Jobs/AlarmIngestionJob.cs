using Microsoft.Extensions.Logging;
using VideoPlatform.Application;

namespace VideoPlatform.Infrastructure.Jobs;

public sealed class AlarmIngestionJob(Database db, IDeviceAdapter adapter, JobLocks locks, ResourceRetries retries,
    WorkerOptions options, TimeProvider clock, JobHealth health, ILogger<AlarmIngestionJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var devices = await db.QueryAsync("select id from devices order by id", ct: ct);
        var failures = 0;
        await Parallel.ForEachAsync(devices, new ParallelOptions { MaxDegreeOfParallelism = options.DeviceConcurrency, CancellationToken = ct }, async (device, token) =>
        {
            if (!await retries.RunAsync($"alarm:{device.Id()}", t => IngestDeviceAsync(device.Id(), t), TimeSpan.FromMinutes(2), token))
                Interlocked.Increment(ref failures);
        });
        health.Set("alarms", failures == 0 ? "healthy" : "degraded", failures == 0 ? null : $"{failures} 台设备的报警读取待重试，未确认记录仍保留在适配器。" );
    }

    public async Task IngestDeviceAsync(long deviceId, CancellationToken ct)
    {
        await using var held = await locks.TryAcquireAsync("alarms", deviceId.ToString(), ct);
        if (held is null) return;
        var checkpoint = await db.OneAsync("select cursor from adapter_checkpoints where device_id=@deviceId", new { deviceId }, ct);
        var previous = checkpoint.Text("cursor");
        if (string.IsNullOrEmpty(previous)) previous = "0:0";
        var response = await adapter.SendAsync(HttpMethod.Get, $"/internal/devices/{deviceId}/events?after={Uri.EscapeDataString(previous)}&limit=100", ct: ct);
        var batch = AdapterAlarmBatch.Parse(deviceId, previous, response, clock.GetUtcNow());
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("insert into adapter_checkpoints(device_id,cursor) values(@deviceId,'0:0') on conflict do nothing", new { deviceId }, ct);
            var stored = await tx.OneAsync("select cursor from adapter_checkpoints where device_id=@deviceId for update", new { deviceId }, ct);
            var current = stored.Text("cursor");
            if ((string.IsNullOrEmpty(current) ? "0:0" : current) != previous)
                throw new InvalidOperationException("报警检查点已被其他任务修改，本批次未确认。");
            var channels = (await tx.QueryAsync("select id,device_channel from channels where device_id=@deviceId", new { deviceId }, ct))
                .ToDictionary(c => (int)c.Id("deviceChannel"), c => c.Id());
            foreach (var item in batch.Items)
            {
                long? channelId = item.Channel is { } number && channels.TryGetValue(number, out var id) ? id : null;
                var inserted = await tx.OneAsync("""
                    insert into alarm_events(device_id,channel_id,source_id,event_type,occurred_at,recovered,payload,image_payload)
                    values(@deviceId,@channelId,@sourceId,@eventType,@occurredAt,@recovered,cast(@payload as jsonb),@image)
                    on conflict(device_id,source_id) do nothing returning id,version
                    """, new { deviceId, channelId, item.SourceId, item.EventType, item.OccurredAt, item.Recovered, item.Payload, item.Image }, ct);
                if (inserted is null) continue;
                var alarmId = inserted.Id();
                await tx.ExecuteAsync("insert into alarm_history(alarm_id,action,note) values(@alarmId,@action,@note)",
                    new { alarmId, action = item.Warning is null ? "received" : "quarantined", note = item.Warning ?? (item.Recovered ? "收到设备恢复事件。" : "收到设备报警事件。") }, ct);
                // 未登记通道不能误关联成设备级报警；恢复只更新设备状态，人工处理状态保持原值。
                if (item.Recovered && (item.Channel is null || channelId is not null))
                {
                    var original = await tx.OneAsync("""
                        select id from alarm_events where device_id=@deviceId and channel_id is not distinct from @channelId::bigint
                          and event_type=@eventType and not recovered and recovery_of is null and occurred_at<=@occurredAt
                          and (@channelId::bigint is not null or payload->>'channel' is null)
                        order by occurred_at desc,id desc limit 1 for update
                        """, new { deviceId, channelId, item.EventType, item.OccurredAt }, ct);
                    if (original is not null)
                    {
                        var originalId = original.Id();
                        await tx.ExecuteAsync("update alarm_events set recovered=true,version=version+1 where id=@originalId; update alarm_events set recovery_of=@originalId where id=@alarmId", new { originalId, alarmId }, ct);
                        await tx.ExecuteAsync("insert into alarm_history(alarm_id,action,note) values(@originalId,'recovered',@note)", new { originalId, note = $"设备已恢复，关联恢复事件 {alarmId}。" }, ct);
                        await NotifyAsync(tx, originalId, ct);
                    }
                }
                await NotifyAsync(tx, alarmId, ct);
            }
            await tx.ExecuteAsync("update adapter_checkpoints set cursor=@cursor,updated_at=now() where device_id=@deviceId", new { deviceId, cursor = batch.NextCursor }, ct);
            return true;
        }, ct);
        foreach (var item in batch.Items.Where(i => i.Warning is not null))
            logger.LogWarning("设备 {DeviceId} 的报警记录 {SourceId} 已持久化隔离：{Reason}", deviceId, item.SourceId, item.Warning);
        // 即使上一轮确认失败且本轮为空，仍重新确认已提交游标。
        await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{deviceId}/events/ack", new { cursor = batch.NextCursor }, ct);
    }

    private static Task NotifyAsync(DbSession tx, long id, CancellationToken ct)
        => tx.ExecuteAsync("insert into outbox(kind,resource_id,channel_id,device_id,version) select 'alarm.changed',id::text,channel_id,device_id,version from alarm_events where id=@id", new { id }, ct);
}
