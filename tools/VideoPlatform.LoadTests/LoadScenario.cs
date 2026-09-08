using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.LoadTests;

public sealed record LoadClient(int Index, string Token);
public sealed record MediaGrant(LoadClient Client, Guid Id, string Kind, JsonObject Body);

public sealed class LoadScenario(LoadEnvironment environment, LoadOptions options, Metrics metrics) : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { MaxConnectionsPerServer = 256, UseCookies = false })
    {
        BaseAddress = new Uri(environment.ApiUrl), Timeout = TimeSpan.FromSeconds(90)
    };
    private readonly List<LoadClient> _clients = new();
    private readonly List<MediaGrant> _media = new();
    private readonly object _gate = new();
    public JsonObject? LoadedDatabase { get; private set; }
    public object? LoadedAdapter { get; private set; }
    public JsonObject? EndDatabase { get; private set; }
    public object? EndAdapter { get; private set; }
    public JsonObject? CleanDatabase { get; private set; }
    public object? CleanAdapter { get; private set; }
    public double QuerySeconds { get; private set; }
    public object? ProcessBefore { get; private set; }
    public object? ProcessAfter { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("开始 100 个账号并发登录，使用真实密码校验与会话接口。");
        await Task.WhenAll(Enumerable.Range(0, LoadOptions.ClientCount).Select(async i =>
        {
            var result = await metrics.SendAsync(_http, "登录", "auth.login", HttpMethod.Post, "/api/v2/auth/login",
                new { username = $"load{i:D4}", password = environment.Password, clientType = "desktop", clientVersion = "2.0.0-load" }, ct: ct);
            var token = result.Text("accessToken");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("登录响应缺少桌面令牌。");
            lock (_gate) _clients.Add(new(i, token));
        }));
        _clients.Sort((a, b) => a.Index.CompareTo(b.Index));
        await Task.WhenAll(_clients.Select(async client =>
        {
            var refreshed = await Send(client, "认证续期", "auth.refresh", HttpMethod.Post, "/api/v2/auth/refresh", ct: ct);
            if (refreshed.Text("accessToken") != client.Token) throw new InvalidOperationException("会话续期错误地更换了稳定令牌。");
        }));
        Console.WriteLine("开始建立 100 路共享实时订阅和 20 路独立模拟回放。");
        await Task.WhenAll(_clients.Select(client => StartMediaAsync(client, "live", 1, ct)));
        await Task.WhenAll(_clients.Take(LoadOptions.PlaybackCount).Select((client, i) => StartMediaAsync(client, "playback", i + 1, ct)));
        await Task.WhenAll(_media.Select(grant => AttachViewerAsync(grant, ct)));
        await RenewAsync("首次媒体续期", ct);
        LoadedDatabase = await DatabaseSnapshotAsync(ct);
        LoadedAdapter = environment.Adapter.Snapshot();
        VerifyLoaded();
        foreach (var path in QueryPaths) await Send(_clients[0], "预热", path, HttpMethod.Get, path, ct: ct);
        ProcessBefore = environment.ProcessSnapshot();
        Console.WriteLine($"会话数量核对通过，开始 {options.DurationSeconds} 秒普通查询，100 个客户端，思考间隔 {options.ThinkMilliseconds} 毫秒。");
        using var renewStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var renew = RenewLoopAsync(renewStop.Token);
        var watch = Stopwatch.StartNew();
        try
        {
            await Task.WhenAll(_clients.Select(async client =>
            {
                await Task.Delay(client.Index * 10, ct);
                var index = client.Index;
                while (watch.Elapsed.TotalSeconds < options.DurationSeconds)
                {
                    var path = QueryPaths[index++ % QueryPaths.Length];
                    try { await Send(client, "普通查询", path, HttpMethod.Get, path, ct: ct); }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
                    if (options.ThinkMilliseconds > 0) await Task.Delay(options.ThinkMilliseconds, ct);
                }
            }));
            QuerySeconds = watch.Elapsed.TotalSeconds;
            ProcessAfter = environment.ProcessSnapshot();
        }
        finally
        {
            await renewStop.CancelAsync();
            await renew;
        }
        await Send(_clients[0], "管理查询", "users.800", HttpMethod.Get, "/api/v2/users?pageSize=50", ct: ct);
        await RenewAsync("结束前媒体续期", ct);
        EndDatabase = await DatabaseSnapshotAsync(ct);
        EndAdapter = environment.Adapter.Snapshot();
        VerifyLoaded(EndDatabase, EndAdapter);
    }

    private static readonly string[] QueryPaths = ["/api/v2/channels?pageSize=50", "/api/v2/alarms?pageSize=50", "/api/v2/organization", "/api/v2/layouts", "/api/v2/favorites"];

    private async Task StartMediaAsync(LoadClient client, string kind, int channelId, CancellationToken ct)
    {
        object request = kind == "live" ? new { channelId, streamType = 2, profile = "native" }
            : new { channelId, start = DateTimeOffset.UtcNow.AddHours(-1), end = DateTimeOffset.UtcNow.AddMinutes(-1), profile = "native" };
        var response = await Send(client, "建立媒体", kind + ".start", HttpMethod.Post, $"/api/v2/{kind}-sessions", request, ct) as JsonObject
            ?? throw new InvalidOperationException("媒体会话返回格式无效。");
        lock (_gate) _media.Add(new(client, Guid.Parse(response.Text("id")), kind, response));
    }

    private async Task AttachViewerAsync(MediaGrant grant, CancellationToken ct)
    {
        var mediaUri = new Uri(grant.Body.Text("httpFlvUrl"));
        var token = QueryHelpers.ParseQuery(mediaUri.Query)["token"].ToString();
        var viewer = "load-viewer-" + grant.Id.ToString("N");
        var response = await metrics.SendAsync(_http, "播放鉴权", grant.Kind + ".onPlay", HttpMethod.Post,
            "/internal/zlm/on-play?key=" + Uri.EscapeDataString(environment.InternalKey),
            new { app = grant.Kind, stream = grant.Body.Text("stream"), @params = "token=" + Uri.EscapeDataString(token), id = viewer, ip = "192.0.2.10" }, ct: ct);
        if (response.Id("code") != 0) throw new InvalidOperationException("真实 API 拒绝模拟播放连接鉴权。");
        environment.Adapter.RegisterViewer(viewer);
    }

    private async Task RenewAsync(string stage, CancellationToken ct)
        => await Task.WhenAll(_media.Select(async grant =>
        {
            var response = await Send(grant.Client, stage, grant.Kind + ".renew", HttpMethod.Post, $"/api/v2/{grant.Kind}-sessions/{grant.Id}/renew", ct: ct);
            if (response.Time("expiresAt") <= DateTimeOffset.UtcNow.AddMinutes(2)) throw new InvalidOperationException("媒体会话未成功续期。");
        }));

    private async Task RenewLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (await timer.WaitForNextTickAsync(ct)) await RenewAsync("定期媒体续期", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void VerifyLoaded() => VerifyLoaded(LoadedDatabase, LoadedAdapter);

    private static void VerifyLoaded(JsonObject? database, object? adapter)
    {
        if (database.Id("users") != 800 || database.Id("authSessions") != 100 || database.Id("liveSessions") != 100 ||
            database.Id("liveStreams") != 1 || database.Id("playbackSessions") != 20 || database.Id("playbackStreams") != 20 || database.Id("viewers") != 120)
            throw new InvalidOperationException("数据库容量核对未通过，实际数量已写入报告。");
        var simulated = JsonSerializer.SerializeToNode(adapter, JsonDefaults.Options);
        if (simulated.Id("liveSessions") != 100 || simulated.Id("liveUpstreams") != 1 || simulated.Id("liveUpstreamStarts") != 1 ||
            simulated.Id("playbackSessions") != 20 || simulated.Id("playbackUpstreams") != 20)
            throw new InvalidOperationException("模拟上游引用计数核对未通过。");
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await Parallel.ForEachAsync(_media.ToArray(), new ParallelOptions { MaxDegreeOfParallelism = 20, CancellationToken = ct }, async (grant, token) =>
        {
            try { await Send(grant.Client, "释放媒体", grant.Kind + ".stop", HttpMethod.Delete, $"/api/v2/{grant.Kind}-sessions/{grant.Id}", ct: token); }
            catch (Exception ex) { failures.Enqueue(ex.GetType().Name); }
        });
        await Parallel.ForEachAsync(_clients.ToArray(), new ParallelOptions { MaxDegreeOfParallelism = 20, CancellationToken = ct }, async (client, token) =>
        {
            try { await Send(client, "退出登录", "auth.logout", HttpMethod.Post, "/api/v2/auth/logout", ct: token); }
            catch (Exception ex) { failures.Enqueue(ex.GetType().Name); }
        });
        CleanDatabase = await DatabaseSnapshotAsync(ct);
        CleanAdapter = environment.Adapter.Snapshot();
        var simulated = JsonSerializer.SerializeToNode(CleanAdapter, JsonDefaults.Options);
        if (!failures.IsEmpty || CleanDatabase.Id("authSessions") != 0 || CleanDatabase.Id("liveSessions") != 0 || CleanDatabase.Id("playbackSessions") != 0 ||
            CleanDatabase.Id("viewers") != 0 || simulated.Id("liveSessions") != 0 || simulated.Id("liveUpstreams") != 0 || simulated.Id("playbackSessions") != 0 || simulated.Id("viewers") != 0)
            throw new InvalidOperationException("API 释放资源核对失败，报告保留实际残留数量；隔离测试数据库仍将删除。");
    }

    private Task<JsonObject?> DatabaseSnapshotAsync(CancellationToken ct)
        => environment.Db.OneAsync("""
            select (select count(*) from users) as users,
              (select count(*) from sessions where revoked_at is null and expires_at>now()) as auth_sessions,
              (select count(*) from media_sessions where kind='live' and closed_at is null and expires_at>now()) as live_sessions,
              (select count(distinct stream) from media_sessions where kind='live' and closed_at is null and expires_at>now()) as live_streams,
              (select count(*) from media_sessions where kind='playback' and closed_at is null and expires_at>now()) as playback_sessions,
              (select count(distinct stream) from media_sessions where kind='playback' and closed_at is null and expires_at>now()) as playback_streams,
              (select count(*) from viewer_connections) as viewers
            """, ct: ct);

    private Task<JsonNode?> Send(LoadClient client, string stage, string operation, HttpMethod method, string path, object? body = null, CancellationToken ct = default)
        => metrics.SendAsync(_http, stage, operation, method, path, body, client.Token, ct);

    public void Dispose() => _http.Dispose();
}
