using Microsoft.AspNetCore.SignalR.Client;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public sealed class EventService(SessionService session) : IAsyncDisposable
{
    private HubConnection? _connection;
    public event Func<string, Task>? Changed;
    public event Action<string>? StateChanged;
    public async Task StartAsync(CancellationToken token = default)
    {
        await StopAsync();
        _connection = new HubConnectionBuilder().WithUrl(session.Server + "/hubs/v2/events", options =>
        {
            options.AccessTokenProvider = session.GetTokenAsync;
        }).WithAutomaticReconnect(new RetryPolicy()).Build();
        foreach (var name in new[] { "alarm.changed", "device.changed", "media.changed", "export.changed", "access.changed" })
            _connection.On<ResourceEvent>(name, async _ => await NotifyAsync(name));
        _connection.Reconnecting += _ => { StateChanged?.Invoke("事件连接重连中"); return Task.CompletedTask; };
        _connection.Reconnected += async _ => { StateChanged?.Invoke("事件已连接"); await NotifyAsync("reconnected"); };
        _connection.Closed += _ => { StateChanged?.Invoke("事件连接已断开"); return Task.CompletedTask; };
        await _connection.StartAsync(token);
        StateChanged?.Invoke("事件已连接");
    }
    private async Task NotifyAsync(string kind)
    {
        if (Changed is null) return;
        try { await Changed(kind); }
        catch (Exception ex) { ClientFiles.Log($"处理平台事件失败：{ex.Message}"); }
    }
    public async Task StopAsync()
    {
        var connection = _connection;
        _connection = null;
        if (connection is not null) await connection.DisposeAsync();
    }
    public async ValueTask DisposeAsync() => await StopAsync();
    private sealed class RetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => TimeSpan.FromSeconds(Math.Min(30, 1 + retryContext.PreviousRetryCount * 3));
    }
}
