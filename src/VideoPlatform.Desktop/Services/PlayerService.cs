using System.IO;
using LibVLCSharp.Shared;

namespace VideoPlatform.Desktop.Services;

public interface IVideoPlayer : IAsyncDisposable
{
    MediaPlayer? NativePlayer { get; }
    bool Muted { get; set; }
    string? AspectRatio { get => null; set { } }
    /// <summary>当前播放位置（毫秒）；用于确认“低占用”确实在推进解码而不是静默失败。读取失败返回 null。</summary>
    long? PlaybackPositionMs { get => null; }
    event Action<string>? Failed;
    event Action? Connected;
    Task PlayAsync(string url, CancellationToken cancellationToken = default);
    Task StopAsync();
    void Pause(bool paused);
    bool Capture(string path);
    void NextFrame() { }
}

public interface IPlayerFactory { IVideoPlayer Create(); }

public interface IConfigurablePlayerFactory : IPlayerFactory
{
    /// <summary>套用实时播放参数（硬解与网络缓存）；已有播放器在下次创建时生效。</summary>
    void Configure(PlayerOptions options);
}

/// <summary>原生播放器参数：硬件解码用 d3d11va，网络缓存毫秒数影响首帧延迟与重传放大。</summary>
public sealed record PlayerOptions(bool HardwareDecoding = true, int NetworkCachingMs = 800, bool DisableClockSync = false)
{
    public int EffectiveCachingMs => Math.Clamp(NetworkCachingMs, 200, 5000);
}

public sealed class VlcPlayerFactory : IConfigurablePlayerFactory, IDisposable
{
    private readonly object _sync = new();
    private LibVLC? _vlc;
    private PlayerOptions _options = new();

    public void Configure(PlayerOptions options)
    {
        lock (_sync)
        {
            if (_vlc is not null && options.HardwareDecoding != _options.HardwareDecoding) { _vlc.Dispose(); _vlc = null; }
            _options = options;
        }
    }

    public IVideoPlayer Create()
    {
        LibVLC vlc;
        lock (_sync)
        {
            _vlc ??= BuildEngine(_options);
            vlc = _vlc;
        }
        return new VlcVideoPlayer(vlc, _options);
    }

    private static LibVLC BuildEngine(PlayerOptions options)
    {
        Core.Initialize();
        var arguments = new List<string> {
            "--quiet",
            "--no-video-title-show",
            "--no-osd",
            "--no-snapshot-preview",
            "--no-mouse-events",
            "--http-reconnect",
            // 方案 E 配套：软解线程数截断为 1，杜绝显卡 NVDEC 硬解配额耗尽回退软解时拉起数百个 worker 线程导致 CPU 100%
            "--avcodec-threads=1"
        };
        // 显式请求 D3D11 硬件解码；驱动或编码不支持时 libVLC 会自行回退。
        if (options.HardwareDecoding) arguments.Add("--avcodec-hw=d3d11va");
        var vlc = new LibVLC([.. arguments]);
        vlc.Log += (_, args) =>
        {
            if (args.Level is LogLevel.Error && !args.Message.Contains("token=", StringComparison.OrdinalIgnoreCase) && !args.Message.Contains("://", StringComparison.Ordinal))
                ClientFiles.Log($"原生播放器错误（{args.Module}）：{args.Message}");
        };
        return vlc;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _vlc?.Dispose();
            _vlc = null;
        }
    }
}

public sealed class VlcVideoPlayer : IVideoPlayer
{
    private readonly LibVLC _vlc;
    private readonly PlayerOptions _options;
    private bool _muted = true;
    public MediaPlayer? NativePlayer { get; private set; }
    public event Action<string>? Failed;
    public event Action? Connected;
    public VlcVideoPlayer(LibVLC vlc, PlayerOptions? options = null)
    {
        _vlc = vlc;
        _options = options ?? new PlayerOptions();
        NativePlayer = new MediaPlayer(vlc) { Mute = true, EnableHardwareDecoding = _options.HardwareDecoding };
        NativePlayer.EncounteredError += EncounteredError;
        NativePlayer.EndReached += EndReached;
        NativePlayer.Playing += Playing;
    }
    private string? _aspectRatio;
    public string? AspectRatio
    {
        get => NativePlayer?.AspectRatio ?? _aspectRatio;
        set { _aspectRatio = value; if (NativePlayer is { } player) player.AspectRatio = value; }
    }
    public bool Muted
    {
        get => NativePlayer?.Mute ?? _muted;
        set
        {
            _muted = value;
            if (NativePlayer is { } player)
            {
                player.Mute = value;
                if (value)
                {
                    try { player.SetAudioTrack(-1); } catch { }
                }
                else
                {
                    try { if (player.AudioTrack == -1) player.SetAudioTrack(1); } catch { }
                }
            }
        }
    }
    public long? PlaybackPositionMs
    {
        get
        {
            try
            {
                var player = NativePlayer;
                if (player is null || !player.IsPlaying) return null;
                var time = player.Time;
                return time < 0 ? null : time;
            }
            catch (Exception) { return null; }
        }
    }
    public Task PlayAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("rtsp" or "http" or "https")) throw new InvalidDataException("平台返回的媒体地址无效。");
        using var media = new Media(_vlc, uri);
        var caching = _options.EffectiveCachingMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        media.AddOption(":rtsp-tcp");
        media.AddOption($":network-caching={caching}");
        media.AddOption(":http-reconnect");
        media.AddOption(":http-continuous");
        // 方案 E 配套：静音流传入 :no-audio，从根源切断 16 路无用音频解复用、解码与时钟处理
        if (_muted)
        {
            media.AddOption(":no-audio");
        }
        // 实测：多路并发时关闭时钟抖动抑制与时钟同步会显著抬高线程与 CPU（每路约 30 个线程），
        // 因此默认不再设置 clock-jitter／clock-synchro，仅保留可选开关用于对比排查。
        if (_options.DisableClockSync)
        {
            media.AddOption(":clock-jitter=0");
            media.AddOption(":clock-synchro=0");
        }
        if (NativePlayer?.Play(media) != true) throw new InvalidOperationException("原生播放器无法打开视频流。");
        if (_muted && NativePlayer is not null)
        {
            try { NativePlayer.SetAudioTrack(-1); } catch { }
        }
        if (!string.IsNullOrEmpty(_aspectRatio) && NativePlayer is not null) NativePlayer.AspectRatio = _aspectRatio;
        return Task.CompletedTask;
    }
    public void Pause(bool paused) => NativePlayer?.SetPause(paused);
    public bool Capture(string path) => NativePlayer?.TakeSnapshot(0, path, 0, 0) == true;
    public void NextFrame() => NativePlayer?.NextFrame();
    public async Task StopAsync()
    {
        var player = NativePlayer;
        if (player is not null) await Task.Run(player.Stop);
    }
    public async ValueTask DisposeAsync()
    {
        var player = NativePlayer;
        NativePlayer = null;
        if (player is null) return;
        player.EncounteredError -= EncounteredError;
        player.EndReached -= EndReached;
        player.Playing -= Playing;
        // VLC 停止会等待原生解码线程，不能占用 WPF 调度线程。
        await Task.Run(() => { player.Stop(); player.Dispose(); });
    }
    private void EncounteredError(object? sender, EventArgs e) => Failed?.Invoke("视频连接中断，正在重新连接。");
    private void EndReached(object? sender, EventArgs e) => Failed?.Invoke("视频流已结束，正在核对会话状态。");
    private void Playing(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_aspectRatio) && NativePlayer is not null)
        {
            NativePlayer.AspectRatio = _aspectRatio;
        }
        Connected?.Invoke();
    }
}
