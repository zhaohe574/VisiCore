using System.Net.Http;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using VideoPlatform.Desktop.Views;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class MediaTests
{
    [Fact]
    public async Task TwoDevicesWithSameLocalChannelUseDifferentGlobalIds()
    {
        var api = new FakeApi(); var factory = new FakePlayerFactory();
        var first = new VideoTileViewModel(0, api, factory, new InlineDispatcher());
        var second = new VideoTileViewModel(1, api, factory, new InlineDispatcher());
        await first.StartAsync(Fixtures.Channel(101, 1), false, 2, default, default);
        await second.StartAsync(Fixtures.Channel(202, 2), false, 2, default, default);
        Assert.Equal(new long[] { 101, 202 }, api.Calls.Where(c => c.Body is LiveRequest).Select(c => ((LiveRequest)c.Body!).ChannelId));
        Assert.NotEqual(factory.Players[0].Url, factory.Players[1].Url);
        Assert.All(factory.Players, player => Assert.StartsWith("https://", player.Url));
        await first.DisposeAsync(); await second.DisposeAsync();
    }

    [Fact]
    public async Task StopDuringCreationDeletesLateSessionWithoutCreatingPlayer()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = Fixtures.Media();
        var api = new FakeApi { Post = (_, _) => { entered.SetResult(); return release.Task; } };
        var factory = new FakePlayerFactory(); var tile = new VideoTileViewModel(0, api, factory, new InlineDispatcher());
        var starting = tile.StartAsync(Fixtures.Channel(), false, 2, default, default); await entered.Task;
        var stopping = tile.StopAsync(); release.SetResult(response);
        await Task.WhenAll(starting, stopping);
        Assert.Empty(factory.Players); Assert.Null(tile.SessionId);
        Assert.Single(api.Calls, c => c.Method == HttpMethod.Delete && c.Path.EndsWith(response.Id));
    }

    [Fact]
    public async Task RepeatedStopDisposesPlayerAndDeletesServerLeaseOnce()
    {
        var api = new FakeApi(); var factory = new FakePlayerFactory(); var tile = new VideoTileViewModel(0, api, factory, new InlineDispatcher());
        await tile.StartAsync(Fixtures.Channel(), false, 2, default, default);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => tile.StopAsync()));
        Assert.Single(api.Calls, c => c.Method == HttpMethod.Delete);
        Assert.True(factory.Players.Single().Disposed);
    }

    [Fact]
    public void NativePlaybackPrefersHttpsAndRequiresExplicitRtspOptIn()
    {
        var tile = new VideoTileViewModel(0, new FakeApi(), new FakePlayerFactory(), new InlineDispatcher());
        var media = Fixtures.Media(); Assert.Equal(media.HttpFlvUrl, tile.MediaUrl(media));
        var ts = media with { HttpTsUrl = "https://platform.test/media/live/native.live.ts", Codec = "H265" };
        Assert.Equal(ts.HttpTsUrl, tile.MediaUrl(ts));
        Assert.Equal(media.HttpFlvUrl, tile.MediaUrl(ts with { HttpTsUrl = "http://intranet/video.ts" }));
        Assert.Throws<InvalidOperationException>(() => tile.MediaUrl(media with { HttpFlvUrl = "http://intranet/video.flv" }));
        tile.PreferRtsp = true; Assert.Equal(media.RtspUrl, tile.MediaUrl(media));
    }

    [Fact]
    public async Task PlaybackControlUsesAbsoluteTimestampAndKeepsOtherWindowIndependent()
    {
        var api = new FakeApi(); var factory = new FakePlayerFactory();
        var first = new VideoTileViewModel(0, api, factory, new InlineDispatcher()); var second = new VideoTileViewModel(1, api, factory, new InlineDispatcher());
        var from = DateTimeOffset.Now.AddHours(-1); var to = DateTimeOffset.Now;
        await first.StartAsync(Fixtures.Channel(101), true, 1, from, to); await second.StartAsync(Fixtures.Channel(202), true, 1, from, to);
        var position = from.AddMinutes(12); await first.ControlAsync(new PlaybackControl("seek", position));
        var command = Assert.Single(api.Calls, c => c.Body is PlaybackControl);
        Assert.Equal($"playback-sessions/{first.SessionId}/control", command.Path); Assert.Equal(position, ((PlaybackControl)command.Body!).Position);
        await first.DisposeAsync(); await second.DisposeAsync();
    }

    [Fact]
    public void TimelineMapsClampedPositionToRealRecordingTime()
    {
        var from = new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8)); var to = from.AddHours(2);
        Assert.Equal(from.AddHours(1), RecordingTimeline.PositionAt(from, to, 0.5));
        Assert.Equal(from, RecordingTimeline.PositionAt(from, to, -1)); Assert.Equal(to, RecordingTimeline.PositionAt(from, to, 2));
    }

    [Fact]
    public async Task RevocationStopsAllPlayersAndClearsVisibleResources()
    {
        var api = new FakeApi(); var factory = new FakePlayerFactory();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel()], 1);
        var vm = new WorkspaceViewModel(api, factory, new PtzService(api), new InlineDispatcher(), new FakeDialogs());
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read")); await vm.RefreshAsync();
        await vm.OpenChannelAsync(Fixtures.Channel()); Assert.NotNull(vm.Tiles[0].SessionId);
        await vm.SetAccessAsync(null);
        Assert.False(vm.CanLive); Assert.Empty(vm.Resources); Assert.All(vm.Tiles, tile => Assert.Null(tile.SessionId));
        Assert.True(factory.Players.Single().Disposed);
    }

    [Fact]
    public async Task UnauthorizedChannelCannotCreateMediaSession()
    {
        var api = new FakeApi(); var factory = new FakePlayerFactory(); var vm = new WorkspaceViewModel(api, factory, new PtzService(api), new InlineDispatcher(), new FakeDialogs());
        await vm.SetAccessAsync(Fixtures.User()); await vm.OpenChannelAsync(Fixtures.Channel());
        Assert.DoesNotContain(api.Calls, call => call.Path == "live-sessions"); Assert.Empty(factory.Players);
        Assert.Contains("无权", vm.Status);
    }

    [Fact]
    public void FilteringDoesNotLeakOtherDevicesWithSameLocalChannelNumber()
    {
        var root = new ResourceNode("资源"); root.Children.Add(new("东门", Fixtures.Channel(101, 1))); root.Children.Add(new("西门", Fixtures.Channel(202, 2)));
        root.Filter("", new HashSet<long> { 202 });
        Assert.False(root.Children[0].IsVisible); Assert.True(root.Children[1].IsVisible);
    }
}
