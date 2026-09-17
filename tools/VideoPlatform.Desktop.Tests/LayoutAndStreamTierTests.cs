using System.IO;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

/// <summary>iVMS-4200 分屏档位、码流档位联动与资源树增量刷新的回归。</summary>
public sealed class LayoutAndStreamTierTests
{
    private static WorkspaceViewModel Create(FakeApi api, out FakePlayerFactory factory)
    {
        factory = new FakePlayerFactory();
        return new WorkspaceViewModel(api, factory, new PtzService(api), new InlineDispatcher(), new FakeDialogs());
    }

    [Fact]
    public async Task PresetsCoverStandardAndFocusLayouts()
    {
        var api = new FakeApi();
        var vm = Create(api, out _);
        Assert.Equal(new[] { "1", "4", "9", "16", "1+7", "1+9" }, vm.LayoutPresets.Select(preset => preset.Key));
        Assert.Equal(4, vm.LayoutCount);
        Assert.Same(vm.DefaultPreset, vm.SelectedLayoutPreset);
        // 档位格数必须同时出现在 LayoutOptions 与 LayoutPresets 中，否则保存方案时无从校验。
        Assert.All(vm.LayoutPresets, preset => Assert.Contains(preset.Count, vm.LayoutOptions));

        // 授予 25 路分屏权限后，LayoutPresets 动态出现 25 档位
        await vm.SetAccessAsync(Fixtures.User("live.view", "live.split25"));
        Assert.Equal(new[] { "1", "4", "9", "16", "25", "1+7", "1+9" }, vm.LayoutPresets.Select(preset => preset.Key));
        Assert.All(vm.LayoutPresets, preset => Assert.Contains(preset.Count, vm.LayoutOptions));
    }

    [Fact]
    public void LayoutCountsMatchMigrationConstraint()
    {
        // 桌面端 LayoutOptions 与 database/v2/010_layout25_and_permissions.sql 的 CHECK 约束必须完全一致；
        // 任何一处漏改都会让「保存 1+7 / 1+9 / 25 方案」在服务端被拒（data.constraint）。
        // 服务端的 AdministrationService.SupportedLayoutCounts 依赖同样的取值，改动时三处必须同步。
        var api = new FakeApi();
        var vm = Create(api, out _);
        var migration = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "database", "v2", "010_layout25_and_permissions.sql"));
        Assert.True(File.Exists(migration), $"找不到迁移文件：{migration}");
        var allowed = System.Text.RegularExpressions.Regex
            .Match(File.ReadAllText(migration), @"check\s*\(\s*layout\s+in\s*\(([^)]+)\)")
            .Groups[1].Value
            .Split(',')
            .Select(value => int.Parse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(allowed, vm.LayoutOptions.OrderBy(value => value));
    }

    [Fact]
    public void FocusPresetPlacesEveryTileInsideTheGridWithoutOverlap()
    {
        foreach (var preset in new[] { new LayoutPreset("1+7", 8, 4, 4, 3), new LayoutPreset("1+9", 10, 5, 5, 4) })
        {
            var occupied = new HashSet<(int Row, int Column)>();
            for (var index = 0; index < preset.Count; ++index)
            {
                var slot = preset.SlotFor(index);
                Assert.True(slot.RowSpan > 0 && slot.ColumnSpan > 0, $"{preset.Key} 第 {index} 格必须有有效跨度。");
                Assert.InRange(slot.Row + slot.RowSpan, 1, preset.Rows);
                Assert.InRange(slot.Column + slot.ColumnSpan, 1, preset.Columns);
                for (var row = slot.Row; row < slot.Row + slot.RowSpan; ++row)
                    for (var column = slot.Column; column < slot.Column + slot.ColumnSpan; ++column)
                        Assert.True(occupied.Add((row, column)), $"{preset.Key} 第 {index} 格与已有格位重叠：{row},{column}");
            }
            Assert.Equal(0, preset.SlotFor(preset.Count).RowSpan);
            Assert.Equal(0, preset.SlotFor(-1).RowSpan);
            // 重点断言：网格全部填满（1+7 填满 16 格，1+9 填满 25 格），彻底杜绝右下角空缺黑屏
            Assert.Equal(preset.Rows * preset.Columns, occupied.Count);
        }
    }

    [Fact]
    public async Task SelectingFocusPresetUpdatesLayoutCountAndCellSpans()
    {
        var api = new FakeApi();
        var vm = Create(api, out _);
        await vm.SetLayoutCommand.ExecuteAsync("1+7");
        Assert.Equal(8, vm.LayoutCount);
        Assert.Equal(8, vm.VisibleTiles.Count);
        Assert.Equal(4, vm.GridColumns);
        Assert.Equal(4, vm.GridRows);
        Assert.Same(vm.LayoutPresets.Single(preset => preset.Key == "1+7"), vm.SelectedLayoutPreset);
        Assert.Equal(3, vm.Tiles[0].RowSpan);
        Assert.Equal(3, vm.Tiles[0].ColumnSpan);
        Assert.Equal(1, vm.Tiles[1].RowSpan);
        // 第 9 格（索引 8）不在 1+7 档位内：跨度为 0，网格单元宽度也为 0。
        Assert.Equal(LayoutSlot.None, vm.LayoutPresets.Single(preset => preset.Key == "1+7").SlotFor(8));
        Assert.Equal(1, vm.Tiles[8].RowSpan);
        Assert.Equal(new System.Windows.GridLength(0), vm.Tiles[8].CellWidth);
        Assert.False(vm.Tiles[8].IsVisible);
        Assert.False(vm.Tiles[8].IsActive);
        Assert.True(vm.Tiles[7].IsActive);
        await vm.ClearAsync();
    }

    [Fact]
    public async Task GridTilesUseSubStreamAndFocusedTileUpgradesToMainStream()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1), Fixtures.Channel(2)], 2);
        // 服务端会按请求的 streamType 建会话并回传实际档位。
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = ((LiveRequest)body!).StreamType }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var vm = Create(api, out var factory);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        await vm.RefreshAsync();
        await vm.OpenChannelAsync(Fixtures.Channel(1), vm.Tiles[0]);
        Assert.Equal(2, vm.Tiles[0].StreamType);

        // 单窗放大：焦点窗口升主码流，旧的子码流解码器被释放。
        vm.SelectedTile = vm.Tiles[0];
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.Equal(TileDisplayTier.Focused, vm.Tiles[0].DisplayTier);
        Assert.Equal(1, vm.Tiles[0].StreamType);
        var subStreamPlayer = factory.Players[0];
        Assert.True(subStreamPlayer.Disposed);

        // 退出单窗放大：回到格子档位并降回子码流，避免小窗口长期占用主码流解码资源。
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.Equal(TileDisplayTier.Grid, vm.Tiles[0].DisplayTier);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.True(factory.Players[1].Disposed);
        Assert.False(factory.Players[^1].Disposed);

        // 再次放大回到主码流，验证档位切换可反复生效。
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.Equal(TileDisplayTier.Focused, vm.Tiles[0].DisplayTier);
        Assert.Equal(1, vm.Tiles[0].StreamType);

        // 还原为分屏后重新打开通道：按格子档位直接取子码流。
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.False(vm.IsMaximized);
        Assert.Equal(TileDisplayTier.Grid, vm.Tiles[0].DisplayTier);
        Assert.Equal(2, vm.Tiles[0].StreamType);

        // 分屏格子内的其他窗口保持子码流。
        await vm.OpenChannelAsync(Fixtures.Channel(2), vm.Tiles[1]);
        Assert.Equal(TileDisplayTier.Grid, vm.Tiles[1].DisplayTier);
        Assert.Equal(2, vm.Tiles[1].StreamType);

        await vm.ClearAsync();
        Assert.True(factory.Players.All(player => player.Disposed));
    }

    [Fact]
    public async Task ManualStreamOverrideWinsOverDisplayTier()
    {
        var api = new FakeApi();
        var tile = new VideoTileViewModel(0, api, new FakePlayerFactory(), new InlineDispatcher());
        await tile.StartAsync(Fixtures.Channel(1), false, 2, default, default);
        tile.ManualStreamOverride = 1;
        await tile.EnsureStreamTierAsync(TileDisplayTier.Grid);
        Assert.Equal(1, tile.StreamType);
        tile.ManualStreamOverride = null;
        await tile.EnsureStreamTierAsync(TileDisplayTier.Grid);
        Assert.Equal(2, tile.StreamType);
        await tile.StopAsync();
    }

    [Fact]
    public void PlayerOptionsKeepClockSyncEnabledByDefault()
    {
        // 真机实测：16 路下设置 clock-jitter=0／clock-synchro=0 会把 CPU 从 26% 推到 86% 以上并导致丢帧，
        // 因此默认必须保持时钟同步开启；该选项只作为排查开关存在。
        Assert.False(new PlayerOptions().DisableClockSync);
        Assert.Equal(800, new PlayerOptions().EffectiveCachingMs);
        Assert.Equal(200, new PlayerOptions(NetworkCachingMs: 10).EffectiveCachingMs);
        Assert.Equal(5000, new PlayerOptions(NetworkCachingMs: 99999).EffectiveCachingMs);
    }

    [Fact]
    public async Task SubStreamUnavailableKeepsMainStreamWithoutRepeatedRebuild()
    {
        var api = new FakeApi();
        var factory = new FakePlayerFactory();
        // 适配器在无子码流时会回退主码流：会话响应的 StreamType 保持 1。
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = 1 }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var tile = new VideoTileViewModel(0, api, factory, new InlineDispatcher());
        await tile.StartAsync(Fixtures.Channel(1), false, 2, default, default);
        await tile.EnsureStreamTierAsync(TileDisplayTier.Grid);
        var playersAfterFirstAttempt = factory.Players.Count;
        await tile.EnsureStreamTierAsync(TileDisplayTier.Grid);
        await tile.EnsureStreamTierAsync(TileDisplayTier.Grid);
        Assert.Equal(playersAfterFirstAttempt, factory.Players.Count);
        await tile.StopAsync();
    }

    [Fact]
    public async Task RefreshKeepsResourceNodeInstancesWhenNothingChanged()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1), Fixtures.Channel(2)], 2);
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view"));
        await vm.RefreshAsync();
        var first = vm.Resources.Single();
        var channelNodes = first.Children.ToArray();
        Assert.Equal(2, channelNodes.Length);

        await vm.RefreshAsync();
        Assert.Same(first, vm.Resources.Single());
        Assert.Same(channelNodes[0], vm.Resources.Single().Children[0]);

        // 通道下线后必须重建节点并反映在线统计。
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1) with { Status = "offline" }, Fixtures.Channel(2)], 2);
        await vm.RefreshAsync();
        Assert.NotSame(first, vm.Resources.Single());
        Assert.Equal("在线 1/2", vm.Resources.Single().OnlineCountLabel);
        await vm.ClearAsync();
    }

    [Fact]
    public async Task OrganizationGroupingOrganizesChannelsByWorkshopsAreasUnits()
    {
        var api = new FakeApi();
        api.GetResults["organization"] = new Organization(
            [new(1, "一号车间", null, "active", null)],
            [new(10, "总装区域", null, "active", 1)],
            [new(100, "测试单元", null, "active", 10)]);
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>(
            [Fixtures.Channel(101, 1) with { UnitId = 100 }, Fixtures.Channel(202, 2) with { UnitId = 100 }, Fixtures.Channel(303, 2)], 3);
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view"));
        await vm.RefreshAsync();

        // 包含 1 个顶层车间节点和 1 个未分配组织节点
        Assert.Equal(2, vm.Resources.Count);
        var wsNode = vm.Resources[0];
        Assert.Equal("一号车间", wsNode.Name);
        var areaNode = Assert.Single(wsNode.Children);
        Assert.Equal("总装区域", areaNode.Name);
        var unitNode = Assert.Single(areaNode.Children);
        Assert.Equal("测试单元", unitNode.Name);
        Assert.Equal(2, unitNode.Children.Count);
        Assert.Equal("在线 2/2", unitNode.OnlineCountLabel);
        Assert.Equal("在线 2/2", wsNode.OnlineCountLabel);

        var unassignedNode = vm.Resources[1];
        Assert.Equal("未分配组织", unassignedNode.Name);
        Assert.Single(unassignedNode.Children);
        Assert.Equal("门区 303", unassignedNode.Children[0].Name);
        Assert.False(unitNode.Children[0].Name.StartsWith("01  "));
        await vm.ClearAsync();
    }

    [Fact]
    public async Task OrganizationChannelsAreOrderedBySortOrderWithinUnit()
    {
        var api = new FakeApi();
        api.GetResults["organization"] = new Organization(
            [new(1, "一号车间", null, "active", null)],
            [new(10, "总装区域", null, "active", 1)],
            [new(100, "测试单元", null, "active", 10)]);

        // 通道 101 的 SortOrder 是 2，通道 202 的 SortOrder 是 1
        var ch1 = Fixtures.Channel(101, 1) with { UnitId = 100, SortOrder = 2, Name = "通道后" };
        var ch2 = Fixtures.Channel(202, 2) with { UnitId = 100, SortOrder = 1, Name = "通道先" };
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([ch1, ch2], 2);

        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("channel.read", "live.view"));
        await vm.RefreshAsync();

        var unitNode = vm.Resources[0].Children[0].Children[0];
        Assert.Equal("测试单元", unitNode.Name);
        Assert.Equal(2, unitNode.Children.Count);

        // 验证通道名称去除了通道号前缀，直接显示 DisplayName
        Assert.Equal("通道先", unitNode.Children[0].Name);
        Assert.Equal("通道后", unitNode.Children[1].Name);

        // 验证通道严格按 SortOrder 升序排列：SortOrder=1 的 ch2 排在首位
        Assert.Equal(202, unitNode.Children[0].Channel!.Id);
        Assert.Equal(101, unitNode.Children[1].Channel!.Id);
        await vm.ClearAsync();
    }

    [Fact]
    public async Task MaximizingTileKeepsOtherPlayingTilesAliveWithoutDisposing()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1), Fixtures.Channel(2)], 2);
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = ((LiveRequest)body!).StreamType }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var vm = Create(api, out var factory);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        await vm.RefreshAsync();

        // 同时打开 2 路视频
        await vm.OpenChannelAsync(Fixtures.Channel(1), vm.Tiles[0]);
        await vm.OpenChannelAsync(Fixtures.Channel(2), vm.Tiles[1]);
        Assert.True(vm.Tiles[0].IsPlaying);
        Assert.True(vm.Tiles[1].IsPlaying);
        var tile1Player = factory.Players[1];
        Assert.False(tile1Player.Disposed);

        // 单窗放大第 0 格：第 1 格转为 Hidden 档位，但严禁主动 StopAsync 销毁解码器
        vm.SelectedTile = vm.Tiles[0];
        vm.ToggleMaximizeCommand.Execute(null);
        Assert.True(vm.IsMaximized);
        Assert.False(vm.Tiles[1].IsActive);

        // 模拟调用 ApplyLayoutTiersAsync：隐藏格位仅套用档位标记，不杀后台播放器
        await vm.ApplyLayoutTiersAsync();
        Assert.False(tile1Player.Disposed);
        Assert.True(vm.Tiles[1].IsPlaying);

        // 还原回多分屏网格：第 1 格立即激活，播放器依然存活
        vm.ToggleMaximizeCommand.Execute(null);
        Assert.False(vm.IsMaximized);
        Assert.True(vm.Tiles[1].IsActive);
        Assert.False(tile1Player.Disposed);
        Assert.True(vm.Tiles[1].IsPlaying);

        await vm.ClearAsync();
    }

    [Fact]
    public void LayoutPresetFindHandlesNumericStringAndNamedKey()
    {
        var api = new FakeApi();
        var vm = Create(api, out _);
        Assert.Equal("1+7", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "1+7")?.Key);
        Assert.Equal("1+7", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "8")?.Key);
        Assert.Equal("1+9", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "1+9")?.Key);
        Assert.Equal("1+9", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "10")?.Key);
        Assert.Equal("25", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "25")?.Key);
        Assert.Equal("4", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "4")?.Key);
        Assert.Equal("9", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "9")?.Key);
        Assert.Equal("16", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "16")?.Key);
        Assert.Equal("1", LayoutPreset.Find(WorkspaceViewModel.AllPresets, "1")?.Key);
    }

    [Fact]
    public async Task ApplyLayoutCommandSwitchesLayoutCountPresetAndGridDimensions()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1), Fixtures.Channel(2)], 2);
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = ((LiveRequest)body!).StreamType }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        await vm.RefreshAsync();

        // 默认 4 分屏
        Assert.Equal(4, vm.LayoutCount);
        Assert.Equal("4", vm.SelectedLayoutPreset?.Key);

        // 切换到 9 分屏视图
        var layout9 = new LayoutDto(100, "9分屏监控", "layout", false, 9, 30, [1, 2]);
        vm.SelectedLayout = layout9;
        await vm.ApplyLayoutCommand.ExecuteAsync(null);

        Assert.Equal(9, vm.LayoutCount);
        Assert.Equal("9", vm.SelectedLayoutPreset?.Key);
        Assert.Equal("9", vm.CurrentPreset?.Key);
        Assert.Equal(3, vm.GridColumns);
        Assert.Equal(3, vm.GridRows);
        Assert.Equal(9, vm.VisibleTiles.Count);
        Assert.True(vm.Tiles[0].IsPlaying);
        Assert.True(vm.Tiles[1].IsPlaying);
        Assert.Equal("空闲", vm.Tiles[2].StateLabel);

        // 再切换到 1+7 聚焦视图
        var layoutFocus = new LayoutDto(101, "聚焦监控", "layout", false, 8, 30, [1]);
        vm.SelectedLayout = layoutFocus;
        await vm.ApplyLayoutCommand.ExecuteAsync(null);

        Assert.Equal(8, vm.LayoutCount);
        Assert.Equal("1+7", vm.SelectedLayoutPreset?.Key);
        Assert.Equal("1+7", vm.CurrentPreset?.Key);
        Assert.Equal(4, vm.GridColumns);
        Assert.Equal(4, vm.GridRows);
        Assert.Equal(8, vm.VisibleTiles.Count);
        Assert.Equal(3, vm.Tiles[0].RowSpan);
        Assert.Equal(3, vm.Tiles[0].ColumnSpan);
        Assert.True(vm.Tiles[0].IsPlaying);

        await vm.ClearAsync();
    }

    [Fact]
    public async Task SelectingSplit25RequiresPermissionAndTogglesCorrectly()
    {
        var api = new FakeApi();
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));

        // 未分配 live.split25 权限时，切换 25 档位被拦截，保持原档位
        await vm.SetLayoutCommand.ExecuteAsync("25");
        Assert.Equal(4, vm.LayoutCount);
        Assert.Equal("当前账号未被分配 25 路分屏权限。", vm.Status);

        // 分配 live.split25 权限后，支持切换 25 档位且 25 格全部激活
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read", "live.split25"));
        Assert.True(vm.CanSplit25);
        await vm.SetLayoutCommand.ExecuteAsync("25");
        Assert.Equal(25, vm.LayoutCount);
        Assert.Equal(5, vm.GridColumns);
        Assert.Equal(5, vm.GridRows);
        Assert.Equal(25, vm.VisibleTiles.Count);
        Assert.Equal("25", vm.SelectedLayoutPreset?.Key);

        // 权限撤销后，自动平滑退回到默认 4 分屏
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        Assert.False(vm.CanSplit25);
        Assert.Equal(4, vm.LayoutCount);

        await vm.ClearAsync();
    }

    [Fact]
    public async Task NewChannelDefaultsToSubStreamAndToggleMaximizeSwitchesStream()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1), Fixtures.Channel(2)], 2);
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = ((LiveRequest)body!).StreamType }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        await vm.RefreshAsync();

        // 1. 新打开通道默认打开子码流 (streamType == 2)
        await vm.OpenChannelAsync(Fixtures.Channel(1), vm.Tiles[0]);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.Equal("SD", vm.Tiles[0].StreamBadge);

        // 2. 双击放大画面：自动切换为主码流 (streamType == 1)
        vm.SelectedTile = vm.Tiles[0];
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.True(vm.IsMaximized);
        Assert.Equal(1, vm.Tiles[0].StreamType);
        Assert.Equal("HD", vm.Tiles[0].StreamBadge);

        // 3. 再次双击切回小画面：自动换回子码流 (streamType == 2)
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.False(vm.IsMaximized);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.Equal("SD", vm.Tiles[0].StreamBadge);

        // 4. 手动切为主码流后，重新打开通道依然默认使用子码流
        vm.Tiles[0].ManualStreamOverride = 1;
        await vm.Tiles[0].SwitchStreamAsync(1);
        Assert.Equal(1, vm.Tiles[0].StreamType);

        await vm.OpenChannelAsync(Fixtures.Channel(2), vm.Tiles[0]);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.Equal("SD", vm.Tiles[0].StreamBadge);
        Assert.Null(vm.Tiles[0].ManualStreamOverride);

        await vm.ClearAsync();
    }

    [Fact]
    public async Task ApplySavedViewDefaultsAllTilesToSubStream()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1), Fixtures.Channel(2), Fixtures.Channel(3), Fixtures.Channel(4)], 4);
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = ((LiveRequest)body!).StreamType }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        await vm.RefreshAsync();

        // 加载包含 4 路通道的保存视图
        var layout = new LayoutDto(200, "测试值守视图", "layout", false, 4, 30, [1, 2, 3, 4]);
        vm.SelectedLayout = layout;
        await vm.ApplyLayoutCommand.ExecuteAsync(null);

        // 视图内所有已打开通道必须全部默认采用子码流 (2)
        Assert.Equal(4, vm.LayoutCount);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.Equal(2, vm.Tiles[1].StreamType);
        Assert.Equal(2, vm.Tiles[2].StreamType);
        Assert.Equal(2, vm.Tiles[3].StreamType);
        Assert.All(vm.VisibleTiles, tile => Assert.Equal("SD", tile.StreamBadge));

        // 双击放大第 2 格，仅第 2 格升为主码流
        vm.SelectedTile = vm.Tiles[1];
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.True(vm.IsMaximized);
        Assert.Equal(1, vm.Tiles[1].StreamType);
        Assert.Equal("HD", vm.Tiles[1].StreamBadge);

        // 还原回 4 分屏，第 2 格自动换回子码流
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.False(vm.IsMaximized);
        Assert.Equal(2, vm.Tiles[1].StreamType);
        Assert.Equal("SD", vm.Tiles[1].StreamBadge);

        await vm.ClearAsync();
    }

    [Fact]
    public async Task SingleLayoutPresetDefaultsToSubStreamAndTogglesOnMaximize()
    {
        var api = new FakeApi();
        api.GetResults["channels?page=1&pageSize=200"] = new Page<Channel>([Fixtures.Channel(1)], 1);
        api.Post = (path, body) => Task.FromResult<object>(path == "live-sessions"
            ? Fixtures.Media(((LiveRequest)body!).ChannelId) with { StreamType = ((LiveRequest)body!).StreamType }
            : Fixtures.Media(((PlaybackRequest)body!).ChannelId));
        var vm = Create(api, out _);
        await vm.SetAccessAsync(Fixtures.User("live.view", "channel.read"));
        await vm.RefreshAsync();

        // 切换到 1 分屏
        await vm.SetLayoutCommand.ExecuteAsync("1");
        Assert.Equal(1, vm.LayoutCount);

        // 1 分屏下新打开摄像头：必须默认打开子码流
        await vm.OpenChannelAsync(Fixtures.Channel(1), vm.Tiles[0]);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.Equal("SD", vm.Tiles[0].StreamBadge);

        // 双击放大：自动切换为主码流
        vm.SelectedTile = vm.Tiles[0];
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.True(vm.IsMaximized);
        Assert.Equal(1, vm.Tiles[0].StreamType);
        Assert.Equal("HD", vm.Tiles[0].StreamBadge);

        // 切换回小画面：自动换回子码流
        vm.ToggleMaximizeCommand.Execute(null);
        await vm.ApplyLayoutTiersAsync();
        Assert.False(vm.IsMaximized);
        Assert.Equal(2, vm.Tiles[0].StreamType);
        Assert.Equal("SD", vm.Tiles[0].StreamBadge);

        await vm.ClearAsync();
    }
}
