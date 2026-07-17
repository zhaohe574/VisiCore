using System.Net;
using System.Net.Http.Json;
using VideoPlatform.Core;

namespace VideoPlatform.StreamGateway;

public sealed class GatewayControlPlaneClient(HttpClient httpClient, GatewayOptions options)
{
    public async Task<IReadOnlyList<WorkerRecorderAssignment>> GetAssignmentsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/device-worker/assignments");
        request.Headers.TryAddWithoutValidation("X-Device-Worker-Token", options.DeviceWorkerAccessToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WorkerRecorderAssignment>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<GatewayStreamSession?> ConsumeTicketAsync(
        Guid sessionId,
        string ticket,
        CancellationToken cancellationToken)
    {
        using var request = CreateGatewayRequest(
            HttpMethod.Post,
            $"api/v1/stream-gateway/sessions/{sessionId:N}/consume");
        request.Content = JsonContent.Create(new GatewayTicketConsumeRequest(ticket, options.GatewayName));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GatewayStreamSession>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayStreamSession>> InspectSessionsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken)
    {
        using var request = CreateGatewayRequest(HttpMethod.Post, "api/v1/stream-gateway/sessions/status");
        request.Content = JsonContent.Create(new GatewaySessionStatusRequest(sessionIds));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<GatewayStreamSession>>(cancellationToken: cancellationToken) ?? [];
    }

    private HttpRequestMessage CreateGatewayRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Stream-Gateway-Token", options.CenterControlToken);
        return request;
    }
}

public sealed record GatewayTicketConsumeRequest(string Ticket, string GatewayName);
public sealed record GatewaySessionStatusRequest(IReadOnlyCollection<Guid> SessionIds);
public sealed record GatewayStreamSession(
    Guid Id,
    string StreamKey,
    Guid CameraId,
    CameraPermission Operation,
    string Profile,
    DateTimeOffset LeaseExpiresAt,
    bool Active,
    string? RevocationReason);
