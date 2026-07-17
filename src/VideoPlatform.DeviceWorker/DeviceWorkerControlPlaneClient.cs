using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using VideoPlatform.Core;

namespace VideoPlatform.DeviceWorker;

public sealed class DeviceWorkerControlPlaneClient(
    HttpClient httpClient,
    IOptions<ControlPlaneOptions> options)
{
    public async Task<IReadOnlyList<WorkerRecorderAssignment>> GetAssignmentsAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/v1/device-worker/assignments");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WorkerRecorderAssignment>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task ReportInventoryAsync(WorkerInventoryReport report, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/v1/device-worker/inventory");
        request.Content = JsonContent.Create(report);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportHealthAsync(WorkerHealthReport report, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/v1/device-worker/health");
        request.Content = JsonContent.Create(report);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportClockAsync(WorkerClockReport report, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/v1/device-worker/clock");
        request.Content = JsonContent.Create(report);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUri)
    {
        var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Add("X-Device-Worker-Token", options.Value.AccessToken);
        return request;
    }
}

public sealed class ControlPlaneOptions
{
    public string? BaseUri { get; init; }
    public string? AccessToken { get; init; }
    public bool AllowInsecureHttpForDevelopment { get; init; }

    public bool TryGetBaseUri(out Uri uri, out string validationError)
    {
        if (string.IsNullOrWhiteSpace(BaseUri) || !Uri.TryCreate(BaseUri, UriKind.Absolute, out var configuredUri))
        {
            uri = null!;
            validationError = "必须配置 ControlPlane:BaseUri。";
            return false;
        }
        if (!configuredUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !(AllowInsecureHttpForDevelopment &&
              configuredUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
              configuredUri.IsLoopback))
        {
            uri = null!;
            validationError = "中心 API 必须使用 HTTPS；仅回环开发地址可显式允许 HTTP。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(AccessToken) || AccessToken.Length < 32)
        {
            uri = null!;
            validationError = "必须配置至少 32 位的 Device Worker 访问令牌。";
            return false;
        }

        uri = new Uri(configuredUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        validationError = string.Empty;
        return true;
    }
}
