using System.IO;
using LibVLCSharp.Shared;

namespace VideoPlatform.Desktop.Services;

public interface IVideoPlayer : IAsyncDisposable
{
    MediaPlayer? NativePlayer { get; }
    bool Muted { get; set; }
    event Action<string>? Failed;
    event Action? Connected;
    Task PlayAsync(string url, CancellationToken cancellationToken = default);
    Task StopAsync();
    void Pause(bool paused);
    bool Capture(string path);
}
public interface IPlayerFactory { IVideoPlayer Create(); }

public sealed class VlcPlayerFactory : IPlayerFactory, IDisposable
{
    private LibVLC? _vlc;
    public IVideoPlayer Create()
    {
        if (_vlc is null)
        {
            Core.Initialize();
            _vlc = new LibVLC("--quiet", "--no-video-title-show", "--no-osd", "--no-snapshot-preview");
            _vlc.Log += (_, args) =>
            {
                if (args.Level is LogLevel.Error && !args.Message.Contains("token=", StringComparison.OrdinalIgnoreCase) && !args.Message.Contains("://", StringComparison.Ordinal))
                    ClientFiles.Log($"原生播放器错误（{args.Module}）：{args.Message}");
            };
        }
        return new VlcVideoPlayer(_vlc);
    }
    public void Dispose() => _vlc?.Dispose();
}

public sealed class VlcVideoPlayer : IVideoPlayer
{
    private readonly LibVLC _vlc;
    public MediaPlayer? NativePlayer { get; private set; }
    public event Action<string>? Failed;
    public event Action? Connected;
    public VlcVideoPlayer(LibVLC vlc)
    {
        _vlc = vlc;
        NativePlayer = new MediaPlayer(vlc) { Mute = true, EnableHardwareDecoding = true };
        NativePlayer.EncounteredError += EncounteredError;
        NativePlayer.EndReached += EndReached;
        NativePlayer.Playing += Playing;
    }
    public bool Muted { get => NativePlayer?.Mute ?? true; set { if (NativePlayer is { } player) player.Mute = value; } }
    public Task PlayAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("rtsp" or "http" or "https")) throw new InvalidDataException("平台返回的媒体地址无效。");
        using var media = new Media(_vlc, uri);
        media.AddOption(":rtsp-tcp");
        media.AddOption(":network-caching=500");
        if (NativePlayer?.Play(media) != true) throw new InvalidOperationException("原生播放器无法打开视频流。");
        return Task.CompletedTask;
    }
    public void Pause(bool paused) => NativePlayer?.SetPause(paused);
    public bool Capture(string path) => NativePlayer?.TakeSnapshot(0, path, 0, 0) == true;
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
    private void Playing(object? sender, EventArgs e) => Connected?.Invoke();
}
