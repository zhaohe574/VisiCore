using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using VideoPlatform.Application;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Jobs;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VideoPlatform.Worker.Tests;

public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WORKER_TEST_DATABASE_URL")))
            Skip = "未设置 WORKER_TEST_DATABASE_URL，未运行 PostgreSQL 集成测试。";
    }
}

public sealed class FakeAdapter : IDeviceAdapter
{
    public ConcurrentQueue<(HttpMethod Method, string Path, JsonNode? Body)> Calls { get; } = new();
    public Func<HttpMethod, string, JsonNode?, CancellationToken, Task<JsonNode?>>? Handler { get; set; }
    public Guid BootId { get; } = Guid.NewGuid();
    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        var json = body is null ? null : JsonSerializer.SerializeToNode(body, JsonDefaults.Options);
        Calls.Enqueue((method, path, json));
        if (Handler is not null)
        {
            var response = await Handler(method, path, json, ct);
            return response is null ? null : JsonNode.Parse(response.ToJsonString());
        }
        if (path == "/internal/sessions") return new JsonObject { ["bootId"] = BootId, ["devices"] = new JsonArray(), ["live"] = new JsonArray(), ["playback"] = new JsonArray(), ["exports"] = new JsonArray() };
        if (path.Contains("/exports/") && method == HttpMethod.Get) throw new PlatformException(404, "export.missing", "模拟任务不存在。");
        if (path.Contains("/exports")) return new JsonObject { ["state"] = method == HttpMethod.Delete ? "cancelled" : "running", ["progress"] = 0 };
        return new JsonObject();
    }
}

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

public sealed class DatabaseHarness : IAsyncDisposable
{
    private readonly NpgsqlDataSource _root;
    private readonly string _schema;
    private readonly string _data;
    public ServiceProvider Services { get; }
    public Database Db => Services.GetRequiredService<Database>();
    public FakeAdapter Adapter => Services.GetRequiredService<FakeAdapter>();
    public WorkerOptions Options => Services.GetRequiredService<WorkerOptions>();
    public TestClock Clock => (TestClock)Services.GetRequiredService<TimeProvider>();
    public Guid AuthId { get; } = Guid.NewGuid();
    public long UserId { get; private set; }

    private DatabaseHarness(NpgsqlDataSource root, string schema, string data, ServiceProvider services)
    {
        _root = root; _schema = schema; _data = data; Services = services;
    }

    public static async Task<DatabaseHarness> CreateAsync()
    {
        var url = Environment.GetEnvironmentVariable("WORKER_TEST_DATABASE_URL") ?? throw new InvalidOperationException("缺少测试数据库连接串。");
        var schema = "worker_test_" + Guid.NewGuid().ToString("N");
        var data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".runtime", schema));
        var root = NpgsqlDataSource.Create(url);
        await using (var cmd = root.CreateCommand($"create schema {schema}")) await cmd.ExecuteNonQueryAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PLATFORM_DATABASE_URL"] = new NpgsqlConnectionStringBuilder(url) { SearchPath = schema, MaxPoolSize = 32 }.ConnectionString,
            ["PLATFORM_DATA_PATH"] = data,
            ["PLATFORM_ADMIN_PASSWORD"] = "Worker-Test-Password-2026!",
            ["HIK_ADAPTER_INTERNAL_KEY"] = "Worker-Test-Key",
            ["ZLM_API_URL"] = "http://127.0.0.1:1"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddPlatform(config);
        services.AddPlatformJobs(config);
        services.AddSingleton<FakeAdapter>();
        services.AddSingleton<IDeviceAdapter>(s => s.GetRequiredService<FakeAdapter>());
        services.AddSingleton<TimeProvider>(new TestClock());
        var harness = new DatabaseHarness(root, schema, data, services.BuildServiceProvider());
        try
        {
            await Bootstrap.InitializeAsync(harness.Services);
            harness.UserId = (await harness.Db.OneAsync("select id from users where username='admin'")).Id();
            await harness.Db.ExecuteAsync("insert into sessions(id,user_id,token_hash,client_type,client_version,expires_at) values(@id,@userId,@hash,'desktop','2.0.0',now()+interval '8 hours')",
                new { id = harness.AuthId, userId = harness.UserId, hash = Passwords.TokenHash("session") });
            var cipher = harness.Services.GetRequiredService<SecretStore>().Protect("device-test-password");
            await harness.Db.ExecuteAsync("""
                insert into devices(id,name,host,port,username,password_cipher,status) values(1,'录像机一','127.0.0.2',8000,'admin',@cipher,'online'),(2,'录像机二','127.0.0.3',8000,'admin',@cipher,'online');
                insert into channels(id,device_id,device_channel,name,status,ptz_capable) values(11,1,1,'一号通道','online',true),(12,1,2,'二号通道','online',true),(21,2,1,'异设备同号通道','online',true);
                """, new { cipher });
            return harness;
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    public async Task<Guid> AddExportAsync(long deviceId = 1, long channelId = 11, string state = "queued", string? workerId = null)
    {
        var id = Guid.NewGuid();
        await Db.ExecuteAsync("""
            insert into export_jobs(id,user_id,auth_session_id,channel_id,device_id,start_at,end_at,state,started_at,worker_id,lease_until)
            values(@id,@userId,@authId,@channelId,@deviceId,now()-interval '1 hour',now(),@state,case when @state='running' then now() else null end,@workerId,now()-interval '1 minute')
            """, new { id, userId = UserId, authId = AuthId, channelId, deviceId, state, workerId });
        return id;
    }

    public async Task<Guid> AddMediaAsync(long deviceId = 1, long channelId = 11, string kind = "live")
    {
        var id = Guid.NewGuid();
        var secret = Services.GetRequiredService<SecretStore>().Protect("media-test-token");
        await Db.ExecuteAsync("""
            insert into media_sessions(id,user_id,auth_session_id,device_id,channel_id,kind,profile,token_hash,token_cipher,state,expires_at)
            values(@id,@userId,@authId,@deviceId,@channelId,@kind,'native',@hash,@secret,'playing',now()+interval '3 minutes')
            """, new { id, userId = UserId, authId = AuthId, deviceId, channelId, kind, hash = Passwords.TokenHash(id.ToString()), secret });
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await using (var cmd = _root.CreateCommand($"drop schema if exists {_schema} cascade")) await cmd.ExecuteNonQueryAsync();
        await _root.DisposeAsync();
        if (Directory.Exists(_data)) Directory.Delete(_data, true);
    }
}
