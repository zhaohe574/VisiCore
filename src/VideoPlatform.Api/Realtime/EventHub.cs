using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VideoPlatform.Contracts;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Realtime;

public sealed record EventConnection(string ConnectionId, Guid SessionId, long UserId);
public sealed class EventConnections
{
    private readonly ConcurrentDictionary<string, EventConnection> _connections = new();
    public IReadOnlyCollection<EventConnection> All => _connections.Values.ToArray();
    public void Add(EventConnection connection) => _connections[connection.ConnectionId] = connection;
    public void Remove(string id) => _connections.TryRemove(id, out _);
}

[Authorize]
public sealed class EventHub(EventConnections connections) : Hub
{
    public override Task OnConnectedAsync()
    {
        var actor = ApiSupport.Actor(Context.GetHttpContext()!);
        connections.Add(new EventConnection(Context.ConnectionId, actor.SessionId, actor.UserId));
        return base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class EventDispatcher(Database db, AccessService access, EventConnections connections, IHubContext<EventHub> hub, ILogger<EventDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await db.QueryAsync("select * from outbox where delivered_at is null order by id limit 100", ct: stoppingToken);
                foreach (var item in pending)
                {
                    foreach (var connection in connections.All)
                    {
                        if (item["userId"] is not null && item.Id("userId") != connection.UserId) continue;
                        var active = await db.OneAsync("select id from sessions where id=@id and revoked_at is null and expires_at>now()", new { id = connection.SessionId }, stoppingToken);
                        if (active is null)
                        {
                            await hub.Clients.Client(connection.ConnectionId).SendAsync("access.changed", new EventNotice(connection.UserId.ToString(), item.Id(), "access.changed"), stoppingToken);
                            connections.Remove(connection.ConnectionId);
                            continue;
                        }
                        var kind = item.Text("kind");
                        var permission = kind switch { "alarm.changed" => "alarm.read", "device.changed" => "device.read", "export.changed" => "export.create", _ => null };
                        if (permission is not null && !await access.HasPermissionAsync(connection.UserId, permission, stoppingToken)) continue;
                        if (item["channelId"] is not null && !await access.CanChannelAsync(connection.UserId, item.Id("channelId"), stoppingToken)) continue;
                        if (item["channelId"] is null && kind == "alarm.changed")
                        {
                            var global = await db.OneAsync("select id from users where id=@id and (all_channels or exists(select 1 from user_roles ur join roles r on r.id=ur.role_id where ur.user_id=@id and r.status='active' and (r.all_channels or r.code='admin')))", new { id = connection.UserId }, stoppingToken);
                            if (global is null) continue;
                        }
                        if (kind == "device.changed" && item["deviceId"] is not null && !await access.HasPermissionAsync(connection.UserId, "device.manage", stoppingToken))
                        {
                            var visible = await db.OneAsync($"select c.id from {AccessService.ChannelFrom} where c.device_id=@deviceId and ({AccessService.ChannelPredicate}) limit 1", new { deviceId = item.Id("deviceId"), userId = connection.UserId }, stoppingToken);
                            if (visible is null) continue;
                        }
                        await hub.Clients.Client(connection.ConnectionId).SendAsync(kind, new EventNotice(item.Text("resourceId"), item.Id(), kind), stoppingToken);
                    }
                    await db.ExecuteAsync("update outbox set delivered_at=now() where id=@id", new { id = item.Id() }, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "实时事件分发失败，下次继续发送"); }
            try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
