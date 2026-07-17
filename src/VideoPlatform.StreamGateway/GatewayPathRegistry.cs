using System.Collections.Concurrent;

namespace VideoPlatform.StreamGateway;

public sealed class GatewayPathRegistry
{
    private readonly ConcurrentDictionary<string, GatewayPathRegistration> paths = new(StringComparer.Ordinal);

    public bool IsAvailable(string pathName) =>
        paths.TryGetValue(pathName, out var registration) && registration.ClientReadable;

    public bool IsCurrent(string pathName, string fingerprint) =>
        paths.TryGetValue(pathName, out var current) && current.Fingerprint == fingerprint;

    public void MarkCurrent(string pathName, string fingerprint, bool clientReadable = true) =>
        paths[pathName] = new GatewayPathRegistration(fingerprint, clientReadable);

    public IReadOnlyCollection<string> SnapshotNames() => paths.Keys.ToArray();

    public void Remove(string pathName) => paths.TryRemove(pathName, out _);
}

public sealed record GatewayPathRegistration(string Fingerprint, bool ClientReadable);
