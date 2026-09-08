using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace VideoPlatform.Infrastructure.Jobs;

public sealed class WorkerOptions
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public int DeviceConcurrency { get; init; } = 4;
    public int MaintenanceConcurrency { get; init; } = 4;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan ExportTimeout { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan ExportLease { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MediaStartGrace { get; init; } = TimeSpan.FromMinutes(2);
    public int BatchSize { get; init; } = 200;
    public int ExportFailureLimit { get; init; } = 5;
}

public static class JobsBootstrap
{
    public static IServiceCollection AddPlatformJobs(this IServiceCollection services, IConfiguration configuration)
    {
        static int Read(IConfiguration c, string name, int fallback, int min, int max)
            => c[name] is not { } value ? fallback : int.TryParse(value, out var n) && n >= min && n <= max
                ? n : throw new InvalidOperationException($"后台任务配置 {name} 必须介于 {min} 和 {max} 之间。");
        services.AddSingleton(new WorkerOptions
        {
            DeviceConcurrency = Read(configuration, "WORKER_DEVICE_CONCURRENCY", 4, 1, 32),
            MaintenanceConcurrency = Read(configuration, "WORKER_MAINTENANCE_CONCURRENCY", 4, 1, 16),
            ExportTimeout = TimeSpan.FromMinutes(Read(configuration, "WORKER_EXPORT_TIMEOUT_MINUTES", 360, 1, 2880))
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JobLocks>();
        services.AddSingleton<JobHealth>();
        services.AddSingleton<ResourceRetries>();
        services.AddSingleton<ExportFiles>();
        services.AddSingleton<AlarmIngestionJob>();
        services.AddSingleton<DeviceSyncJob>();
        services.AddSingleton<MediaMaintenanceJob>();
        services.AddSingleton<ExportProcessingJob>();
        services.AddSingleton<RetentionJob>();
        return services;
    }
}

// 使用独立连接持有会话锁，长时间的设备调用不占用数据库事务。
public sealed class JobLocks(NpgsqlDataSource source)
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string kind, string id, CancellationToken ct)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"VideoPlatform.Worker.v2:{kind}:{id}"));
        var key = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var connection = await source.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            if ((bool)(await command.ExecuteScalarAsync(ct))!) return new HeldLock(connection, key);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class HeldLock(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection) { CommandTimeout = 5 };
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                // 连接失效时清除池，避免任何后续借用者继承会话锁。
                NpgsqlConnection.ClearPool(connection);
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}

public sealed class ResourceRetries(TimeProvider clock, ILogger<ResourceRetries> logger)
{
    private sealed record Failure(int Count, DateTimeOffset Next, DateTimeOffset Last);
    private readonly ConcurrentDictionary<string, Failure> _failures = new();

    public async Task<bool> RunAsync(string key, Func<CancellationToken, Task> action, TimeSpan timeout, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_failures.TryGetValue(key, out var previous) && previous.Next > now) return false;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout);
        try
        {
            await action(bounded.Token);
            _failures.TryRemove(key, out _);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var count = Math.Min((previous?.Count ?? 0) + 1, 10);
            var delay = TimeSpan.FromSeconds(Math.Min(60, 2 * (1 << count)));
            _failures[key] = new(count, now + delay, now);
            logger.LogWarning("后台资源 {Resource} 处理失败，{Seconds} 秒后重试。异常类型：{ErrorType}", key, delay.TotalSeconds, ex.GetType().Name);
            foreach (var stale in _failures.Where(p => p.Value.Last < now.AddHours(-1)).Select(p => p.Key)) _failures.TryRemove(stale, out _);
            if (_failures.Count > 4096)
                foreach (var stale in _failures.OrderBy(p => p.Value.Last).Take(_failures.Count - 4096).Select(p => p.Key)) _failures.TryRemove(stale, out _);
            return false;
        }
    }
}

public sealed class JobHealth(Database db, WorkerOptions options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, object> _states = new();
    public void Set(string name, string state, string? message = null)
        => _states[name] = new { state, message, checkedAt = clock.GetUtcNow() };

    public Task BeatAsync(CancellationToken ct)
        => db.ExecuteAsync("insert into service_heartbeats(name,checked_at,details) values(@name,now(),cast(@details as jsonb)) on conflict(name) do update set checked_at=excluded.checked_at,details=excluded.details",
            new { name = $"worker:{options.InstanceId}", details = JsonSerializer.Serialize(new { version = "2.0.0", instanceId = options.InstanceId, jobs = _states }, JsonDefaults.Options) }, ct);
}

public static class JobAuthorization
{
    // 表达式仅由本程序集的固定 SQL 片段组成，不接受外部输入。
    public static string ForRow(string alias, string permission)
    {
        if (alias is not ("m" or "p" or "j")) throw new ArgumentException("后台授权表别名无效。");
        if (permission is not ("'export.create'" or "'ptz.control'" or "case when m.kind='live' then 'live.view' else 'playback.view' end"))
            throw new ArgumentException("后台授权权限表达式无效。");
        var scope = AccessService.ChannelPredicate.Replace("@userId", $"{alias}.user_id", StringComparison.Ordinal);
        return $"""
            exists(select 1 from sessions s join users u on u.id=s.user_id
              where s.id={alias}.auth_session_id and s.user_id={alias}.user_id and s.revoked_at is null and s.expires_at>now() and u.status='active')
            and exists(select 1 from user_roles ur join roles r on r.id=ur.role_id join role_permissions rp on rp.role_id=r.id
              where ur.user_id={alias}.user_id and r.status='active' and rp.permission_code=({permission}))
            and exists(select 1 from {AccessService.ChannelFrom}
              where c.id={alias}.channel_id and d.enabled and c.status<>'disabled' and ({scope})
              {(alias == "p" ? "" : $"and d.id={alias}.device_id")})
            """;
    }
}

public static class JobOutbox
{
    public static Task ChangedAsync(DbSession tx, System.Text.Json.Nodes.JsonObject job, CancellationToken ct)
        => tx.ExecuteAsync("""
            insert into outbox(kind,resource_id,user_id,channel_id,device_id,version)
            values('export.changed',@id,@userId,@channelId,@deviceId,(extract(epoch from clock_timestamp())*1000000)::bigint)
            """, new { id = job.Text("id"), userId = job.Id("userId"), channelId = job.Id("channelId"), deviceId = job.Id("deviceId") }, ct);
}
