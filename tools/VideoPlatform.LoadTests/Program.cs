using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using VideoPlatform.LoadTests;

Console.OutputEncoding = Encoding.UTF8;
LoadOptions options;
try { options = LoadOptions.Read(); }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; stop.Cancel(); };
var metrics = new Metrics();
var started = DateTimeOffset.UtcNow;
var failures = new List<string>();
LoadEnvironment? environment = null;
LoadScenario? scenario = null;
var workflowPassed = false;
var cleanupPassed = false;
try
{
    Console.WriteLine("正在准备独立容量测试数据库和专用 API；测试不连接现有业务 API。");
    environment = await LoadEnvironment.CreateAsync(options, stop.Token);
    Console.WriteLine($"专用测试 API：{environment.ApiUrl}，数据集：800 账号、81 模拟通道。");
    scenario = new(environment, options, metrics);
    await scenario.RunAsync(stop.Token);
    workflowPassed = true;
}
catch (Exception ex)
{
    failures.Add(ex is InvalidOperationException or TimeoutException ? ex.Message : $"容量测试失败，异常类型：{ex.GetType().Name}。");
    Console.Error.WriteLine(failures[^1]);
}
finally
{
    if (scenario is not null)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await scenario.CleanupAsync(cleanup.Token);
            cleanupPassed = true;
        }
        catch (Exception ex) { failures.Add($"API 资源清理失败：{ex.Message}"); }
        scenario.Dispose();
    }
    if (environment is not null)
    {
        try { await environment.DisposeAsync(); }
        catch (Exception ex) { failures.Add($"隔离环境清理失败：{ex.GetType().Name}，数据库 {environment.DatabaseName} 需检查。"); }
    }
}

if (environment is null) return 2;
var samples = metrics.Samples;
var summaries = metrics.Summaries();
var queryLatencies = samples.Where(s => s.Stage == "普通查询").Select(s => s.Milliseconds).Order().ToArray();
var queryP95 = Metrics.Percentile(queryLatencies, .95);
var latencyPassed = queryLatencies.Length > 0 && queryP95 < 500;
var requestsPassed = samples.All(s => s.Success);
var passed = workflowPassed && cleanupPassed && environment.Cleaned && latencyPassed && requestsPassed;
var report = new
{
    startedAt = started, endedAt = DateTimeOffset.UtcNow, simulatedMedia = true,
    conclusion = passed ? "本次模拟控制接口容量目标满足。" : "本次模拟控制接口容量目标未全部满足，详见失败项和指标。",
    boundary = "真实 API 进程、真实 PostgreSQL、模拟适配器及模拟 ZLMediaKit 控制接口；没有传输或解码视频，不代表实机媒体容量。",
    passed, workflowPassed, cleanupPassed, databaseDropped = environment.Cleaned, latencyPassed, requestsPassed,
    workload = new { accounts = 800, concurrentLogins = 100, liveSubscribers = 100, liveUpstreams = 1, playbackSessions = 20,
        channels = 81, options.DurationSeconds, options.ThinkMilliseconds, loginType = "desktop", queryWorkers = 100, mediaRenewSeconds = 60 },
    environment = new { os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.OSArchitecture.ToString(), runtime = Environment.Version.ToString(),
        logicalProcessors = Environment.ProcessorCount, databaseVersion = environment.DatabaseVersion, databaseMaxConnections = environment.DatabaseMaxConnections,
        apiPoolSize = options.ApiPoolSize, apiVersion = environment.ApiAssemblyVersion,
        apiSha256 = environment.ApiSha256, assemblyHashes = environment.AssemblyHashes, apiUrl = environment.ApiUrl, databaseName = environment.DatabaseName,
        note = "负载进程、API 和模拟器在同机运行；数据库为本次新建数据库。账号批量准备复用符合正式算法的测试密码散列，不计入 HTTP 请求时延。" },
    query = new { requests = queryLatencies.Length, elapsedSeconds = scenario?.QuerySeconds ?? 0,
        requestsPerSecond = scenario?.QuerySeconds > 0 ? queryLatencies.Length / scenario.QuerySeconds : 0, p95Milliseconds = queryP95, targetP95Milliseconds = 500 },
    scenario?.LoadedDatabase, scenario?.LoadedAdapter, scenario?.EndDatabase, scenario?.EndAdapter, scenario?.CleanDatabase, scenario?.CleanAdapter,
    scenario?.ProcessBefore, scenario?.ProcessAfter, summaries, samples, failures
};
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
await File.WriteAllTextAsync(Path.Combine(environment.ReportDirectory, "report.json"), JsonSerializer.Serialize(report, jsonOptions));
var markdown = new StringBuilder();
markdown.AppendLine("# API 模拟容量实测报告").AppendLine();
markdown.AppendLine($"开始时间：{started:O}。结论：{report.conclusion}").AppendLine();
markdown.AppendLine(report.boundary).AppendLine();
markdown.AppendLine($"800 个账号，100 个并发登录，100 路共享实时订阅，1 路模拟上游，20 路独立模拟回放。普通查询：{options.DurationSeconds} 秒，每客户端间隔 {options.ThinkMilliseconds} 毫秒。").AppendLine();
markdown.AppendLine($"普通查询共 {queryLatencies.Length} 次，P95 为 {queryP95.ToString("F2", CultureInfo.InvariantCulture)} 毫秒，目标小于 500 毫秒。资源释放核对：{cleanupPassed}；隔离数据库已删除：{environment.Cleaned}。").AppendLine();
markdown.AppendLine("| 阶段 | 操作 | 请求 | 失败 | P50 毫秒 | P95 毫秒 | P99 毫秒 | 最大毫秒 |");
markdown.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
foreach (var metric in summaries)
    markdown.AppendLine(FormattableString.Invariant($"| {metric.Stage} | {metric.Operation} | {metric.Requests} | {metric.Failures} | {metric.P50Ms:F2} | {metric.P95Ms:F2} | {metric.P99Ms:F2} | {metric.MaxMs:F2} |"));
markdown.AppendLine().AppendLine($"API 文件 SHA-256：`{environment.ApiSha256}`。完整请求样本、数据库／适配器数量核对和运行环境见同目录 `report.json`。");
foreach (var failure in failures) markdown.AppendLine().AppendLine("失败项：" + failure);
await File.WriteAllTextAsync(Path.Combine(environment.ReportDirectory, "report.md"), markdown.ToString());
Console.WriteLine($"普通查询 {queryLatencies.Length} 次，P95：{queryP95:F2} 毫秒，HTTP 失败：{samples.Count(s => !s.Success)} 次。");
Console.WriteLine($"报告：{Path.Combine(environment.ReportDirectory, "report.md")}");
Console.WriteLine(report.conclusion);
return passed ? 0 : 1;
