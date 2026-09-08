using System.Net.Http;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

public sealed class WorkspaceBehaviorTests
{
    private static WorkspaceViewModel Create(FakeApi api) => new(api, new FakePlayerFactory(), new PtzService(api), new InlineDispatcher(), new FakeDialogs());

    [Fact]
    public async Task ResourceRefreshReadsAllPagesBeyondTwoHundredChannels()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>(Enumerable.Range(1, 200).Select(id => Fixtures.Channel(id)).ToArray(), 201);
        api.GetResults["channels?page=2&pageSize=200"] = new Page<Channel>([Fixtures.Channel(201)], 201, 2);
        var vm = Create(api); await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view")); await vm.RefreshAsync();
        Assert.Equal(201, vm.Channels.Count);
        Assert.Contains(201, vm.Channels.Keys);
        Assert.Equal(201, vm.Resources.SelectMany(n => n.Flatten()).Count(n => n.IsChannel));
    }

    [Fact]
    public async Task SharedPatrolSkipsOfflineAndRevokedChannelsWithoutCreatingSessions()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(101), Fixtures.Channel(102) with { Status = "offline" }], 2);
        var vm = Create(api); await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view")); await vm.RefreshAsync();
        vm.SelectedLayout = new(1, "共享轮巡", "patrol", true, 4, 30, [101, 102, 999]);
        await vm.ApplyLayoutCommand.ExecuteAsync(null);
        Assert.True(vm.IsPatrolling);
        Assert.All(api.Calls.Where(c => c.Body is LiveRequest), c => Assert.Equal(101, ((LiveRequest)c.Body!).ChannelId));
        Assert.Null(vm.Tiles[1].SessionId); Assert.Null(vm.Tiles[2].SessionId);
        await vm.ClearAsync();
    }

    [Fact]
    public async Task UnifiedPlaybackControlsAllActiveWindowsButIndependentControlsOnlySelected()
    {
        var api = new FakeApi(); var vm = Create(api); await vm.SetAccessAsync(Fixtures.User("playback.view"));
        for (var i = 0; i < 3; ++i) await vm.Tiles[i].StartAsync(Fixtures.Channel(i + 1), true, 1, DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now);
        vm.UnifiedControl = true; await vm.PlaybackActionCommand.ExecuteAsync("pause");
        Assert.Equal(3, api.Calls.Count(c => c.Body is PlaybackControl { Action: "pause" }));
        vm.SelectedTile = vm.Tiles[1]; vm.UnifiedControl = false; await vm.PlaybackActionCommand.ExecuteAsync("resume");
        var resumed = Assert.Single(api.Calls, c => c.Body is PlaybackControl { Action: "resume" });
        Assert.Equal($"playback-sessions/{vm.Tiles[1].SessionId}/control", resumed.Path);
        await vm.ClearAsync();
    }

    [Fact]
    public async Task ShrinkingLayoutStopsHiddenWindowsAndKeepsSelectionVisible()
    {
        var api = new FakeApi(); var vm = Create(api);
        await vm.SetLayoutCommand.ExecuteAsync("16"); Assert.Equal(16, vm.VisibleTiles.Count);
        await vm.Tiles[15].StartAsync(Fixtures.Channel(), false, 2, default, default); vm.SelectedTile = vm.Tiles[15];
        await vm.SetLayoutCommand.ExecuteAsync("4");
        Assert.Equal(4, vm.VisibleTiles.Count); Assert.True(vm.SelectedTile!.Index < 4); Assert.Null(vm.Tiles[15].SessionId);
    }

    [Fact]
    public async Task AlarmClosureRequiresNoteBeforeSendingMutation()
    {
        var api = new FakeApi(); var vm = new AlarmsViewModel(api); vm.SetAccess(Fixtures.User("alarm.read", "alarm.ack"));
        vm.Selected = new(1, 1, "录像机", 101, "东门", "移动侦测", DateTimeOffset.Now, "processing", true, 1, "值守员", null, false, null);
        await vm.HandleCommand.ExecuteAsync("close");
        Assert.Contains("备注", vm.Status); Assert.DoesNotContain(api.Calls, c => c.Method == HttpMethod.Post);
        vm.Note = "现场已核实"; await vm.HandleCommand.ExecuteAsync("close");
        Assert.Contains(api.Calls, c => c.Body is AlarmAction { Action: "close", Note: "现场已核实" });
    }
}
