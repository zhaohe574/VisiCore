using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IPlatformApi _api;
    private readonly PtzService _ptz;
    private readonly IUserInteraction _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _patrolGate = new(1, 1);
    private readonly HashSet<long> _favoriteIds = [];
    private readonly Dictionary<long, Channel> _channels = [];
    private User? _user;
    private long _generation;
    private Organization? _lastOrganization;
    private LayoutDto? _patrol;
    private int _patrolOffset;
    private DateTimeOffset _nextPatrol;
    public ObservableCollection<ResourceNode> Resources { get; } = [];
    public ObservableCollection<VideoTileViewModel> Tiles { get; } = [];
    public ObservableCollection<VideoTileViewModel> VisibleTiles { get; } = [];
    public ObservableCollection<Recording> Recordings { get; } = [];
    public ObservableCollection<LayoutDto> Layouts { get; } = [];
    public IReadOnlyDictionary<long, Channel> Channels => _channels;
    /// <summary>播放器工厂：设置页通过 IConfigurablePlayerFactory 套用硬解与网络缓存参数。</summary>
    public IPlayerFactory PlayerFactory { get; }
    /// <summary>
    /// 允许的分屏格数，必须与 database/v2/007_layout_presets.sql 的约束一致，
    /// 否则保存的布局会被服务端以 data.constraint 拒绝。
    /// </summary>
    public int[] LayoutOptions { get; } = [1, 4, 6, 8, 9, 10, 16, 25];
    /// <summary>iVMS-4200 主线分屏档位：1 / 4 / 9 / 16 / 25，以及“1 大屏 + n 小屏”的 1+7、1+9 聚焦档位。</summary>
    public static readonly LayoutPreset[] AllPresets =
    [
        new("1", 1, 1, 1, 0),
        new("4", 4, 2, 2, 0),
        new("9", 9, 3, 3, 0),
        new("16", 16, 4, 4, 0),
        new("25", 25, 5, 5, 0),
        new("1+7", 8, 4, 4, 3),
        new("1+9", 10, 5, 5, 4)
    ];
    public LayoutPreset DefaultPreset => LayoutPresets.Count > 1 ? LayoutPresets[1] : AllPresets[1];
    public ObservableCollection<LayoutPreset> LayoutPresets { get; } = [];
    public double[] SpeedOptions { get; } = [0.25, 0.5, 1, 2, 4, 8];
    public Choice[] StreamOptions { get; } = [new("2", "子码流"), new("1", "主码流")];
    [ObservableProperty] private VideoTileViewModel? _selectedTile;
    [ObservableProperty] private ResourceNode? _selectedResource;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _isPlayback;
    [ObservableProperty] private bool _unifiedControl = true;
    [ObservableProperty] private bool _isMaximized;
    [ObservableProperty] private int _layoutCount = 4;
    [ObservableProperty] private int _gridColumns = 2;
    [ObservableProperty] private int _gridRows = 2;
    [ObservableProperty] private LayoutPreset? _selectedLayoutPreset;
    public LayoutPreset? CurrentPreset { get; private set; }
    [ObservableProperty] private string _resourceGroupMode = "organization";
    [ObservableProperty] private string _streamType = "2";
    [ObservableProperty] private DateTime _recordingDate = DateTime.Today;
    [ObservableProperty] private string _startTime = "00:00:00";
    [ObservableProperty] private string _endTime = "23:59:59";
    [ObservableProperty] private DateTimeOffset _rangeStart = new DateTimeOffset(DateTime.Today);
    [ObservableProperty] private DateTimeOffset _rangeEnd = new DateTimeOffset(DateTime.Today.AddDays(1));
    [ObservableProperty] private DateTimeOffset? _playhead;
    [ObservableProperty] private RecordingSegment[] _timelineSegments = [];
    [ObservableProperty] private TimelineTrack[] _timelineTracks = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTimelineClip))] [NotifyPropertyChangedFor(nameof(ExportButtonText))] private DateTimeOffset? _timelineClipStart;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTimelineClip))] [NotifyPropertyChangedFor(nameof(ExportButtonText))] private DateTimeOffset? _timelineClipEnd;
    public bool HasTimelineClip => TimelineClipStart.HasValue && TimelineClipEnd.HasValue && TimelineClipEnd > TimelineClipStart;
    public string ExportButtonText => HasTimelineClip ? "导出所选区间" : "导出录像";
    [ObservableProperty] private Recording? _selectedRecording;
    [ObservableProperty] private double _playbackSpeed = 1;
    [ObservableProperty] private LayoutDto? _selectedLayout;
    [ObservableProperty] private string _layoutName = "我的值守布局";
    [ObservableProperty] private bool _saveAsPatrol;
    [ObservableProperty] private bool _shareLayout;
    [ObservableProperty] private int _intervalSeconds = 30;
    [ObservableProperty] private bool _isPatrolling;
    [ObservableProperty] private int _ptzSpeed = 4;
    [ObservableProperty] private int _preset = 1;
    [ObservableProperty] private bool _canLive;
    [ObservableProperty] private bool _canPlayback;
    [ObservableProperty] private bool _canPtz;
    [ObservableProperty] private bool _canExport;
    [ObservableProperty] private bool _canShareLayouts;
    [ObservableProperty] private bool _canSplit25;
    [ObservableProperty] private bool _isPtzCollapsed;
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private bool _isBottomBarPinned;
    [ObservableProperty] private bool _isBottomBarHovered;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private string _toastFilePath = "";
    private CancellationTokenSource? _toastCts;
    public event Func<Task>? ExportCreated;
    public event Action? FullscreenRequested;

    public void UpdateAvailableLayoutPresets()
    {
        var wanted = AllPresets.Where(p => p.Count != 25 || CanSplit25).ToArray();
        if (LayoutPresets.SequenceEqual(wanted)) return;
        LayoutPresets.Clear();
        foreach (var p in wanted) LayoutPresets.Add(p);
    }

    public WorkspaceViewModel(IPlatformApi api, IPlayerFactory players, PtzService ptz, IUiDispatcher dispatcher, IUserInteraction dialogs)
    {
        _api = api; _ptz = ptz; _dialogs = dialogs; _dispatcher = dispatcher;
        PlayerFactory = players;
        for (var i = 0; i < 25; ++i) Tiles.Add(new(i, api, players, dispatcher));
        foreach (var tile in Tiles) tile.PreferSubStreamInGrid = _preferSubStreamInGrid;
        SelectedTile = Tiles[0];
        UpdateAvailableLayoutPresets();
        CurrentPreset = DefaultPreset;
        SelectedLayoutPreset = DefaultPreset;
        UpdateVisibleTiles();
        _ptz.Failed += message => _ = dispatcher.InvokeAsync(() => { Status = message; return Task.CompletedTask; });
    }
    public async Task SetAccessAsync(User? user)
    {
        _user = user;
        CanLive = user?.Can("live.view") == true;
        CanPlayback = user?.Can("playback.view") == true;
        CanPtz = user?.Can("ptz.control") == true;
        CanExport = user?.Can("recording.export") == true || user?.Can("export.create") == true || user?.Can("playback.export") == true;
        CanShareLayouts = user?.Can("layout.share") == true;
        CanSplit25 = user?.Can("live.split25") == true;
        UpdateAvailableLayoutPresets();
        if (!CanSplit25 && (SelectedLayoutPreset?.Count == 25 || LayoutCount == 25))
        {
            await SetLayoutAsync("4");
        }
        if (user is null) await ClearAsync();
    }
    public async Task SuspendAccessAsync()
    {
        ++_generation;
        CanLive = CanPlayback = CanPtz = CanExport = CanSplit25 = false;
        UpdateAvailableLayoutPresets();
        await StopAllCoreAsync();
    }
    public async Task ClearAsync()
    {
        await SuspendAccessAsync();
        _user = null;
        _channels.Clear(); Resources.Clear(); Recordings.Clear(); Layouts.Clear(); _favoriteIds.Clear();
        SelectedResource = null; SelectedLayout = null; TimelineSegments = []; Playhead = null;
    }
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        var generation = _generation;
        try
        {
            if (_user is null) return;
            var channels = new List<Channel>();
            for (var page = 1; ; ++page)
            {
                var result = await _api.GetAsync<Page<Channel>>($"channels?page={page}&pageSize=200");
                if (result is null || result.Items.Length == 0) break;
                channels.AddRange(result.Items);
                if (channels.Count >= result.Total) break;
            }
            var favorites = await _api.GetAsync<Channel[]>("favorites") ?? [];
            var layouts = await _api.GetAsync<LayoutDto[]>("layouts") ?? [];
            Organization? organization = null;
            try { organization = await _api.GetAsync<Organization>("organization"); }
            catch (PlatformException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden) { }
            _lastOrganization = organization;
            if (generation != _generation) return;
            var selectedId = SelectedResource?.Channel?.Id;
            var checkedIds = CheckedChannels().Select(c => c.Id).ToHashSet();
            if (channels.Count > 0)
            {
                // 通道集合与在线状态都未变化时保留现有节点实例，避免每 60 秒重建整棵树造成容器重排与闪烁。
                var changed = channels.Count != _channels.Count || channels.Any(channel => !_channels.TryGetValue(channel.Id, out var existing)
                    || existing.Online != channel.Online || existing.Alias != channel.Alias || existing.PtzCapable != channel.PtzCapable
                    || existing.Name != channel.Name || existing.UnitId != channel.UnitId || existing.DeviceName != channel.DeviceName);
                if (changed)
                {
                    foreach (var channel in channels) _channels[channel.Id] = channel;
                    foreach (var stale in _channels.Keys.Where(id => channels.All(channel => channel.Id != id)).ToArray()) _channels.Remove(stale);
                }
                _favoriteIds.Clear(); _favoriteIds.UnionWith(favorites.Select(c => c.Id).Where(_channels.ContainsKey));
                if (changed) BuildResources(channels, organization, checkedIds);
                SelectedResource = Resources.SelectMany(n => n.Flatten()).FirstOrDefault(n => n.Channel?.Id == selectedId);
                var layoutId = SelectedLayout?.Id;
                Layouts.Clear(); foreach (var layout in layouts) Layouts.Add(layout);
                SelectedLayout = Layouts.FirstOrDefault(l => l.Id == layoutId);
                if (changed)
                {
                    foreach (var tile in Tiles.Where(t => t.Channel is not null && !_channels.ContainsKey(t.Channel.Id))) await tile.StopAsync();
                    Filter();
                }
                OnPropertyChanged(nameof(OnlineStatsLabel));
            }
        }
        finally { _refreshGate.Release(); }
    }
    /// <summary>
    /// 构建资源树。只保留组织架构模式（车间 → 区域 → 单元），未关联组织的通道归入“未分配组织”节点。
    /// </summary>
    private void BuildResources(IEnumerable<Channel> channels, Organization? organization, HashSet<long> checkedIds)
    {
        Resources.Clear();
        var channelList = channels.ToList();

        var units = organization?.Units.ToDictionary(u => u.Id) ?? [];
        var areas = organization?.Areas.ToDictionary(a => a.Id) ?? [];
        var workshops = organization?.Workshops.ToDictionary(w => w.Id) ?? [];

        var assigned = channelList.Where(c => c.UnitId is not null && units.ContainsKey(c.UnitId.Value)).ToList();
        var unassigned = channelList.Where(c => c.UnitId is null || !units.ContainsKey(c.UnitId.Value)).ToList();

        var workshopNodes = new Dictionary<long, ResourceNode>();
        var areaNodes = new Dictionary<long, ResourceNode>();
        var unitNodes = new Dictionary<long, ResourceNode>();

        foreach (var channel in assigned.OrderBy(c => c.DeviceChannel))
        {
            var unit = units[channel.UnitId!.Value];
            if (!unitNodes.TryGetValue(unit.Id, out var unitNode))
            {
                unitNode = new ResourceNode(unit.Name);
                unitNodes[unit.Id] = unitNode;

                if (unit.ParentId is { } parentId && areas.TryGetValue(parentId, out var area))
                {
                    if (!areaNodes.TryGetValue(area.Id, out var areaNode))
                    {
                        areaNode = new ResourceNode(area.Name);
                        areaNodes[area.Id] = areaNode;

                        if (area.ParentId is { } wsId && workshops.TryGetValue(wsId, out var ws))
                        {
                            if (!workshopNodes.TryGetValue(ws.Id, out var wsNode))
                            {
                                wsNode = new ResourceNode(ws.Name);
                                workshopNodes[ws.Id] = wsNode;
                                Resources.Add(wsNode);
                            }
                            wsNode.Children.Add(areaNode);
                        }
                        else
                        {
                            Resources.Add(areaNode);
                        }
                    }
                    areaNode.Children.Add(unitNode);
                }
                else if (unit.ParentId is { } wsParentId && workshops.TryGetValue(wsParentId, out var directWs))
                {
                    if (!workshopNodes.TryGetValue(directWs.Id, out var wsNode))
                    {
                        wsNode = new ResourceNode(directWs.Name);
                        workshopNodes[directWs.Id] = wsNode;
                        Resources.Add(wsNode);
                    }
                    wsNode.Children.Add(unitNode);
                }
                else
                {
                    Resources.Add(unitNode);
                }
            }
            unitNode.Children.Add(CreateChannelNode(channel, checkedIds));
        }

        if (unassigned.Count > 0)
        {
            var unassignedNode = new ResourceNode("未分配组织");
            foreach (var channel in unassigned.OrderBy(c => c.DeviceName).ThenBy(c => c.DeviceChannel))
                unassignedNode.Children.Add(CreateChannelNode(channel, checkedIds));
            Resources.Add(unassignedNode);
        }
    }

    private static ResourceNode CreateChannelNode(Channel channel, HashSet<long> checkedIds) =>
        new($"{channel.DeviceChannel:00}  {channel.DisplayName}", channel) { IsChecked = checkedIds.Contains(channel.Id), IsExpanded = false };
    public string OnlineStatsLabel => $"在线 {_channels.Values.Count(c => c.Online)}/{_channels.Count}";
    private IEnumerable<Channel> CheckedChannels() => Resources.SelectMany(n => n.Flatten()).Where(n => n.IsChecked && n.Channel is not null).Select(n => n.Channel!);
    partial void OnSearchChanged(string value) => Filter();
    partial void OnFavoritesOnlyChanged(bool value) => Filter();
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void ExpandAllResources() { foreach (var root in Resources) root.SetExpandedRecursive(true); }
    [RelayCommand] private void CollapseAllResources() { foreach (var root in Resources) root.SetExpandedRecursive(false); }
    partial void OnSelectedLayoutChanged(LayoutDto? value)
    {
        if (value is null) return;
        LayoutName = value.Name; SaveAsPatrol = value.Kind == "patrol"; ShareLayout = value.Shared; IntervalSeconds = value.IntervalSeconds;
    }
    private void Filter() { foreach (var node in Resources) node.Filter(Search.Trim(), FavoritesOnly ? _favoriteIds : null); }
    partial void OnSelectedTileChanged(VideoTileViewModel? oldValue, VideoTileViewModel? newValue)
    {
        if (oldValue is not null) { oldValue.IsSelected = false; oldValue.PropertyChanged -= TileChanged; }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            newValue.PropertyChanged += TileChanged;
            SetPlayhead(newValue.CurrentTime);
            TimelineSegments = newValue.Segments;
            if (newValue.Channel is not null)
            {
                StreamType = newValue.StreamType.ToString();
                UpdateActiveTrack(newValue.Channel.DisplayName);
            }
        }
        _ = StopPtzAsync();
        // 单窗放大时焦点窗口决定可见集合，选中变化必须同步收敛。
        if (IsMaximized) UpdateVisibleTiles();
    }
    private void UpdateActiveTrack(string? channelName)
    {
        if (TimelineTracks.Length == 0) return;
        TimelineTracks = TimelineTracks
            .Select(t => t with { IsActive = !string.IsNullOrEmpty(channelName) && t.ChannelName == channelName })
            .ToArray();
    }
    private void TileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoTileViewModel.CurrentTime)) SetPlayhead(SelectedTile?.CurrentTime);
        if (e.PropertyName == nameof(VideoTileViewModel.Segments)) TimelineSegments = SelectedTile?.Segments ?? [];
        if (e.PropertyName == nameof(VideoTileViewModel.StreamType) && SelectedTile?.Channel is not null) StreamType = SelectedTile.StreamType.ToString();
    }

    /// <summary>回放播放头按需刷新：变化小于 1 秒时不触发时间轴重绘，避免每拍整幅重画。</summary>
    private long _playheadTicksStamp;
    private void SetPlayhead(DateTimeOffset? value)
    {
        if (value is null) { Playhead = null; _playheadTicksStamp = 0; return; }
        var ticks = value.Value.UtcTicks;
        if (Playhead is { } current && Math.Abs(ticks - _playheadTicksStamp) < TimeSpan.TicksPerSecond) return;
        _playheadTicksStamp = ticks;
        Playhead = value;
    }
    private void UpdateVisibleTiles()
    {
        VideoMouseHook.ClearHoveredTile();
        var wanted = new List<VideoTileViewModel>(LayoutCount);
        foreach (var tile in Tiles)
        {
            tile.IsVisible = tile.Index < LayoutCount && (!IsMaximized || ReferenceEquals(tile, SelectedTile));
            if (tile.IsVisible) wanted.Add(tile);
        }
        // 可见集合未变化时不重置集合，避免每 5 秒 tick 触发的保存/恢复造成容器与原生窗口重建。
        if (VisibleTiles.Count != wanted.Count || !VisibleTiles.SequenceEqual(wanted))
        {
            VisibleTiles.Clear();
            foreach (var tile in wanted) VisibleTiles.Add(tile);
        }
        CurrentPreset ??= SelectedLayoutPreset ?? LayoutPreset.Find(AllPresets, LayoutCount.ToString());
        GridColumns = IsMaximized ? 1 : (CurrentPreset?.Columns ?? (int)Math.Sqrt(LayoutCount));
        GridRows = IsMaximized ? 1 : (CurrentPreset?.Rows ?? (int)Math.Sqrt(LayoutCount));
        ApplyPresetSpans();
    }

    /// <summary>按当前档位把格位跨度写回每格，1+7/1+9 的首格跨列跨行占满左上大屏区域。</summary>
    private void ApplyPresetSpans()
    {
        var preset = CurrentPreset;
        var cols = Math.Max(GridColumns, 1);
        foreach (var tile in Tiles)
        {
            var visible = tile.IsVisible;
            LayoutSlot slot;
            if (!visible)
            {
                slot = LayoutSlot.None;
            }
            else if (IsMaximized)
            {
                slot = new LayoutSlot(0, 0, 1, 1);
            }
            else if (preset is not null)
            {
                slot = preset.SlotFor(tile.Index);
            }
            else
            {
                slot = new LayoutSlot(tile.Index / cols, tile.Index % cols, 1, 1);
            }
            var active = slot.RowSpan > 0 && slot.ColumnSpan > 0;
            tile.RowSpan = active ? slot.RowSpan : 1;
            tile.ColumnSpan = active ? slot.ColumnSpan : 1;
            tile.GridRow = active ? slot.Row : 0;
            tile.GridColumn = active ? slot.Column : 0;
            tile.CellWidth = visible && active ? new GridLength(slot.ColumnSpan) : new GridLength(0);
            tile.CellHeight = visible && active ? new GridLength(slot.RowSpan) : new GridLength(0);
            // 非当前档位的格子必须从网格排布中移除，否则会占位并把其它格挤成小格。
            tile.IsActive = visible && active;
            tile.IsMaximized = IsMaximized;
        }
    }
    [RelayCommand] private Task RefreshResourcesAsync() => RunAsync(RefreshAsync, "资源已同步。");
    [RelayCommand] private Task SetLayoutAsync(string? value) => RunAsync(async () =>
    {
        var preset = LayoutPreset.Find(AllPresets, value);
        if (preset?.Count == 25 && !CanSplit25)
        {
            Status = "当前账号未被分配 25 路分屏权限。";
            return;
        }
        if (preset is null)
        {
            if (!int.TryParse(value, out var count) || !LayoutOptions.Contains(count)) return;
            if (count == 25 && !CanSplit25)
            {
                Status = "当前账号未被分配 25 路分屏权限。";
                return;
            }
            var cols = (int)Math.Max(1, Math.Round(Math.Sqrt(count)));
            var rows = (int)Math.Ceiling((double)count / cols);
            await ApplyLayoutCountAsync(count, cols, rows);
            return;
        }
        await ApplyLayoutCountAsync(preset.Count, preset.Columns, preset.Rows, preset);
    });
    private async Task ApplyLayoutCountAsync(int count, int columns, int rows, LayoutPreset? preset = null)
    {
        foreach (var tile in Tiles.Skip(count)) await tile.StopAsync();
        LayoutCount = count; IsMaximized = false;
        SelectedLayoutPreset = preset;
        GridColumns = columns;
        GridRows = rows;
        CurrentPreset = preset;
        if (SelectedTile is null || SelectedTile.Index >= count) SelectedTile = Tiles[0];
        UpdateVisibleTiles();
    }
    [RelayCommand] private Task SetLayoutPresetAsync(string? value) => SetLayoutAsync(value);
    /// <summary>界面在设置完 VisibleTiles 之后调用，统一套用显示档位；不阻塞命令本身。</summary>
    public Task ApplyLayoutTiersAsync()
    {
        // 先收敛可见集合与格位跨度，保证按下述档位计算的 DisplayTier 与界面一致。
        UpdateVisibleTiles();
        foreach (var tile in Tiles) tile.PreferSubStreamInGrid = _preferSubStreamInGrid;
        return Task.WhenAll(VisibleTiles.Select(tile => tile.EnsureStreamTierAsync(WorkspaceTier(tile), _preferSubStreamInGrid)));
    }
    /// <summary>显示档位：格子默认子码流，单窗放大与全屏升主码流，降低 16 路时的解码开销。</summary>
    public bool PreferSubStreamInGrid
    {
        get => _preferSubStreamInGrid;
        set
        {
            if (_preferSubStreamInGrid == value) return;
            _preferSubStreamInGrid = value;
            foreach (var tile in Tiles) tile.PreferSubStreamInGrid = value;
        }
    }
    private bool _preferSubStreamInGrid = true;
    private TileDisplayTier WorkspaceTier(VideoTileViewModel tile) =>
        IsMaximized ? (ReferenceEquals(tile, SelectedTile) ? TileDisplayTier.Focused : TileDisplayTier.Hidden)
        : LayoutCount == 1 ? (IsFullscreen ? TileDisplayTier.Fullscreen : TileDisplayTier.Focused)
        : TileDisplayTier.Grid;
    [RelayCommand] private void ToggleMaximize() { VideoMouseHook.ClearHoveredTile(); IsMaximized = !IsMaximized; UpdateVisibleTiles(); }
    [RelayCommand]
    private void Fullscreen()
    {
        IsFullscreen = !IsFullscreen;
        FullscreenRequested?.Invoke();
    }
    [RelayCommand] private void ToggleMute() { if (SelectedTile is not null) SelectedTile.IsMuted = !SelectedTile.IsMuted; }
    [RelayCommand] private void TogglePtzCollapsed() => IsPtzCollapsed = !IsPtzCollapsed;
    [RelayCommand] private void ToggleBottomBarPin() => IsBottomBarPinned = !IsBottomBarPinned;
    public void ShowToast(string message, string filePath)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        ToastMessage = message;
        ToastFilePath = filePath;
        IsToastVisible = true;
        var token = _toastCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000, token);
                if (!token.IsCancellationRequested)
                {
                    await _dispatcher.InvokeAsync(() => { IsToastVisible = false; return Task.CompletedTask; });
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    [RelayCommand] private void DismissToast()
    {
        _toastCts?.Cancel();
        IsToastVisible = false;
    }
    [RelayCommand] private void OpenToastFile()
    {
        if (File.Exists(ToastFilePath))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ToastFilePath) { UseShellExecute = true }); }
            catch (Exception ex) { Status = $"打开文件失败：{ex.Message}"; }
        }
    }
    [RelayCommand] private void OpenToastFolder()
    {
        if (File.Exists(ToastFilePath))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{ToastFilePath}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Status = $"打开目录失败：{ex.Message}"; }
        }
    }
    [RelayCommand] private Task QuickCaptureAsync(VideoTileViewModel? targetTile) => RunAsync(() =>
    {
        var tile = targetTile ?? SelectedTile;
        if (tile?.SessionId is null) throw new InvalidOperationException("请先选择正在播放的窗口。");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VisiCore", DateTime.Now.ToString("yyyy-MM-dd"));
        var path = tile.QuickCapture(dir);
        if (path is null) throw new InvalidOperationException("当前视频帧尚不可用，抓图失败。");
        ShowToast($"抓图已保存：{Path.GetFileName(path)}", path);
        Status = $"抓图已保存：{path}";
        return Task.CompletedTask;
    });
    [RelayCommand] private Task QuickCaptureAllAsync() => RunAsync(() =>
    {
        var playing = VisibleTiles.Where(t => t.SessionId is not null).ToArray();
        if (playing.Length == 0) throw new InvalidOperationException("当前分屏中没有正在播放的窗口。");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VisiCore", DateTime.Now.ToString("yyyy-MM-dd"));
        var count = 0;
        string? last = null;
        foreach (var tile in playing)
        {
            var p = tile.QuickCapture(dir);
            if (p is not null) { count++; last = p; }
        }
        if (count == 0) throw new InvalidOperationException("抓图失败。");
        if (last is not null) ShowToast($"已完成全部抓图（共 {count} 张）", last);
        Status = $"已抓取 {count} 个窗口画面至目录：{dir}";
        return Task.CompletedTask;
    });
    [RelayCommand] private Task SaveCaptureAsAsync(VideoTileViewModel? targetTile) => RunAsync(() =>
    {
        var tile = targetTile ?? SelectedTile;
        if (tile?.SessionId is null) throw new InvalidOperationException("请先选择正在播放的窗口。");
        var path = _dialogs.SaveFile($"截图-{DateTime.Now:yyyyMMdd-HHmmss}.png", "PNG 图片|*.png");
        if (path is not null && !tile.Capture(path)) throw new InvalidOperationException("当前视频帧尚不可用，截图失败。");
        if (path is not null)
        {
            ShowToast($"截图已保存：{Path.GetFileName(path)}", path);
            Status = $"截图已保存：{path}";
        }
        return Task.CompletedTask;
    });
    [RelayCommand] private Task CaptureAsync() => QuickCaptureAsync(SelectedTile);
    [RelayCommand] private Task StopTileAsync(VideoTileViewModel? tile) => RunAsync(async () =>
    {
        if (tile is not null)
        {
            if (tile == SelectedTile) await StopPtzAsync();
            await tile.StopAsync();
        }
    });
    [RelayCommand] private void ToggleMuteTile(VideoTileViewModel? tile)
    {
        if (tile is not null) tile.IsMuted = !tile.IsMuted;
    }
    [RelayCommand] private Task SwitchTileStreamAsync(VideoTileViewModel? tile) => RunAsync(async () =>
    {
        if (tile?.Channel is null || IsPlayback) return;
        var next = tile.StreamType == 1 ? 2 : 1;
        tile.ManualStreamOverride = next;
        await tile.SwitchStreamAsync(next);
        Status = $"窗口 {tile.Number} 已切为{(next == 1 ? "主码流" : "子码流")}。";
    });
    [RelayCommand] private void SetTileAspectRatio(object? parameter)
    {
        if (parameter is string ratio && SelectedTile is not null)
        {
            SelectedTile.SetAspectRatio(ratio == "default" ? null : ratio);
            Status = $"已设置画面比例：{ratio}";
        }
    }
    public async Task SetModeAsync(bool playback)
    {
        if (IsPlayback == playback) return;
        await StopAllCoreAsync();
        IsPlayback = playback;
        Recordings.Clear();
        TimelineSegments = [];
        TimelineTracks = [];
        TimelineClipStart = null;
        TimelineClipEnd = null;
    }
    public (DateTimeOffset Start, DateTimeOffset End) ReadRange()
    {
        if (!TimeSpan.TryParse(StartTime, out var start) || start < TimeSpan.Zero || start >= TimeSpan.FromDays(1))
            start = TimeSpan.Zero;
        var date = RecordingDate.Date;
        var from = new DateTimeOffset(date.Add(start));
        var to = new DateTimeOffset(date.AddDays(1).AddSeconds(-1)); // 当天 23:59:59
        RangeStart = new DateTimeOffset(date); // 当天 00:00:00（时间轴起点）
        RangeEnd = new DateTimeOffset(date.AddDays(1)); // 当天 24:00:00（时间轴终点，整整24小时）
        return (from, to);
    }
    public async Task OpenChannelCoreAsync(Channel channel, VideoTileViewModel tile)
    {
        if (!_channels.ContainsKey(channel.Id) || (IsPlayback ? !CanPlayback : !CanLive))
        {
            tile.StateLabel = "无权播放此通道";
            throw new InvalidOperationException("当前账号无权播放此通道。");
        }
        if (!channel.Online)
        {
            tile.StateLabel = "通道离线";
            throw new InvalidOperationException("通道离线，暂时无法播放。");
        }
        // 尽力而为地探测该通道的子码流能力（B1）：缓存就绪后，后续决策可省掉一次失败往返。
        if (!IsPlayback) tile.RequestCapabilityProbe(_api);
        var range = IsPlayback ? ReadRange() : (DateTimeOffset.Now, DateTimeOffset.Now);
        // 首开即按显示档位选码流，避免先建主码流再切换造成的浪费与画面重建。
        // 回放同样按档位选录像码流：小窗口取子码流录像，放大/全屏取主码流录像。
        await tile.StartAsync(channel, IsPlayback, tile.DesiredStreamType, range.Item1, range.Item2);
    }

    /// <summary>
    /// 批量加载闸门。实测每格播放器会一次性占用约 1160 个句柄与约 30 个线程，
    /// 16 格同时建流会让句柄瞬间冲到 1.5 万，机器明显卡顿（用户实测“卡住、只加载几个摄像头”）。
    /// 因此限制同时在建的格数，并保证相邻两格至少间隔 <see cref="LoadStartInterval"/> 再开始。
    /// </summary>
    private SemaphoreSlim LoadGate { get; } = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));
    private static readonly TimeSpan LoadStartInterval = TimeSpan.FromMilliseconds(250);
    private long _lastLoadStartTicks;

    /// <summary>把「进入闸门 + 错峰间隔」一次做完，然后才真正开始建流。</summary>
    private async Task<VideoTileViewModel> AcquireLoadSlotAsync(Channel channel, VideoTileViewModel tile, CancellationToken token = default)
    {
        await LoadGate.WaitAsync(token);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastLoadStartTicks);
        var wait = LoadStartInterval.TotalMilliseconds - (now - last);
        Interlocked.Exchange(ref _lastLoadStartTicks, now);
        if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), token);
        tile.Channel = channel;
        tile.StateLabel = "连接中";
        return tile;
    }

    public Task OpenChannelAsync(Channel channel, VideoTileViewModel? tile = null) => RunAsync(async () =>
    {
        tile ??= SelectedTile ?? Tiles[0];
        SelectedTile = tile;
        await OpenChannelCoreAsync(channel, tile);
        Status = $"已打开通道：{channel.Name}。";
    });

    public Task OpenChannelsBatchAsync(IEnumerable<Channel> channels) => RunAsync(async () =>
    {
        var channelList = channels.Take(LayoutCount).ToArray();
        if (channelList.Length == 0) return;
        var limit = Math.Min(channelList.Length, LayoutCount);
        Status = $"正在加载 {limit} 路视频（错峰建流，避免卡顿）...";
        var generation = _generation;
        var tasks = new List<Task>();
        for (var i = 0; i < limit; ++i)
        {
            var tile = Tiles[i];
            var channel = channelList[i];
            tasks.Add(OpenChannelGatedAsync(channel, tile, generation));
        }
        await Task.WhenAll(tasks);
        if (generation == _generation) Status = $"已成功加载 {limit} 路视频。";
    });

    /// <summary>受闸门与错峰间隔约束的建流：真正开始前才置“连接中”，避免整屏同时显示连接状态。</summary>
    private async Task OpenChannelGatedAsync(Channel channel, VideoTileViewModel tile, long generation)
    {
        try
        {
            await AcquireLoadSlotAsync(channel, tile);
            if (generation != _generation) { LoadGate.Release(); return; }
            try { await OpenChannelCoreAsync(channel, tile); }
            catch (Exception ex) { ReportTileFailure(tile, channel, ex); }
            finally { LoadGate.Release(); }
        }
        catch (OperationCanceledException) { }
    }

    private static void ReportTileFailure(VideoTileViewModel tile, Channel channel, Exception ex)
    {
        var isTimeout = ex is TaskCanceledException || ex is TimeoutException || ex.Message.Contains("HttpClient.Timeout") || ex.Message.Contains("canceled") || ex.Message.Contains("timed out");
        tile.StateLabel = ex.Message.Contains("无权")
            ? "无权播放此通道"
            : (ex.Message.Contains("离线")
                ? "通道离线"
                : (isTimeout ? "连接超时，请重试" : $"连接失败：{ex.Message}"));
        ClientFiles.Log($"打开通道 {channel.Name} 失败：{ex.Message}");
    }

    [RelayCommand] private Task StartSelectedAsync() => RunAsync(async () =>
    {
        var channels = CheckedChannels().ToArray();
        if (channels.Length == 0 && SelectedResource?.Channel is { } selected) channels = [selected];
        if (channels.Length == 0) throw new InvalidOperationException("请选择通道。");
        if (channels.Length > LayoutCount) throw new InvalidOperationException("已选通道超过当前分屏数量，请调整分屏或减少选择。");
        var generation = _generation;
        var limit = Math.Min(channels.Length, LayoutCount);
        for (var i = 0; i < limit; ++i)
        {
            Tiles[i].Channel = channels[i];
            Tiles[i].StateLabel = "连接中";
        }
        var tasks = new List<Task>();
        for (var i = 0; i < limit && generation == _generation; ++i)
        {
            tasks.Add(OpenChannelGatedAsync(channels[i], Tiles[i], generation));
        }
        if (tasks.Count > 0)
        {
            Status = $"正在加载 {tasks.Count} 路视频（错峰建流，避免卡顿）...";
            await Task.WhenAll(tasks);
            if (generation == _generation) Status = $"已加载 {tasks.Count} 路视频。";
        }
    });
    [RelayCommand] private Task StopSelectedAsync() => RunAsync(async () => { await StopPtzAsync(); if (SelectedTile is not null) await SelectedTile.StopAsync(); });
    [RelayCommand] private Task StopAllAsync() => RunAsync(StopAllCoreAsync, "全部媒体已停止。");
    public async Task StopAllCoreAsync()
    {
        ++_generation; StopPatrol();
        foreach (var tile in Tiles) tile.Invalidate();
        await StopPtzAsync();
        await Task.WhenAll(Tiles.Select(t => t.StopAsync()));
        try { await _api.SendAsync(HttpMethod.Delete, IsPlayback ? "playback-sessions?all=true" : "live-sessions?all=true"); }
        catch { }
    }
    public async Task PurgeOrphanSessionsAsync()
    {
        var orphans = ClientFiles.GetAndClearActiveSessions();
        foreach (var (id, path) in orphans)
        {
            try { await _api.SendAsync(HttpMethod.Delete, $"{path}/{id}"); }
            catch (Exception ex) { ClientFiles.Log($"清理历史孤儿会话 {id} 失败：{ex.Message}"); }
        }
        try
        {
            await _api.SendAsync(HttpMethod.Delete, "live-sessions?all=true");
            await _api.SendAsync(HttpMethod.Delete, "playback-sessions?all=true");
        }
        catch { }
    }
    public async ValueTask DisposeAsync() => await StopAllCoreAsync();
    [RelayCommand] private Task ApplyStreamAsync() => RunAsync(async () =>
    {
        if (IsPlayback) return;
        var tile = SelectedTile;
        if (tile?.Channel is not null)
        {
            var targetStream = int.TryParse(StreamType, out var s) ? s : 2;
            if (tile.StreamType == targetStream && tile.IsPlaying) return;
            await tile.SwitchStreamAsync(targetStream);
            Status = $"窗口 {tile.Number} 已切换为{(targetStream == 1 ? "主码流" : "子码流")}。";
        }
    });
    [RelayCommand] private Task SearchRecordingsAsync() => RunAsync(async () =>
    {
        if (!CanPlayback) throw new InvalidOperationException("当前账号没有回放权限。");
        var primaryChannel = SelectedResource?.Channel ?? SelectedTile?.Channel;
        var checkedChannels = CheckedChannels().ToArray();
        var candidateChannels = checkedChannels.Length > 0
            ? checkedChannels
            : (primaryChannel is not null ? [primaryChannel] : Tiles.Where(t => t.Channel is not null).Select(t => t.Channel!).Distinct().ToArray());

        if (candidateChannels.Length == 0) throw new InvalidOperationException("请选择录像通道。");
        var range = ReadRange();

        var tracks = new List<TimelineTrack>();
        Recording[]? primaryRecordings = null;

        foreach (var ch in candidateChannels.Take(4))
        {
            try
            {
                var recs = await _api.PostAsync<Recording[]>("recordings/search", new RecordingRequest(ch.Id, range.Start, range.End));
                var segs = recs.Select(r => new RecordingSegment(r.Start, r.End)).ToArray();
                var isActive = (primaryChannel is not null && ch.Id == primaryChannel.Id) || (SelectedTile?.Channel?.Id == ch.Id);
                tracks.Add(new TimelineTrack(ch.DisplayName, segs, isActive));
                if (primaryRecordings is null || ch.Id == primaryChannel?.Id)
                {
                    primaryRecordings = recs;
                }
            }
            catch (Exception ex)
            {
                ClientFiles.Log($"查询通道 {ch.DisplayName} 录像失败：{ex.Message}");
            }
        }

        Recordings.Clear();
        if (primaryRecordings is not null)
        {
            foreach (var recording in primaryRecordings) Recordings.Add(recording);
            TimelineSegments = primaryRecordings.Select(r => new RecordingSegment(r.Start, r.End)).ToArray();
        }
        else
        {
            TimelineSegments = [];
        }

        TimelineTracks = [.. tracks];
        Status = $"已找到 {Recordings.Count} 段录像（共检索 {tracks.Count} 个通道）。";
    });
    [RelayCommand] private Task PlayRecordingAsync() => RunAsync(async () =>
    {
        if (SelectedRecording is null || !CanPlayback) return;
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        var target = SelectedTile ?? Tiles[0];
        await target.StartAsync(channel, true, target.DesiredStreamType, SelectedRecording.Start, SelectedRecording.End);
        Playhead = SelectedRecording.Start;
    });
    [RelayCommand] private Task PlaybackActionAsync(string? action) => RunAsync(() => ControlAsync(new PlaybackControl(action ?? "pause")));
    [RelayCommand] private Task ApplySpeedAsync() => RunAsync(() => ControlAsync(new PlaybackControl("speed", Speed: PlaybackSpeed)));
    public Task SeekAsync(DateTimeOffset position) => RunAsync(() => ControlAsync(new PlaybackControl("seek", position)));

    [RelayCommand]
    private Task StepBackwardAsync() => RunAsync(async () =>
    {
        var current = Playhead ?? RangeStart;
        var target = current.AddSeconds(-10);
        if (target < RangeStart) target = RangeStart;
        await SeekAsync(target);
    });

    [RelayCommand]
    private Task StepForwardAsync() => RunAsync(async () =>
    {
        var current = Playhead ?? RangeStart;
        var target = current.AddSeconds(10);
        if (target > RangeEnd) target = RangeEnd;
        await SeekAsync(target);
    });

    [RelayCommand]
    private Task FrameStepAsync() => RunAsync(async () =>
    {
        await ControlAsync(new PlaybackControl("pause"));
        var tiles = UnifiedControl ? Tiles.Where(t => t.IsPlayback && t.SessionId is not null).ToArray() : SelectedTile is { } selected ? [selected] : Array.Empty<VideoTileViewModel>();
        foreach (var tile in tiles) tile.StepFrame();
    });

    [RelayCommand]
    private void ApplyQuickPreset(string? preset)
    {
        switch (preset)
        {
            case "today":
                RecordingDate = DateTime.Today;
                StartTime = "00:00:00";
                EndTime = "23:59:59";
                break;
            case "yesterday":
                RecordingDate = DateTime.Today.AddDays(-1);
                StartTime = "00:00:00";
                EndTime = "23:59:59";
                break;
        }
        ReadRange();
        _ = SearchRecordingsAsync();
    }

    [RelayCommand]
    private Task ExportTimelineClipAsync() => RunAsync(async () =>
    {
        if (!CanExport) throw new InvalidOperationException("当前账号没有录像导出权限。");
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        var (start, end) = HasTimelineClip ? (TimelineClipStart!.Value, TimelineClipEnd!.Value) : ReadRange();
        await _api.PostAsync<ExportJob>("exports", new ExportRequest(channel.Id, start, end));
        Status = $"已提交 {channel.DisplayName} 录像导出任务（{start.LocalDateTime:MM-dd HH:mm:ss} ~ {end.LocalDateTime:HH:mm:ss}）。";
        if (ExportCreated is not null) await ExportCreated();
    });

    public void SetTimelineClip(DateTimeOffset start, DateTimeOffset end)
    {
        TimelineClipStart = start;
        TimelineClipEnd = end;
    }

    private async Task ControlAsync(PlaybackControl command)
    {
        if (!CanPlayback) throw new InvalidOperationException("当前账号没有回放权限。");
        var tiles = UnifiedControl ? Tiles.Where(t => t.IsPlayback && t.SessionId is not null).ToArray() : SelectedTile is { } selected ? [selected] : Array.Empty<VideoTileViewModel>();
        var errors = new List<string>();
        foreach (var tile in tiles)
        {
            try { await tile.ControlAsync(command); }
            catch (Exception ex) { errors.Add($"窗口 {tile.Number}：{ex.Message}"); }
        }
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("；", errors));
    }
    [RelayCommand] private Task ToggleFavoriteAsync() => RunAsync(async () =>
    {
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择通道。");
        var changed = _favoriteIds.ToHashSet();
        if (!changed.Add(channel.Id)) changed.Remove(channel.Id);
        await _api.SendAsync(HttpMethod.Put, "favorites", new FavoritesRequest(changed.ToArray()));
        _favoriteIds.Clear(); _favoriteIds.UnionWith(changed); Filter();
        Status = changed.Contains(channel.Id) ? "通道已加入收藏。" : "通道已移出收藏。";
    });
    [RelayCommand] private Task SaveLayoutAsync() => SaveLayoutCoreAsync(false);
    [RelayCommand] private Task UpdateLayoutAsync() => SaveLayoutCoreAsync(true);
    private Task SaveLayoutCoreAsync(bool update) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(LayoutName)) throw new ArgumentException("请输入布局名称。");
        if (IntervalSeconds is < 10 or > 300) throw new ArgumentException("轮巡停留时间必须在 10 至 300 秒之间。");
        if (ShareLayout && !CanShareLayouts) throw new InvalidOperationException("当前账号无权发布共享轮巡。");
        var slots = Tiles.Take(LayoutCount).Select(t => t.Channel?.Id).ToArray();
        var ids = SaveAsPatrol ? CheckedChannels().Select(c => (long?)c.Id).Distinct().ToArray() : slots;
        if (!ids.Any(id => id is > 0)) throw new InvalidOperationException("请先选择轮巡通道或打开视频窗口。");
        if (SaveAsPatrol && ids.Any(id => id is null)) throw new InvalidOperationException("轮巡方案不能包含空窗口。");
        var request = new LayoutRequest(LayoutName.Trim(), SaveAsPatrol ? "patrol" : "layout", ShareLayout, LayoutCount, IntervalSeconds, ids);
        if (update)
        {
            if (SelectedLayout is null) throw new InvalidOperationException("请选择要更新的方案。");
            await _api.SendAsync(HttpMethod.Put, $"layouts/{SelectedLayout.Id}", request);
        }
        else await _api.PostAsync<LayoutDto>("layouts", request);
        await RefreshAsync(); Status = "布局已保存并同步至平台。";
    });
    [RelayCommand] private Task DeleteLayoutAsync() => RunAsync(async () =>
    {
        if (SelectedLayout is null) return;
        if (!_dialogs.Confirm($"确定删除“{SelectedLayout.Name}”？")) return;
        await _api.SendAsync(HttpMethod.Delete, $"layouts/{SelectedLayout.Id}"); await RefreshAsync();
    });
    /// <summary>iVMS-4200 主预览“新建视图”：把当前分屏窗口安排保存为新的视图方案。</summary>
    [RelayCommand] private Task NewViewAsync() => RunAsync(async () =>
    {
        if (IsPlayback) throw new InvalidOperationException("请在主预览模式下新建视图。");
        LayoutName = $"视图 {Layouts.Count(l => l.Kind == "layout") + 1}";
        SaveAsPatrol = false; ShareLayout = false; SelectedLayout = null;
        await SaveLayoutCoreAsync(false);
        SelectedLayout = Layouts.FirstOrDefault(l => l.Kind == "layout" && l.Name == LayoutName);
        Status = $"视图“{LayoutName}”已保存。";
    });
    /// <summary>iVMS-4200 主预览“保存视图”：更新当前选中的视图，或按当前名称另存为新的视图。</summary>
    [RelayCommand] private Task SaveViewAsync() => RunAsync(async () =>
    {
        if (IsPlayback) throw new InvalidOperationException("请在主预览模式下保存视图。");
        if (SelectedLayout is { Kind: "layout" } current)
        {
            SaveAsPatrol = false;
            await SaveLayoutCoreAsync(true);
            Status = $"视图“{current.Name}”已更新。";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(LayoutName) || LayoutName == "我的值守布局") LayoutName = $"视图 {Layouts.Count(l => l.Kind == "layout") + 1}";
            SaveAsPatrol = false; ShareLayout = false;
            await SaveLayoutCoreAsync(false);
            SelectedLayout = Layouts.FirstOrDefault(l => l.Kind == "layout" && l.Name == LayoutName);
            Status = $"视图“{LayoutName}”已保存。";
        }
    });
    /// <summary>iVMS-4200 主预览工具栏轮巡开关：按选中/首个轮巡方案启动，正在轮巡时停止。</summary>
    [RelayCommand] private Task TogglePatrolAsync() => RunAsync(async () =>
    {
        if (IsPatrolling) { StopPatrol(); Status = "轮巡已停止。"; return; }
        if (IsPlayback) throw new InvalidOperationException("请在主预览模式下启动轮巡。");
        var patrol = SelectedLayout is { Kind: "patrol" } selected ? selected : Layouts.FirstOrDefault(l => l.Kind == "patrol");
        if (patrol is null) throw new InvalidOperationException("没有可用的轮巡方案：请在“方案设置”中勾选“设为巡视轮巡”并保存。");
        if (!CanLive) throw new InvalidOperationException("当前账号没有预览权限。");
        SelectedLayout = patrol;
        await ApplyLayoutCommand.ExecuteAsync(null);
    });
    [RelayCommand] private Task ApplyLayoutAsync() => RunAsync(async () =>
    {
        var layout = SelectedLayout ?? throw new InvalidOperationException("请选择布局或轮巡方案。");
        if (!LayoutOptions.Contains(layout.Layout)) throw new InvalidOperationException("此布局的分屏数量无效。");
        if (layout.Layout == 25 && !CanSplit25) throw new InvalidOperationException("当前账号未被分配 25 路分屏权限。");
        await StopAllCoreAsync();
        var preset = LayoutPreset.Find(AllPresets, layout.Layout.ToString());
        if (preset is not null)
        {
            await ApplyLayoutCountAsync(preset.Count, preset.Columns, preset.Rows, preset);
        }
        else
        {
            var cols = (int)Math.Max(1, Math.Round(Math.Sqrt(layout.Layout)));
            var rows = (int)Math.Ceiling((double)layout.Layout / cols);
            await ApplyLayoutCountAsync(layout.Layout, cols, rows);
        }
        _ = ApplyLayoutTiersAsync();
        if (layout.Kind == "patrol")
        {
            if (!CanLive) throw new InvalidOperationException("当前账号没有预览权限。");
            IsPlayback = false; _patrol = layout; _patrolOffset = 0; IsPatrolling = true;
            await AdvancePatrolAsync(DateTimeOffset.UtcNow);
        }
        else
        {
            var limit = Math.Min(layout.ChannelIds.Length, LayoutCount);
            var generation = _generation;
            var tasks = new List<Task>();
            for (var i = 0; i < limit; ++i)
            {
                var channelId = layout.ChannelIds[i];
                var tile = Tiles[i];
                if (channelId is { } id && _channels.TryGetValue(id, out var channel) && channel.Online)
                {
                    tasks.Add(OpenChannelGatedAsync(channel, tile, generation));
                }
                else
                {
                    tile.StateLabel = channelId is null ? "空闲" : "通道离线或无权访问";
                }
            }
            for (var i = limit; i < LayoutCount; ++i)
            {
                Tiles[i].StateLabel = "空闲";
            }
            if (tasks.Count > 0)
            {
                Status = $"正在同时加载 {tasks.Count} 路视频...";
                await Task.WhenAll(tasks);
                if (generation == _generation) Status = $"已加载 {tasks.Count} 路分屏视频。";
            }
        }
    });
    [RelayCommand] private void StopPatrol() { IsPatrolling = false; _patrol = null; }
    private async Task AdvancePatrolAsync(DateTimeOffset now)
    {
        if (!await _patrolGate.WaitAsync(0)) return;
        var generation = _generation;
        try
        {
            var patrol = _patrol;
            if (patrol is null || patrol.ChannelIds.Length == 0) { StopPatrol(); return; }
            var tasks = new List<Task>();
            for (var i = 0; i < LayoutCount && IsPatrolling && generation == _generation; ++i)
            {
                var id = patrol.ChannelIds[(_patrolOffset + i) % patrol.ChannelIds.Length];
                var tile = Tiles[i];
                if (id is { } channelId && _channels.TryGetValue(channelId, out var channel) && channel.Online)
                {
                    tasks.Add(OpenChannelGatedAsync(channel, tile, generation));
                }
                else
                {
                    tasks.Add(Task.Run(async () => { await tile.StopAsync(); tile.StateLabel = "通道离线或无权访问"; }));
                }
            }
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
            _patrolOffset = (_patrolOffset + LayoutCount) % patrol.ChannelIds.Length;
            _nextPatrol = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(patrol.IntervalSeconds, 10, 300));
        }
        finally { _patrolGate.Release(); }
    }
    [RelayCommand] private Task CreateExportAsync() => RunAsync(async () =>
    {
        if (!CanExport) throw new InvalidOperationException("当前账号没有录像导出权限。");
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        var range = ReadRange();
        await _api.PostAsync<ExportJob>("exports", new ExportRequest(channel.Id, range.Start, range.End));
        Status = "录像导出任务已提交。";
        if (ExportCreated is not null) await ExportCreated();
    });
    /// <summary>把选中的录像段直接提交导出（iVMS-4200 回放检索面板的“导出”语义）。</summary>
    [RelayCommand] private Task ExportRecordingAsync() => RunAsync(async () =>
    {
        if (!CanExport) throw new InvalidOperationException("当前账号没有录像导出权限。");
        var recording = SelectedRecording ?? throw new InvalidOperationException("请先选择录像段。");
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        await _api.PostAsync<ExportJob>("exports", new ExportRequest(channel.Id, recording.Start, recording.End));
        Status = $"录像段导出已提交：{recording.Label}";
        if (ExportCreated is not null) await ExportCreated();
    });
    public Task StartPtzAsync(string command) => RunAsync(async () =>
    {
        if (!CanPtz || SelectedTile?.Channel is not { PtzCapable: true } channel || IsPlayback) throw new InvalidOperationException("请选择有云台控制权限的实时通道。");
        await _ptz.StartAsync(channel.Id, command, PtzSpeed);
    });
    public Task StopPtzAsync() => _ptz.StopAsync();
    [RelayCommand] private Task StopPtzCommandAsync() => StopPtzAsync();
    [RelayCommand] private Task CallPresetAsync() => RunAsync(async () =>
    {
        if (!CanPtz || SelectedTile?.Channel is not { PtzCapable: true } channel) throw new InvalidOperationException("请选择支持云台的通道。");
        await _ptz.PresetAsync(channel.Id, Preset);
    });
    public async Task TickAsync()
    {
        var now = DateTimeOffset.UtcNow;
        await Task.WhenAll(Tiles.Where(t => t.SessionId is not null).Select(t => t.TickAsync(now)));
        if (IsPatrolling && now >= _nextPatrol) await AdvancePatrolAsync(now);
    }
}



