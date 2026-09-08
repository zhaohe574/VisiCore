using System.Net;
using System.Net.Http;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public sealed class PtzService(IPlatformApi api) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _keepAlive;
    private long? _channel;
    public event Action<string>? Failed;
    public static readonly string[] Commands = ["up", "down", "left", "right", "auto", "zoomIn", "zoomOut", "focusNear", "focusFar", "irisOpen", "irisClose"];

    public async Task StartAsync(long channelId, string command, int speed)
    {
        if (!Commands.Contains(command) || speed is < 1 or > 7) throw new ArgumentException("云台指令或速度无效。");
        await StopAsync();
        await _gate.WaitAsync();
        try
        {
            var request = new PtzRequest(command, speed);
            await api.SendAsync(HttpMethod.Post, $"channels/{channelId}/ptz", request);
            _channel = channelId;
            _keepAlive = new();
            _ = KeepAliveAsync(channelId, request, _keepAlive.Token);
        }
        catch (PlatformException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("此通道的云台正由其他值守员控制。", ex);
        }
        finally { _gate.Release(); }
    }

    private async Task KeepAliveAsync(long channelId, PtzRequest request, CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(token))
            {
                await _gate.WaitAsync(token);
                try
                {
                    if (_channel != channelId || token.IsCancellationRequested) return;
                    await api.SendAsync(HttpMethod.Post, $"channels/{channelId}/ptz", request, token);
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await StopAsync();
            Failed?.Invoke($"云台保活中断，已请求停止：{ex.Message}");
        }
    }
    public async Task StopAsync()
    {
        _keepAlive?.Cancel();
        await _gate.WaitAsync();
        try
        {
            _keepAlive?.Dispose();
            _keepAlive = null;
            var channel = _channel;
            _channel = null;
            if (channel is null) return;
            try { await api.SendAsync(HttpMethod.Post, $"channels/{channel}/ptz/stop"); }
            catch (Exception ex) { Failed?.Invoke($"云台停止请求失败，服务端将在租约超时后停止：{ex.Message}"); }
        }
        finally { _gate.Release(); }
    }
    public async Task PresetAsync(long channelId, int preset)
    {
        if (preset is < 1 or > 300) throw new ArgumentException("预置位编号必须在 1 至 300 之间。");
        await StopAsync();
        await api.SendAsync(HttpMethod.Post, $"channels/{channelId}/ptz/presets/{preset}");
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
