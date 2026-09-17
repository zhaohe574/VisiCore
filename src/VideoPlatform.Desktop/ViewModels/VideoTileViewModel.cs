using System.IO;
using System.Net.Http;
using System.Windows;
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] [NotifyPropertyChangedFor(nameof(HeaderTitle))] private Channel? _channel;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private bool _isPlayback;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isMaximized;
    [ObservableProperty] private bool _isMuted = true;
    partial void OnIsMutedChanged(bool value)
    {
        if (_player is not null)
        {
            _player.Muted = value;
            if (!value && IsPlaying && _session is not null)
            {
                _ = PlayAsync(MediaUrl(_session));
            }
        }
    }
    [ObservableProperty] private MediaPlayer? _nativePlayer;
    [ObservableProperty] private string _stateLabel = "空闲";
    [ObservableProperty] private DateTimeOffset? _currentTime;
    [ObservableProperty] private RecordingSegment[] _segments = [];
    [ObservableProperty] private double _speed = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StreamBadge))] [NotifyPropertyChangedFor(nameof(StreamDetailLabel))] private int _streamType = 2;
    [ObservableProperty] private string? _aspectRatio;
    [ObservableProperty] private bool _isPlaying;
    /// <summary>分屏网格中的行列跨度与位置；CellWidth/CellHeight 为 0 表示该格不在当前档位内。</summary>
    [ObservableProperty] private int _rowSpan = 1;
    [ObservableProperty] private int _columnSpan = 1;
    [ObservableProperty] private int _gridRow;
    [ObservableProperty] private int _gridColumn;
    [ObservableProperty] private GridLength _cellWidth = new(1);
    [ObservableProperty] private GridLength _cellHeight = new(1);
    /// <summary>该格是否在当前档位的网格内参与排布（可见且有有效跨度）。</summary>
    [ObservableProperty] private bool _isActive;
    public string StreamBadge => StreamType == 1 ? "HD" : "SD";
    /// <summary>服务端回传的真实分辨率；未知为 null（旧版平台或适配器不提供）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StreamDetailLabel))] private string? _actualResolution;
    /// <summary>服务端回传的真实码率；未知为 null。</summary>
    [ObservableProperty] private string? _actualBitrate;
    /// <summary>形如「主码流 2560×1440」，仅在拿到真实分辨率时才组成，未知时不伪装。</summary>
    public string StreamDetailLabel => ActualResolution is null ? "" : $"{(StreamType == 1 ? "主码流" : "子码流")} {ActualResolution}";
    public string HeaderTitle => Channel?.DisplayName ?? $"窗口 {Number}";
    public string Title => Channel is null ? $"窗口 {Number}" : $"{Channel.DisplayName} · {(IsPlayback ? "回放" : "预览")}";
    /// <summary>当前是否持有原生解码器：用于状态栏诊断，不触发界面刷新语义。</summary>
    public bool IsDecoding => _player is not null;
    /// <summary>原生播放器已推进的播放位置（毫秒）；用于确认低占用场景下确实在解码。</summary>
    public long? PlaybackPositionMs => _player?.PlaybackPositionMs;
    /// <summary>当前生效的显示档位，由工作区在布局/放大/全屏变化时写入。</summary>
    public TileDisplayTier DisplayTier { get; private set; } = TileDisplayTier.Grid;
    /// <summary>人工指定的码流档位；非空时优先于自动档位策略。</summary>
    public int? ManualStreamOverride
    {
        get => _manualStreamOverride;
        set
        {
            _manualStreamOverride = value;
            if (value is { } manual && _session is not null && StreamType != manual) _pendingStreamOverride = manual;
        }
    }
    private int? _manualStreamOverride;
    /// <summary>
    /// 待生效的人工码流。格子不可见时不做网络切换，等重新进入分屏后再执行；
    /// 这样单击“切换码流”在窗口隐藏期间也不会出现无声失败。
    /// </summary>
    private int? _pendingStreamOverride;
    /// <summary>当前状态下首次打开通道应采用的码流档位。</summary>
    public int DesiredStreamType => _manualStreamOverride ?? (DisplayTier == TileDisplayTier.Grid && PreferSubStreamInGrid
        && !_subStreamUnavailable && !SubStreamKnownUnavailable(Channel) && PlatformDidNotDenySubStream(Channel) ? 2 : 1);

    /// <summary>
    /// 平台缓存未就绪（Unknown）时返回 true，从而**保持原有行为完全不变**；
    /// 只有平台明确答复「该通道没有子码流」时才返回 false，直接选主码流避开一次失败往返。
    /// </summary>
    private static bool PlatformDidNotDenySubStream(Channel? channel) =>
        channel is null || !PlatformCapability.TryGetValue(channel.Id, out var available) || available;
    /// <summary>分屏子码流偏好由工作区设置页写入。</summary>
    public bool PreferSubStreamInGrid { get; set; } = true;
    /// <summary>设备确认没有子码流后置位，避免反复尝试降档造成无谓的会话重建。</summary>
    private bool _subStreamUnavailable;

    /// <summary>重置人工指定的码流覆盖与无子码流标记，使新打开的通道恢复默认子码流策略。</summary>
    public void ResetStreamPreference()
    {
        _manualStreamOverride = null;
        _pendingStreamOverride = null;
        _subStreamUnavailable = false;
    }

    /// <summary>新开通道时默认采用的子码流档位（若设备已知不支持子码流则安全回退为主码流）。</summary>
    public int DefaultSubStreamType(Channel channel) =>
        !_subStreamUnavailable && !SubStreamKnownUnavailable(channel) && PlatformDidNotDenySubStream(channel) ? 2 : 1;
    partial void OnAspectRatioChanged(string? value)
    {
        if (value is not null && value != "fill" && value != "default" && value != "original" && _player is not null)
        {
            _player.AspectRatio = value;
        }
        else if (value == "original" && _player is not null)
        {
            _player.AspectRatio = null;
        }
    }
    public void Invalidate() => Interlocked.Increment(ref _generation);

    public async Task StartAsync(Channel channel, bool playback, int streamType, DateTimeOffset start, DateTimeOffset end)
    {
        var generation = Interlocked.Increment(ref _generation);
        await _gate.WaitAsync();
        try
        {
            if (generation != _generation) return;
            var hadPreviousLiveSession = !IsPlayback && _session is not null;
            var isSameChannel = Channel?.Id == channel.Id;
            await StopCoreAsync();
            if (generation != _generation) return;
            // 切换同一通道码流或重新打开同一通道时，设备端（如海康 NVR/CVR）释放上一条 RTSP 会话与硬件编码通道需要物理耗时。
            // 立即发起新连接会导致设备拒绝或超时（502 Bad Gateway）；在此等待以确保设备完成套接字回收。
            if (hadPreviousLiveSession && isSameChannel && !playback)
            {
                await Task.Delay(500);
                if (generation != _generation) return;
            }

            Channel = channel;
            IsPlayback = playback;
            StreamType = streamType;
            StateLabel = "连接中";
            var path = playback ? "playback-sessions" : "live-sessions";
            MediaSession response;
            try
            {
                response = playback
                    ? await api.PostAsync<MediaSession>(path, new PlaybackRequest(channel.Id, start, end, StreamType: streamType))
                    : await api.PostAsync<MediaSession>(path, new LiveRequest(channel.Id, streamType));
            }
            catch (Exception ex) when (!playback && (
                ex is TaskCanceledException ||
                ex is TimeoutException ||
                ex is HttpRequestException ||
                ex.Message.Contains("HttpClient.Timeout") ||
                ex.Message.Contains("canceled") ||
                (ex is PlatformException pe && (pe.StatusCode == System.Net.HttpStatusCode.BadGateway || pe.StatusCode == System.Net.HttpStatusCode.GatewayTimeout || (int?)pe.StatusCode == 429 || pe.Message.Contains("设备操作未成功") || pe.Message.Contains("超时")))))
            {
                if (generation != _generation) return;
                StateLabel = "正在重试连接...";
                ClientFiles.Log($"通道 {channel.Name} 实时流连接遇到排队或网络波动，等待 1.2s 后自动重试：{ex.Message}");
                await Task.Delay(1200);
                if (generation != _generation) return;
                response = await api.PostAsync<MediaSession>(path, new LiveRequest(channel.Id, streamType));
            }

            if (generation != _generation)
            {
                await DeleteSessionAsync(path, response.Id);
                return;
            }
            _session = response;
            ClientFiles.RecordActiveSession(path, response.Id);
            _lastRenew = DateTimeOffset.UtcNow.AddSeconds((Index % 8) * 3 - 12);
            _reconnectAttempts = 0;
            _nextReconnect = default;
            Apply(response);
            await PlayAsync(MediaUrl(response));
        }
        catch (Exception ex)
        {
            await StopCoreAsync();
            var isTimeout = ex is TaskCanceledException || ex is TimeoutException || ex.Message.Contains("HttpClient.Timeout") || ex.Message.Contains("canceled") || ex.Message.Contains("timed out");
            StateLabel = isTimeout ? "连接超时，请重试" : $"连接失败：{ex.Message}";
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
        if (!string.IsNullOrEmpty(AspectRatio) && AspectRatio != "fill" && AspectRatio != "default" && AspectRatio != "original")
        {
            _player.AspectRatio = AspectRatio;
        }
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
                IsPlaying = false;
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
                IsPlaying = true;
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
                if (IsPlaying || _session.State == "paused" || (_nextReconnect != default && _reconnectAttempts < 5))
                {
                    await api.SendAsync(HttpMethod.Post, $"{SessionPath}/{_session.Id}/renew");
                    _lastRenew = now.AddSeconds((Index % 6) * 2 - 5);
                }
                else if (_reconnectAttempts >= 5)
                {
                    await StopCoreAsync();
                    StateLabel = "重连失败，请重新打开通道。";
                    return;
                }
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
                if (!IsPlayback && _reconnectAttempts >= 3 && Channel is { } ch)
                {
                    try
                    {
                        var refreshed = await api.PostAsync<MediaSession>(SessionPath, new LiveRequest(ch.Id, StreamType));
                        if (generation == _generation)
                        {
                            _session = refreshed;
                            ClientFiles.RecordActiveSession(SessionPath, refreshed.Id);
                            _lastRenew = now;
                            Apply(refreshed);
                            await PlayAsync(MediaUrl(refreshed));
                            return;
                        }
                    }
                    catch { /* 申请新会话失败则继续回退尝试原地址 */ }
                }
                if (_session is not null)
                {
                    await PlayAsync(MediaUrl(_session));
                }
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
                if (refreshed is not null && generation == _generation)
                {
                    _session = refreshed;
                    Apply(refreshed);
                    await PlayAsync(MediaUrl(refreshed));
                }
            }
            else if (control.Action == "step")
            {
                _player?.NextFrame();
            }
        }
        finally { _gate.Release(); }
    }
    public void StepFrame() => _player?.NextFrame();
    public bool Capture(string path) => _player?.Capture(path) == true;
    public string MediaUrl(MediaSession session)
    {
        if (_rtspFailed) return HttpsTsUrl(session) ?? throw new InvalidOperationException("平台未提供有效的 HTTPS TS 回退地址。");
        // 压力测试可强制指定传输方式，用于对比不同封装／传输的真实 CPU 成本。
        if (StressMode.StressMediaOverride is { Length: > 0 } forced)
        {
            if (forced == "rtsp" && !string.IsNullOrWhiteSpace(session.RtspUrl)) return session.RtspUrl;
            if (forced == "ts") return HttpsTsUrl(session) ?? throw new InvalidOperationException("平台未提供有效的 HTTPS TS 地址。");
            if (forced == "flv" && Uri.TryCreate(session.HttpFlvUrl, UriKind.Absolute, out var forcedFlv) && forcedFlv.Scheme == "https") return forcedFlv.AbsoluteUri;
        }
        if (PreferRtsp && !string.IsNullOrWhiteSpace(session.RtspUrl)) return session.RtspUrl;
        // 实测结论：HTTPS-FLV 看似“占用更低”，但 16 路下 0/16 路播放位置推进（连得上却不解码），
        // 只适用于浏览器路径；原生播放必须优先 HTTPS-TS。见 docs/v2-桌面2.1.0界面复核.md 第四节。
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
        ActualResolution = session.ResolutionLabel;
        ActualBitrate = session.BitrateLabel;
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
        IsPlaying = false;
        ResetStreamPreference();
        await ReleasePlayerAsync();
        if (session is not null) await DeleteSessionAsync(path, session.Id);
    }
    public async Task SwitchStreamAsync(int streamType)
    {
        if (Channel is not { } channel || IsPlayback) return;
        if (StreamType == streamType && IsPlaying) return;
        var previousStreamType = StreamType;
        var range = (DateTimeOffset.Now, DateTimeOffset.Now);
        try
        {
            await StartAsync(channel, false, streamType, range.Item1, range.Item2);
        }
        catch (Exception ex)
        {
            ClientFiles.Log($"切换码流至 {streamType} 失败：{ex.Message}，尝试恢复原码流 {previousStreamType}");
            try
            {
                await Task.Delay(500);
                await StartAsync(channel, false, previousStreamType, range.Item1, range.Item2);
            }
            catch (Exception fallbackEx)
            {
                ClientFiles.Log($"恢复原码流亦失败：{fallbackEx.Message}");
            }
            throw;
        }
    }
    public void SetAspectRatio(string? ratio) => AspectRatio = ratio;
    public void ApplyDisplayRatio(string? ratio)
    {
        if (_player is not null) _player.AspectRatio = ratio;
    }

    /// <summary>
    /// 按显示档位套用码流：格子用子码流，单窗放大/全屏用主码流。只在档位真正变化时重建会话，
    /// 设备确认无子码流后不再重试，避免反复拆建解码器。
    /// </summary>
    public async Task EnsureStreamTierAsync(TileDisplayTier tier, bool preferSubStreamInGrid = true)
    {
        DisplayTier = tier;
        PreferSubStreamInGrid = preferSubStreamInGrid;
        if (IsPlayback || Channel is null) return;
        // 非当前显示格位（单窗放大时的后台窗口）保持活跃播放，不拆除会话，以便还原多分屏时秒级恢复无缝渲染。
        if (tier == TileDisplayTier.Hidden)
        {
            return;
        }
        // 先把之前排队的人工档位落地，再评估自动档位。
        if (_pendingStreamOverride is { } pending && StreamType != pending)
        {
            _pendingStreamOverride = null;
            await SwitchTierAsync(pending);
            if (StreamType == pending) return;
        }
        if (ManualStreamOverride is { } manual)
        {
            if (StreamType != manual) await SwitchTierAsync(manual);
            return;
        }
        var desired = DesiredStreamType;
        if (StreamType == desired && IsPlaying) return;
        if (desired == 2 && _subStreamUnavailable) return;
        await SwitchTierAsync(desired);
    }

    /// <summary>
    /// 设备级子码流可用性缓存。实测存在单个摄像机未配置子码流的情况（适配器会回退主码流），
    /// 若每格都先请求子码流再回退，每格要多付一次失败重试（约 1.2 秒）并多占一次设备通道。
    /// 这里按设备记录一次结果，后续格子直接请求主码流。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, bool> SubStreamAvailability = new();

    private static bool SubStreamKnownUnavailable(Channel? channel) =>
        channel is not null && SubStreamAvailability.TryGetValue(channel.DeviceId, out var available) && !available;

    /// <summary>
    /// 平台侧能力探测缓存（B1），按通道保存。平台确认子码流不存在时，客户端直接选主码流，
    /// 不必「先请求 → 失败 → 回退」。缓存未就绪时返回 false，行为与没有该能力时完全一致。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, bool> PlatformCapability = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> Probing = new();

    /// <summary>
    /// 尽力而为地探测子码流能力：同一通道只探测一次，失败不写缓存以便下次重试。
    /// 平台或适配器不支持该接口时静默跳过，不影响打开通道。
    /// </summary>
    public void RequestCapabilityProbe(IPlatformApi api)
    {
        if (Channel is not { } channel) return;
        if (PlatformCapability.ContainsKey(channel.Id) || !Probing.TryAdd(channel.Id, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var capability = await api.GetAsync<StreamCapability>($"channels/{channel.Id}/stream-probe?streamType=2");
                if (capability is null) Probing.TryRemove(channel.Id, out _);
                else PlatformCapability[channel.Id] = capability.Available;
            }
            catch (Exception ex)
            {
                Probing.TryRemove(channel.Id, out _);
                ClientFiles.Log($"通道 {channel.Name} 码流能力探测不可用，保持原有试错行为：{ex.Message}");
            }
        });
    }

    private static void RememberSubStream(Channel channel, bool available)
    {
        if (available) SubStreamAvailability.TryRemove(channel.DeviceId, out _);
        else SubStreamAvailability[channel.DeviceId] = false;
    }

    private async Task SwitchTierAsync(int streamType)
    {
        try
        {
            await SwitchStreamAsync(streamType);
            if (streamType == 2 && StreamType != 2)
            {
                // 适配器确认回退到主码流：标记后不再重复请求子码流。
                _subStreamUnavailable = true;
                if (Channel is { } channel) RememberSubStream(channel, false);
                ClientFiles.Log($"窗口 {Number} 所在设备未提供子码流，已保持主码流。");
            }
        }
        catch (Exception ex)
        {
            if (streamType == 2)
            {
                _subStreamUnavailable = true;
                if (Channel is { } channel) RememberSubStream(channel, false);
            }
            ClientFiles.Log($"窗口 {Number} 自动切换码流失败，保持当前码流：{ex.Message}");
        }
    }
    public string? QuickCapture(string saveDirectory)
    {
        if (_session is null) return null;
        try
        {
            Directory.CreateDirectory(saveDirectory);
            var safeChannelName = string.Join("_", (Channel?.DisplayName ?? $"Window_{Number}").Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{safeChannelName}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var fullPath = Path.Combine(saveDirectory, fileName);
            return Capture(fullPath) ? fullPath : null;
        }
        catch (Exception ex)
        {
            ClientFiles.Log($"抓图异常：{ex.Message}");
            return null;
        }
    }
    private async Task DeleteSessionAsync(string path, string id)
    {
        ClientFiles.RemoveActiveSession(id);
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

