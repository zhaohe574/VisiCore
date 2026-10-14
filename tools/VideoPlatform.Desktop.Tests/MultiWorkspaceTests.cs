using System.Net.Http;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class MultiWorkspaceTests
{
    private static WorkspaceViewModel Create(FakeApi api, PtzService? ptz = null) => new(api, new FakePlayerFactory(), ptz ?? new(api), new InlineDispatcher(), new FakeDialogs());
    private static async Task Load(WorkspaceViewModel workspace, FakeApi api)
    {
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(101), Fixtures.Channel(102)], 2);
        await workspace.SetAccessAsync(Fixtures.User("live.view", "playback.view", "channel.read", "ptz.control"));
        await workspace.RefreshAsync();
    }

    [Fact]
    public async Task ClosingWorkspaceStopsOnlyItsTileSessions()
    {
        var api = new FakeApi();
        var first = Create(api);
        var second = first.CreateSibling();
        await first.Tiles[0].StartAsync(Fixtures.Channel(), false, 2, default, default);
        await second.Tiles[0].StartAsync(Fixtures.Channel(), false, 2, default, default);
        var secondSession = second.Tiles[0].SessionId;
        await first.ClearAsync();
        Assert.Equal(secondSession, second.Tiles[0].SessionId);
        Assert.DoesNotContain(api.Calls, call => call.Path.Contains("all=true"));
        Assert.DoesNotContain(api.Calls, call => call.Method == HttpMethod.Delete && call.Path.EndsWith(secondSession!));
        var requests = api.Calls.Select(call => call.Body).OfType<LiveRequest>().ToArray();
        Assert.NotNull(requests[0].ConsumerId);
        Assert.NotEqual(requests[0].ConsumerId, requests[1].ConsumerId);
        await second.ClearAsync();
    }

    [Fact]
    public async Task SwitchingModeReleasesHiddenPlayersAndRestoresSeparateLayoutAndChannels()
    {
        var api = new FakeApi(); var workspace = Create(api); await Load(workspace, api);
        await workspace.SetLayoutCommand.ExecuteAsync("9");
        await workspace.Tiles[2].StartAsync(Fixtures.Channel(101), false, 2, default, default);
        var consumer = workspace.Tiles[2].ConsumerId;
        await workspace.SetModeAsync(true);
        Assert.Null(workspace.Tiles[2].SessionId);
        Assert.Equal(4, workspace.LayoutCount);
        workspace.StartTime = "10:15:00";
        await workspace.Tiles[0].StartAsync(Fixtures.Channel(102), true, 1, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now);
        await workspace.SetModeAsync(false);
        Assert.Equal(9, workspace.LayoutCount);
        Assert.Equal(101, workspace.Tiles[2].Channel?.Id);
        Assert.Equal(consumer, workspace.Tiles[2].ConsumerId);
        Assert.Null(workspace.Tiles[0].Channel);
        await workspace.SetModeAsync(true);
        Assert.Equal("10:15:00", workspace.StartTime);
        Assert.Equal(102, workspace.Tiles[0].Channel?.Id);
        Assert.Equal(4, workspace.LayoutCount);
        await workspace.ClearAsync();
    }

    [Fact]
    public async Task RestoringSavedWorkspaceRequiresExplicitPlaybackAndSkipsRevokedChannels()
    {
        var api = new FakeApi(); var workspace = Create(api); await Load(workspace, api);
        await workspace.Tiles[0].StartAsync(Fixtures.Channel(101), false, 2, default, default);
        await workspace.Tiles[1].StartAsync(Fixtures.Channel(999), false, 2, default, default);
        var saved = workspace.CaptureState();
        await workspace.ClearAsync(); await Load(workspace, api);
        var starts = api.Calls.Count(call => call.Body is LiveRequest);
        await workspace.RestoreStateAsync(saved);
        Assert.Equal(starts, api.Calls.Count(call => call.Body is LiveRequest));
        await workspace.RestorePlaybackCommand.ExecuteAsync(null);
        Assert.Equal(101, workspace.Tiles[0].Channel?.Id);
        Assert.Null(workspace.Tiles[1].Channel);
        await workspace.ClearAsync();
    }

    [Fact]
    public async Task LogoutDiscardsBothModesAndManualPlaybackReplacesPendingRestore()
    {
        var api = new FakeApi(); var workspace = Create(api); await Load(workspace, api);
        await workspace.OpenChannelCoreAsync(Fixtures.Channel(101), workspace.Tiles[0]);
        var saved = workspace.CaptureState();
        await workspace.RestoreStateAsync(saved);
        await workspace.SetModeAsync(true);
        await workspace.SetModeAsync(false);
        Assert.Null(workspace.Tiles[0].SessionId);
        await workspace.OpenChannelCoreAsync(Fixtures.Channel(102), workspace.Tiles[0]);
        Assert.Equal(102, workspace.CaptureState().Live!.Tiles.Single().ChannelId);
        await workspace.SetModeAsync(true);
        await workspace.ClearAsync(); await Load(workspace, api);
        var starts = api.Calls.Count(c => c.Body is LiveRequest or PlaybackRequest);
        await workspace.SetModeAsync(true); await workspace.SetModeAsync(false);
        Assert.Equal(starts, api.Calls.Count(c => c.Body is LiveRequest or PlaybackRequest));
        Assert.Empty(workspace.CaptureState().Live!.Tiles);
        await workspace.ClearAsync();
    }

    [Fact]
    public void PlaybackRangeUsesBothTimesAndRejectsReversedOrInvalidRange()
    {
        var workspace = Create(new FakeApi());
        workspace.RecordingDate = new DateTime(2026, 10, 7);
        workspace.StartTime = "10:15:00"; workspace.EndTime = "10:45:00";
        var range = workspace.ReadRange();
        Assert.Equal(TimeSpan.FromMinutes(30), range.End - range.Start);
        Assert.Equal(TimeSpan.FromHours(10.75), range.End.TimeOfDay);
        workspace.EndTime = "10:00:00";
        Assert.Throws<ArgumentException>(() => workspace.ReadRange());
        workspace.EndTime = "错误时间";
        Assert.Throws<ArgumentException>(() => workspace.ReadRange());
    }

    [Fact]
    public async Task EmptyResourceRefreshPurgesTreeAndUnauthorizedPlayback()
    {
        var api = new FakeApi(); var workspace = Create(api); await Load(workspace, api);
        await workspace.Tiles[0].StartAsync(Fixtures.Channel(101), false, 2, default, default);
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([], 0);
        await workspace.RefreshAsync();
        Assert.Empty(workspace.Channels);
        Assert.DoesNotContain(workspace.Resources.SelectMany(node => node.Flatten()), node => node.IsChannel);
        Assert.Null(workspace.Tiles[0].SessionId);
    }

    [Fact]
    public async Task PtzStopAndPresetCannotReleaseAnotherWorkspaceLease()
    {
        var api = new FakeApi(); await using var ptz = new PtzService(api);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await ptz.StartAsync(101, "left", 2, first);
        await ptz.StopAsync(second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ptz.StartAsync(101, "right", 2, second));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ptz.PresetAsync(101, 1, second));
        Assert.DoesNotContain(api.Calls, call => call.Path.EndsWith("/stop"));
        await ptz.StartAsync(102, "up", 2, second);
        await ptz.StopAsync(second);
        Assert.DoesNotContain(api.Calls, call => call.Path == "channels/101/ptz/stop");
        Assert.Single(api.Calls, call => call.Path == "channels/102/ptz/stop");
        await ptz.StopAsync(first);
    }
}
