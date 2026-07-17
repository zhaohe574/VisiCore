using System.Net;
using System.Net.Http.Json;
using VideoPlatform.Core;
using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class StreamAuthorizationStoreTests
{
    [Fact(DisplayName = "同一票据只绑定一个 HLS 会话且撤销后拒绝后续鉴权")]
    public async Task RepeatedAuthenticationUsesCacheAndRevocationRemovesAuthorization()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var pathName = $"live/{cameraId:N}/sub";
        var ticket = new string('t', 48);
        var centerConsumeCount = 0;
        var centerHandler = new DelegateHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/consume", StringComparison.Ordinal) == true)
            {
                centerConsumeCount++;
                if (centerConsumeCount > 2)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new GatewayStreamSession(
                        sessionId,
                        pathName,
                        cameraId,
                        CameraPermission.LiveView,
                        "sub",
                        DateTimeOffset.UtcNow.AddMinutes(2),
                        true,
                        null))
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var options = CreateGatewayOptions();
        var controlClient = new GatewayControlPlaneClient(
            new HttpClient(centerHandler) { BaseAddress = new Uri("https://center.test/") },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(pathName, "fingerprint");
        var store = new StreamAuthorizationStore(controlClient, registry);
        var query = $"sessionId={sessionId:N}&ticket={ticket}";
        var reconnectQuery = $"sessionId={sessionId:N}&ticket={new string('r', 48)}";

        Assert.True(await store.AuthorizeAsync(CreateAuth(pathName, "hls-1", query), CancellationToken.None));
        Assert.True(await store.AuthorizeAsync(CreateAuth(pathName, "hls-1", ""), CancellationToken.None));
        Assert.False(await store.AuthorizeAsync(CreateAuth(pathName, "hls-2", query), CancellationToken.None));
        Assert.Equal(1, centerConsumeCount);
        Assert.Equal(1, store.Count);

        Assert.True(await store.AuthorizeAsync(CreateAuth(pathName, "hls-2", reconnectQuery), CancellationToken.None));
        Assert.Equal(2, centerConsumeCount);

        await store.RevokeAsync(sessionId, CancellationToken.None);
        Assert.Equal(0, store.Count);

        Assert.False(await store.AuthorizeAsync(CreateAuth(pathName, "hls-3", query), CancellationToken.None));
        Assert.Equal(3, centerConsumeCount);
    }

    [Fact(DisplayName = "鉴权只接受已配置路径的 HLS 读操作")]
    public async Task AuthenticationRejectsUnsupportedActionProtocolAndPath()
    {
        var options = CreateGatewayOptions();
        var centerClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(_ => throw new InvalidOperationException("不应访问中心。")))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        var internalPath = LiveTranscodePath.BuildInternalSource(Guid.NewGuid());
        registry.MarkCurrent(internalPath, "private-fingerprint", clientReadable: false);
        var store = new StreamAuthorizationStore(centerClient, registry);

        Assert.False(await store.AuthorizeAsync(CreateAuth("live/missing/sub", "hls-1", ""), CancellationToken.None));
        Assert.False(await store.AuthorizeAsync(CreateAuth("live/missing/sub", "hls-1", "", action: "publish"), CancellationToken.None));
        Assert.False(await store.AuthorizeAsync(CreateAuth("live/missing/sub", "hls-1", "", protocol: "rtsp"), CancellationToken.None));
        Assert.False(await store.AuthorizeAsync(CreateAuth("playback/not-a-session-id", "hls-1", ""), CancellationToken.None));
        Assert.False(await store.AuthorizeAsync(CreateAuth(internalPath, "hls-1", ""), CancellationToken.None));
    }

    [Fact(DisplayName = "动态回放路径只能由匹配会话的一次性票据授权")]
    public async Task PlaybackRelayPathUsesExactCenterStreamKeyWithoutRegistryEntry()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var pathName = $"playback/{sessionId:N}";
        var options = CreateGatewayOptions();
        var centerClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(request =>
            {
                if (request.RequestUri?.AbsolutePath.EndsWith("/consume", StringComparison.Ordinal) != true)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new GatewayStreamSession(
                        sessionId,
                        pathName,
                        cameraId,
                        CameraPermission.Playback,
                        "playback",
                        DateTimeOffset.UtcNow.AddMinutes(2),
                        true,
                        null))
                };
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var store = new StreamAuthorizationStore(
            centerClient,
            new GatewayPathRegistry());

        Assert.True(await store.AuthorizeAsync(
            CreateAuth(pathName, "playback-hls", $"sessionId={sessionId:N}&ticket={new string('p', 48)}"),
            CancellationToken.None));
    }

    [Fact(DisplayName = "撤销与票据消费串行化且会移除刚建立的授权")]
    public async Task RevocationWaitsForInflightTicketConsumption()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var pathName = $"live/{cameraId:N}/sub";
        var consumeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConsume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = CreateGatewayOptions();
        var centerClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(async (request, cancellationToken) =>
            {
                if (request.RequestUri?.AbsolutePath.EndsWith("/consume", StringComparison.Ordinal) != true)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                consumeStarted.TrySetResult();
                await releaseConsume.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new GatewayStreamSession(
                        sessionId,
                        pathName,
                        cameraId,
                        CameraPermission.LiveView,
                        "sub",
                        DateTimeOffset.UtcNow.AddMinutes(2),
                        true,
                        null))
                };
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(pathName, "fingerprint");
        var store = new StreamAuthorizationStore(
            centerClient,
            registry);

        var authorizeTask = store.AuthorizeAsync(
            CreateAuth(pathName, "hls-race", $"sessionId={sessionId:N}&ticket={new string('t', 48)}"),
            CancellationToken.None);
        await consumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var revokeTask = store.RevokeAsync(sessionId, CancellationToken.None);
        Assert.False(revokeTask.IsCompleted);

        releaseConsume.TrySetResult();
        Assert.True(await authorizeTask);
        await revokeTask;

        Assert.Equal(0, store.Count);
    }

    [Fact(DisplayName = "重复撤销会话是幂等操作")]
    public async Task RepeatedRevocationIsIdempotent()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var pathName = $"live/{cameraId:N}/sub";
        var options = CreateGatewayOptions();
        var centerClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(request =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new GatewayStreamSession(
                        sessionId,
                        pathName,
                        cameraId,
                        CameraPermission.LiveView,
                        "sub",
                        DateTimeOffset.UtcNow.AddMinutes(2),
                        true,
                        null))
                }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(pathName, "fingerprint");
        var store = new StreamAuthorizationStore(
            centerClient,
            registry);
        var query = $"sessionId={sessionId:N}&ticket={new string('t', 48)}";

        Assert.True(await store.AuthorizeAsync(CreateAuth(pathName, "hls-retry", query), CancellationToken.None));
        await store.RevokeAsync(sessionId, CancellationToken.None);
        await store.RevokeAsync(sessionId, CancellationToken.None);
        Assert.Equal(0, store.Count);
        Assert.False(await store.AuthorizeAsync(CreateAuth(pathName, "hls-retry", ""), CancellationToken.None));
    }

    [Fact(DisplayName = "成功消费票据只挂接一次会话且重复撤销只释放一次")]
    public async Task SessionLifecycleAttachAndDetachAreIdempotent()
    {
        var sessionId = Guid.NewGuid();
        var cameraId = Guid.NewGuid();
        var pathName = $"live/{cameraId:N}/main";
        var lifecycle = new RecordingSessionLifecycle();
        var options = CreateGatewayOptions();
        var centerClient = new GatewayControlPlaneClient(
            new HttpClient(new DelegateHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new GatewayStreamSession(
                    sessionId,
                    pathName,
                    cameraId,
                    CameraPermission.LiveView,
                    "main",
                    DateTimeOffset.UtcNow.AddMinutes(2),
                    true,
                    null))
            }))
            {
                BaseAddress = new Uri("https://center.test/")
            },
            options);
        var registry = new GatewayPathRegistry();
        registry.MarkCurrent(pathName, "fingerprint");
        var store = new StreamAuthorizationStore(centerClient, registry, lifecycle);
        var query = $"sessionId={sessionId:N}&ticket={new string('t', 48)}";

        Assert.True(await store.AuthorizeAsync(CreateAuth(pathName, "hls-lifecycle", query), CancellationToken.None));
        Assert.True(await store.AuthorizeAsync(CreateAuth(pathName, "hls-lifecycle", query), CancellationToken.None));
        Assert.Single(lifecycle.Attached);

        await store.RevokeAsync(sessionId, CancellationToken.None);
        await store.RevokeAsync(sessionId, CancellationToken.None);

        Assert.Single(lifecycle.Detached);
        Assert.Equal(sessionId, lifecycle.Detached[0]);
    }

    private static GatewayOptions CreateGatewayOptions() => new()
    {
        GatewayName = "integration",
        CenterBaseUri = "https://center.test/",
        CenterControlToken = "center-control-token-at-least-32-bytes",
        DeviceWorkerAccessToken = "device-worker-token-at-least-32-bytes",
        CommandToken = "gateway-command-token-at-least-32-bytes"
    };

    private static MediaMtxAuthRequest CreateAuth(
        string path,
        string id,
        string query,
        string action = "read",
        string protocol = "hls") =>
        new("", "", "", "127.0.0.1", action, path, protocol, id, query, "test");

    private sealed class RecordingSessionLifecycle : IStreamSessionLifecycle
    {
        public List<(Guid SessionId, string PathName)> Attached { get; } = [];
        public List<Guid> Detached { get; } = [];

        public void Attach(Guid sessionId, string pathName) => Attached.Add((sessionId, pathName));
        public void Detach(Guid sessionId) => Detached.Add(sessionId);
    }
}

internal sealed class DelegateHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

    public DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : this((request, _) => Task.FromResult(handler(request)))
    {
    }

    public DelegateHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        this.handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => handler(request, cancellationToken);
}
