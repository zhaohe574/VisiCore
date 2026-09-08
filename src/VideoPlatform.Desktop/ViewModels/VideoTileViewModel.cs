using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class VideoTileViewModel(int index, IPlatformApi api, IPlayerFactory playerFactory, IUiDispatcher dispatcher) : ViewModelBase, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IVideoPlayer? _player;
    private MediaSession? _session;
    private long _generation;
    private int _reconnectAttempts;
    private DateTimeOffset _nextReconnect;
    private DateTimeOffset _lastRenew;
    private DateTimeOffset _connectedAt;
    private bool _rtspFailed;
    private Action<string>? _playerFailedHandler;
    private Action? _playerConnectedHandler;
    public int Index { get; } = index;
    public string Number => (Index + 1).ToString("00");
    public string? SessionId => _session?.Id;
    public string SessionPath => IsPlayback ? "playback-sessions" : "live-sessions";
    public bool PreferRtsp { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private Channel? _channel;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private bool _isPlayback;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isMuted = true;
    [ObservableProperty] private MediaPlayer? _nativePlayer;
    [ObservableProperty] private string _stateLabel = "空闲";
    [ObservableProperty] private DateTimeOffset? _currentTime;
    [ObservableProperty] private RecordingSegment[] _segments = [];
    [ObservableProperty] private double _speed = 1;
    public string Title => Channel is null ? $"窗口 {Number}" : $"{Channel.Name} · {(IsPlayback ? "回放" : "预览")}";
    partial void OnIsMutedChanged(bool value) { if (_player is not null) _player.Muted = value; }
    public void Invalidate() => Interlocked.Increment(ref _generation);

    public async Task StartAsync(Channel channel, bool playback, int streamType, DateTimeOffset start, DateTimeOffset end)
    {
        var generation = Interlocked.Increment(ref _generation);
        await _gate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            await StopCoreAsync();
            if (generation != _generation) return;
            Channel = channel;
            IsPlayback = playback;
            StateLabel = "连接中";
            var path = playback ? "playback-sessions" : "live-sessions";
            var response = playback
                ? await api.PostAsync<MediaSession>(path, new PlaybackRequest(channel.Id, start, end))
                : await api.PostAsync<MediaSession>(path, new LiveRequest(channel.Id, streamType));
            if (generation != _generation)
            {
                await DeleteSessionAsync(path, response.Id);
                return;
            }
            _session = response;
            _lastRenew = DateTimeOffset.UtcNow;
            _reconnectAttempts = 0;
            _nextReconnect = default;
            Apply(response);
            await PlayAsync(MediaUrl(response));
        }
        catch (Exception ex)
        {
            await StopCoreAsync();
            StateLabel = $"连接失败：{ex.Message}";
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task PlayAsync(string url)
    {
        var generation = _generation;
        await ReleasePlayerAsync();
        if (_session is null || generation != _generation) return;
        _nextReconnect = default;
        _player = playerFactory.Create();
        _player.Muted = IsMuted;
        var player = _player;
        _playerFailedHandler = message => PlayerFailed(player, generation, url, message);
        _playerConnectedHandler = () => PlayerConnected(player, generation);
        _player.Failed += _playerFailedHandler;
        _player.Connected += _playerConnectedHandler;
        NativePlayer = _player.NativePlayer;
        try
        {
            await player.PlayAsync(url);
            if (ReferenceEquals(_player, player) && generation == _generation && _session?.State == "paused") player.Pause(true);
        }
        catch (Exception ex) { PlayerFailed(player, generation, url, $"视频连接失败：{ex.Message}"); }
    }
    private void PlayerFailed(IVideoPlayer player, long generation, string url, string message)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            if (_session is not null && generation == _generation && ReferenceEquals(_player, player))
            {
                // 回退仅属于当前会话，不修改用户下次打开通道时的 RTSP 偏好。
                if (Uri.TryCreate(url, UriKind.Absolute, out var source) && source.Scheme == "rtsp" && HttpsTsUrl(_session) is not null)
                    _rtspFailed = true;
                _connectedAt = default;
                StateLabel = message;
                if (_nextReconnect == default) _nextReconnect = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, _reconnectAttempts)));
            }
            return Task.CompletedTask;
        });
    }
    private void PlayerConnected(IVideoPlayer player, long generation)
    {
        _ = dispatcher.InvokeAsync(() =>
        {
            if (_session is not null && generation == _generation && ReferenceEquals(_player, player) && _nextReconnect == default)
            {
                _connectedAt = DateTimeOffset.UtcNow;
                StateLabel = _session.State == "paused" ? "已暂停" : "播放中";
            }
            return Task.CompletedTask;
        });
    }

    public async Task TickAsync(DateTimeOffset now)
    {
        if (_session is null || !await _gate.WaitAsync(0)) return;
        var generation = _generation;
        try
        {
            if (_session is null) return;
            // 原生 Playing 可能紧接着失败，稳定连接后才恢复重试预算。
            if (_connectedAt != default && now - _connectedAt >= TimeSpan.FromSeconds(30))
            {
                _reconnectAttempts = 0;
                _connectedAt = default;
            }
            if (now - _lastRenew >= TimeSpan.FromSeconds(30))
            {
                await api.SendAsync(HttpMethod.Post, $"{SessionPath}/{_session.Id}/renew");
                _lastRenew = now;
            }
            if (IsPlayback)
            {
                var response = await api.GetAsync<MediaSession>($"playback-sessions/{_session.Id}");
                if (generation != _generation || response is null) return;
                var old = _session;
                _session = response;
                Apply(response);
                if (response.State is "completed" or "stopped" or "failed")
                {
                    var state = StateLabel;
                    await StopCoreAsync();
                    StateLabel = state;
                    return;
                }
                if (MediaUrl(response) != MediaUrl(old)) await PlayAsync(MediaUrl(response));
            }
            if (_nextReconnect != default && now >= _nextReconnect && _session is not null && generation == _generation)
            {
                if (_reconnectAttempts >= 5) { await StopCoreAsync(); StateLabel = "重连失败，请重新打开通道。"; return; }
                ++_reconnectAttempts;
                _nextReconnect = default;
                StateLabel = $"正在恢复连接（{_reconnectAttempts}/5）";
                await PlayAsync(MediaUrl(_session));
            }
        }
        catch (Exception ex)
        {
            var expired = ex is PlatformException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone };
            if (expired || now - _lastRenew > TimeSpan.FromSeconds(90)) await StopCoreAsync();
            StateLabel = $"会话同步失败：{ex.Message}";
        }
        finally { _gate.Release(); }
    }

    public async Task ControlAsync(PlaybackControl control)
    {
        var generation = _generation;
        await _gate.WaitAsync();
        try
        {
            if (!IsPlayback || _session is null || generation != _generation) return;
            await api.SendAsync(HttpMethod.Post, $"playback-sessions/{_session.Id}/control", control);
            if (generation != _generation) return;
            if (control.Action is "pause" or "resume") _player?.Pause(control.Action == "pause");
            if (control.Action == "speed" && control.Speed is { } speed) Speed = speed;
            if (control.Action == "seek")
            {
                var refreshed = await api.GetAsync<MediaSession>($"playback-sessions/{_session.Id}");
                if (refreshed is not null && generation == _generation) { _session = refreshed; Apply(refreshed); await PlayAsync(MediaUrl(refreshed)); }
            }
        }
        finally { _gate.Release(); }
    }
    public bool Capture(string path) => _player?.Capture(path) == true;
    public string MediaUrl(MediaSession session)
    {
        if (_rtspFailed) return HttpsTsUrl(session) ?? throw new InvalidOperationException("平台未提供有效的 HTTPS TS 回退地址。");
        if (PreferRtsp && !string.IsNullOrWhiteSpace(session.RtspUrl)) return session.RtspUrl;
        if (Uri.TryCreate(session.HttpTsUrl, UriKind.Absolute, out var ts) && (ts.Scheme == "https" || ts.IsLoopback)) return ts.AbsoluteUri;
        if (Uri.TryCreate(session.HttpFlvUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.IsLoopback)) return uri.AbsoluteUri;
        throw new InvalidOperationException("平台未提供 HTTPS 媒体地址；局域网可在设置中选择 RTSP 兼容模式。");
    }
    private static string? HttpsTsUrl(MediaSession session) =>
        Uri.TryCreate(session.HttpTsUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri.AbsoluteUri : null;
    public async Task StopAsync()
    {
        Invalidate();
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }
    private void Apply(MediaSession session)
    {
        CurrentTime = session.CurrentTime;
        Segments = session.Segments ?? [];
        Speed = session.Speed;
        StateLabel = session.State switch
        {
            "playing" => "播放中", "starting" => "连接中", "paused" => "已暂停", "gap" => "录像缺口",
            "completed" => "已完成", "failed" => "播放失败", "stopped" => "已停止", _ => "已连接"
        };
    }
    private async Task StopCoreAsync()
    {
        var session = _session;
        var path = SessionPath;
        _session = null;
        _nextReconnect = default;
        _rtspFailed = false;
        Channel = null;
        CurrentTime = null;
        Segments = [];
        StateLabel = "空闲";
        await ReleasePlayerAsync();
        if (session is not null) await DeleteSessionAsync(path, session.Id);
    }
    private async Task DeleteSessionAsync(string path, string id)
    {
        try { await api.SendAsync(HttpMethod.Delete, $"{path}/{id}"); }
        catch (Exception ex) { ClientFiles.Log($"停止媒体会话失败，等待服务端租约回收：{ex.Message}"); }
    }
    private async Task ReleasePlayerAsync()
    {
        var player = _player;
        _player = null;
        _connectedAt = default;
        NativePlayer = null;
        if (player is not null)
        {
            player.Failed -= _playerFailedHandler;
            player.Connected -= _playerConnectedHandler;
            _playerFailedHandler = null;
            _playerConnectedHandler = null;
            await player.DisposeAsync();
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
