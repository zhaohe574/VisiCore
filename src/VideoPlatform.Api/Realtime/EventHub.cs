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
    private DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 定期清理：截断积压超过 5 分钟未投递的历史风暴事件，清理超过 24 小时的已完成记录
                if (DateTimeOffset.UtcNow - _lastCleanup > TimeSpan.FromMinutes(1))
                {
                    await db.ExecuteAsync("update outbox set delivered_at = now() where delivered_at is null and created_at < now() - interval '5 minutes'", ct: stoppingToken);
                    await db.ExecuteAsync("delete from outbox where delivered_at < now() - interval '24 hours'", ct: stoppingToken);
                    _lastCleanup = DateTimeOffset.UtcNow;
                }

                var pending = await db.QueryAsync("select * from outbox where delivered_at is null order by id limit 100", ct: stoppingToken);
                if (pending.Count > 0)
                {
                    var deliveredIds = new List<long>(pending.Count);
                    foreach (var item in pending)
                    {
                        var itemId = item.Id();
                        deliveredIds.Add(itemId);

                        foreach (var connection in connections.All)
                        {
                            if (item["userId"] is not null && item.Id("userId") != connection.UserId) continue;
                            var active = await db.OneAsync("select id from sessions where id=@id and revoked_at is null and expires_at>now()", new { id = connection.SessionId }, stoppingToken);
                            if (active is null)
                            {
                                await hub.Clients.Client(connection.ConnectionId).SendAsync("access.changed", new EventNotice(connection.UserId.ToString(), itemId, "access.changed"), stoppingToken);
                                connections.Remove(connection.ConnectionId);
                                continue;
                            }
                            var kind = item.Text("kind");
                            var resourceId = item.Text("resourceId");
                            if (kind == "asset.changed")
                            {
                                var parts = item.Text("resourceId").Split(':', 2);
                                var assetPermission = parts[0] switch
                                {
                                    "port" or "link" => "device_port.manage", "organization" => "area.read",
                                    _ => AssetQueryService.Permissions.GetValueOrDefault(parts[0])
                                };
                                if (assetPermission is null || !await access.HasPermissionAsync(connection.UserId, assetPermission, stoppingToken)) continue;
                                // 删除或移出范围后仍需清理旧画面，但刷新提示不得暴露越权编号。
                                if (parts.Length != 2 || !long.TryParse(parts[1], out var assetId)
                                    || !await CanReadAssetAsync(connection.UserId, parts[0], assetId, stoppingToken))
                                    resourceId = parts[0] + ":refresh";
                            }
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
                            await hub.Clients.Client(connection.ConnectionId).SendAsync(kind, new EventNotice(resourceId, itemId, kind), stoppingToken);
                        }
                    }

                    // 批量标记已投递，避免逐行 round-trip
                    if (deliveredIds.Count > 0)
                    {
                        await db.ExecuteAsync("update outbox set delivered_at = now() where id = any(@ids)", new { ids = deliveredIds.ToArray() }, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "实时事件分发失败，下次继续发送"); }
            try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task<bool> CanReadAssetAsync(long userId, string type, long id, CancellationToken ct)
    {
        if (type == "channel") return await access.CanChannelAsync(userId, id, ct);
        var sql = type switch
        {
            "computer" or "terminal-box" or "onu" or "network-device" or "virtual-machine" or "ip-pool"
                => $"select a.id from {AssetScope.From(type)} where a.id=@id and ({AssetScope.For(type)})",
            "port" => $"select p.id from device_ports p where p.id=@id and ({AssetScope.PortPredicate("p")})",
            "link" => $"select l.id from device_links l join device_ports p on p.id=l.local_port_id join device_ports peer on peer.id=l.peer_port_id where l.id=@id and ({AssetScope.PortPredicate("p")}) and ({AssetScope.PortPredicate("peer")})",
            "device" => $"select d.id from devices d where d.id=@id and exists(select 1 from {AccessService.ChannelFrom} where c.device_id=d.id and ({AccessService.ChannelPredicate}))",
            _ => null
        };
        return sql is not null && await db.OneAsync(sql, new { id, userId }, ct) is not null;
    }
}
