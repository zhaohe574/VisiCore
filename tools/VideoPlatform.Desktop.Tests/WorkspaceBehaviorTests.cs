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

    [Fact]
    public void PtzDrawerCanToggleCollapseState()
    {
        var api = new FakeApi(); var vm = Create(api);
        Assert.False(vm.IsPtzCollapsed);
        vm.TogglePtzCollapsedCommand.Execute(null);
        Assert.True(vm.IsPtzCollapsed);
        vm.TogglePtzCollapsedCommand.Execute(null);
        Assert.False(vm.IsPtzCollapsed);
    }

    [Fact]
    public void ToastNotificationLifecycleAndCommandsWork()
    {
        var api = new FakeApi(); var vm = Create(api);
        Assert.False(vm.IsToastVisible);
        vm.ShowToast("测试抓图成功", "C:\\fake\\path.png");
        Assert.True(vm.IsToastVisible);
        Assert.Equal("测试抓图成功", vm.ToastMessage);
        Assert.Equal("C:\\fake\\path.png", vm.ToastFilePath);
        vm.DismissToastCommand.Execute(null);
        Assert.False(vm.IsToastVisible);
    }

    [Fact]
    public async Task TileLevelStreamSwitchAndAspectRatioWork()
    {
        var api = new FakeApi();
        var factory = new FakePlayerFactory();
        var dispatcher = new InlineDispatcher();
        var tile = new VideoTileViewModel(0, api, factory, dispatcher);
        Assert.Equal(2, tile.StreamType);
        Assert.Equal("SD", tile.StreamBadge);
        Assert.Null(tile.AspectRatio);

        tile.SetAspectRatio("16:9");
        Assert.Equal("16:9", tile.AspectRatio);

        await tile.StartAsync(Fixtures.Channel(1), false, 2, default, default);
        Assert.Equal(2, tile.StreamType);
        Assert.Equal("SD", tile.StreamBadge);
        Assert.Equal("16:9", factory.Players.Last().AspectRatio);

        tile.SetAspectRatio("fill");
        Assert.Equal("fill", tile.AspectRatio);
        tile.ApplyDisplayRatio("1920:1080");
        Assert.Equal("1920:1080", factory.Players.Last().AspectRatio);

        tile.SetAspectRatio("original");
        Assert.Equal("original", tile.AspectRatio);
        Assert.Null(factory.Players.Last().AspectRatio);

        await tile.SwitchStreamAsync(1);
        Assert.Equal(1, tile.StreamType);
        Assert.Equal("HD", tile.StreamBadge);

        await tile.StopAsync();
    }

    [Fact]
    public async Task ResourceTreeExpansionAndOnlineStatsWork()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([
            Fixtures.Channel(1) with { Status = "online" },
            Fixtures.Channel(2) with { Status = "offline" }
        ], 2);
        var vm = Create(api);
        await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view"));
        await vm.RefreshAsync();

        Assert.Equal("在线 1/2", vm.OnlineStatsLabel);

        vm.CollapseAllResourcesCommand.Execute(null);
        Assert.All(vm.Resources.SelectMany(n => n.Flatten()), n => Assert.False(n.IsExpanded));

        vm.ExpandAllResourcesCommand.Execute(null);
        Assert.All(vm.Resources.SelectMany(n => n.Flatten()), n => Assert.True(n.IsExpanded));
    }

    [Fact]
    public void FullscreenCommandTogglesIsFullscreenAndFiresEvent()
    {
        var api = new FakeApi();
        var vm = Create(api);
        Assert.False(vm.IsFullscreen);

        var eventFired = 0;
        vm.FullscreenRequested += () => eventFired++;

        vm.FullscreenCommand.Execute(null);
        Assert.True(vm.IsFullscreen);
        Assert.Equal(1, eventFired);

        vm.FullscreenCommand.Execute(null);
        Assert.False(vm.IsFullscreen);
        Assert.Equal(2, eventFired);
    }

    [Fact]
    public void ToggleBottomBarPinCommandTogglesPinnedState()
    {
        var api = new FakeApi();
        var vm = Create(api);
        Assert.False(vm.IsBottomBarPinned);

        vm.ToggleBottomBarPinCommand.Execute(null);
        Assert.True(vm.IsBottomBarPinned);

        vm.ToggleBottomBarPinCommand.Execute(null);
        Assert.False(vm.IsBottomBarPinned);
    }

    [Fact]
    public async Task ShellSettingsSupportsToggleAndReturnToLoginOrPreviousModule()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(StubHandler.Ok(Fixtures.Login()))));
        var session = new SessionService(http, new MemoryCredentials());
        var api = new FakeApi();
        var workspace = Create(api);
        var alarms = new AlarmsViewModel(api);
        var exports = new ExportsViewModel(api, new FakeDialogs());
        var shell = new ShellViewModel(session, api, new EventService(session), new UpdateService(api),
            workspace, alarms, exports, new InlineDispatcher(), new FakeDialogs());

        // 1. 未登录状态下进入设置并返回登录
        Assert.False(shell.IsAuthenticated);
        Assert.Equal("live", shell.Module);
        Assert.Equal("← 返回登录", shell.BackButtonText);

        await shell.SwitchModuleCommand.ExecuteAsync("settings");
        Assert.Equal("settings", shell.Module);

        // 点击返回按钮恢复 live (即登录界面)
        await shell.CloseSettingsCommand.ExecuteAsync(null);
        Assert.Equal("live", shell.Module);

        // 再次点击设置齿轮按钮应支持切换/恢复
        await shell.SwitchModuleCommand.ExecuteAsync("settings");
        Assert.Equal("settings", shell.Module);
        await shell.SwitchModuleCommand.ExecuteAsync("settings");
        Assert.Equal("live", shell.Module);

        // 2. 已登录状态下从特定模块进入设置并返回原模块
        shell.IsAuthenticated = true;
        Assert.Equal("← 返回工作台", shell.BackButtonText);
        await shell.SwitchModuleCommand.ExecuteAsync("playback");
        Assert.Equal("playback", shell.Module);

        await shell.SwitchModuleCommand.ExecuteAsync("settings");
        Assert.Equal("settings", shell.Module);

        await shell.CloseSettingsCommand.ExecuteAsync(null);
        Assert.Equal("playback", shell.Module);
    }

    [Fact]
    public void BottomBarHoveredPropertyWorks()
    {
        var api = new FakeApi();
        var vm = Create(api);
        Assert.False(vm.IsBottomBarHovered);

        vm.IsBottomBarHovered = true;
        Assert.True(vm.IsBottomBarHovered);
    }

    [Fact]
    public async Task ApplyLayoutLoadsUpToSixteenChannelsConcurrently()
    {
        var api = new FakeApi();
        var channels = Enumerable.Range(1, 16).Select(i => Fixtures.Channel(i)).ToArray();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>(channels, 16);
        var vm = Create(api);
        await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view"));
        await vm.RefreshAsync();

        var channelIds = channels.Select(c => (long?)c.Id).ToArray();
        vm.SelectedLayout = new(1, "16路全开", "layout", false, 16, 30, channelIds);
        await vm.ApplyLayoutCommand.ExecuteAsync(null);

        Assert.Equal(16, vm.LayoutCount);
        Assert.Equal(16, vm.VisibleTiles.Count);
        for (var i = 0; i < 16; i++)
        {
            Assert.NotNull(vm.Tiles[i].SessionId);
            Assert.Equal(i + 1, vm.Tiles[i].Channel?.Id);
        }
        Assert.Equal(16, api.Calls.Count(c => c.Body is LiveRequest));
        await vm.ClearAsync();
    }
}


