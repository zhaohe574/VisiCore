using System.Text.Json.Nodes;
using VideoPlatform.Contracts;

namespace VideoPlatform.Application;

public sealed record Actor(Guid SessionId, long UserId, string Username, string ClientType, DateTimeOffset ExpiresAt);
public interface IAccessService
{
    Task DemandAsync(Actor actor, string permission, CancellationToken ct = default);
    Task<JsonObject> ChannelAsync(Actor actor, long channelId, string permission, CancellationToken ct = default);
    Task<bool> CanChannelAsync(long userId, long channelId, CancellationToken ct = default);
}
public interface IDeviceAdapter
{
    Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default);
}
public interface ISettingsStore
{
    Task<PlatformSettings> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(PlatformSettings settings, CancellationToken ct = default);
}
