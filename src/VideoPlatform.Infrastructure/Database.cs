using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace VideoPlatform.Infrastructure;

public sealed class Database(NpgsqlDataSource source)
{
    public async Task<List<JsonObject>> QueryAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        return await new DbSession(connection).QueryAsync(sql, args, ct);
    }

    public async Task<JsonObject?> OneAsync(string sql, object? args = null, CancellationToken ct = default)
        => (await QueryAsync(sql, args, ct)).FirstOrDefault();

    public async Task<int> ExecuteAsync(string sql, object? args = null, CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        return await new DbSession(connection).ExecuteAsync(sql, args, ct);
    }

    public async Task<T> TransactionAsync<T>(Func<DbSession, Task<T>> action, CancellationToken ct = default)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var result = await action(new DbSession(connection, transaction));
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002000); create table if not exists schema_migrations(version text primary key,sha256 text not null,applied_at timestamptz not null default now())", ct: ct);
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
                    if (existing.Text("sha256") != hash) throw new InvalidOperationException($"已执行的迁移内容发生变化：{name}");
                    continue;
                }
                await tx.ExecuteAsync(sql, ct: ct);
                await tx.ExecuteAsync("insert into schema_migrations(version,sha256) values(@name,@hash)", new { name, hash }, ct);
            }
            return true;
        }, ct);
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
}
