using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoPlatform.Desktop;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private readonly ObservableCollection<ChannelNode> _channelNodes = [];
    private readonly Dictionary<int, LiveSession> _live = [];
    private readonly Dictionary<int, PlaybackSession> _playback = [];
    private readonly Dictionary<MediaPlayer, string> _playerUrls = [];
    private readonly Dictionary<MediaPlayer, int> _playerRetries = [];
    private readonly Dictionary<MediaPlayer, CancellationTokenSource> _retryTokens = [];
    private readonly List<MediaPlayer> _players = [];
    private readonly List<LibVLCSharp.WPF.VideoView> _videoViews = [];
    private readonly List<Border> _videoTiles = [];
    private readonly List<TextBlock> _videoTitles = [];
    private bool _playbackMode;
    private bool _loggingIn;
    private WindowState _beforeFullscreen;
    private readonly DispatcherTimer _renewTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _authTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _ptzTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _ptzCommand;
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private HubConnection? _alarmConnection;
    private LibVLC? _libVlc;
    private string? _token;
    private int _layout = 4;
    private int _streamType = 2;
    private int? _focusedSlot;
    private bool _closing;
    private bool _closeReady;
    private bool _renewing;
    private bool _polling;
    private bool _startingMedia;
    private TaskCompletionSource? _mediaIdle;
    private int _mediaGeneration;
    private int _activeSlot;
    private readonly Dictionary<int, int> _slotChannels = [];
    private Task<bool>? _refreshTask;
    private DateTimeOffset _tokenExpiresAt;
    private Task? _ptzStarting;
    private Task? _ptzStopping;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ObservableCollection<string> _alarmItems = [];
    private readonly System.Windows.Forms.NotifyIcon _notification = new() { Icon = System.Drawing.SystemIcons.Information, Text = "京华安防平台", Visible = true };
    private int _unreadAlarms;
    private bool _checkingUpdate;
    private bool _forceUpdate;
    private string? _requiredVersion;
    private HashSet<string> _permissions = [];
    private int? _ptzChannel;

    public MainWindow()
    {
        InitializeComponent();
        MediaPanel.Visibility = Visibility.Collapsed;
        VersionText.Text = $"京华安防平台  {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · Windows x64";
        Core.Initialize();
        ChannelTree.ItemsSource = _channelNodes;
        AlarmList.ItemsSource = _alarmItems;
        _renewTimer.Tick += async (_, _) => await RenewSessionsAsync();
        _playbackTimer.Tick += async (_, _) => await PollPlaybackAsync();
        _ptzTimer.Tick += async (_, _) =>
        {
            if (_ptzChannel is not { } channel || _ptzStarting is { IsCompleted: false } || _ptzStopping is not null) return;
            _ptzStarting = PostCommandAsync($"/api/channels/{channel}/ptz/start", new { command = _ptzCommand, speed = (int)PtzSpeedSlider.Value });
            try { await _ptzStarting; } catch { await StopPtzAsync(channel); }
        };
        _authTimer.Tick += async (_, _) =>
        {
            try { await RefreshTokenAsync(); }
            catch (Exception ex) { SetStatus($"登录续期失败：{ex.Message}"); }
        };
        StartText.Text = DateTime.Now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss");
        EndText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        SetLayout(4);
        SetStatus("请输入平台账号登录");
    }

    private string ApiBase => ApiBaseText.Text.Trim().TrimEnd('/');

    private void SetModule(string module)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = module is "live" or "playback" ? Visibility.Visible : Visibility.Collapsed;
        AlarmPanel.Visibility = module == "alarms" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = module == "settings" ? Visibility.Visible : Visibility.Collapsed;
        LiveTab.IsChecked = module == "live";
        PlaybackTab.IsChecked = module == "playback";
        AlarmsTab.IsChecked = module == "alarms";
        SettingsTab.IsChecked = module == "settings";
        if (module is not ("live" or "playback")) return;
        _playbackMode = module == "playback";
        RecordingSearchPanel.Visibility = PlaybackPanel.Visibility = _playbackMode ? Visibility.Visible : Visibility.Collapsed;
        PtzPanel.Visibility = StreamPanel.Visibility = _playbackMode ? Visibility.Collapsed : Visibility.Visible;
        ViewTitleText.Text = _playbackMode ? "远程回放" : "主预览";
    }

    private async void ModuleClick(object sender, RoutedEventArgs e)
    {
        if (_loggingIn) return;
        var module = Convert.ToString((sender as FrameworkElement)?.Tag) ?? "live";
        if (module is "live" or "playback" && _playbackMode != (module == "playback"))
        {
            await StopSessionsAsync();
            if (_mediaIdle is not null) await _mediaIdle.Task;
        }
        if (_ptzChannel is { } channel) await StopPtzAsync(channel);
        SetModule(module);
    }

    private void DismissLoginClick(object sender, RoutedEventArgs e) => SetModule(_playbackMode ? "playback" : "live");
    private void ShowAlarmsClick(object sender, RoutedEventArgs e) => SetModule("alarms");
    private void ShowSettingsClick(object sender, RoutedEventArgs e) => SetModule("settings");
    private void ShowLoginClick(object sender, RoutedEventArgs e)
    {
        if (_token is not null) { SetStatus("当前账号已登录，可从右上角退出登录"); return; }
        MediaPanel.Visibility = AlarmPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        LoginErrorText.Text = string.Empty;
        PasswordText.Focus();
    }

    private void AccountClick(object sender, RoutedEventArgs e)
    {
        if (_token is null) ShowLoginClick(sender, e);
        else LogoutClick(sender, e);
    }

    private void SaveSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!ValidApiBase()) return;
        SaveConfig();
        SetStatus("平台设置已保存");
    }

    private bool ValidApiBase()
    {
        if (Uri.TryCreate(ApiBase, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)) return true;
        LoginErrorText.Text = "请输入有效的 HTTP 或 HTTPS 平台地址";
        SetStatus(LoginErrorText.Text);
        return false;
    }

    private void OpenCapturesClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus($"打开截图目录失败：{ex.Message}"); }
    }

    private async void RefreshChannelsClick(object sender, RoutedEventArgs e)
    {
        if (_token is null) { ShowLoginClick(sender, e); return; }
        try { await LoadChannelsAsync(); SetStatus("资源已同步"); }
        catch (Exception ex) { SetStatus($"资源同步失败：{ex.Message}"); }
    }

    private void ChannelTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ChannelNode { IsChannel: true } node) PlaybackChannelText.Text = node.ChannelNumber.ToString();
    }

    private async void ChannelTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChannelTree.SelectedItem is not ChannelNode { IsChannel: true } node || _startingMedia || _token is null || _forceUpdate) return;
        if (_playbackMode) { SearchRecordingsClick(sender, e); return; }
        if (!_permissions.Contains("live.view")) { SetStatus("当前账号没有预览权限"); return; }
        _startingMedia = true;
        _mediaIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var slot = _activeSlot;
        var generation = _mediaGeneration;
        try
        {
            await StopChannelAsync(node.ChannelNumber);
            await StopSlotAsync(slot);
            var session = await StartLiveAsync(node.ChannelNumber);
            if (_closing || generation != _mediaGeneration) { await DeleteAsync($"/api/live-sessions/{session.Id}"); return; }
            _live[node.ChannelNumber] = session;
            _slotChannels[slot] = node.ChannelNumber;
            PlaySlot(slot, session.RtspUrl);
            _renewTimer.Start();
        }
        catch (Exception ex) { SetStatus($"通道预览失败：{ex.Message}"); }
        finally { _startingMedia = false; _mediaIdle?.TrySetResult(); }
    }

    private async void StartMediaClick(object sender, RoutedEventArgs e)
    {
        if (_token is null) { ShowLoginClick(sender, e); return; }
        await StartSelectedAsync(_playbackMode);
    }

    private async void PtzStopButtonClick(object sender, RoutedEventArgs e)
    {
        if (_ptzChannel is { } channel) await StopPtzAsync(channel);
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && WindowStyle == WindowStyle.None) { FullscreenClick(sender, e); e.Handled = true; }
    }
    private IEnumerable<int> SelectedChannels => _channelNodes.SelectMany(Flatten).Where(item => item.IsChannel && item.IsSelected).Select(item => item.ChannelNumber).Distinct();

    private static IEnumerable<ChannelNode> Flatten(ChannelNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten)) yield return child;
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(ConfigPath), _json);
                if (!string.IsNullOrWhiteSpace(config?.ApiBase)) ApiBaseText.Text = config.ApiBase;
                _requiredVersion = config?.RequiredVersion;
                _forceUpdate = Version.TryParse(_requiredVersion, out var required) && typeof(MainWindow).Assembly.GetName().Version! < required;
                LoginPanel.IsEnabled = !_forceUpdate;
            }
        }
        catch { SetStatus("读取本地设置失败，将使用当前平台地址"); }
        await CheckUpdateAsync(false);
    }

    private string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform", "desktop.json");

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new ClientConfig(ApiBase, _requiredVersion)));
        }
        catch { SetStatus("平台地址保存失败"); }
    }

    private async void LoginClick(object sender, RoutedEventArgs e)
    {
        if (_forceUpdate || _loggingIn || _token is not null || !ValidApiBase()) return;
        if (string.IsNullOrWhiteSpace(UsernameText.Text) || string.IsNullOrEmpty(PasswordText.Password)) { LoginErrorText.Text = "请输入用户名和密码"; return; }
        _loggingIn = true;
        LoginErrorText.Text = string.Empty;
        LoginButton.Content = "正在登录…";
        LoginPanel.IsEnabled = false;
        try
        {
            SaveConfig();
            using var response = await _http.PostAsJsonAsync($"{ApiBase}/api/auth/login", new { username = UsernameText.Text.Trim(), password = PasswordText.Password });
            response.EnsureSuccessStatusCode();
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(_json) ?? throw new InvalidOperationException("登录响应为空");
            _token = login.AccessToken;
            _tokenExpiresAt = login.ExpiresAt;
            var profile = await GetAsync<User>("/api/auth/me");
            _permissions = (profile?.Permissions ?? []).ToHashSet(StringComparer.Ordinal);
            _authTimer.Start();
            ApiBaseText.IsReadOnly = true;
            WorkspacePanel.IsEnabled = true;
            UserText.Text = login.User.Username;
            AccountButton.ToolTip = "退出登录";
            ConnectionText.Text = "平台已连接";
            ConnectionText.Foreground = Brushes.MediumSeaGreen;
            PasswordText.Clear();
            SetModule("live");
            await LoadChannelsAsync();
            await ConnectAlarmAsync();
            await CheckUpdateAsync(false);
            SetStatus("登录成功，已同步通道");
        }
        catch (Exception ex)
        {
            _token = null;
            _authTimer.Stop();
            ApiBaseText.IsReadOnly = false;
            WorkspacePanel.IsEnabled = false;
            LoginPanel.Visibility = Visibility.Visible;
            MediaPanel.Visibility = Visibility.Collapsed;
            LoginErrorText.Text = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } ? "账号或密码不正确" : $"连接平台失败：{ex.Message}";
            SetStatus(LoginErrorText.Text);
        }
        finally { _loggingIn = false; LoginButton.Content = "登录"; LoginPanel.IsEnabled = !_forceUpdate; }
    }

    private async Task LoadChannelsAsync()
    {
        var selected = SelectedChannels.ToHashSet();
        var channels = _permissions.Contains("channel.read") ? await GetAsync<List<Channel>>("/api/channels") ?? [] : [];
        var workshops = _permissions.Contains("area.read") ? await GetRowsAsync("/api/workshops", 4) : [];
        var areas = _permissions.Contains("area.read") ? await GetRowsAsync("/api/areas", 5) : [];
        var units = _permissions.Contains("area.read") ? await GetRowsAsync("/api/units", 5) : [];
        var assigned = new HashSet<int>();
        _channelNodes.Clear();
        foreach (var workshop in workshops)
        {
            var workshopNode = new ChannelNode($"车间／{workshop[1]}", false, 0);
            foreach (var area in areas.Where(row => Number(row[1]) == Number(workshop[0])))
            {
                var areaNode = new ChannelNode($"区域／{area[2]}", false, 0);
                foreach (var unit in units.Where(row => Number(row[1]) == Number(area[0])))
                {
                    var unitNode = new ChannelNode($"机组／{unit[2]}", false, 0);
                    foreach (var channel in channels.Where(item => item.UnitId == Number(unit[0])))
                    {
                        assigned.Add(channel.ChannelNumber);
                        unitNode.Children.Add(new ChannelNode(ChannelLabel(channel), true, channel.ChannelNumber));
                    }
                    areaNode.Children.Add(unitNode);
                }
                workshopNode.Children.Add(areaNode);
            }
            _channelNodes.Add(workshopNode);
        }
        var unassigned = channels.Where(channel => !assigned.Contains(channel.ChannelNumber)).ToList();
        if (unassigned.Count > 0)
        {
            var node = new ChannelNode("未分配通道", false, 0);
            foreach (var channel in unassigned) node.Children.Add(new ChannelNode(ChannelLabel(channel), true, channel.ChannelNumber));
            _channelNodes.Add(node);
        }
        FilterTree();
        foreach (var node in _channelNodes.SelectMany(Flatten)) node.IsSelected = selected.Contains(node.ChannelNumber);
        ChannelCountText.Text = $"{channels.Count} 路通道 · {channels.Count(item => item.Online)} 路在线";
    }

    private static string ChannelLabel(Channel channel) => $"CH {channel.ChannelNumber:00} · {channel.Name ?? "未命名"} · {(channel.Online ? "在线" : "离线")}";
    private static int Number(object? value) => int.TryParse(Convert.ToString(value), out var result) ? result : 0;

    private async Task<List<object?[]>> GetRowsAsync(string path, int columns)
    {
        var rows = await GetAsync<List<JsonElement>>(path) ?? [];
        return rows.Select(row => Enumerable.Range(0, columns).Select(index => index < row.GetArrayLength() ? JsonToValue(row[index]) : null).ToArray()).ToList();
    }

    private static object? JsonToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private void ChannelSearchChanged(object sender, TextChangedEventArgs e) => FilterTree();
    private void FilterTree()
    {
        var needle = ChannelSearchText.Text.Trim();
        foreach (var root in _channelNodes) SetVisibility(root, needle);
    }

    private static bool SetVisibility(ChannelNode node, string needle)
    {
        var own = string.IsNullOrWhiteSpace(needle) || node.Label.Contains(needle, StringComparison.OrdinalIgnoreCase);
        var child = false;
        foreach (var item in node.Children) child |= SetVisibility(item, own ? string.Empty : needle);
        node.IsVisible = own || child;
        return node.IsVisible;
    }

    private void ChannelChecked(object sender, RoutedEventArgs e)
    {
        var selected = SelectedChannels.ToHashSet();
        if (selected.Count > _layout)
        {
            if (sender is CheckBox box) box.IsChecked = false;
            SetStatus($"当前分屏最多选择 {_layout} 路通道");
        }
    }

    private async void LiveClick(object sender, RoutedEventArgs e) => await StartSelectedAsync(false);
    private async void PlaybackClick(object sender, RoutedEventArgs e) => await StartSelectedAsync(true);

    private async Task StartSelectedAsync(bool playback)
    {
        if (_startingMedia || _closing || _token is null || _forceUpdate) return;
        if (!_permissions.Contains(playback ? "playback.view" : "live.view")) { SetStatus("当前账号没有对应媒体权限"); return; }
        var selected = SelectedChannels.Take(_layout).ToList();
        if (selected.Count == 0) { SetStatus("请先选择通道"); return; }
        _startingMedia = true;
        _mediaIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await StopSessionsAsync();
        var generation = _mediaGeneration;
        try
        {
            if (playback)
            {
                if (!DateTimeOffset.TryParse(StartText.Text, out var start) || !DateTimeOffset.TryParse(EndText.Text, out var end) || end <= start || end - start > TimeSpan.FromDays(1))
                    throw new InvalidOperationException("回放时间范围无效，不能超过 1 天");
                for (var index = 0; index < selected.Count; index++)
                {
                    var session = await StartPlaybackAsync(selected[index], start, end);
                    if (_closing || generation != _mediaGeneration) { await DeleteAsync($"/api/playback-sessions/{session.Id}"); break; }
                    _playback[selected[index]] = session;
                    _slotChannels[index] = selected[index];
                    PlaySlot(index, session.RtspUrl);
                }
                _playbackTimer.Start();
            }
            else
            {
                for (var index = 0; index < selected.Count; index++)
                {
                    var session = await StartLiveAsync(selected[index]);
                    if (_closing || generation != _mediaGeneration) { await DeleteAsync($"/api/live-sessions/{session.Id}"); break; }
                    _live[selected[index]] = session;
                    _slotChannels[index] = selected[index];
                    PlaySlot(index, session.RtspUrl);
                }
            }
            if (_closing || generation != _mediaGeneration) return;
            _renewTimer.Start();
            SetStatus(playback ? "回放会话已建立" : "实时预览已建立");
        }
        catch (Exception ex) { SetStatus($"建立媒体会话失败：{ex.Message}"); }
        finally
        {
            _startingMedia = false;
            _mediaIdle?.TrySetResult();
            if (!_closing && (_live.Count > 0 || _playback.Count > 0)) _renewTimer.Start();
            if (!_closing && _playback.Count > 0) _playbackTimer.Start();
        }
    }

    private async Task<LiveSession> StartLiveAsync(int channel)
    {
        return await PostAsync<LiveSession>("/api/live-sessions", new { channel, streamType = _streamType }) ?? throw new InvalidOperationException("实时会话响应为空");
    }

    private async Task<PlaybackSession> StartPlaybackAsync(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        return await PostAsync<PlaybackSession>("/api/playback-sessions", new { channel, start, end }) ?? throw new InvalidOperationException("回放会话响应为空");
    }

    private async void StreamTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement button && int.TryParse(Convert.ToString(button.Tag), out var type) && type is 1 or 2) _streamType = type;
        if (_live.Count > 0) await StartSelectedAsync(false);
        SetStatus(_streamType == 2 ? "已选择子码流，失败时服务端自动回退主码流" : "已选择主码流");
    }

    private void SetLayout(int layout)
    {
        if (layout is not (1 or 4 or 9 or 16)) throw new ArgumentOutOfRangeException(nameof(layout));
        _layout = layout;
        StopPlayers();
        VideoGrid.Children.Clear();
        foreach (var old in _videoViews) old.Dispose();
        _videoViews.Clear();
        _videoTiles.Clear();
        _videoTitles.Clear();
        _focusedSlot = null;
        VideoGrid.Rows = (int)Math.Ceiling(Math.Sqrt(layout));
        VideoGrid.Columns = VideoGrid.Rows;
        for (var index = 0; index < layout; index++)
        {
            var view = new LibVLCSharp.WPF.VideoView { Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Visibility = Visibility.Collapsed };
            view.MouseDoubleClick += VideoViewDoubleClick;
            var slot = index;
            var title = new TextBlock { Text = $"窗口 {slot + 1:00}", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 11, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var surface = new Grid();
            surface.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            surface.RowDefinitions.Add(new RowDefinition());
            surface.Children.Add(title);
            var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            empty.Children.Add(new TextBlock { Text = "\uE714", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 32, Foreground = new SolidColorBrush(Color.FromRgb(111, 115, 122)), HorizontalAlignment = HorizontalAlignment.Center });
            empty.Children.Add(new TextBlock { Text = $"{slot + 1:00}", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetRow(empty, 1);
            surface.Children.Add(empty);
            Grid.SetRow(view, 1);
            surface.Children.Add(view);
            var tile = new Border { Child = surface, Background = new SolidColorBrush(Color.FromRgb(65, 68, 74)), BorderThickness = new Thickness(1), Margin = new Thickness(1) };
            tile.MouseLeftButtonDown += (_, e) => { SelectSlot(slot); if (e.ClickCount == 2) VideoViewDoubleClick(view, e); };
            view.MouseLeftButtonDown += (_, _) => { SelectSlot(slot); view.Focus(); };
            _videoViews.Add(view);
            _videoTiles.Add(tile);
            _videoTitles.Add(title);
            VideoGrid.Children.Add(tile);
        }
        _activeSlot = Math.Min(_activeSlot, layout - 1);
        SelectSlot(_activeSlot);
        foreach (var button in new[] { Layout1Button, Layout4Button, Layout9Button, Layout16Button }) button.IsChecked = Convert.ToString(button.Tag) == layout.ToString();
        foreach (var pair in _slotChannels.Where(item => item.Key < layout))
        {
            if (_live.TryGetValue(pair.Value, out var live)) PlaySlot(pair.Key, live.RtspUrl);
            if (_playback.TryGetValue(pair.Value, out var playback)) PlaySlot(pair.Key, playback.RtspUrl);
        }
    }

    private async void LayoutClick(object sender, RoutedEventArgs e)
    {
        if (_startingMedia) return;
        if (sender is FrameworkElement button && int.TryParse(Convert.ToString(button.Tag), out var layout) && layout is 1 or 4 or 9 or 16)
        {
            foreach (var slot in _slotChannels.Keys.Where(slot => slot >= layout).ToArray()) await StopSlotAsync(slot);
            SetLayout(layout);
        }
    }

    private void VideoViewDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not LibVLCSharp.WPF.VideoView view) return;
        if (_focusedSlot is not null)
        {
            _focusedSlot = null;
            VideoGrid.Rows = VideoGrid.Columns = (int)Math.Ceiling(Math.Sqrt(_layout));
            VideoGrid.Children.Clear();
            foreach (var item in _videoTiles) VideoGrid.Children.Add(item);
            if (e.RoutedEvent is not null) e.Handled = true;
            return;
        }
        var slot = _videoViews.IndexOf(view);
        if (slot < 0) return;
        _focusedSlot = slot;
        SelectSlot(slot);
        VideoGrid.Rows = VideoGrid.Columns = 1;
        VideoGrid.Children.Clear();
        VideoGrid.Children.Add(_videoTiles[slot]);
        if (e.RoutedEvent is not null) e.Handled = true;
    }

    private void SelectSlot(int slot)
    {
        _activeSlot = slot;
        for (var index = 0; index < _videoTiles.Count; index++) _videoTiles[index].BorderBrush = index == slot ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("PageBrush");
        ActiveWindowText.Text = _slotChannels.TryGetValue(slot, out var channel) ? $"窗口 {slot + 1:00} · 通道 {channel:00}" : $"窗口 {slot + 1:00}";
    }

    private void PlaySlot(int slot, string url)
    {
        if (slot < 0 || slot >= _videoViews.Count) return;
        var view = _videoViews[slot];
        ReleasePlayer(view);
        var player = CreatePlayer();
        view.MediaPlayer = player;
        view.Visibility = Visibility.Visible;
        if (_slotChannels.TryGetValue(slot, out var channel)) _videoTitles[slot].Text = $"CH {channel:00} · {(_playback.ContainsKey(channel) ? "远程回放" : _streamType == 1 ? "主码流" : "子码流")}";
        SelectSlot(_activeSlot);
        _playerUrls[player] = url;
        using var media = new Media(_libVlc ??= new LibVLC(), url, FromType.FromLocation);
        player.Play(media);
    }

    private MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer(_libVlc ??= new LibVLC());
        _players.Add(player);
        _playerRetries[player] = 0;
        player.EncounteredError += PlayerEncounteredError;
        player.Playing += PlayerPlaying;
        player.EndReached += (_, _) => Dispatcher.BeginInvoke(() => PlaybackStatusText.Text = "回放已结束");
        return player;
    }

    private async void PlayerEncounteredError(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess()) { await Dispatcher.InvokeAsync(() => PlayerEncounteredError(sender, e)); return; }
        if (_closing || sender is not MediaPlayer player || !_playerUrls.ContainsKey(player) || _retryTokens.ContainsKey(player)) return;
        if (_ptzChannel is { } ptzChannel) _ = StopPtzAsync(ptzChannel);
        var attempt = _playerRetries.TryGetValue(player, out var count) ? count : 0;
        if (attempt >= 5) { _ = Dispatcher.BeginInvoke(() => SetStatus("媒体多次重连失败，请检查网络和设备状态")); return; }
        _playerRetries[player] = attempt + 1;
        var delay = TimeSpan.FromSeconds(Math.Min(16, Math.Pow(2, attempt)));
        var cancellation = new CancellationTokenSource();
        _retryTokens[player] = cancellation;
        _ = Dispatcher.BeginInvoke(() => SetStatus($"媒体连接中断，{delay.TotalSeconds:0} 秒后重连"));
        try
        {
            await Task.Delay(delay, cancellation.Token);
            if (_closing || !_playerUrls.ContainsKey(player)) return;
            if (_playerUrls.TryGetValue(player, out var url))
            {
                using var media = new Media(_libVlc ??= new LibVLC(), url, FromType.FromLocation);
                player.Play(media);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _ = Dispatcher.BeginInvoke(() => SetStatus($"媒体重连失败：{ex.Message}")); }
        finally { _retryTokens.Remove(player); cancellation.Dispose(); }
    }

    private void PlayerPlaying(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (sender is MediaPlayer player && _playerRetries.ContainsKey(player)) _playerRetries[player] = 0;
    });

    private void ReleasePlayer(LibVLCSharp.WPF.VideoView view)
    {
        view.Visibility = Visibility.Collapsed;
        var slot = _videoViews.IndexOf(view);
        if (slot >= 0 && slot < _videoTitles.Count) _videoTitles[slot].Text = $"窗口 {slot + 1:00}";
        if (view.MediaPlayer is not { } player) return;
        view.MediaPlayer = null;
        if (_retryTokens.Remove(player, out var cancellation)) cancellation.Cancel();
        _players.Remove(player);
        _playerUrls.Remove(player);
        _playerRetries.Remove(player);
        player.EncounteredError -= PlayerEncounteredError;
        player.Playing -= PlayerPlaying;
        try { player.Stop(); }
        catch (Exception ex) { SetStatus($"播放器停止失败：{ex.Message}"); }
        finally
        {
            try { player.Dispose(); }
            catch (Exception ex) { SetStatus($"播放器释放失败：{ex.Message}"); }
        }
    }

    private void StopPlayers()
    {
        foreach (var view in _videoViews) ReleasePlayer(view);
        _players.Clear();
        _playerUrls.Clear();
        _playerRetries.Clear();
        foreach (var view in _videoViews) view.MediaPlayer = null;
    }

    private async void PauseClick(object sender, RoutedEventArgs e) => await ControlPlaybackAsync("pause");
    private async void ResumeClick(object sender, RoutedEventArgs e) => await ControlPlaybackAsync("resume");
    private async void PlaybackSpeedClick(object sender, RoutedEventArgs e) => await ControlPlaybackAsync(Convert.ToString((sender as Button)?.Tag) ?? "normal");

    private async Task ControlPlaybackAsync(string action, int? position = null)
    {
        if (_playback.Count == 0) { SetStatus("当前没有回放会话"); return; }
        foreach (var pair in _playback.ToArray())
        {
            try
            {
                var current = await PostAsync<PlaybackSession>($"/api/playback-sessions/{pair.Value.Id}/control", new { action, position });
                if (current is not null && _playback.GetValueOrDefault(pair.Key)?.Id == pair.Value.Id) _playback[pair.Key] = current;
            }
            catch (Exception ex) { SetStatus($"通道 {pair.Key} 回放控制失败：{ex.Message}"); }
        }
    }

    private async void PlaybackSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_playback.Count == 0) return;
        await ControlPlaybackAsync("seek", (int)Math.Round(PlaybackSlider.Value));
    }

    private async void SearchRecordingsClick(object sender, RoutedEventArgs e)
    {
        if (_token is null) { ShowLoginClick(sender, e); return; }
        if (!_permissions.Contains("playback.view")) { SetStatus("当前账号没有回放权限"); return; }
        if (!int.TryParse(PlaybackChannelText.Text, out var channel) || channel <= 0 || !DateTimeOffset.TryParse(StartText.Text, out var start) || !DateTimeOffset.TryParse(EndText.Text, out var end) || end <= start || end - start > TimeSpan.FromDays(1))
        {
            SetStatus("录像检索参数无效");
            return;
        }
        try
        {
            var result = (await PostAsync<List<Recording>>("/api/recordings/search", new { channel, start, end }) ?? []).Select(item => item with { Channel = channel }).ToList();
            RecordingList.ItemsSource = result;
            SetStatus($"检索到 {result.Count} 段录像");
        }
        catch (Exception ex) { SetStatus($"录像检索失败：{ex.Message}"); }
    }

    private async void RecordingClick(object sender, RoutedEventArgs e)
    {
        if (_startingMedia || _token is null || _forceUpdate || !_permissions.Contains("playback.view") || sender is not Button { Tag: Recording record }) return;
        _startingMedia = true;
        _mediaIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StartText.Text = record.Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        EndText.Text = record.End.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        PlaybackChannelText.Text = record.Channel.ToString();
        var slot = _slotChannels.FirstOrDefault(item => item.Value == record.Channel, new KeyValuePair<int, int>(_activeSlot, record.Channel)).Key;
        await StopSlotAsync(slot);
        var generation = _mediaGeneration;
        try
        {
            var session = await StartPlaybackAsync(record.Channel, record.Start, record.End);
            if (_closing || generation != _mediaGeneration) { await DeleteAsync($"/api/playback-sessions/{session.Id}"); return; }
            _playback[record.Channel] = session;
            _slotChannels[slot] = record.Channel;
            PlaySlot(slot, session.RtspUrl);
            _renewTimer.Start();
            _playbackTimer.Start();
            SetStatus($"已按录像时间回放通道 {record.Channel}");
        }
        catch (Exception ex) { SetStatus($"回放启动失败：{ex.Message}"); }
        finally { _startingMedia = false; _mediaIdle?.TrySetResult(); }
    }

    private async Task PollPlaybackAsync()
    {
        if (_polling || _closing) return;
        _polling = true;
        try {
        foreach (var pair in _playback.ToArray())
        {
            try
            {
                var current = await GetAsync<PlaybackSession>($"/api/playback-sessions/{pair.Value.Id}");
                if (current is null || _playback.GetValueOrDefault(pair.Key)?.Id != pair.Value.Id) continue;
                _playback[pair.Key] = current;
                if (!PlaybackSlider.IsMouseCaptureWithin && _slotChannels.GetValueOrDefault(_activeSlot) == pair.Key) PlaybackSlider.Value = current.Progress;
                if (_slotChannels.GetValueOrDefault(_activeSlot) == pair.Key) PlaybackStatusText.Text = $"通道 {pair.Key}：{PlaybackState(current.State)}，{current.Progress:0}% · 共 {_playback.Count} 路";
            }
            catch { SetStatus("回放状态暂时无法同步，正在重试"); }
        }
        } finally { _polling = false; }
    }

    private static string PlaybackState(string state) => state switch { "playing" => "播放中", "paused" => "已暂停", "completed" => "已完成", "gap" => "无录像", _ => "连接中" };

    private async Task RenewSessionsAsync()
    {
        if (_renewing || _closing) return;
        _renewing = true;
        try {
        foreach (var pair in _live.ToArray())
        {
            try
            {
                var current = await PostAsync<LiveSession>($"/api/live-sessions/{pair.Value.Id}/renew", null);
                if (current is not null && _live.GetValueOrDefault(pair.Key)?.Id == pair.Value.Id)
                {
                    _live[pair.Key] = current;
                    UpdatePlayerUrl(pair.Key, current.RtspUrl);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"通道 {pair.Key} 实时续期失败：{ex.Message}");
                if (pair.Value.ExpiresAt <= DateTimeOffset.UtcNow || ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound }) await StopChannelAsync(pair.Key);
            }
        }
        foreach (var pair in _playback.ToArray())
        {
            try
            {
                var current = await PostAsync<PlaybackSession>($"/api/playback-sessions/{pair.Value.Id}/renew", null);
                if (current is not null && _playback.GetValueOrDefault(pair.Key)?.Id == pair.Value.Id)
                {
                    _playback[pair.Key] = current;
                    UpdatePlayerUrl(pair.Key, current.RtspUrl);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"通道 {pair.Key} 回放续期失败：{ex.Message}");
                if (pair.Value.ExpiresAt <= DateTimeOffset.UtcNow || ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound }) await StopChannelAsync(pair.Key);
            }
        }
        } finally { _renewing = false; }
    }

    private void UpdatePlayerUrl(int channel, string url)
    {
        foreach (var slot in _slotChannels.Where(item => item.Value == channel).Select(item => item.Key))
            if (slot < _videoViews.Count && _videoViews[slot].MediaPlayer is { } player) _playerUrls[player] = url;
    }

    private async void PtzStartClick(object sender, MouseButtonEventArgs e)
    {
        var channel = SelectedChannels.FirstOrDefault();
        if (_slotChannels.TryGetValue(_activeSlot, out var activeChannel)) channel = activeChannel;
        if (!_permissions.Contains("ptz.control") || channel <= 0 || _ptzChannel is not null || _ptzStarting is not null || sender is not Button button) return;
        _ptzChannel = channel;
        _ptzCommand = Convert.ToString(button.Tag);
        _ptzTimer.Start();
        _ptzStarting = PostCommandAsync($"/api/channels/{channel}/ptz/start", new { command = _ptzCommand, speed = (int)PtzSpeedSlider.Value });
        try { await _ptzStarting; if (_ptzChannel == channel) SetStatus($"通道 {channel} 云台运动中"); }
        catch (Exception ex) { await StopPtzAsync(channel); SetStatus($"云台启动失败：{ex.Message}"); }
    }

    private async void PtzStopClick(object sender, MouseEventArgs e)
    {
        if (_ptzChannel is { } channel) await StopPtzAsync(channel);
    }

    private async Task StopPtzAsync(int channel)
    {
        _ptzTimer.Stop();
        if (_ptzStopping is not null) { await _ptzStopping; return; }
        _ptzStopping = StopCoreAsync();
        try { await _ptzStopping; } finally { _ptzStopping = null; }
        async Task StopCoreAsync()
        {
            try
            {
                if (_ptzStarting is not null) try { await _ptzStarting; } catch { }
                await PostCommandAsync($"/api/channels/{channel}/ptz/stop", null);
            }
            catch (Exception ex) { SetStatus($"云台停止请求失败：{ex.Message}"); }
            finally { _ptzChannel = null; _ptzStarting = null; }
        }
    }

    private async void WindowDeactivated(object? sender, EventArgs e)
    {
        if (_ptzChannel is { } channel) await StopPtzAsync(channel);
    }

    private async void StopClick(object sender, RoutedEventArgs e) { await StopSessionsAsync(); SetStatus("媒体会话已停止"); }

    private async Task StopSessionsAsync()
    {
        _mediaGeneration++;
        if (_ptzChannel is { } channel) await StopPtzAsync(channel);
        _renewTimer.Stop();
        _playbackTimer.Stop();
        var requests = _live.Values.Select(session => DeleteAsync($"/api/live-sessions/{session.Id}")).Concat(_playback.Values.Select(session => DeleteAsync($"/api/playback-sessions/{session.Id}"))).ToArray();
        _live.Clear();
        _playback.Clear();
        _slotChannels.Clear();
        StopPlayers();
        PlaybackSlider.Value = 0;
        PlaybackStatusText.Text = "暂无回放会话";
        SelectSlot(_activeSlot);
        await Task.WhenAll(requests);
    }

    private async void StopWindowClick(object sender, RoutedEventArgs e) => await StopSlotAsync(_activeSlot);
    private async Task StopChannelAsync(int channel)
    {
        foreach (var slot in _slotChannels.Where(item => item.Value == channel).Select(item => item.Key).ToArray()) await StopSlotAsync(slot);
    }
    private async Task StopSlotAsync(int slot)
    {
        if (!_slotChannels.Remove(slot, out var channel)) return;
        if (_ptzChannel == channel) await StopPtzAsync(channel);
        if (slot < _videoViews.Count) ReleasePlayer(_videoViews[slot]);
        if (_live.Remove(channel, out var live)) await DeleteAsync($"/api/live-sessions/{live.Id}");
        if (_playback.Remove(channel, out var playback)) await DeleteAsync($"/api/playback-sessions/{playback.Id}");
    }

    private void CaptureClick(object sender, RoutedEventArgs e)
    {
        var view = _videoViews.ElementAtOrDefault(_focusedSlot ?? _activeSlot);
        if (view?.MediaPlayer is not { } player) { SetStatus("当前没有可截图的视频"); return; }
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"京华安防-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SetStatus(player.TakeSnapshot(0, path, 0, 0) ? $"截图已保存：{path}" : "截图失败，当前画面尚未准备好");
        }
        catch (Exception ex) { SetStatus($"截图失败：{ex.Message}"); }
    }

    private void FullscreenClick(object sender, RoutedEventArgs e)
    {
        if (WindowStyle == WindowStyle.None) { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = _beforeFullscreen; }
        else { _beforeFullscreen = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
    }

    private async void LogoutClick(object sender, RoutedEventArgs e)
    {
        await StopSessionsAsync();
        if (_mediaIdle is not null) await _mediaIdle.Task;
        _authTimer.Stop();
        try { if (_token is not null) await PostCommandAsync("/api/auth/logout", null); }
        catch (Exception ex) { SetStatus($"服务端注销失败：{ex.Message}"); }
        _token = null;
        _permissions.Clear();
        await DisconnectAlarmAsync();
        LoginPanel.Visibility = Visibility.Visible;
        MediaPanel.Visibility = AlarmPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed;
        ApiBaseText.IsReadOnly = false;
        WorkspacePanel.IsEnabled = !_forceUpdate;
        _channelNodes.Clear();
        _alarmItems.Clear();
        _unreadAlarms = 0;
        UserText.Text = "未登录";
        AccountButton.ToolTip = "登录平台";
        ConnectionText.Text = "平台未连接";
        ConnectionText.Foreground = (Brush)FindResource("MutedBrush");
        ChannelCountText.Text = "尚未同步通道";
        AlarmText.Text = "暂无未读报警";
        RecordingList.ItemsSource = null;
        PasswordText.Clear();
    }

    private async void CheckUpdateClick(object sender, RoutedEventArgs e) => await CheckUpdateAsync(true);

    private async Task CheckUpdateAsync(bool manual)
    {
        if (_checkingUpdate || _closing) return;
        _checkingUpdate = true;
        LoginButton.IsEnabled = false;
        try
        {
            var release = await GetAsync<PublicRelease>($"/api/desktop-releases/latest?currentVersion={Uri.EscapeDataString(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0")}");
            if (release is null || !release.UpdateAvailable) { _forceUpdate = false; _requiredVersion = null; WorkspacePanel.IsEnabled = true; SaveConfig(); UpdateText.Text = "已是最新版本"; return; }
            _forceUpdate = release.ForceUpdate;
            _requiredVersion = _forceUpdate ? release.Version : null;
            SaveConfig();
            WorkspacePanel.IsEnabled = !_forceUpdate;
            if (_forceUpdate) await StopSessionsAsync();
            UpdateText.Text = $"发现版本 {release.Version}";
            if (!release.FileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                UpdateText.Text = "此版本需手动下载安装包，自动更新仅支持 MSI";
                return;
            }
            if (!_forceUpdate && !manual) return;
            var updater = Path.Combine(AppContext.BaseDirectory, "VideoPlatform.Updater.exe");
            if (!File.Exists(updater)) throw new FileNotFoundException("更新器未随程序安装，请重新安装桌面端");
            var downloadUrl = new Uri(new Uri(ApiBase + "/"), release.DownloadUrl).ToString();
            // 从临时目录运行，避免 MSI 替换正在使用的更新器文件。
            var updateDirectory = Path.Combine(Path.GetTempPath(), "VideoPlatform-Updater", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updateDirectory);
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "VideoPlatform.Updater*"))
            {
                var target = Path.Combine(updateDirectory, Path.GetRelativePath(AppContext.BaseDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var start = new ProcessStartInfo(Path.Combine(updateDirectory, "VideoPlatform.Updater.exe")) { UseShellExecute = false, WorkingDirectory = updateDirectory };
            foreach (var argument in new[] { "--url", downloadUrl, "--file-name", release.FileName, "--sha256", release.Sha256, "--parent", Environment.ProcessPath!, "--parent-pid", Environment.ProcessId.ToString(), "--restart", Environment.ProcessPath! }) start.ArgumentList.Add(argument);
            if (_forceUpdate) start.ArgumentList.Add("--force");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动更新器");
            Close();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound && !_forceUpdate) { UpdateText.Text = "暂无已发布版本"; }
        catch (Exception ex) { UpdateText.Text = _forceUpdate ? $"必须更新，点击检查更新重试：{ex.Message}" : releaseError(manual, ex.Message); }
        finally { _checkingUpdate = false; LoginButton.IsEnabled = !_forceUpdate; LoginPanel.IsEnabled = !_forceUpdate && !_loggingIn; }
    }

    private static string releaseError(bool manual, string detail) => manual ? $"更新检查失败：{detail}" : "更新检查不可用";

    private async Task ConnectAlarmAsync()
    {
        await DisconnectAlarmAsync();
        if (_token is null || _closing || !_permissions.Contains("alarm.read")) return;
        _alarmConnection = new HubConnectionBuilder().WithUrl($"{ApiBase}/hubs/alarm", options => options.AccessTokenProvider = () => Task.FromResult<string?>(_token)).WithAutomaticReconnect(new AlarmRetryPolicy()).Build();
        var connection = _alarmConnection;
        _alarmConnection.On<JsonElement>("alarm", payload => Dispatcher.BeginInvoke(() =>
        {
            if (_closing || _alarmConnection != connection) return;
            var eventType = payload.TryGetProperty("eventType", out var value) ? value.GetString() : "未知事件";
            var channel = payload.TryGetProperty("channel", out var channelValue) ? channelValue.ToString() : "设备级";
            var summary = $"{DateTime.Now:HH:mm:ss} 通道 {channel}：{eventType}";
            _alarmItems.Insert(0, summary);
            while (_alarmItems.Count > 100) _alarmItems.RemoveAt(_alarmItems.Count - 1);
            AlarmText.Text = $"未读报警 {++_unreadAlarms} 条";
            _notification.ShowBalloonTip(5000, "京华安防平台报警", summary, System.Windows.Forms.ToolTipIcon.Warning);
            SystemSounds.Exclamation.Play();
        }));
        _alarmConnection.Reconnecting += _ => { Dispatcher.BeginInvoke(() => AlarmText.Text = "报警连接重连中"); return Task.CompletedTask; };
        _alarmConnection.Reconnected += _ => { Dispatcher.BeginInvoke(() => AlarmText.Text = "报警连接已恢复"); return Task.CompletedTask; };
        try { await _alarmConnection.StartAsync(_lifetime.Token); }
        catch (Exception ex) { AlarmText.Text = "报警实时连接失败"; SetStatus($"报警推送未连接：{ex.Message}"); }
    }

    private sealed class AlarmRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(retryContext.PreviousRetryCount, 5))));
    }

    private void ReadAlarmsClick(object sender, RoutedEventArgs e) { _unreadAlarms = 0; AlarmText.Text = "暂无未读报警"; }

    private async Task DisconnectAlarmAsync()
    {
        var connection = _alarmConnection;
        _alarmConnection = null;
        if (connection is null) return;
        try { await connection.StopAsync(); }
        catch (Exception ex) { SetStatus($"报警连接停止失败：{ex.Message}"); }
        finally
        {
            try { await connection.DisposeAsync(); }
            catch (Exception ex) { SetStatus($"报警连接释放失败：{ex.Message}"); }
        }
    }

    private async Task<T?> GetAsync<T>(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_json);
    }

    private Task<HttpResponseMessage> PostAsync(string path, object? body) => SendAsync(HttpMethod.Post, path, body);
    private async Task PostCommandAsync(string path, object? body)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body);
        response.EnsureSuccessStatusCode();
    }
    private async Task<T?> PostAsync<T>(string path, object? body)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_json);
    }

    private async Task DeleteAsync(string path)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Delete, path);
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) { SetStatus($"会话停止失败，服务端将在过期后回收：{ex.Message}"); }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool retry = true)
    {
        if (_token is not null && !path.StartsWith("/api/auth/", StringComparison.Ordinal))
        {
            if (_refreshTask is not null) await _refreshTask;
            else if (!_closing && _tokenExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(10)) await RefreshTokenAsync();
        }
        var token = _token;
        using var request = new HttpRequestMessage(method, $"{ApiBase}{path}");
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(_token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        var response = await _http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && token is not null && retry && !path.StartsWith("/api/auth/", StringComparison.Ordinal))
        {
            if (_token != token || await RefreshTokenAsync())
            {
                response.Dispose();
                return await SendAsync(method, path, body, false);
            }
        }
        return response;
    }

    private async Task<bool> RefreshTokenAsync()
    {
        if (_refreshTask is not null) return await _refreshTask;
        _refreshTask = RefreshCoreAsync();
        try { return await _refreshTask; } finally { _refreshTask = null; }
    }

    private async Task<bool> RefreshCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_token)) return false;
        var token = _token;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _token == token)
            {
                _token = null;
                _authTimer.Stop();
                _ = Dispatcher.InvokeAsync(async () => { await StopSessionsAsync(); await DisconnectAlarmAsync(); });
                LoginPanel.Visibility = Visibility.Visible;
                MediaPanel.Visibility = AlarmPanel.Visibility = SettingsPanel.Visibility = Visibility.Collapsed;
                WorkspacePanel.IsEnabled = false;
                ApiBaseText.IsReadOnly = false;
                SetStatus("登录会话已失效，请重新登录");
            }
            return false;
        }
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(_json);
        if (result is null || _token != token) return false;
        _token = result.AccessToken;
        _tokenExpiresAt = result.ExpiresAt;
        var profile = await GetAsync<User>("/api/auth/me");
        _permissions = (profile?.Permissions ?? []).ToHashSet(StringComparer.Ordinal);
        await ConnectAlarmAsync();
        return true;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private async void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _authTimer.Stop();
        _lifetime.Cancel();
        WorkspacePanel.IsEnabled = false;
        try
        {
            await StopSessionsAsync();
            if (_mediaIdle is not null) await _mediaIdle.Task;
        }
        finally
        {
            try { await DisconnectAlarmAsync(); }
            finally
            {
                _notification.Dispose();
                _libVlc?.Dispose();
                _http.Dispose();
                _closeReady = true;
                _ = Dispatcher.BeginInvoke(Close);
            }
        }
    }

    private sealed record ClientConfig(string ApiBase, string? RequiredVersion = null);
    private sealed record LoginResponse(string AccessToken, User User, DateTimeOffset ExpiresAt);
    private sealed record User(long Id, string Username, string[]? Permissions = null);
    private sealed record Channel(int ChannelNumber, bool Enabled, bool Online, string Name, string Model, int? UnitId = null);
    private sealed record LiveSession(Guid Id, int Channel, int StreamType, DateTimeOffset ExpiresAt, string RtspUrl, string HttpFlvUrl, string HlsUrl);
    private sealed record PlaybackSession(Guid Id, int Channel, DateTimeOffset ExpiresAt, string RtspUrl, string HttpFlvUrl, string HlsUrl, string Start = "", string End = "", string State = "", double Progress = 0);
    private sealed record Recording(string FileName, DateTimeOffset Start, DateTimeOffset End, long FileSize, int FileType, int StreamType, uint FileIndex)
    {
        public int Channel { get; init; }
        public string Display => $"{Start.LocalDateTime:MM-dd HH:mm:ss} - {End.LocalDateTime:HH:mm:ss}  {FileName}";
    }
    private sealed record PublicRelease(string Version, string FileName, string Sha256, bool ForceUpdate, bool UpdateAvailable, string DownloadUrl);

    private sealed class ChannelNode(string label, bool isChannel, int channelNumber) : INotifyPropertyChanged
    {
        public string Label { get; } = label;
        public bool IsChannel { get; } = isChannel;
        public string Icon => IsChannel ? "\uE714" : "\uE8B7";
        public int ChannelNumber { get; } = channelNumber;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible == value) return; _isVisible = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<ChannelNode> Children { get; } = [];
    }
}
