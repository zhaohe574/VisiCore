using System.Net.Http.Json;
using VideoPlatform.Core;

namespace VideoPlatform.OnvifEdgeWorker;

public sealed class OnvifEdgeControlPlaneClient(HttpClient httpClient, OnvifEdgeOptions options)
{
    public async Task<IReadOnlyList<WorkerRecorderAssignment>> GetAssignmentsAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/v1/device-worker/assignments");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WorkerRecorderAssignment>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<WorkerEdgeCommand>> ClaimCommandsAsync(CancellationToken cancellationToken)
    {
        var commandTypes = string.Join("&", new[]
        {
            EdgeCommandTypes.OnvifPtzControl,
            EdgeCommandTypes.OnvifRecordingSearch,
            EdgeCommandTypes.OnvifPlaybackRelayStart,
            EdgeCommandTypes.OnvifPlaybackRelayStop,
            EdgeCommandTypes.OnvifPlaybackRelayControl
        }.Select(item => $"commandTypes={Uri.EscapeDataString(item)}"));
        using var request = CreateRequest(HttpMethod.Post, $"api/v1/device-worker/commands/claim?limit=1&{commandTypes}");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WorkerEdgeCommand>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task CompleteCommandAsync(
        Guid commandId,
        WorkerEdgeCommandCompletion completion,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"api/v1/device-worker/commands/{commandId:N}/complete");
        request.Content = JsonContent.Create(completion);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportOperationStatusesAsync(
        WorkerOperationStatusReport report,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, "api/v1/device-worker/operation-statuses");
        request.Content = JsonContent.Create(report);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> CanStartPlaybackRelayAsync(
        WorkerEdgeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandType != EdgeCommandTypes.OnvifPlaybackRelayStart || command.AggregateId == Guid.Empty ||
            command.Id == Guid.Empty || string.IsNullOrWhiteSpace(command.DeliveryToken))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "ONVIF 回放启动授权参数无效。 ");
        }
        using var request = CreateRequest(HttpMethod.Post, $"api/v1/device-worker/playback-relays/{command.AggregateId:N}/authorize-start");
        request.Headers.TryAddWithoutValidation("X-Edge-Command-Id", command.Id.ToString("N"));
        request.Headers.TryAddWithoutValidation("X-Edge-Command-Delivery", command.DeliveryToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> CanContinuePlaybackRelayAsync(
        Guid playbackSessionId,
        CancellationToken cancellationToken)
    {
        if (playbackSessionId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackSessionId), "ONVIF 回放持续授权参数无效。 ");
        }
        using var request = CreateRequest(HttpMethod.Post, $"api/v1/device-worker/playback-relays/{playbackSessionId:N}/authorize-continue");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();
        return true;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Device-Worker-Token", options.AccessToken);
        return request;
    }
}
