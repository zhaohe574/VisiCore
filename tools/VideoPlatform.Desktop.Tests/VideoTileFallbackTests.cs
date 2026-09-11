using System.Net.Http;
using LibVLCSharp.Shared;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class VideoTileFallbackTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RtspFailureFallsBackWithinSameSessionAndPreservesPreference(bool throws)
    {
        var media = Media();
        var api = Api(media);
        var factory = new PlayerFactory { ThrowOnPlay = throws };
        await using var tile = Tile(api, factory);
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        var rtsp = Assert.Single(factory.Players);
        Assert.Equal(media.RtspUrl, rtsp.Url);
        if (!throws) rtsp.Fail();
        factory.ThrowOnPlay = false;
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(media.HttpTsUrl, factory.Players[1].Url);
        Assert.Equal(1, rtsp.DisposeCount);
        Assert.True(tile.PreferRtsp);
        Assert.Equal(media.Id, tile.SessionId);
        Assert.Single(api.Calls, call => call.Body is LiveRequest);
        factory.Players[1].Connect();
        Assert.Equal("播放中", tile.StateLabel);
        await tile.StopAsync();
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        Assert.Equal(media.RtspUrl, factory.Players[2].Url);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HttpsFailuresExhaustFiveRetriesEvenAfterBriefPlaying(bool briefPlaying, bool throws)
    {
        var media = Media();
        var api = Api(media);
        var factory = new PlayerFactory();
        await using var tile = Tile(api, factory);
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        factory.Players[0].Fail();
        factory.ThrowOnPlay = throws;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(31));
            Assert.Equal(attempt + 1, factory.Players.Count);
            var player = factory.Players[^1];
            Assert.Equal(media.HttpTsUrl, player.Url);
            if (briefPlaying) player.Connect();
            if (!throws) player.Fail();
        }
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(31));
        Assert.Null(tile.SessionId);
        Assert.Equal("重连失败，请重新打开通道。", tile.StateLabel);
        Assert.Equal(6, factory.Players.Count);
        Assert.All(factory.Players, player => Assert.Equal(1, player.DisposeCount));
        Assert.Single(api.Calls, call => call.Method == HttpMethod.Delete);
        await tile.TickAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(6, factory.Players.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("无效地址")]
    [InlineData("http://platform.test/video.ts")]
    [InlineData("http://localhost/video.ts")]
    public async Task RtspFallbackDoesNotUseInsecureOrMissingTs(string? ts)
    {
        var media = Media() with { HttpTsUrl = ts };
        var factory = new PlayerFactory();
        await using var tile = Tile(Api(media), factory);
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        factory.Players[0].Fail();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(media.RtspUrl, factory.Players[1].Url);
    }

    [Fact]
    public async Task PlaybackFallbackKeepsControlsAndUsesRefreshedHttpsAddressAfterSeek()
    {
        var media = Media();
        var api = Api(media);
        var factory = new PlayerFactory();
        await using var tile = Tile(api, factory);
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        await tile.StartAsync(Fixtures.Channel(), true, 1, start, start.AddMinutes(30));
        factory.Players[0].Fail();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        var https = factory.Players[1];
        Assert.Equal(media.HttpTsUrl, https.Url);
        await tile.ControlAsync(new PlaybackControl("pause"));
        Assert.True(https.Paused);
        await tile.ControlAsync(new PlaybackControl("resume"));
        Assert.False(https.Paused);
        await tile.ControlAsync(new PlaybackControl("speed", Speed: 2));
        Assert.Equal(2, tile.Speed);
        var refreshed = media with { HttpTsUrl = "https://platform.test/media/seek.live.ts", State = "paused", Speed = 2 };
        api.GetResults[$"playback-sessions/{media.Id}"] = refreshed;
        https.Fail();
        var position = start.AddMinutes(10);
        await tile.ControlAsync(new PlaybackControl("seek", position));
        var afterSeek = factory.Players[2];
        Assert.Equal(refreshed.HttpTsUrl, afterSeek.Url);
        Assert.True(afterSeek.Paused);
        afterSeek.Connect();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(3, factory.Players.Count);
        Assert.Equal("已暂停", tile.StateLabel);
        Assert.Equal(1, https.DisposeCount);
        Assert.Equal(position, Assert.IsType<PlaybackControl>(api.Calls.Last(call => call.Body is PlaybackControl).Body).Position);
        Assert.All(api.Calls.Where(call => call.Body is PlaybackControl), call => Assert.Equal($"playback-sessions/{media.Id}/control", call.Path));
        await tile.StopAsync();
        await tile.StopAsync();
        Assert.All(factory.Players, player => Assert.Equal(1, player.DisposeCount));
        Assert.Single(api.Calls, call => call.Method == HttpMethod.Delete && call.Path == $"playback-sessions/{media.Id}");
    }

    [Fact]
    public async Task StopWhileSeekIsPendingDoesNotCreateAnotherPlayer()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var media = Media();
        var api = Api(media);
        var factory = new PlayerFactory();
        await using var tile = Tile(api, factory);
        await tile.StartAsync(Fixtures.Channel(), true, 1, default, default);
        factory.Players[0].Fail();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        api.Send = async (_, _, body) =>
        {
            if (body is PlaybackControl) { entered.SetResult(); await release.Task; }
        };
        var seek = tile.ControlAsync(new PlaybackControl("seek", DateTimeOffset.UtcNow));
        await entered.Task;
        var stop = tile.StopAsync();
        release.SetResult();
        await Task.WhenAll(seek, stop);
        Assert.Equal(2, factory.Players.Count);
        Assert.Null(tile.SessionId);
        Assert.All(factory.Players, player => Assert.Equal(1, player.DisposeCount));
        Assert.Single(api.Calls, call => call.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task QueuedCallbacksFromStoppedSessionCannotChangeNewSessionOrRestartPlayback()
    {
        var dispatcher = new QueuedDispatcher();
        var factory = new PlayerFactory();
        var api = Api(Media());
        await using var tile = new VideoTileViewModel(0, api, factory, dispatcher) { PreferRtsp = true };
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        var old = factory.Players[0];
        old.Fail();
        old.Connect();
        await tile.StopAsync();
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        await dispatcher.DrainAsync();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(2, factory.Players.Count);
        Assert.Equal(factory.Players[1].Url, tile.MediaUrl(Media()));
        factory.Players[1].Fail();
        await tile.StopAsync();
        await dispatcher.DrainAsync();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Null(tile.SessionId);
        Assert.Equal("空闲", tile.StateLabel);
        Assert.Equal(2, factory.Players.Count);
        Assert.All(factory.Players, player => Assert.Equal(1, player.DisposeCount));
        Assert.Equal(2, api.Calls.Count(call => call.Method == HttpMethod.Delete));
    }

    [Fact]
    public async Task QueuedFailureFromReplacedPlayerCannotRestartHealthyFallback()
    {
        var dispatcher = new QueuedDispatcher();
        var media = Media();
        var factory = new PlayerFactory();
        await using var tile = new VideoTileViewModel(0, Api(media), factory, dispatcher) { PreferRtsp = true };
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        var rtsp = factory.Players[0];
        rtsp.Fail();
        await dispatcher.DrainAsync();
        rtsp.Fail();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        factory.Players[1].Connect();
        await dispatcher.DrainAsync();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(2, factory.Players.Count);
        Assert.Equal(media.HttpTsUrl, factory.Players[1].Url);
        Assert.Equal("播放中", tile.StateLabel);
    }

    [Fact]
    public async Task StableHttpsPlaybackRestoresRetryBudgetWithoutReturningToRtsp()
    {
        var factory = new PlayerFactory();
        var media = Media();
        await using var tile = Tile(Api(media), factory);
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        factory.Players[0].Fail();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        factory.Players[1].Connect();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(31));
        factory.Players[1].Fail();
        await tile.TickAsync(DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal("正在恢复连接（1/5）", tile.StateLabel);
        Assert.Equal(media.HttpTsUrl, factory.Players[2].Url);
    }

    [Fact]
    public async Task SwitchStreamRetriesOnTransientDeviceError()
    {
        var factory = new PlayerFactory();
        var mediaSub = Media() with { Id = "sub-session" };
        var mediaMain = Media() with { Id = "main-session" };
        var attempts = 0;
        var api = new FakeApi
        {
            Post = (path, body) =>
            {
                if (body is LiveRequest req && req.StreamType == 1)
                {
                    attempts++;
                    if (attempts == 1)
                        return Task.FromException<object>(new PlatformException("设备操作未成功，请检查设备状态和适配服务日志", System.Net.HttpStatusCode.BadGateway, "adapter.failed"));
                    return Task.FromResult<object>(mediaMain);
                }
                return Task.FromResult<object>(mediaSub);
            }
        };
        await using var tile = Tile(api, factory);
        await tile.StartAsync(Fixtures.Channel(3), false, 2, default, default);
        Assert.Equal(2, tile.StreamType);
        Assert.Equal("sub-session", tile.SessionId);

        await tile.SwitchStreamAsync(1);
        Assert.Equal(1, tile.StreamType);
        Assert.Equal("main-session", tile.SessionId);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SwitchStreamFallsBackToPreviousStreamOnTotalFailure()
    {
        var factory = new PlayerFactory();
        var mediaSub = Media() with { Id = "sub-session" };
        var api = new FakeApi
        {
            Post = (path, body) =>
            {
                if (body is LiveRequest req && req.StreamType == 1)
                {
                    return Task.FromException<object>(new PlatformException("设备不可达", System.Net.HttpStatusCode.BadGateway, "adapter.failed"));
                }
                return Task.FromResult<object>(mediaSub);
            }
        };
        await using var tile = Tile(api, factory);
        await tile.StartAsync(Fixtures.Channel(3), false, 2, default, default);
        Assert.Equal(2, tile.StreamType);
        Assert.Equal("sub-session", tile.SessionId);

        await Assert.ThrowsAsync<PlatformException>(() => tile.SwitchStreamAsync(1));
        Assert.Equal(2, tile.StreamType);
        Assert.Equal("sub-session", tile.SessionId);
    }

    private static MediaSession Media() => Fixtures.Media() with { HttpTsUrl = "https://platform.test/media/native.live.ts" };
    private static FakeApi Api(MediaSession media)
    {
        var api = new FakeApi { Post = (_, _) => Task.FromResult<object>(media) };
        api.GetResults[$"playback-sessions/{media.Id}"] = media;
        return api;
    }
    private static VideoTileViewModel Tile(FakeApi api, PlayerFactory factory) => new(0, api, factory, new InlineDispatcher()) { PreferRtsp = true };

    // 手动推进原生事件，覆盖先报告 Playing、随后失败的真实异步顺序。
    private sealed class PlayerFactory : IPlayerFactory
    {
        public bool ThrowOnPlay { get; set; }
        public List<Player> Players { get; } = [];
        public IVideoPlayer Create()
        {
            var player = new Player(ThrowOnPlay);
            Players.Add(player);
            return player;
        }
    }
    private sealed class Player(bool throwOnPlay) : IVideoPlayer
    {
        public string? Url { get; private set; }
        public int DisposeCount { get; private set; }
        public bool Paused { get; private set; }
        public MediaPlayer? NativePlayer => null;
        public bool Muted { get; set; }
        public event Action<string>? Failed;
        public event Action? Connected;
        public void Fail() => Failed?.Invoke("模拟传输失败");
        public void Connect() => Connected?.Invoke();
        public Task PlayAsync(string url, CancellationToken cancellationToken = default)
        {
            Url = url;
            return throwOnPlay ? Task.FromException(new InvalidOperationException("模拟打开失败")) : Task.CompletedTask;
        }
        public Task StopAsync() => Task.CompletedTask;
        public void Pause(bool paused) => Paused = paused;
        public bool Capture(string path) => false;
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Func<Task>> _pending = [];
        public Task InvokeAsync(Func<Task> action) { _pending.Enqueue(action); return Task.CompletedTask; }
        public async Task DrainAsync() { while (_pending.TryDequeue(out var action)) await action(); }
    }
}
