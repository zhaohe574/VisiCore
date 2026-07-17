using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class LiveTranscodeRelayManagerTests
{
    [Fact(DisplayName = "同一主码流的多个会话共享一个 FFmpeg 且发布授权随最后会话收回")]
    public async Task SessionsShareRelayAndPublisherAuthorizationTracksDemand()
    {
        var factory = new FixedProcessFactory();
        var mediaMtx = new FixedMediaMtxClient { Ready = true };
        await using var manager = CreateManager(factory, mediaMtx, maxConcurrentRelays: 1);
        var route = CreateRoute(Guid.NewGuid());
        await manager.SynchronizeRoutesAsync([route], completeSnapshot: true, CancellationToken.None);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        manager.Attach(firstSession, route.PublicPath);
        manager.Attach(secondSession, route.PublicPath);

        await manager.EnsureReadyAsync(route.PublicPath, CancellationToken.None);
        await manager.EnsureReadyAsync(route.PublicPath, CancellationToken.None);

        Assert.Equal(1, factory.StartCount);
        Assert.Equal(2, mediaMtx.ReadinessRequests);
        Assert.Equal(1, mediaMtx.RemovedPublisherPaths);
        Assert.Equal(1, mediaMtx.AppliedPublisherPaths);
        var credential = factory.Credentials.Single();
        Assert.True(manager.AuthorizePublisher(CreatePublishAuth(route.PublicPath, "127.0.0.1", credential)));
        Assert.True(manager.AuthorizeReader(CreateReadAuth(route.InputPath, "127.0.0.1", credential)));
        Assert.False(manager.AuthorizePublisher(CreatePublishAuth(route.PublicPath, "192.0.2.10", credential)));
        Assert.False(manager.AuthorizeReader(CreateReadAuth(route.InputPath, "192.0.2.10", credential)));
        Assert.False(manager.AuthorizePublisher(CreatePublishAuth(route.PublicPath.Replace("/main", "/sub"), "127.0.0.1", credential)));
        Assert.False(manager.AuthorizePublisher(CreatePublishAuth(
            route.PublicPath,
            "127.0.0.1",
            credential with { Password = new string('x', credential.Password.Length) })));

        manager.Detach(firstSession);
        Assert.True(manager.AuthorizePublisher(CreatePublishAuth(route.PublicPath, "127.0.0.1", credential)));
        manager.Detach(secondSession);
        Assert.False(manager.AuthorizePublisher(CreatePublishAuth(route.PublicPath, "127.0.0.1", credential)));

        await manager.SynchronizeRoutesAsync([], completeSnapshot: true, CancellationToken.None);
        Assert.True(factory.Processes.Single().Disposed);
    }

    [Fact(DisplayName = "不同主码流达到转码容量上限时返回有界失败")]
    public async Task ConcurrentPathCapacityIsBounded()
    {
        var factory = new FixedProcessFactory();
        var mediaMtx = new FixedMediaMtxClient { Ready = true };
        await using var manager = CreateManager(factory, mediaMtx, maxConcurrentRelays: 1);
        var first = CreateRoute(Guid.NewGuid());
        var second = CreateRoute(Guid.NewGuid());
        await manager.SynchronizeRoutesAsync([first, second], completeSnapshot: true, CancellationToken.None);
        manager.Attach(Guid.NewGuid(), first.PublicPath);
        manager.Attach(Guid.NewGuid(), second.PublicPath);

        await manager.EnsureReadyAsync(first.PublicPath, CancellationToken.None);
        await Assert.ThrowsAsync<LiveTranscodeUnavailableException>(() =>
            manager.EnsureReadyAsync(second.PublicPath, CancellationToken.None));

        Assert.Equal(1, factory.StartCount);
    }

    [Fact(DisplayName = "最后一个主码流会话离开后按空闲宽限期停止进程")]
    public async Task LastSessionDetachmentStopsRelayAfterIdleGrace()
    {
        var factory = new FixedProcessFactory();
        var mediaMtx = new FixedMediaMtxClient { Ready = true };
        await using var manager = CreateManager(factory, mediaMtx, maxConcurrentRelays: 1);
        await manager.StartAsync(CancellationToken.None);
        var route = CreateRoute(Guid.NewGuid());
        await manager.SynchronizeRoutesAsync([route], completeSnapshot: true, CancellationToken.None);
        var sessionId = Guid.NewGuid();
        manager.Attach(sessionId, route.PublicPath);
        await manager.EnsureReadyAsync(route.PublicPath, CancellationToken.None);

        manager.Detach(sessionId);

        await factory.Processes.Single().DisposedSignal.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(factory.Processes.Single().Disposed);
        await manager.StopAsync(CancellationToken.None);
    }

    [Fact(DisplayName = "路由替换会迁移现有会话并停止旧代中继")]
    public async Task RouteReplacementMigratesSessionsWithoutOrphanProcess()
    {
        var factory = new FixedProcessFactory();
        var mediaMtx = new FixedMediaMtxClient { Ready = true };
        await using var manager = CreateManager(factory, mediaMtx, maxConcurrentRelays: 1);
        var route = CreateRoute(Guid.NewGuid());
        var sessionId = Guid.NewGuid();
        await manager.SynchronizeRoutesAsync([route], completeSnapshot: true, CancellationToken.None);
        manager.Attach(sessionId, route.PublicPath);
        await manager.EnsureReadyAsync(route.PublicPath, CancellationToken.None);

        var replacement = route with { Fingerprint = Guid.NewGuid().ToString("N") };
        await manager.SynchronizeRoutesAsync([replacement], completeSnapshot: true, CancellationToken.None);
        await manager.EnsureReadyAsync(replacement.PublicPath, CancellationToken.None);

        Assert.Equal(2, factory.StartCount);
        Assert.True(factory.Processes[0].Disposed);
        Assert.False(factory.Processes[1].Disposed);
        Assert.False(manager.AuthorizePublisher(CreatePublishAuth(
            replacement.PublicPath,
            "127.0.0.1",
            factory.Credentials[0])));
        Assert.True(manager.AuthorizePublisher(CreatePublishAuth(
            replacement.PublicPath,
            "127.0.0.1",
            factory.Credentials[1])));
        manager.Detach(sessionId);
        Assert.False(manager.AuthorizePublisher(CreatePublishAuth(
            replacement.PublicPath,
            "127.0.0.1",
            factory.Credentials[1])));
    }

    [Fact(DisplayName = "中继退出未确认时保留容量并拒绝超额启动")]
    public async Task UnconfirmedTerminationKeepsCapacityFailClosed()
    {
        var factory = new FixedProcessFactory { ConfirmTermination = false };
        var mediaMtx = new FixedMediaMtxClient { Ready = true };
        await using var manager = CreateManager(factory, mediaMtx, maxConcurrentRelays: 1);
        var first = CreateRoute(Guid.NewGuid());
        var second = CreateRoute(Guid.NewGuid());
        await manager.SynchronizeRoutesAsync([first, second], completeSnapshot: true, CancellationToken.None);
        manager.Attach(Guid.NewGuid(), first.PublicPath);
        await manager.EnsureReadyAsync(first.PublicPath, CancellationToken.None);

        await manager.SynchronizeRoutesAsync([second], completeSnapshot: true, CancellationToken.None);
        manager.Attach(Guid.NewGuid(), second.PublicPath);

        await Assert.ThrowsAsync<LiveTranscodeUnavailableException>(() =>
            manager.EnsureReadyAsync(second.PublicPath, CancellationToken.None));
        Assert.Equal(1, factory.StartCount);
    }

    [Fact(DisplayName = "同步释放管理器也会停止全部 FFmpeg 中继")]
    public async Task SynchronousDisposeStopsAllRelays()
    {
        var factory = new FixedProcessFactory();
        var manager = CreateManager(factory, new FixedMediaMtxClient { Ready = true }, maxConcurrentRelays: 1);
        var route = CreateRoute(Guid.NewGuid());
        await manager.SynchronizeRoutesAsync([route], completeSnapshot: true, CancellationToken.None);
        manager.Attach(Guid.NewGuid(), route.PublicPath);
        await manager.EnsureReadyAsync(route.PublicPath, CancellationToken.None);

        manager.Dispose();

        Assert.True(factory.Processes.Single().Disposed);
    }

    [Fact(DisplayName = "路由换代期间新请求会等待旧中继释放容量")]
    public async Task ReplacementRequestWaitsForRetirementBarrier()
    {
        var releaseFirstDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FixedProcessFactory { FirstDisposeRelease = releaseFirstDispose };
        await using var manager = CreateManager(
            factory,
            new FixedMediaMtxClient { Ready = true },
            maxConcurrentRelays: 1);
        var route = CreateRoute(Guid.NewGuid());
        var sessionId = Guid.NewGuid();
        await manager.SynchronizeRoutesAsync([route], completeSnapshot: true, CancellationToken.None);
        manager.Attach(sessionId, route.PublicPath);
        await manager.EnsureReadyAsync(route.PublicPath, CancellationToken.None);

        var replacement = route with { Fingerprint = Guid.NewGuid().ToString("N") };
        var synchronize = manager.SynchronizeRoutesAsync(
            [replacement],
            completeSnapshot: true,
            CancellationToken.None);
        await factory.Processes[0].DisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var ensureReplacement = manager.EnsureReadyAsync(replacement.PublicPath, CancellationToken.None);
        await Task.Delay(100);
        Assert.False(ensureReplacement.IsCompleted);

        releaseFirstDispose.TrySetResult();
        await synchronize;
        await ensureReplacement;
        Assert.Equal(2, factory.StartCount);
    }

    private static LiveTranscodeRelayManager CreateManager(
        FixedProcessFactory factory,
        FixedMediaMtxClient mediaMtx,
        int maxConcurrentRelays) =>
        new(
            new LiveTranscodeOptions
            {
                Enabled = true,
                MaxConcurrentRelays = maxConcurrentRelays,
                StartupTimeoutSeconds = 5,
                IdleCloseAfterSeconds = 1,
                RestartBackoffSeconds = 1
            },
            factory,
            mediaMtx,
            NullLogger<LiveTranscodeRelayManager>.Instance);

    private static LiveTranscodeRoute CreateRoute(Guid cameraId)
    {
        var inputPath = LiveTranscodePath.BuildInternalSource(cameraId);
        var publicPath = LiveTranscodePath.BuildPublicMain(cameraId);
        return new LiveTranscodeRoute(
            publicPath,
            inputPath,
            new Uri($"rtsp://127.0.0.1:8554/{inputPath}"),
            new Uri($"rtsp://127.0.0.1:8554/{publicPath}"),
            Guid.NewGuid().ToString("N"));
    }

    private static MediaMtxAuthRequest CreatePublishAuth(
        string path,
        string ip,
        LiveTranscodePublisherCredential credential) =>
        new(credential.Username, credential.Password, null, ip, "publish", path, "rtsp", "publisher", null, "ffmpeg");

    private static MediaMtxAuthRequest CreateReadAuth(
        string path,
        string ip,
        LiveTranscodePublisherCredential credential) =>
        new(credential.Username, credential.Password, null, ip, "read", path, "rtsp", "reader", null, "ffmpeg");

    private sealed class FixedProcessFactory : ILiveTranscodeProcessFactory
    {
        public List<FixedProcess> Processes { get; } = [];
        public List<LiveTranscodePublisherCredential> Credentials { get; } = [];
        public bool ConfirmTermination { get; init; } = true;
        public TaskCompletionSource? FirstDisposeRelease { get; init; }
        public int StartCount => Processes.Count;

        public ILiveTranscodeProcess Start(
            LiveTranscodeRoute route,
            LiveTranscodePublisherCredential credential)
        {
            var process = new FixedProcess(
                ConfirmTermination,
                Processes.Count == 0 ? FirstDisposeRelease : null);
            Processes.Add(process);
            Credentials.Add(credential);
            return process;
        }
    }

    private sealed class FixedProcess(
        bool confirmTermination,
        TaskCompletionSource? disposeRelease) : ILiveTranscodeProcess
    {
        public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public bool HasExited => Disposed;
        public int? ExitCode => Disposed ? 0 : null;
        public int CapturedStderrCharacters => 0;
        public bool TerminationConfirmed => Disposed && confirmTermination;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            if (disposeRelease is not null)
            {
                await disposeRelease.Task;
            }
            Disposed = true;
            DisposedSignal.TrySetResult();
        }
    }

    private sealed class FixedMediaMtxClient : IMediaMtxClient
    {
        public bool Ready { get; init; }
        public int AppliedPublisherPaths { get; private set; }
        public int RemovedPublisherPaths { get; private set; }
        public int ReadinessRequests { get; private set; }

        public Task ApplyPullPathAsync(string pathName, Uri sourceUri, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ApplyPublisherPathAsync(string pathName, CancellationToken cancellationToken) =>
            Task.FromResult(AppliedPublisherPaths++);

        public Task<bool> IsPathReadyAsync(string pathName, CancellationToken cancellationToken) =>
            Task.FromResult(ReadReady());

        public Task RemovePathAsync(string pathName, CancellationToken cancellationToken) =>
            Task.FromResult(RemovedPublisherPaths++);

        private bool ReadReady()
        {
            ReadinessRequests++;
            return Ready;
        }
    }
}
