using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace VideoPlatform.Infrastructure;

public sealed class Database(NpgsqlDataSource source)
{
    private readonly AsyncLocal<DbSession?> _applicationSession = new();

    /// <summary>资产应用服务显式共享事务，兼容旧仓储和审计入口。</summary>
    public Task<T> InApplicationTransactionAsync<T>(Func<DbSession, Task<T>> action, CancellationToken ct = default)
        => TransactionAsync(async tx =>
        {
            var previous = _applicationSession.Value;
            _applicationSession.Value = tx;
            try { return await action(tx); }
            finally { _applicationSession.Value = previous; }
        }, ct);

    public async Task<List<JsonObject>> QueryAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        if (_applicationSession.Value is { } session) return await session.QueryAsync(sql, args, ct);
        await using var connection = await source.OpenConnectionAsync(ct);
        return await new DbSession(connection).QueryAsync(sql, args, ct);
    }

    public async Task<JsonObject?> OneAsync(string sql, object? args = null, CancellationToken ct = default)
        => (await QueryAsync(sql, args, ct)).FirstOrDefault();

    public async Task<int> ExecuteAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        if (_applicationSession.Value is { } session) return await session.ExecuteAsync(sql, args, ct);
        await using var connection = await source.OpenConnectionAsync(ct);
        return await new DbSession(connection).ExecuteAsync(sql, args, ct);
    }

    public async Task<T> TransactionAsync<T>(Func<DbSession, Task<T>> action, CancellationToken ct = default)
    {
        if (_applicationSession.Value is { } session) return await action(session);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var result = await action(new DbSession(connection, transaction));
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        // API 与 Worker 启动时都会调用本方法，两个进程可能同时执行迁移。
        // 事务内的 pg_advisory_xact_lock 无法在到达之前串行化两个进程，因此必须在
        // 事务之外用独立连接获取会话级锁；独立连接确保锁的生命周期不依赖事务，
        // 且迁移结束、连接关闭时锁自动释放。
        await using var connection = await source.OpenConnectionAsync(ct);
        try
        {
            await using (var acquire = new NpgsqlCommand("select pg_advisory_lock(@key)", connection) { CommandTimeout = 60 })
            {
                acquire.Parameters.AddWithValue("key", MigrationLockKey);
                await acquire.ExecuteScalarAsync(ct);
            }

            await using var transaction = await connection.BeginTransactionAsync(ct);
            await MigrateCoreAsync(new DbSession(connection, transaction), ct);
            await transaction.CommitAsync(ct);
        }
        finally
        {
            try
            {
                await using var release = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection) { CommandTimeout = 5 };
                release.Parameters.AddWithValue("key", MigrationLockKey);
                await release.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                // 连接失效时清除池，避免后续借用者继承会话锁。
                NpgsqlConnection.ClearPool(connection);
            }
        }
    }

    // 迁移串行化锁。已占用的号段：72002000/72002001（引导种子）、72002002（媒体会话）、
    // 72002003（导出）、72002010/72002030（资产与通道分配）。此处选用未占用的 72002099，
    // 避免与媒体会话锁产生交叉阻塞。
    private const long MigrationLockKey = 72002099;

    private static async Task MigrateCoreAsync(DbSession tx, CancellationToken ct)
    {
        await tx.ExecuteAsync("create table if not exists schema_migrations(version text primary key,sha256 text not null,applied_at timestamptz not null default now())", ct: ct);
        var assembly = typeof(Database).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql", StringComparison.Ordinal)).Order())
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql)));
            var existing = await tx.OneAsync("select sha256 from schema_migrations where version=@name", new { name }, ct);
            if (existing is not null)
            {
                // 已执行的迁移内容发生变化属于严重漂移，必须中止启动而非告警后继续。
                if (existing.Text("sha256") != hash) throw new InvalidOperationException($"已执行的迁移内容发生变化：{name}");
                continue;
            }
            await tx.ExecuteAsync(sql, ct: ct);
            await tx.ExecuteAsync("insert into schema_migrations(version,sha256) values(@name,@hash)", new { name, hash }, ct);
        }
    }
}

public sealed class DbSession(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    private NpgsqlCommand Command(string sql, object? args)
    {
        var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        if (args is not null)
        {
            var parameters = args is IDictionary<string, object?> dictionary
                ? dictionary : args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name, p => p.GetValue(args));
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    public async Task<int> ExecuteAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        await using var command = Command(sql, args);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<JsonObject?> OneAsync(string sql, object? args = null, CancellationToken ct = default)
        => (await QueryAsync(sql, args, ct)).FirstOrDefault();

    public async Task<List<JsonObject>> QueryAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        await using var command = Command(sql, args);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<JsonObject>();
        while (await reader.ReadAsync(ct))
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var parts = reader.GetName(i).Split('_');
                var name = parts[0] + string.Concat(parts.Skip(1).Select(s => char.ToUpperInvariant(s[0]) + s[1..]));
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[name] = value is null ? null : reader.GetDataTypeName(i) is "json" or "jsonb"
                    ? JsonNode.Parse((string)value) : JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
            }
            rows.Add(row);
        }
        return rows;
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Text(this JsonNode? row, string key, string fallback = "") => row?[key]?.ToString() ?? fallback;
    public static long Id(this JsonNode? row, string key = "id") => long.TryParse(row.Text(key), out var id) ? id : 0;
    public static bool Flag(this JsonNode? row, string key) => bool.TryParse(row.Text(key), out var value) && value;
    public static DateTimeOffset Time(this JsonNode? row, string key) => DateTimeOffset.Parse(row.Text(key), System.Globalization.CultureInfo.InvariantCulture);
    public static decimal? Decimal(this JsonNode? row, string key) => decimal.TryParse(row.Text(key), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    public static int? NullableInt(this JsonNode? row, string key) => int.TryParse(row.Text(key), out var v) ? v : null;
    public static DateTimeOffset? NullableTime(this JsonNode? row, string key)
    {
        var text = row?[key]?.ToString();
        return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
    }
}
