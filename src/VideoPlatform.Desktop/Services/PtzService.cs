using System.Net;
using System.Net.Http;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public sealed class PtzService(IPlatformApi api) : IAsyncDisposable
{
    private sealed record Lease(long ChannelId, CancellationTokenSource Lifetime);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, Lease> _leases = [];
    public event Action<string>? Failed;
    public event Action<Guid, string>? OwnerFailed;
    public static readonly string[] Commands = ["up", "down", "left", "right", "auto", "zoomIn", "zoomOut", "focusNear", "focusFar", "irisOpen", "irisClose"];

    public async Task StartAsync(long channelId, string command, int speed, Guid owner = default)
    {
        if (!Commands.Contains(command) || speed is < 1 or > 7) throw new ArgumentException("云台指令或速度无效。");
        await _gate.WaitAsync();
        try
        {
            DemandAvailable(channelId, owner);
            await StopCoreAsync(owner);
            var request = new PtzRequest(command, speed);
            await api.SendAsync(HttpMethod.Post, $"channels/{channelId}/ptz", request);
            var lease = new Lease(channelId, new());
            _leases[owner] = lease;
            _ = KeepAliveAsync(owner, lease, request);
        }
        catch (PlatformException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("此通道的云台正由其他值守员控制。", ex);
        }
        finally { _gate.Release(); }
    }

    private void DemandAvailable(long channelId, Guid owner)
    {
        if (_leases.Any(pair => pair.Key != owner && pair.Value.ChannelId == channelId))
            throw new InvalidOperationException("此通道的云台正由另一个工作区控制。请先在该工作区停止控制。");
    }

    private async Task KeepAliveAsync(Guid owner, Lease lease, PtzRequest request)
    {
        var token = lease.Lifetime.Token;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(token))
            {
                await _gate.WaitAsync(token);
                try
                {
                    if (!_leases.TryGetValue(owner, out var current) || !ReferenceEquals(current, lease)) return;
                    await api.SendAsync(HttpMethod.Post, $"channels/{lease.ChannelId}/ptz", request, token);
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await _gate.WaitAsync();
            try
            {
                if (_leases.TryGetValue(owner, out var current) && ReferenceEquals(current, lease)) await StopCoreAsync(owner);
            }
            finally { _gate.Release(); }
            ReportFailure(owner, $"云台保活中断，已请求停止：{ex.Message}");
        }
    }

    public async Task StopAsync(Guid owner = default)
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(owner); }
        finally { _gate.Release(); }
    }

    private async Task StopCoreAsync(Guid owner)
    {
        if (!_leases.Remove(owner, out var lease)) return;
        lease.Lifetime.Cancel();
        lease.Lifetime.Dispose();
        try { await api.SendAsync(HttpMethod.Post, $"channels/{lease.ChannelId}/ptz/stop"); }
        catch (Exception ex) { ReportFailure(owner, $"云台停止请求失败，服务端将在租约超时后停止：{ex.Message}"); }
    }

    private void ReportFailure(Guid owner, string message) { OwnerFailed?.Invoke(owner, message); Failed?.Invoke(message); }

    public async Task PresetAsync(long channelId, int preset, Guid owner = default)
    {
        if (preset is < 1 or > 300) throw new ArgumentException("预置位编号必须在 1 至 300 之间。");
        await _gate.WaitAsync();
        try
        {
            DemandAvailable(channelId, owner);
            await StopCoreAsync(owner);
            await api.SendAsync(HttpMethod.Post, $"channels/{channelId}/ptz/presets/{preset}");
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { foreach (var owner in _leases.Keys.ToArray()) await StopCoreAsync(owner); }
        finally { _gate.Release(); }
    }
}
