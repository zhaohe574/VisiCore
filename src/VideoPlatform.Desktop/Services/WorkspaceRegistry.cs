using System.IO;
using System.Text.Json;
using System.Windows;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.ViewModels;
using VideoPlatform.Desktop.Views;

namespace VideoPlatform.Desktop.Services;

public sealed class WorkspaceRegistry(WorkspaceViewModel primary)
{
    private readonly List<WorkspaceViewModel> _items = [primary];
    private readonly Dictionary<WorkspaceViewModel, WorkspaceWindow> _windows = [];
    private string? _profileKey;
    private bool _closing;
    public IReadOnlyList<WorkspaceViewModel> Items => _items.ToArray();
    public WorkspaceViewModel Primary => primary;
    public event Action<WorkspaceViewModel>? Created;

    public async Task RefreshAsync()
    {
        var items = Items;
        var snapshot = await primary.Catalog.LoadAsync();
        foreach (var workspace in items.Where(_items.Contains)) await workspace.ApplyResourcesAsync(snapshot);
    }

    public async Task SetAccessAsync(User? user)
    {
        foreach (var workspace in Items) await workspace.SetAccessAsync(user);
    }

    public async Task OpenAsync(User user, bool playback)
    {
        var workspace = primary.CreateSibling();
        await workspace.SetAccessAsync(user);
        CopyPreferences(workspace);
        await workspace.ApplyResourcesAsync(await primary.Catalog.LoadAsync());
        await workspace.SetModeAsync(playback);
        Show(workspace);
    }

    private void CopyPreferences(WorkspaceViewModel workspace)
    {
        workspace.PreferSubStreamInGrid = primary.PreferSubStreamInGrid;
        foreach (var tile in workspace.Tiles) tile.PreferRtsp = primary.Tiles[0].PreferRtsp;
    }

    private void Show(WorkspaceViewModel workspace, WorkspacePlacement? placement = null)
    {
        _items.Add(workspace);
        var window = new WorkspaceWindow(workspace, this, placement);
        _windows.Add(workspace, window);
        Created?.Invoke(workspace);
        window.Show();
    }

    public async Task RestoreConfigurationAsync(string server, User user)
    {
        var key = $"{server.TrimEnd('/')}|{user.Id}";
        if (_profileKey == key) return;
        _profileKey = key;
        _closing = false;
        var saved = LoadProfiles().GetValueOrDefault(key) ?? [];
        var main = saved.FirstOrDefault(item => item.Primary);
        if (main is not null)
        {
            await primary.RestoreStateAsync(main.State);
            if (Application.Current?.MainWindow is { } window) WindowPlacement.Restore(window, main.Placement);
        }
        var snapshot = saved.Any(item => !item.Primary) ? await primary.Catalog.LoadAsync() : null;
        foreach (var item in saved.Where(item => !item.Primary).Take(8))
        {
            var workspace = primary.CreateSibling();
            await workspace.SetAccessAsync(user);
            CopyPreferences(workspace);
            await workspace.ApplyResourcesAsync(snapshot!);
            await workspace.RestoreStateAsync(item.State);
            Show(workspace, item.Placement);
        }
    }

    public void SaveConfiguration()
    {
        if (_profileKey is null || _closing) return;
        try
        {
            var profiles = LoadProfiles();
            profiles[_profileKey] = Items.Select(workspace => new SavedWorkspace(workspace.CaptureState(),
                WindowPlacement.Capture(ReferenceEquals(workspace, primary) ? Application.Current?.MainWindow : _windows.GetValueOrDefault(workspace)),
                ReferenceEquals(workspace, primary))).ToArray();
            Directory.CreateDirectory(ClientFiles.Directory);
            var path = Path.Combine(ClientFiles.Directory, "workspaces-v2.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(profiles));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) { ClientFiles.Log($"保存工作区配置失败：{ex.Message}"); }
    }

    private static Dictionary<string, SavedWorkspace[]> LoadProfiles()
    {
        try
        {
            var path = Path.Combine(ClientFiles.Directory, "workspaces-v2.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, SavedWorkspace[]>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) { ClientFiles.Log($"读取工作区配置失败：{ex.Message}"); return []; }
    }

    public async Task RemoveAsync(WorkspaceViewModel workspace)
    {
        _items.Remove(workspace);
        _windows.Remove(workspace);
        await workspace.DisposeAsync();
        SaveConfiguration();
    }

    public async Task ClearAsync(bool save = true)
    {
        if (save) SaveConfiguration();
        _closing = true;
        foreach (var window in _windows.Values.ToArray()) window.CloseForShutdown();
        _windows.Clear();
        var items = Items;
        _items.RemoveAll(workspace => !ReferenceEquals(workspace, primary));
        await Task.WhenAll(items.Select(workspace => ReferenceEquals(workspace, primary) ? workspace.ClearAsync() : workspace.DisposeAsync().AsTask()));
        _profileKey = null;
        _closing = false;
    }
}
