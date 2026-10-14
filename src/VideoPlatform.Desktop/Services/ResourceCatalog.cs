using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Services;

public sealed record ResourceSnapshot(Channel[] Channels, Channel[] Favorites, LayoutDto[] Layouts, Organization? Organization);

public sealed class ResourceCatalog(IPlatformApi api)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<ResourceSnapshot> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var channels = new List<Channel>();
            for (var page = 1; ; ++page)
            {
                var result = await api.GetAsync<Page<Channel>>($"channels?page={page}&pageSize=200");
                if (result is null || result.Items.Length == 0) break;
                channels.AddRange(result.Items);
                if (channels.Count >= result.Total) break;
            }
            Channel[] favorites = [];
            try { favorites = await api.GetAsync<Channel[]>("favorites") ?? []; }
            catch (PlatformException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden) { }
            var layouts = await api.GetAsync<LayoutDto[]>("layouts") ?? [];
            Organization? organization = null;
            try { organization = await api.GetAsync<Organization>("organization"); }
            catch (PlatformException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden) { }
            return new([.. channels], favorites, layouts, organization);
        }
        finally { _gate.Release(); }
    }
}
