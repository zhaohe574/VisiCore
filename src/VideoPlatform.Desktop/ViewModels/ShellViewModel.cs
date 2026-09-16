using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly IPlatformApi _api;
    private readonly EventService _events;
    private readonly UpdateService _updates;
    private readonly IUserInteraction _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _accessGate = new(1, 1);
    private readonly SemaphoreSlim _logoutGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _monitor;
    private Release? _release;
    private bool _closing;
    private DateTimeOffset _lastSync;
    private ClientSettings _settings;
    public WorkspaceViewModel Workspace { get; }
    public AlarmsViewModel Alarms { get; }
    public ExportsViewModel Exports { get; }
    public string VersionLabel => $"{UpdateService.CurrentVersion.ToString(3)} · Windows x64";
    private string _previousModule = "live";
    public string BackButtonText => IsAuthenticated ? "← 返回工作台" : "← 返回登录";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackButtonText))]
    private bool _isAuthenticated;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private string _userLabel = "未登录";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _module = "live";
    [ObservableProperty] private string _connectionState = "未连接";
    [ObservableProperty] private string _updateStatus = "尚未检查更新";
    [ObservableProperty] private bool _updateRequired;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _canUseWorkspace;
    [ObservableProperty] private bool _preferRtsp;
    [ObservableProperty] private string _clock = "";
    [ObservableProperty] private string _theme = "light";
    [ObservableProperty] private bool _showDiagnostics;
    [ObservableProperty] private bool _preferSubStreamInGrid = true;
    [ObservableProperty] private bool _hardwareDecoding = true;
    [ObservableProperty] private int _networkCachingMs = 800;
    [ObservableProperty] private int _sessionCount;
    [ObservableProperty] private int _decoderCount;
    [ObservableProperty] private string _moduleLabel = "实时预览";
    [ObservableProperty] private string _decodedFramesLabel = "";
    [ObservableProperty] private string _streamDetailLabel = "";
    [ObservableProperty] private string _settingsTab = "general";
    [ObservableProperty] private string _snapshotPath = "";
    [ObservableProperty] private string _exportPath = "";
    [ObservableProperty] private string _snapshotFormat = "PNG";

    public string DiagnosticsLabel => $"会话 {SessionCount} · 解码 {DecoderCount}{DecodedFramesLabel}";

    public ShellViewModel(SessionService session, IPlatformApi api, EventService events, UpdateService updates,
        WorkspaceViewModel workspace, AlarmsViewModel alarms, ExportsViewModel exports, IUiDispatcher dispatcher, IUserInteraction dialogs)
    {
        _session = session; _api = api; _events = events; _updates = updates; Workspace = workspace; Alarms = alarms; Exports = exports; _dispatcher = dispatcher; _dialogs = dialogs;
        _settings = ClientFiles.LoadSettings(); Server = _settings.Server;
        PreferRtsp = _settings.PreferRtsp;
        ShowDiagnostics = _settings.ShowDiagnostics;
        _hardwareDecoding = _settings.HardwareDecoding;
        _networkCachingMs = _settings.NetworkCachingMs;
        _preferSubStreamInGrid = _settings.PreferSubStreamInGrid;
        Workspace.PreferSubStreamInGrid = _settings.PreferSubStreamInGrid;
        (Workspace.PlayerFactory as IConfigurablePlayerFactory)?.Configure(PlayerOptions());
        _theme = ThemeService.Normalize(_settings.Theme);
        ThemeService.Apply(_theme);
        _snapshotPath = _settings.EffectiveSnapshotPath;
        _exportPath = _settings.EffectiveExportPath;
        _snapshotFormat = _settings.SnapshotFormat;
        foreach (var tile in Workspace.Tiles) tile.PreferRtsp = PreferRtsp;
        _events.Changed += kind => _dispatcher.InvokeAsync(() => OnEventAsync(kind));
        _events.StateChanged += value => _ = _dispatcher.InvokeAsync(() => { ConnectionState = value; return Task.CompletedTask; });
        _session.Invalidated += () => _ = _dispatcher.InvokeAsync(async () => { Status = "登录已失效，请重新登录。"; await LogoutCoreAsync(); });
        Workspace.ExportCreated += async () => { Module = "exports"; await Exports.RefreshAsync(); };
        Alarms.VideoRequested += (alarm, mode) => _ = _dispatcher.InvokeAsync(async () => await OpenAlarmVideoAsync(alarm, mode));
        Clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        UpdateModuleLabel();
    }

    partial void OnThemeChanged(string value)
    {
        var normalized = ThemeService.Normalize(value);
        ThemeService.Apply(normalized);
        ClientFiles.SaveSettings(_settings = _settings with { Theme = normalized });
        PushPreferences();
    }

    partial void OnShowDiagnosticsChanged(bool value)
    {
        ClientFiles.SaveSettings(_settings = _settings with { ShowDiagnostics = value });
        RefreshDiagnostics();
        PushPreferences();
    }

    partial void OnPreferSubStreamInGridChanged(bool value)
    {
        Workspace.PreferSubStreamInGrid = value;
        ClientFiles.SaveSettings(_settings = _settings with { PreferSubStreamInGrid = value });
        _ = Workspace.ApplyLayoutTiersAsync();
        PushPreferences();
    }

    partial void OnHardwareDecodingChanged(bool value)
    {
        ClientFiles.SaveSettings(_settings = _settings with { HardwareDecoding = value });
        (Workspace.PlayerFactory as IConfigurablePlayerFactory)?.Configure(PlayerOptions());
        PushPreferences();
    }

    partial void OnNetworkCachingMsChanged(int value)
    {
        ClientFiles.SaveSettings(_settings = _settings with { NetworkCachingMs = value });
        (Workspace.PlayerFactory as IConfigurablePlayerFactory)?.Configure(PlayerOptions());
        PushPreferences();
    }

    private PlayerOptions PlayerOptions() => new(HardwareDecoding, NetworkCachingMs);

    /// <summary>正在套用平台偏好时抑制回写，避免「读取 → 触发变更 → 立即回写」的无谓往返。</summary>
    private bool _applyingPreferences;

    /// <summary>
    /// 登录后拉取平台偏好并覆盖本机设置。平台不可用或仍是旧版本（无该端点）时静默保留本机设置，
    /// 本机 desktop-v2.json 始终是离线兜底，不会被清空。
    /// </summary>
    private async Task LoadPreferencesAsync()
    {
        if (!IsAuthenticated) return;
        try
        {
            var preferences = await _api.GetAsync<Preferences>("auth/preferences");
            if (preferences is null) return;
            _applyingPreferences = true;
            try
            {
                Theme = preferences.Theme;
                PreferSubStreamInGrid = preferences.PreferSubStreamInGrid;
                HardwareDecoding = preferences.HardwareDecoding;
                NetworkCachingMs = preferences.NetworkCachingMs;
                ShowDiagnostics = preferences.ShowDiagnostics;
            }
            finally { _applyingPreferences = false; }
            ClientFiles.SaveSettings(_settings = _settings.With(preferences));
        }
        catch (Exception ex)
        {
            ClientFiles.Log($"读取平台显示偏好失败，保留本机设置：{ex.Message}");
        }
    }

    /// <summary>把当前显示偏好回写平台；失败只记日志，不影响本机设置已生效的结果。</summary>
    private void PushPreferences()
    {
        if (_applyingPreferences || !IsAuthenticated) return;
        var preferences = _settings.ToPreferences();
        _ = Task.Run(async () =>
        {
            try { await _api.SendAsync(HttpMethod.Put, "auth/preferences", preferences); }
            catch (Exception ex) { ClientFiles.Log($"保存平台显示偏好失败（本机设置已生效）：{ex.Message}"); }
        });
    }

    partial void OnModuleChanged(string value) => UpdateModuleLabel();

    private void UpdateModuleLabel()
    {
        ModuleLabel = Module switch
        {
            "playback" => "远程回放",
            "alarms" => "报警中心",
            "exports" => "录像导出",
            "settings" => "设置与更新",
            _ => "实时预览"
        };
    }

    /// <summary>状态栏诊断只统计可见分屏中的媒体会话与原生解码器数量，供真机性能核对。</summary>
    public void RefreshDiagnostics()
    {
        var tiles = Workspace.Tiles;
        SessionCount = tiles.Count(tile => tile.SessionId is not null);
        DecoderCount = tiles.Count(tile => tile.IsDecoding);
        var positions = tiles.Select(tile => tile.PlaybackPositionMs).Where(value => value is not null).Select(value => value!.Value).ToArray();
        // 只有在确实取到播放位置时才显示：位置为 0 或缺失说明连接上了但没有推进解码。
        DecodedFramesLabel = positions.Length == 0 ? "" : $" · 播放 {positions.Sum() / 1000}s";
        // 焦点窗口的真实档位（分辨率来自服务端回传的媒体参数）；未知时不显示，不用估计值顶替。
        var detail = Workspace.SelectedTile?.StreamDetailLabel;
        StreamDetailLabel = string.IsNullOrEmpty(detail) ? "" : $" · {detail}";
        OnPropertyChanged(nameof(DiagnosticsLabel));
    }

    [RelayCommand] private Task ToggleDiagnosticsAsync() => RunAsync(() => { ShowDiagnostics = !ShowDiagnostics; return Task.CompletedTask; });

    /// <summary>设置页主题切换：light / dark。</summary>
    [RelayCommand] private void SetTheme(string? theme)
    {
        if (theme is "light" or "dark") Theme = theme;
    }

    [RelayCommand] private void SetSettingsTab(string? tab)
    {
        if (tab is "general" or "media" or "storage" or "about") SettingsTab = tab;
    }

    partial void OnSnapshotPathChanged(string value) => ClientFiles.SaveSettings(_settings = _settings with { SnapshotPath = value });
    partial void OnExportPathChanged(string value) => ClientFiles.SaveSettings(_settings = _settings with { ExportPath = value });
    partial void OnSnapshotFormatChanged(string value) => ClientFiles.SaveSettings(_settings = _settings with { SnapshotFormat = value });

    [RelayCommand] private void BrowseSnapshotPath()
    {
        var folder = _dialogs.SelectFolder("选择抓图保存目录");
        if (!string.IsNullOrWhiteSpace(folder)) SnapshotPath = folder;
    }

    [RelayCommand] private void BrowseExportPath()
    {
        var folder = _dialogs.SelectFolder("选择录像导出保存目录");
        if (!string.IsNullOrWhiteSpace(folder)) ExportPath = folder;
    }

    [RelayCommand] private void OpenProfile()
    {
        if (!IsAuthenticated) return;
        _dialogs.ShowProfileDialog(this);
    }

    [RelayCommand] private void OpenChangePassword()
    {
        if (!IsAuthenticated) return;
        _dialogs.ShowChangePasswordDialog(this);
    }

    /// <summary>顶栏刷新：重新拉取资源、报警与导出，并刷新诊断计数。</summary>
    [RelayCommand] private Task RefreshAllAsync() => RunAsync(async () =>
    {
        if (!IsAuthenticated) return;
        await RefreshDataAsync();
        RefreshDiagnostics();
    }, "平台数据已刷新。");

    partial void OnPreferRtspChanged(bool value)
    {
        foreach (var tile in Workspace.Tiles) tile.PreferRtsp = value;
        ClientFiles.SaveSettings(_settings = _settings with { PreferRtsp = value });
    }
    public Task InitializeAsync() => RunAsync(async () =>
    {
        _monitor ??= MonitorAsync(_lifetime.Token);
        _clockTimer ??= StartClockTimer();
        _session.SetServer(Server);
        UpdateRequired = Version.TryParse(_settings.RequiredVersion, out var minimum) && UpdateService.CurrentVersion < minimum;
        await CheckUpdateCoreAsync();
        if (await _session.RestoreAsync(_lifetime.Token)) await AfterLoginAsync();
    });

    /// <summary>时钟独立走 1 秒 DispatcherTimer，不再与业务 tick 绑定，避免每拍触发整条同步链。</summary>
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer StartClockTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        timer.Start();
        return timer;
    }
    public Task LoginAsync(string password) => LoginAsync(Username, password);

    /// <summary>
    /// 登录入口。显式传入账号供压力测试模式复用，界面仍使用输入框内容。
    /// <paramref name="enforceUpdateGate"/> 为 false 时跳过“必须更新后才能登录”的限制，
    /// 用于在同一平台上对旧客户端做受控性能采集；界面登录始终走默认的强制校验。
    /// </summary>
    public Task LoginAsync(string username, string password, bool enforceUpdateGate = true) => RunAsync(async () =>
    {
        if (_closing) return;
        Username = username;
        _session.SetServer(Server);
        if (enforceUpdateGate)
        {
            await CheckUpdateCoreAsync();
            if (UpdateRequired) throw new InvalidOperationException("当前版本必须更新后才能登录。");
        }
        await _session.LoginAsync(username, password, _lifetime.Token);
        await AfterLoginAsync();
        ClientFiles.SaveSettings(_settings = _settings with { Server = _session.Server });
        Status = "登录成功。";
    });
    private async Task AfterLoginAsync()
    {
        IsAuthenticated = true; CanUseWorkspace = !UpdateRequired;
        if (Module == "settings") Module = "live";
        await ApplyAccessAsync();
        await RefreshDataAsync();
        await ApplyPreferencesAsync();
        try { await Workspace.PurgeOrphanSessionsAsync(); }
        catch { }
        try { await _events.StartAsync(_lifetime.Token); }
        catch (Exception ex) { ConnectionState = "事件连接不可用"; Status = $"事件连接失败，将定期同步数据：{ex.Message}"; }
    }
    private async Task ApplyAccessAsync()
    {
        var user = _session.CurrentUser;
        UserLabel = user?.Label ?? "未登录"; DisplayName = user?.DisplayName ?? ""; Phone = user?.Phone ?? "";
        await Workspace.SetAccessAsync(user); Alarms.SetAccess(user); Exports.SetAccess(user);
    }
    private async Task RefreshDataAsync()
    {
        await Workspace.RefreshAsync();
        await Alarms.RefreshAsync();
        await Exports.RefreshAsync();
        _lastSync = DateTimeOffset.UtcNow;
    }

    /// <summary>登录后套用账号偏好；与数据刷新分开，避免任一失败影响另一项。</summary>
    private Task ApplyPreferencesAsync() => LoadPreferencesAsync();
    private async Task OnEventAsync(string kind)
    {
        if (!IsAuthenticated || _closing) return;
        await RunAsync(async () =>
        {
            if (kind is "access.changed" or "reconnected") await RefreshAccessAsync();
            else if (kind == "device.changed") await Workspace.RefreshAsync();
            else if (kind == "alarm.changed") await Alarms.RefreshAsync();
            else if (kind == "export.changed") await Exports.RefreshAsync();
            else if (kind == "media.changed") await Workspace.TickAsync();
        });
    }
    private async Task RefreshAccessAsync()
    {
        await Workspace.SuspendAccessAsync();
        Alarms.Clear(); Exports.Clear();
        await _accessGate.WaitAsync();
        try
        {
            if (!IsAuthenticated || !_session.IsAuthenticated) return;
            await _session.ReloadUserAsync(_lifetime.Token);
            await ApplyAccessAsync();
            await RefreshDataAsync();
        }
        finally { _accessGate.Release(); }
    }
    private async Task MonitorAsync(CancellationToken token)
    {
        // 5 秒一拍：仅做登录续期、媒体会话同步与低频数据校对；界面读秒由独立的时钟计时器负责。
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await _dispatcher.InvokeAsync(async () =>
                {
                    if (!IsAuthenticated || !CanUseWorkspace || _closing)
                    {
                        SessionCount = 0; DecoderCount = 0;
                        return;
                    }
                    try
                    {
                        await _session.GetTokenAsync();
                        await Workspace.TickAsync();
                        RefreshDiagnostics();
                        if (DateTimeOffset.UtcNow - _lastSync > TimeSpan.FromSeconds(60))
                        {
                            var previous = _session.CurrentUser;
                            var user = await _session.ReloadUserAsync(token);
                            if (previous is not null && !previous.Permissions.Order().SequenceEqual(user.Permissions.Order())) await RefreshAccessAsync();
                            else await RefreshDataAsync();
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception ex) { Status = $"同步平台状态失败：{ex.Message}"; }
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    [RelayCommand] private Task SwitchModuleAsync(string? module) => RunAsync(async () =>
    {
        if (module is not ("live" or "playback" or "alarms" or "exports" or "settings")) return;
        if (module == "settings" && Module == "settings")
        {
            Module = _previousModule;
            return;
        }
        if (module == "settings")
        {
            _previousModule = Module == "settings" ? "live" : Module;
        }
        await Workspace.StopPtzAsync();
        if (module is "live" or "playback") await Workspace.SetModeAsync(module == "playback");
        Module = module;
        if (module == "alarms") await Alarms.RefreshAsync();
        if (module == "exports") await Exports.RefreshAsync();
    });
    [RelayCommand] private Task CloseSettingsAsync() => RunAsync(() =>
    {
        Module = _previousModule;
        return Task.CompletedTask;
    });
    /// <summary>报警中心视频联动：切到主预览/远程回放并打开报警通道（iVMS-4200 事件中心使用逻辑）。</summary>
    private async Task OpenAlarmVideoAsync(Alarm alarm, string mode)
    {
        try
        {
            if (!IsAuthenticated || !CanUseWorkspace) return;
            var channelId = alarm.ChannelId;
            if (channelId is not { } id) { Status = "该报警没有关联通道，无法联动视频。"; return; }
            if (!Workspace.Channels.TryGetValue(id, out var channel)) { Status = "该报警通道不在当前权限范围或尚未同步，请刷新资源。"; return; }
            if (!channel.Online) { Status = $"报警通道已离线，无法联动：{channel.Name}"; return; }
            await Workspace.StopPtzAsync();
            if (mode == "playback")
            {
                var occurred = alarm.OccurredAt.ToLocalTime();
                var start = occurred.AddMinutes(-1);
                var end = occurred.AddMinutes(1) > DateTimeOffset.Now ? DateTimeOffset.Now : occurred.AddMinutes(1);
                if (end <= start) end = start.AddSeconds(30);
                Workspace.RecordingDate = start.Date;
                Workspace.StartTime = start.ToString("HH:mm:ss");
                Workspace.EndTime = end.ToString("HH:mm:ss");
                await Workspace.SetModeAsync(true);
                Module = "playback";
            }
            else
            {
                await Workspace.SetModeAsync(false);
                Module = "live";
            }
            await Workspace.OpenChannelAsync(channel);
            Status = mode == "playback"
                ? $"已联动回放报警通道：{channel.Name}（{alarm.OccurredAt.LocalDateTime:MM-dd HH:mm:ss} 前后）"
                : $"已联动实况：{channel.Name}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = $"报警视频联动失败：{ex.Message}"; }
    }
    [RelayCommand] private Task LogoutAsync() => RunAsync(LogoutCoreAsync, "已退出登录。");
    private async Task LogoutCoreAsync()
    {
        await _logoutGate.WaitAsync();
        try
        {
            CanUseWorkspace = false; IsAuthenticated = false;
            _previousModule = "live"; Module = "live";
            Alarms.SetAccess(null); Exports.SetAccess(null);
            await Workspace.ClearAsync();
            await _events.StopAsync();
            await _session.LogoutAsync();
            UserLabel = "未登录"; DisplayName = Phone = ""; ConnectionState = "未连接";
        }
        finally { _logoutGate.Release(); }
    }
    [RelayCommand] private Task SaveProfileAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(DisplayName)) throw new ArgumentException("请输入姓名。");
        await _api.SendAsync(HttpMethod.Put, "auth/profile", new ProfileRequest(DisplayName.Trim(), Phone.Trim()));
        await _session.ReloadUserAsync(); await ApplyAccessAsync();
    }, "个人资料已保存。");
    public Task ChangePasswordAsync(string current, string next) => RunAsync(async () =>
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next)) throw new ArgumentException("请输入当前密码和新密码。");
        await _api.SendAsync(HttpMethod.Put, "auth/password", new PasswordRequest(current, next));
        await LogoutCoreAsync(); Status = "密码已修改，请重新登录。";
    });
    [RelayCommand] private Task CheckUpdateAsync() => RunAsync(CheckUpdateCoreAsync);
    private async Task CheckUpdateCoreAsync()
    {
        try
        {
            _release = await _updates.CheckAsync();
            UpdateAvailable = _release is not null && Version.TryParse(_release.Version, out var latest) && latest > UpdateService.CurrentVersion;
            if (_release is not null)
            {
                UpdateRequired = UpdateService.IsRequired(_release);
                _settings = _settings with { RequiredVersion = UpdateRequired ? _release.Version : null };
                ClientFiles.SaveSettings(_settings);
            }
            UpdateStatus = UpdateAvailable ? $"发现版本 {_release!.Version}{(UpdateRequired ? "，需要更新" : "")}" : "当前为最新版本或暂无已发布版本";
            if (UpdateRequired) { CanUseWorkspace = false; await Workspace.StopAllCoreAsync(); }
        }
        catch (Exception ex) { UpdateStatus = $"检查更新失败：{ex.Message}"; }
    }
    [RelayCommand] private Task InstallUpdateAsync() => RunAsync(async () =>
    {
        if (_release is null) { await CheckUpdateCoreAsync(); if (_release is null) return; }
        if (!UpdateAvailable) return;
        if (!UpdateRequired && !_dialogs.Confirm($"安装版本 {_release.Version} 并重新启动客户端？")) return;
        _updates.Launch(_release, _session.Server);
        _dialogs.Shutdown();
    });
    public async Task CloseAsync()
    {
        if (_closing) return;
        _closing = true; _lifetime.Cancel();
        try
        {
            var clearTask = Workspace.ClearAsync();
            var eventsTask = _events.StopAsync();
            await Task.WhenAll(clearTask, eventsTask).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { }
        if (_monitor is not null)
        {
            try { await _monitor.WaitAsync(TimeSpan.FromMilliseconds(500)); }
            catch { }
        }
        // 关闭窗口保留 DPAPI 会话，主动退出命令才撤销服务器登录。
    }
}


