using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using Xunit;
using Generated = VideoPlatform.Client.Generated;

namespace VideoPlatform.Desktop.Tests;

public sealed class GeneratedClientIntegrationTests
{
    private static readonly Guid SessionId = Guid.Parse("0925b789-936a-485b-9cbb-c3cb4e5a7045");
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task LoginRestoreLogoutAndServerSwitchUseGeneratedAuthentication()
    {
        var seen = new List<(string Host, string Path, string? Token)>();
        using var http = new HttpClient(new StubHandler(async request =>
        {
            seen.Add((request.RequestUri!.Host, request.RequestUri.AbsolutePath, request.Headers.Authorization?.Parameter));
            Assert.False(request.Headers.Contains("X-CSRF-Token"));
            if (request.RequestUri.AbsolutePath.EndsWith("login"))
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("tester", body.GetProperty("username").GetString());
                Assert.Equal("desktop", body.GetProperty("clientType").GetString());
                Assert.Equal(typeof(SessionService).Assembly.GetName().Version!.ToString(3), body.GetProperty("clientVersion").GetString());
                return StubHandler.Ok(Fixtures.Login());
            }
            return request.RequestUri.AbsolutePath.EndsWith("logout") ? StubHandler.Empty() : StubHandler.Ok(Fixtures.User("live.view"));
        }));
        var store = new MemoryCredentials();
        var session = new SessionService(http, store);
        session.SetServer("https://first.test");
        await session.LoginAsync(" tester ", "测试密码");
        var restored = new SessionService(http, store);
        restored.SetServer("https://first.test");
        Assert.True(await restored.RestoreAsync());
        Assert.True(restored.CurrentUser!.Can("live.view"));
        await restored.LogoutAsync();
        restored.SetServer("https://second.test");
        await restored.LoginAsync("tester", "测试密码");
        Assert.Equal(new[]
        {
            ("first.test", "/api/v2/auth/login", (string?)null),
            ("first.test", "/api/v2/auth/me", "stable-token"),
            ("first.test", "/api/v2/auth/logout", "stable-token"),
            ("second.test", "/api/v2/auth/login", (string?)null)
        }, seen);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task ChannelQueryPreservesChineseSearchAndLargeStringIdentifiers()
    {
        using var context = await Context.CreateAsync((request, _) =>
        {
            Assert.Equal("/api/v2/channels", request.RequestUri!.AbsolutePath);
            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            Assert.Equal("2", query["page"]); Assert.Equal("200", query["pageSize"]);
            Assert.Equal("9007199254740993", query["deviceId"]); Assert.Equal("7", query["unitId"]);
            Assert.Equal("true", query["online"]); Assert.Equal("视枢 + 东门&西门", query["search"]);
            return Task.FromResult(Json("""
                {"items":[{"id":"9007199254740993","deviceId":"9007199254740994","deviceName":"视枢录像机","deviceChannel":1,"name":"东门","model":null,"status":"online","unitId":7,"ptzCapable":true,"codec":"H265"}],"total":"401","page":2,"pageSize":200}
                """));
        });
        var result = await context.Api.GetAsync<Page<Channel>>("channels?page=2&pageSize=200&deviceId=9007199254740993&unitId=7&online=true&search=" + Uri.EscapeDataString("视枢 + 东门&西门"));
        Assert.Equal(401, result!.Total); Assert.Equal(2, result.PageNumber); Assert.Equal(200, result.PageSize);
        var channel = Assert.Single(result.Items);
        Assert.Equal(9007199254740993, channel.Id); Assert.Equal(9007199254740994, channel.DeviceId);
        Assert.True(channel.Online); Assert.Contains("东门", channel.Label);
    }

    [Fact]
    public async Task LayoutCreateUpdateAndReadKeepNullSlots()
    {
        var layout = new LayoutDto(12, "四窗口", "layout", false, 4, 30, [null, 101, null, 202]);
        var seen = new List<string>();
        using var context = await Context.CreateAsync(async (request, ct) =>
        {
            seen.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Delete) return StubHandler.Empty();
            if (request.Method == HttpMethod.Get) return StubHandler.Ok(new[] { layout });
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("[null,101,null,202]", body.GetProperty("channelIds").GetRawText());
            var response = StubHandler.Ok(layout);
            if (request.Method == HttpMethod.Post) response.StatusCode = HttpStatusCode.Created;
            return response;
        });
        var body = new LayoutRequest(layout.Name, layout.Kind, layout.Shared, layout.Layout, layout.IntervalSeconds, layout.ChannelIds);
        var created = await context.Api.PostAsync<LayoutDto>("layouts", body);
        Assert.Equal(layout.ChannelIds, created.ChannelIds);
        await context.Api.SendAsync(HttpMethod.Put, "layouts/12", body);
        Assert.Equal(layout.ChannelIds, Assert.Single((await context.Api.GetAsync<LayoutDto[]>("layouts"))!).ChannelIds);
        await context.Api.SendAsync(HttpMethod.Delete, "layouts/12");
        Assert.Equal(new[] { "POST /api/v2/layouts", "PUT /api/v2/layouts/12", "GET /api/v2/layouts", "DELETE /api/v2/layouts/12" }, seen);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MediaLifecyclePreservesNativeProfileUrlsAndPlaybackTime(bool playback, bool hasTs)
    {
        var route = playback ? "playback-sessions" : "live-sessions";
        var media = new MediaSession(SessionId.ToString(), 9007199254740993, 2, "playing", Start.AddHours(1),
            "rtsp://device.test/live", "https://platform.test/media/live.flv", "https://platform.test/media/live.m3u8", "H265",
            Start: Start, End: Start.AddMinutes(30), CurrentTime: Start.AddMinutes(5), Progress: 16, Speed: 2,
            Segments: [new(Start, Start.AddMinutes(20))], HttpTsUrl: hasTs ? "https://platform.test/media/live.ts" : null);
        var calls = new List<string>();
        using var context = await Context.CreateAsync(async (request, ct) =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Delete) return StubHandler.Empty();
            if (request.RequestUri.AbsolutePath == "/api/v2/" + route)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Equal("native", body.GetProperty("profile").GetString());
                Assert.Equal(media.ChannelId, body.GetProperty("channelId").GetInt64());
                if (playback) Assert.Equal(Start, body.GetProperty("start").GetDateTimeOffset());
                else Assert.Equal(2, body.GetProperty("streamType").GetInt32());
            }
            if (request.RequestUri.AbsolutePath.EndsWith("control"))
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Assert.Equal("seek", body.GetProperty("action").GetString());
                Assert.Equal(Start.AddMinutes(10), body.GetProperty("position").GetDateTimeOffset());
                Assert.Equal(1.5, body.GetProperty("speed").GetDouble());
            }
            return StubHandler.Ok(media);
        });
        var created = await context.Api.PostAsync<MediaSession>(route, playback
            ? new PlaybackRequest(media.ChannelId, Start, Start.AddMinutes(30)) : new LiveRequest(media.ChannelId));
        Assert.Equal(media.Id, created.Id); Assert.Equal(media.HttpFlvUrl, created.HttpFlvUrl);
        Assert.Equal(media.RtspUrl, created.RtspUrl); Assert.Equal(media.HlsUrl, created.HlsUrl);
        Assert.Equal(media.HttpTsUrl, created.HttpTsUrl);
        await context.Api.SendAsync(HttpMethod.Post, $"{route}/{SessionId}/renew");
        if (playback) await context.Api.SendAsync(HttpMethod.Post, $"{route}/{SessionId}/control", new PlaybackControl("seek", Start.AddMinutes(10), 1.5));
        var current = await context.Api.GetAsync<MediaSession>($"{route}/{SessionId}");
        Assert.Equal(media.HttpTsUrl, current!.HttpTsUrl);
        if (playback)
        {
            Assert.Equal(media.CurrentTime, current!.CurrentTime); Assert.Equal(2, current.Speed);
            Assert.Equal(media.Segments, current.Segments);
        }
        await context.Api.SendAsync(HttpMethod.Delete, $"{route}/{SessionId}");
        Assert.Equal(playback ? 5 : 4, calls.Count);
    }

    [Fact]
    public async Task AlarmFiltersDetailPayloadAndHistorySurviveMapping()
    {
        using var context = await Context.CreateAsync((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/alarms")
            {
                var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
                Assert.Equal("移动侦测", query["eventType"]); Assert.Equal("门区", query["search"]);
                Assert.Equal("processing", query["state"]); Assert.Equal("8", query["deviceId"]); Assert.Equal("101", query["channelId"]);
                Assert.Equal(Start, DateTimeOffset.Parse(query["from"]!, CultureInfo.InvariantCulture));
                Assert.Equal(Start.AddDays(1), DateTimeOffset.Parse(query["to"]!, CultureInfo.InvariantCulture));
                return Task.FromResult(Json("""{"items":[],"total":0,"page":3,"pageSize":50}"""));
            }
            return Task.FromResult(Json("""
                {"id":12,"deviceId":8,"deviceName":"录像机","channelId":101,"channelName":"门区","eventType":"移动侦测","occurredAt":"2026-09-07T08:00:00+08:00","state":"processing","recovered":false,"ownerId":null,"ownerName":null,"note":"核查中","imageAvailable":true,"payload":{"区域":"门区"},"history":[{"id":1,"action":"note","note":"人工核查","username":null,"createdAt":"2026-09-07T08:00:00+08:00"}]}
                """));
        });
        var page = await context.Api.GetAsync<Page<Alarm>>("alarms?page=3&pageSize=50&deviceId=8&channelId=101&state=processing&eventType="
            + Uri.EscapeDataString("移动侦测") + "&search=" + Uri.EscapeDataString("门区")
            + "&from=" + Uri.EscapeDataString(Start.ToString("O")) + "&to=" + Uri.EscapeDataString(Start.AddDays(1).ToString("O")));
        Assert.Equal(3, page!.PageNumber);
        var alarm = await context.Api.GetAsync<Alarm>("alarms/12");
        Assert.Equal("门区", alarm!.Payload!.Value.GetProperty("区域").GetString());
        Assert.Equal("人工核查", Assert.Single(alarm.History!).Note);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatestReleaseWorksAnonymouslyAndTreats204AsNoRelease(bool available)
    {
        var currentVersion = UpdateService.CurrentVersion.ToString(3);
        var nextVersion = new Version(UpdateService.CurrentVersion.Major, UpdateService.CurrentVersion.Minor,
            UpdateService.CurrentVersion.Build + 1).ToString(3);
        using var http = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("/api/v2/public/releases/latest", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            Assert.Equal(currentVersion, query["currentVersion"]); Assert.Equal("msi", query["packageType"]);
            return Task.FromResult(available ? StubHandler.Ok(new Generated.LatestReleaseDto
            {
                Id = 2, Version = nextVersion, FileName = "desktop.msi", Sha256 = new string('a', 64), FileSize = 512,
                DownloadUrl = "/api/v2/public/releases/2/download", MinimumVersion = nextVersion, ForceUpdate = true
            }) : StubHandler.Empty());
        }));
        var api = new PlatformApi(new SessionService(http, new MemoryCredentials()));
        var result = await api.GetAsync<Release>($"public/releases/latest?currentVersion={currentVersion}&packageType=msi");
        if (available) { Assert.Equal("desktop.msi", result!.FileName); Assert.True(UpdateService.IsRequired(result)); }
        else Assert.Null(result);
    }

    [Theory]
    [InlineData(403, "当前账号无权执行此操作。")]
    [InlineData(429, "已达到服务端配额，请停止部分会话后重试。")]
    [InlineData(502, "平台请求失败，HTTP 状态码 502。")]
    public async Task EmptyOrNonJsonErrorsKeepChineseFallback(int status, string message)
    {
        using var context = await Context.CreateAsync((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            { Content = new StringContent(status == 502 ? "<html>网关不可用</html>" : "") }));
        var error = await Assert.ThrowsAsync<PlatformException>(() => context.Api.GetAsync<User>("auth/me"));
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task UnauthorizedWriteReplaysGeneratedBodyWithRefreshedTokenOnce()
    {
        var tokens = new List<string?>(); var bodies = new List<string>();
        using var http = new HttpClient(new StubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return StubHandler.Ok(Fixtures.Login("old-token"));
            if (request.RequestUri.AbsolutePath.EndsWith("refresh")) return StubHandler.Ok(Fixtures.Login("new-token"));
            tokens.Add(request.Headers.Authorization?.Parameter);
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return tokens.Count == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : StubHandler.Empty();
        }));
        var session = new SessionService(http, new MemoryCredentials()); await session.LoginAsync("tester", "测试密码");
        await new PlatformApi(session).SendAsync(HttpMethod.Post, "channels/101/ptz", new PtzRequest("focusNear", 4));
        Assert.Equal(new[] { "old-token", "new-token" }, tokens);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal("focusNear", JsonDocument.Parse(bodies[1]).RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task RequestCancellationReachesUnderlyingTransport()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = await Context.CreateAsync(async (_, ct) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return StubHandler.Empty();
        });
        using var cancellation = new CancellationTokenSource();
        var request = context.Api.GetAsync<User>("auth/me", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("logout")]
    [InlineData("truncated")]
    public async Task GeneratedDownloadStreamsAndCleansPartialFiles(string outcome)
    {
        var bytes = Encoding.UTF8.GetBytes("桌面导出文件内容");
        var source = new TrackedStream(bytes);
        using var context = await Context.CreateAsync((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("logout")) return Task.FromResult(StubHandler.Empty());
            Assert.Equal($"/api/v2/exports/{SessionId}/download", request.RequestUri.AbsolutePath);
            var content = new StreamContent(source);
            content.Headers.ContentLength = bytes.Length + (outcome == "truncated" ? 1 : 0);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var directory = Path.Combine(Path.GetTempPath(), "VideoPlatform.Desktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "export.mp4");
        await File.WriteAllTextAsync(destination, "原文件");
        using var cancellation = new CancellationTokenSource();
        var values = new List<double>();
        var progress = new CallbackProgress(value =>
        {
            values.Add(value);
            if (outcome == "cancel") cancellation.Cancel();
            if (outcome == "logout") context.Session.LogoutAsync().GetAwaiter().GetResult();
        });
        try
        {
            var download = context.Api.DownloadAsync($"exports/{SessionId}/download", destination, progress, cancellation.Token);
            if (outcome == "success")
            {
                await download;
                Assert.Equal(bytes, await File.ReadAllBytesAsync(destination)); Assert.Equal(100, values.Last());
            }
            else
            {
                if (outcome == "truncated") await Assert.ThrowsAsync<IOException>(() => download);
                else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
                Assert.Equal("原文件", await File.ReadAllTextAsync(destination));
            }
            Assert.Empty(Directory.GetFiles(directory, "*.part")); Assert.True(source.Disposed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task AlarmImageUsesGeneratedBinaryResponseAndDisposesStream()
    {
        var bytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var source = new TrackedStream(bytes);
        using var context = await Context.CreateAsync((request, _) =>
        {
            Assert.Equal("/api/v2/alarms/12/image", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) });
        });
        Assert.Equal(bytes, await context.Api.GetBytesAsync("alarms/12/image"));
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task ExportCreateListRetryAndCancelUseAcceptedResponses()
    {
        var seen = new List<string>();
        using var context = await Context.CreateAsync(async (request, ct) =>
        {
            seen.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("2", HttpUtility.ParseQueryString(request.RequestUri.Query)["page"]);
                return StubHandler.Ok(new Generated.PagedExportResponse
                {
                    Items = [new() { Id = SessionId, ChannelId = 101, ChannelName = "东门", Start = Start, End = Start.AddMinutes(30),
                        State = "completed", Progress = 100, FileSize = 4096, FileName = "export.mp4", CreatedAt = Start }],
                    Total = 51, Page = 2, PageSize = 50
                });
            }
            if (request.RequestUri.AbsolutePath.EndsWith("cancel")) return StubHandler.Empty();
            if (request.RequestUri.AbsolutePath.EndsWith("retry")) return new(HttpStatusCode.Accepted)
                { Content = JsonContent.Create(new Generated.ExportQueuedResponse { Id = SessionId, State = "queued" }) };
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(101, body.GetProperty("channelId").GetInt64());
            Assert.Equal(Start, body.GetProperty("start").GetDateTimeOffset());
            Assert.Equal(Start.AddMinutes(30), body.GetProperty("end").GetDateTimeOffset());
            return new(HttpStatusCode.Accepted)
                { Content = JsonContent.Create(new Generated.ExportCreatedResponse { Id = SessionId, State = "queued", Progress = 0 }) };
        });
        var created = await context.Api.PostAsync<ExportJob>("exports", new ExportRequest(101, Start, Start.AddMinutes(30)));
        Assert.Equal(SessionId.ToString(), created.Id); Assert.Equal("queued", created.State);
        Assert.Equal(101, created.ChannelId); Assert.Equal(Start, created.Start);
        var page = await context.Api.GetAsync<Page<ExportJob>>("exports?page=2&pageSize=50");
        Assert.Equal(51, page!.Total); Assert.Equal(2, page.PageNumber);
        Assert.Equal(4096, Assert.Single(page.Items).FileSize);
        await context.Api.SendAsync(HttpMethod.Post, $"exports/{SessionId}/retry");
        await context.Api.SendAsync(HttpMethod.Post, $"exports/{SessionId}/cancel");
        Assert.Equal(new[] { "POST /api/v2/exports", "GET /api/v2/exports", $"POST /api/v2/exports/{SessionId}/retry", $"POST /api/v2/exports/{SessionId}/cancel" }, seen);
    }

    [Fact]
    public async Task FavoritesOrganizationAndRecordingSearchKeepDesktopModels()
    {
        using var context = await Context.CreateAsync(async (request, ct) =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/v2/favorites":
                    if (request.Method == HttpMethod.Put)
                    {
                        var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                        Assert.Equal(202, body.GetProperty("channelIds")[0].GetInt64());
                    }
                    return StubHandler.Ok(new[] { Fixtures.Channel(202) });
                case "/api/v2/organization":
                    return StubHandler.Ok(new Organization([new(1, "车间", null, "active", null)], [], [new(3, "班组", "unit", "active", 1)]));
                case "/api/v2/recordings/search":
                    var recording = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                    Assert.Equal(202, recording.GetProperty("channelId").GetInt64());
                    Assert.Equal(Start, recording.GetProperty("start").GetDateTimeOffset());
                    return StubHandler.Ok(new[] { new Recording("record.mp4", Start, Start.AddMinutes(10), 9007199254740993, 1, 2, 7) });
                default: throw new InvalidOperationException("出现未预期的测试请求。");
            }
        });
        Assert.Equal(202, Assert.Single((await context.Api.GetAsync<Channel[]>("favorites"))!).Id);
        await context.Api.SendAsync(HttpMethod.Put, "favorites", new FavoritesRequest([202]));
        var organization = await context.Api.GetAsync<Organization>("organization");
        Assert.Equal(1, Assert.Single(organization!.Units).ParentId); Assert.Empty(organization.Areas);
        var recordings = await context.Api.PostAsync<Recording[]>("recordings/search", new RecordingRequest(202, Start, Start.AddMinutes(10)));
        Assert.Equal(9007199254740993, Assert.Single(recordings).FileSize); Assert.Equal(7u, recordings[0].FileIndex);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("password")]
    [InlineData("alarm")]
    [InlineData("ptz-stop")]
    [InlineData("ptz-preset")]
    public async Task AccountAlarmAndPtzCommandsUseGeneratedRoutes(string operation)
    {
        var expected = operation switch
        {
            "profile" => (HttpMethod.Put, "auth/profile", (object)new ProfileRequest("值守员", "123")),
            "password" => (HttpMethod.Put, "auth/password", (object)new PasswordRequest("旧密码", "新密码")),
            "alarm" => (HttpMethod.Post, "alarms/12/actions", (object)new AlarmAction("close", "已核查")),
            "ptz-stop" => (HttpMethod.Post, "channels/101/ptz/stop", (object?)null),
            _ => (HttpMethod.Post, "channels/101/ptz/presets/3", (object?)null)
        };
        using var context = await Context.CreateAsync(async (request, ct) =>
        {
            Assert.Equal(expected.Item1, request.Method); Assert.Equal("/api/v2/" + expected.Item2, request.RequestUri!.AbsolutePath);
            if (expected.Item3 is not null)
            {
                var actual = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                var desired = JsonSerializer.SerializeToElement(expected.Item3, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                foreach (var property in desired.EnumerateObject()) Assert.Equal(property.Value.GetString(), actual.GetProperty(property.Name).GetString());
            }
            return operation switch
            {
                "profile" => StubHandler.Ok(Fixtures.User()),
                "alarm" => StubHandler.Ok(new Generated.AlarmActionResponse { Id = 12, State = "closed" }),
                _ => StubHandler.Empty()
            };
        });
        await context.Api.SendAsync(expected.Item1, expected.Item2, expected.Item3);
    }

    [Fact]
    public async Task LogoutRejectsLateGeneratedJsonAndFileResponses()
    {
        foreach (var file in new[] { false, true })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = new TrackedStream([1, 2, 3]);
            using var context = await Context.CreateAsync(async (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("logout")) return StubHandler.Empty();
                entered.SetResult(); await release.Task;
                return file ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) } : StubHandler.Ok(Fixtures.User());
            });
            Task request = file ? context.Api.GetBytesAsync("alarms/12/image") : context.Api.GetAsync<User>("auth/me");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await context.Session.LogoutAsync(); release.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(5)));
            if (file) Assert.True(source.Disposed);
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class CallbackProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class Context(HttpClient http, SessionService session) : IDisposable
    {
        public SessionService Session { get; } = session;
        public PlatformApi Api { get; } = new(session);
        public static async Task<Context> CreateAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            var http = new HttpClient(new Handler((request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(StubHandler.Ok(Fixtures.Login()));
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("stable-token", request.Headers.Authorization?.Parameter);
                return send(request, ct);
            }));
            var session = new SessionService(http, new MemoryCredentials());
            session.SetServer("https://platform.test");
            await session.LoginAsync("tester", "测试密码");
            return new(http, session);
        }
        public void Dispose() => http.Dispose();
    }
}
