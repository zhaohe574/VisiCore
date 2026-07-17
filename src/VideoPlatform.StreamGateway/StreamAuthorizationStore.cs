using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace VideoPlatform.StreamGateway;

public sealed class StreamAuthorizationStore(
    GatewayControlPlaneClient controlPlaneClient,
    GatewayPathRegistry pathRegistry,
    IStreamSessionLifecycle? sessionLifecycle = null)
{
    private readonly IStreamSessionLifecycle sessionLifecycle = sessionLifecycle ?? NoopStreamSessionLifecycle.Instance;
    private readonly ConcurrentDictionary<Guid, AuthorizedGatewaySession> sessions = new();
    private readonly ConcurrentDictionary<string, Guid> connectionSessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] sessionGates = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public int Count => sessions.Count;

    public Task<bool> AuthorizeProxyAsync(
        string streamKey,
        Guid sessionId,
        string ticket,
        string connectionId,
        CancellationToken cancellationToken) =>
        AuthorizeCoreAsync(
            new MediaMtxAuthRequest(
                null,
                null,
                null,
                null,
                "read",
                streamKey,
                "hls",
                connectionId,
                $"sessionId={sessionId:N}&ticket={ticket}",
                null),
            cancellationToken,
            requireTicketMatch: true);

    public Task<bool> AuthorizeAsync(MediaMtxAuthRequest request, CancellationToken cancellationToken) =>
        AuthorizeCoreAsync(request, cancellationToken, requireTicketMatch: false);

    private async Task<bool> AuthorizeCoreAsync(
        MediaMtxAuthRequest request,
        CancellationToken cancellationToken,
        bool requireTicketMatch)
    {
        if (!string.Equals(request.Action, "read", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Protocol, "hls", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Id) ||
            (!pathRegistry.IsAvailable(request.Path) && !IsPlaybackRelayPath(request.Path)))
        {
            return false;
        }

        string? requestedTicketHash = null;
        if (requireTicketMatch && !TryGetTicketHash(request.Query, out requestedTicketHash))
        {
            return false;
        }
        if (connectionSessions.TryGetValue(request.Id, out var mappedSessionId) &&
            sessions.TryGetValue(mappedSessionId, out var mappedSession) &&
            !mappedSession.IsRevoked &&
            mappedSession.LeaseExpiresAt > DateTimeOffset.UtcNow &&
            mappedSession.PathName == request.Path &&
            mappedSession.IsCurrentConnection(request.Id) &&
            (!requireTicketMatch || FixedTimeEquals(mappedSession.TicketHash, requestedTicketHash!)))
        {
            return true;
        }

        if (!TryGetTicket(request.Query, out var sessionId, out var ticket))
        {
            return false;
        }
        var ticketHash = requestedTicketHash ?? HashTicket(ticket);
        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (sessions.TryGetValue(sessionId, out var cached) &&
                !cached.IsRevoked && cached.LeaseExpiresAt > DateTimeOffset.UtcNow &&
                cached.PathName == request.Path &&
                FixedTimeEquals(cached.TicketHash, ticketHash))
            {
                return cached.IsCurrentConnection(request.Id);
            }

            var consumed = await controlPlaneClient.ConsumeTicketAsync(sessionId, ticket, cancellationToken);
            if (consumed is null || !consumed.Active || consumed.LeaseExpiresAt <= DateTimeOffset.UtcNow ||
                consumed.StreamKey != request.Path)
            {
                return false;
            }

            var created = false;
            if (!sessions.TryGetValue(sessionId, out var authorized))
            {
                authorized = new AuthorizedGatewaySession(sessionId, request.Path, ticketHash, consumed.LeaseExpiresAt);
                sessions[sessionId] = authorized;
                created = true;
            }
            if (authorized.IsRevoked || authorized.PathName != request.Path)
            {
                return false;
            }

            var staleConnectionIds = authorized.ConnectionIds.Keys
                .Where(connectionId => connectionId != request.Id)
                .ToList();
            foreach (var connectionId in staleConnectionIds)
            {
                authorized.ConnectionIds.TryRemove(connectionId, out _);
                connectionSessions.TryRemove(connectionId, out _);
            }

            authorized.TicketHash = ticketHash;
            authorized.LeaseExpiresAt = consumed.LeaseExpiresAt;
            authorized.ConnectionIds.TryAdd(request.Id, 0);
            authorized.SetCurrentConnection(request.Id);
            connectionSessions[request.Id] = sessionId;
            if (created)
            {
                this.sessionLifecycle.Attach(sessionId, request.Path);
            }
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public IReadOnlyList<Guid> SnapshotSessionIds() => sessions.Keys.ToList();

    public void UpdateLease(Guid sessionId, DateTimeOffset leaseExpiresAt)
    {
        if (sessions.TryGetValue(sessionId, out var session) && !session.IsRevoked)
        {
            session.LeaseExpiresAt = leaseExpiresAt;
        }
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                return;
            }

            session.MarkRevoked();
            foreach (var connectionId in session.ConnectionIds.Keys)
            {
                session.ConnectionIds.TryRemove(connectionId, out _);
                connectionSessions.TryRemove(connectionId, out _);
            }
            if (sessions.TryRemove(sessionId, out _))
            {
                this.sessionLifecycle.Detach(sessionId);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetSessionGate(Guid sessionId) =>
        sessionGates[(int)((uint)sessionId.GetHashCode() % (uint)sessionGates.Length)];

    private static bool IsPlaybackRelayPath(string path) =>
        path.Length == "playback/".Length + 32 &&
        path.StartsWith("playback/", StringComparison.Ordinal) &&
        Guid.TryParseExact(path["playback/".Length..], "N", out _);

    private static string HashTicket(string ticket) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ticket)));

    private static bool TryGetTicketHash(string? rawQuery, out string ticketHash)
    {
        ticketHash = string.Empty;
        if (!TryGetTicket(rawQuery, out _, out var ticket))
        {
            return false;
        }
        ticketHash = HashTicket(ticket);
        return true;
    }

    private static bool TryGetTicket(string? rawQuery, out Guid sessionId, out string ticket)
    {
        sessionId = Guid.Empty;
        ticket = string.Empty;
        var value = rawQuery ?? string.Empty;
        var query = QueryHelpers.ParseQuery(value.StartsWith("?", StringComparison.Ordinal) ? value : "?" + value);
        if (!query.TryGetValue("sessionId", out var sessionValues) ||
            !Guid.TryParse(sessionValues.ToString(), out sessionId) ||
            !query.TryGetValue("ticket", out var ticketValues))
        {
            return false;
        }
        ticket = ticketValues.ToString();
        return ticket.Length is >= 32 and <= 256;
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}

internal sealed class NoopStreamSessionLifecycle : IStreamSessionLifecycle
{
    public static NoopStreamSessionLifecycle Instance { get; } = new();

    public void Attach(Guid sessionId, string pathName)
    {
    }

    public void Detach(Guid sessionId)
    {
    }
}

public sealed class AuthorizedGatewaySession(
    Guid sessionId,
    string pathName,
    string ticketHash,
    DateTimeOffset leaseExpiresAt)
{
    private long leaseExpiresAtUtcTicks = leaseExpiresAt.UtcDateTime.Ticks;
    private string? currentConnectionId;
    private int revoked;

    public Guid SessionId { get; } = sessionId;
    public string PathName { get; } = pathName;
    public string TicketHash { get; set; } = ticketHash;
    public DateTimeOffset LeaseExpiresAt
    {
        get => new(Interlocked.Read(ref leaseExpiresAtUtcTicks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref leaseExpiresAtUtcTicks, value.UtcDateTime.Ticks);
    }
    public bool IsRevoked => Volatile.Read(ref revoked) == 1;
    public ConcurrentDictionary<string, byte> ConnectionIds { get; } = new(StringComparer.Ordinal);

    public bool IsCurrentConnection(string connectionId) =>
        string.Equals(Volatile.Read(ref currentConnectionId), connectionId, StringComparison.Ordinal);

    public void SetCurrentConnection(string connectionId) =>
        Volatile.Write(ref currentConnectionId, connectionId);

    public void MarkRevoked() => Interlocked.Exchange(ref revoked, 1);
}

public sealed record MediaMtxAuthRequest(
    string? User,
    string? Password,
    string? Token,
    string? Ip,
    string? Action,
    string? Path,
    string? Protocol,
    string? Id,
    string? Query,
    string? UserAgent);
