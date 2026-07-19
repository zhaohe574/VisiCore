using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VisiCore.StreamGateway;

public interface IMediaMtxClient
{
    Task ApplyPullPathAsync(string pathName, Uri sourceUri, CancellationToken cancellationToken);
    Task ApplyPublisherPathAsync(string pathName, CancellationToken cancellationToken);
    Task<bool> IsPathReadyAsync(string pathName, CancellationToken cancellationToken);
    Task RemovePathAsync(string pathName, CancellationToken cancellationToken);
}

public sealed class MediaMtxClient(HttpClient httpClient, MediaMtxOptions options, GatewayOptions gatewayOptions) : IMediaMtxClient
{
    public async Task ApplyPathAsync(
        string pathName,
        Uri sourceUri,
        CancellationToken cancellationToken) =>
        await ApplyPullPathAsync(pathName, sourceUri, cancellationToken);

    public async Task ApplyPullPathAsync(
        string pathName,
        Uri sourceUri,
        CancellationToken cancellationToken)
    {
        var payload = new MediaMtxPathConfiguration(
            sourceUri.AbsoluteUri,
            true,
            options.SourceOnDemandStartTimeout,
            options.SourceOnDemandCloseAfter,
            gatewayOptions.MaxReadersPerPath,
            "tcp");
        await ApplyConfigurationAsync(pathName, payload, cancellationToken);
    }

    public async Task ApplyPublisherPathAsync(
        string pathName,
        CancellationToken cancellationToken)
    {
        var payload = new MediaMtxPublisherPathConfiguration(
            "publisher",
            false,
            gatewayOptions.MaxReadersPerPath,
            false);
        await ApplyConfigurationAsync(pathName, payload, cancellationToken);
    }

    public async Task<bool> IsPathReadyAsync(string pathName, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"v3/paths/get/{Uri.EscapeDataString(pathName)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("ready", out var ready) && ready.GetBoolean();
    }

    private async Task ApplyConfigurationAsync<TPayload>(
        string pathName,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var escapedPath = Uri.EscapeDataString(pathName);
        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"v3/config/paths/patch/{escapedPath}")
        {
            Content = JsonContent.Create(payload)
        };
        using var patchResponse = await httpClient.SendAsync(patchRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (patchResponse.IsSuccessStatusCode)
        {
            return;
        }
        if (patchResponse.StatusCode != HttpStatusCode.NotFound)
        {
            throw new HttpRequestException($"MediaMTX 更新路径 {pathName} 失败，HTTP {(int)patchResponse.StatusCode}。");
        }

        using var addRequest = new HttpRequestMessage(HttpMethod.Post, $"v3/config/paths/add/{escapedPath}")
        {
            Content = JsonContent.Create(payload)
        };
        using var addResponse = await httpClient.SendAsync(addRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!addResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"MediaMTX 新增路径 {pathName} 失败，HTTP {(int)addResponse.StatusCode}。");
        }
    }

    public async Task RemovePathAsync(string pathName, CancellationToken cancellationToken)
    {
        using var response = await httpClient.DeleteAsync(
            $"v3/config/paths/delete/{Uri.EscapeDataString(pathName)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new HttpRequestException($"MediaMTX 删除路径 {pathName} 失败，HTTP {(int)response.StatusCode}。");
        }
    }

}

public sealed record MediaMtxPathConfiguration(
    string Source,
    bool SourceOnDemand,
    string SourceOnDemandStartTimeout,
    string SourceOnDemandCloseAfter,
    int MaxReaders,
    string RtspTransport);

public sealed record MediaMtxPublisherPathConfiguration(
    string Source,
    bool SourceOnDemand,
    int MaxReaders,
    bool OverridePublisher);
