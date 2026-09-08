using System.Net.Http;
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
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _server = "https://10.37.200.74";
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

    public ShellViewModel(SessionService session, IPlatformApi api, EventService events, UpdateService updates,
        WorkspaceViewModel workspace, AlarmsViewModel alarms, ExportsViewModel exports, IUiDispatcher dispatcher, IUserInteraction dialogs)
    {
        _session = session; _api = api; _events = events; _updates = updates; Workspace = workspace; Alarms = alarms; Exports = exports; _dispatcher = dispatcher; _dialogs = dialogs;
        _settings = ClientFiles.LoadSettings(); Server = _settings.Server;
        PreferRtsp = _settings.PreferRtsp;
        foreach (var tile in Workspace.Tiles) tile.PreferRtsp = PreferRtsp;
        _events.Changed += kind => _dispatcher.InvokeAsync(() => OnEventAsync(kind));
        _events.StateChanged += value => _ = _dispatcher.InvokeAsync(() => { ConnectionState = value; return Task.CompletedTask; });
        _session.Invalidated += () => _ = _dispatcher.InvokeAsync(async () => { Status = "登录已失效，请重新登录。"; await LogoutCoreAsync(); });
        Workspace.ExportCreated += async () => { Module = "exports"; await Exports.RefreshAsync(); };
    }
    partial void OnPreferRtspChanged(bool value)
    {
        foreach (var tile in Workspace.Tiles) tile.PreferRtsp = value;
        ClientFiles.SaveSettings(_settings = _settings with { PreferRtsp = value });
    }
    public Task InitializeAsync() => RunAsync(async () =>
    {
        _monitor ??= MonitorAsync(_lifetime.Token);
        _session.SetServer(Server);
        UpdateRequired = Version.TryParse(_settings.RequiredVersion, out var minimum) && UpdateService.CurrentVersion < minimum;
        await CheckUpdateCoreAsync();
        if (await _session.RestoreAsync(_lifetime.Token)) await AfterLoginAsync();
    });
    public Task LoginAsync(string password) => RunAsync(async () =>
    {
        if (_closing) return;
        _session.SetServer(Server);
        await CheckUpdateCoreAsync();
        if (UpdateRequired) throw new InvalidOperationException("当前版本必须更新后才能登录。");
        await _session.LoginAsync(Username, password, _lifetime.Token);
        await AfterLoginAsync();
        ClientFiles.SaveSettings(_settings = _settings with { Server = _session.Server });
        Status = "登录成功。";
    });
    private async Task AfterLoginAsync()
    {
        IsAuthenticated = true; CanUseWorkspace = !UpdateRequired;
        await ApplyAccessAsync();
        await RefreshDataAsync();
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await _dispatcher.InvokeAsync(async () =>
                {
                    if (!IsAuthenticated || !CanUseWorkspace || _closing) return;
                    try
                    {
                        await _session.GetTokenAsync();
                        await Workspace.TickAsync();
                        if (DateTimeOffset.UtcNow - _lastSync > TimeSpan.FromSeconds(30))
                        {
                            var previous = _session.CurrentUser;
                            var user = await _session.ReloadUserAsync(token);
                            if (previous is null || !previous.Permissions.Order().SequenceEqual(user.Permissions.Order())) await RefreshAccessAsync();
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
        await Workspace.StopPtzAsync();
        if (module is "live" or "playback") await Workspace.SetModeAsync(module == "playback");
        Module = module;
        if (module == "alarms") await Alarms.RefreshAsync();
        if (module == "exports") await Exports.RefreshAsync();
    });
    [RelayCommand] private Task LogoutAsync() => RunAsync(LogoutCoreAsync, "已退出登录。");
    private async Task LogoutCoreAsync()
    {
        await _logoutGate.WaitAsync();
        try
        {
            CanUseWorkspace = false; IsAuthenticated = false;
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
        await Workspace.ClearAsync(); await _events.StopAsync();
        if (_monitor is not null) await _monitor;
        // 关闭窗口保留 DPAPI 会话，主动退出命令才撤销服务器登录。
    }
}
