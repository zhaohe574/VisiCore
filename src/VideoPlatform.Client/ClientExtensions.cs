using VideoPlatform.Client.Generated;

namespace VideoPlatform.Client;

public static class ClientExtensions
{
    // NSwag 将混合响应中的 204 视为异常，此封装表达“尚无发布版本”的可空结果。
    public static async Task<LatestReleaseDto?> GetLatestReleaseOrDefaultAsync(this IVideoPlatformClient client,
        string? currentVersion = null, PackageType? packageType = null, CancellationToken cancellationToken = default)
    {
        try { return await client.GetLatestReleaseAsync(currentVersion, packageType, cancellationToken); }
        catch (ApiException error) when (error.StatusCode == 204) { return null; }
    }
}
