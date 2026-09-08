using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.LoadTests;

public sealed class LoadEnvironment : IAsyncDisposable
{
    private readonly NpgsqlDataSource _root;
    private readonly ServiceProvider _services;
    private readonly string _runtimeDirectory;
    private readonly List<string> _apiLog = new();
    private Process? _api;
    public string DatabaseName { get; }
    public string ApiUrl { get; private set; } = "";
    public string ReportDirectory { get; }
    public string Password { get; } = "Load-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
    public string InternalKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public SimulatedAdapter Adapter { get; }
    public Database Db => _services.GetRequiredService<Database>();
    public string DatabaseVersion { get; private set; } = "";
    public long DatabaseMaxConnections { get; private set; }
    public string ApiSha256 { get; private set; } = "";
    public string ApiAssemblyVersion { get; private set; } = "";
    public Dictionary<string, string> AssemblyHashes { get; } = new();
    public bool Cleaned { get; private set; }
    private readonly LoadOptions _options;
    private readonly IConfigurationRoot _configuration;

    private LoadEnvironment(LoadOptions options, NpgsqlDataSource root, string databaseName, string runId)
    {
        _options = options; _root = root; DatabaseName = databaseName;
        ReportDirectory = Path.Combine(options.OutputRoot, runId);
        _runtimeDirectory = Path.GetFullPath(Path.Combine("tools", "VideoPlatform.LoadTests", ".runtime", runId));
        Directory.CreateDirectory(ReportDirectory);
        Directory.CreateDirectory(_runtimeDirectory);
        Adapter = new(InternalKey);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PLATFORM_DATABASE_URL"] = new NpgsqlConnectionStringBuilder(options.DatabaseUrl) { Database = databaseName, MaxPoolSize = options.ApiPoolSize, ApplicationName = "VideoPlatform.LoadTests" }.ConnectionString,
            ["PLATFORM_DATA_PATH"] = Path.Combine(_runtimeDirectory, "data"),
            ["PLATFORM_ADMIN_USER"] = "load0000", ["PLATFORM_ADMIN_PASSWORD"] = Password,
            ["HIK_ADAPTER_INTERNAL_KEY"] = InternalKey,
            ["ZLM_API_SECRET"] = InternalKey
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddPlatform(_configuration);
        _services = services.BuildServiceProvider();
    }

    public static async Task<LoadEnvironment> CreateAsync(LoadOptions options, CancellationToken ct)
    {
        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var databaseName = "vp_load_" + Guid.NewGuid().ToString("N");
        var root = NpgsqlDataSource.Create(options.DatabaseUrl);
        await using (var command = root.CreateCommand($"create database {databaseName}")) await command.ExecuteNonQueryAsync(ct);
        var environment = new LoadEnvironment(options, root, databaseName, runId);
        try
        {
            await Bootstrap.InitializeAsync(environment._services, ct);
            await environment.SeedAsync(ct);
            await environment.Adapter.StartAsync(ct);
            await environment.StartApiAsync(ct);
            return environment;
        }
        catch { await environment.DisposeAsync(); throw; }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        DatabaseVersion = (await Db.OneAsync("select version() as version", ct: ct)).Text("version");
        DatabaseMaxConnections = (await Db.OneAsync("select current_setting('max_connections')::int as count", ct: ct)).Id("count");
        var hash = Passwords.Hash(Password);
        var cipher = _services.GetRequiredService<SecretStore>().Protect("Simulated-Device-Password-2026!");
        // 数据准备不计入请求时延；使用真实密码散列算法，批量账号复用测试散列以缩短准备时间。
        await Db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("""
                insert into users(username,password_hash,display_name)
                  select 'load'||lpad(n::text,4,'0'),@hash,'容量测试账号 '||n from generate_series(1,799) n;
                insert into user_roles(user_id,role_id) select u.id,r.id from users u cross join roles r where u.username<>'load0000' and r.code='operator';
                insert into devices(id,name,host,port,username,password_cipher,enabled,status,model)
                  values(1,'容量模拟录像机','load-simulator.invalid',8000,'simulated',@cipher,true,'online','CONTROL-PLANE-SIMULATOR');
                insert into channels(id,device_id,device_channel,name,status,ptz_capable,codec)
                  select n,1,n,'模拟通道 '||n,'online',true,'h264' from generate_series(1,81) n;
                insert into user_scopes(user_id,type,scope_id) select u.id,'channel',c.id from users u cross join channels c where u.username<>'load0000';
                select setval(pg_get_serial_sequence('devices','id'),1,true);
                select setval(pg_get_serial_sequence('channels','id'),81,true);
                """, new { hash, cipher }, ct);
            return true;
        }, ct);
        var count = (await Db.OneAsync("select count(*) as count from users", ct: ct)).Id("count");
        if (count != LoadOptions.AccountCount) throw new InvalidOperationException($"容量数据准备失败，账号数为 {count}。");
    }

    private async Task StartApiAsync(CancellationToken ct)
    {
        ApiSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(_options.ApiDll, ct)));
        ApiAssemblyVersion = FileVersionInfo.GetVersionInfo(_options.ApiDll).ProductVersion ?? "unknown";
        foreach (var name in new[] { "VideoPlatform.Api", "VideoPlatform.Infrastructure", "VideoPlatform.Application", "VideoPlatform.Domain", "VideoPlatform.Contracts" })
        {
            var path = Path.Combine(Path.GetDirectoryName(_options.ApiDll)!, name + ".dll");
            AssemblyHashes[name] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, ct)));
        }
        ApiUrl = $"http://127.0.0.1:{LoadOptions.FreeLoopbackPort()}";
        var start = new ProcessStartInfo(_options.Dotnet)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(_options.ApiDll)!
        };
        start.ArgumentList.Add(_options.ApiDll);
        foreach (var item in _configuration.AsEnumerable()) if (item.Value is not null) start.Environment[item.Key] = item.Value;
        start.Environment["PLATFORM_API_URL"] = ApiUrl;
        start.Environment["PLATFORM_PUBLIC_URL"] = ApiUrl;
        start.Environment["HIK_ADAPTER_API_URL"] = Adapter.Url;
        start.Environment["ZLM_API_URL"] = Adapter.Url;
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning";
        _api = Process.Start(start) ?? throw new InvalidOperationException("无法启动容量测试 API 进程。");
        void Capture(object sender, DataReceivedEventArgs args)
        {
            if (args.Data is null) return;
            lock (_apiLog) if (_apiLog.Count < 10000) _apiLog.Add(args.Data);
        }
        _api.OutputDataReceived += Capture; _api.ErrorDataReceived += Capture;
        _api.BeginOutputReadLine(); _api.BeginErrorReadLine();
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (_api.HasExited) throw new InvalidOperationException($"测试 API 启动失败，退出码 {_api.ExitCode}，请查看本次 api.log。");
            try
            {
                using var response = await probe.GetAsync(ApiUrl + "/health", ct);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(200, ct);
        }
        throw new TimeoutException("容量测试 API 启动超时。");
    }

    public object ProcessSnapshot()
    {
        _api?.Refresh();
        return new { processId = _api?.Id, workingSetBytes = _api?.WorkingSet64, totalProcessorSeconds = _api?.TotalProcessorTime.TotalSeconds };
    }

    public async ValueTask DisposeAsync()
    {
        if (Cleaned) return;
        if (_api is not null)
        {
            if (!_api.HasExited) { _api.Kill(entireProcessTree: true); await _api.WaitForExitAsync(); }
            _api.Dispose();
        }
        await Adapter.DisposeAsync();
        await _services.DisposeAsync();
        // 名称完全由工具生成，绝不删除配置连接串指定的原数据库。
        if (!DatabaseName.StartsWith("vp_load_", StringComparison.Ordinal) || DatabaseName.Length != 40) throw new InvalidOperationException("容量测试数据库名称校验失败，已拒绝清理。");
        await using (var command = _root.CreateCommand($"drop database if exists {DatabaseName} with (force)")) await command.ExecuteNonQueryAsync();
        await _root.DisposeAsync();
        string[] lines;
        lock (_apiLog) lines = _apiLog.ToArray();
        await File.WriteAllLinesAsync(Path.Combine(ReportDirectory, "api.log"), lines);
        if (Directory.Exists(_runtimeDirectory)) Directory.Delete(_runtimeDirectory, true);
        Cleaned = true;
    }
}
