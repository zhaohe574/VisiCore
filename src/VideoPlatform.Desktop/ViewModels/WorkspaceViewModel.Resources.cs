using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public async Task RefreshAsync()
    {
        if (_user is null) return;
        var generation = _generation;
        var snapshot = await Catalog.LoadAsync();
        if (generation == _generation) await ApplyResourcesAsync(snapshot);
    }
    public async Task ApplyResourcesAsync(ResourceSnapshot snapshot)
    {
        await _refreshGate.WaitAsync();
        var generation = _generation;
        try
        {
            if (_user is null) return;
            var channels = snapshot.Channels;
            var favorites = snapshot.Favorites;
            var layouts = snapshot.Layouts;
            var organization = snapshot.Organization;
            _lastOrganization = organization;
            if (generation != _generation) return;
            var selectedId = SelectedResource?.Channel?.Id;
            var checkedIds = CheckedChannels().Select(c => c.Id).ToHashSet();
            // 空结果同样应用，确保删除最后通道或撤权后释放旧资源。
            if (generation == _generation)
            {
                // 通道集合与在线状态都未变化时保留现有节点实例，避免每 60 秒重建整棵树造成容器重排与闪烁。
                var changed = channels.Length != _channels.Count || channels.Any(channel => !_channels.TryGetValue(channel.Id, out var existing)
                    || existing.Online != channel.Online || existing.Alias != channel.Alias || existing.PtzCapable != channel.PtzCapable
                    || existing.Name != channel.Name || existing.UnitId != channel.UnitId || existing.DeviceName != channel.DeviceName);
                if (changed)
                {
                    foreach (var channel in channels) _channels[channel.Id] = channel;
                    foreach (var stale in _channels.Keys.Where(id => channels.All(channel => channel.Id != id)).ToArray()) _channels.Remove(stale);
                }
                _favoriteIds.Clear(); _favoriteIds.UnionWith(favorites.Select(c => c.Id).Where(_channels.ContainsKey));
                if (changed) BuildResources(channels, organization, checkedIds);
                SelectedResource = Resources.SelectMany(n => n.Flatten()).FirstOrDefault(n => n.Channel?.Id == selectedId);
                var layoutId = SelectedLayout?.Id;
                Layouts.Clear(); foreach (var layout in layouts) Layouts.Add(layout);
                SelectedLayout = Layouts.FirstOrDefault(l => l.Id == layoutId);
                if (changed)
                {
                    foreach (var tile in Tiles.Where(t => t.Channel is not null && !_channels.ContainsKey(t.Channel.Id))) await tile.StopAsync();
                    Filter();
                }
                OnPropertyChanged(nameof(OnlineStatsLabel));
            }
        }
        finally { _refreshGate.Release(); }
    }
    /// <summary>
    /// 构建资源树。只保留组织架构模式（车间 → 区域 → 单元），未关联组织的通道归入“未分配组织”节点。
    /// </summary>
    private void BuildResources(IEnumerable<Channel> channels, Organization? organization, HashSet<long> checkedIds)
    {
        Resources.Clear();
        var channelList = channels.ToList();

        var units = organization?.Units.ToDictionary(u => u.Id) ?? [];
        var areas = organization?.Areas.ToDictionary(a => a.Id) ?? [];
        var workshops = organization?.Workshops.ToDictionary(w => w.Id) ?? [];

        var assigned = channelList.Where(c => c.UnitId is not null && units.ContainsKey(c.UnitId.Value)).ToList();
        var unassigned = channelList.Where(c => c.UnitId is null || !units.ContainsKey(c.UnitId.Value)).ToList();

        var workshopNodes = new Dictionary<long, ResourceNode>();
        var areaNodes = new Dictionary<long, ResourceNode>();
        var unitNodes = new Dictionary<long, ResourceNode>();

        var unitOrder = organization?.Units.Select((u, i) => (u.Id, i)).ToDictionary(x => x.Id, x => x.i) ?? [];
        foreach (var channel in assigned.OrderBy(c => unitOrder.GetValueOrDefault(c.UnitId!.Value)).ThenBy(c => c.SortOrder).ThenBy(c => c.DeviceChannel))
        {
            var unit = units[channel.UnitId!.Value];
            if (!unitNodes.TryGetValue(unit.Id, out var unitNode))
            {
                unitNode = new ResourceNode(unit.Name);
                unitNodes[unit.Id] = unitNode;

                if (unit.ParentId is { } parentId && areas.TryGetValue(parentId, out var area))
                {
                    if (!areaNodes.TryGetValue(area.Id, out var areaNode))
                    {
                        areaNode = new ResourceNode(area.Name);
                        areaNodes[area.Id] = areaNode;

                        if (area.ParentId is { } wsId && workshops.TryGetValue(wsId, out var ws))
                        {
                            if (!workshopNodes.TryGetValue(ws.Id, out var wsNode))
                            {
                                wsNode = new ResourceNode(ws.Name);
                                workshopNodes[ws.Id] = wsNode;
                                Resources.Add(wsNode);
                            }
                            wsNode.Children.Add(areaNode);
                        }
                        else
                        {
                            Resources.Add(areaNode);
                        }
                    }
                    areaNode.Children.Add(unitNode);
                }
                else if (unit.ParentId is { } wsParentId && workshops.TryGetValue(wsParentId, out var directWs))
                {
                    if (!workshopNodes.TryGetValue(directWs.Id, out var wsNode))
                    {
                        wsNode = new ResourceNode(directWs.Name);
                        workshopNodes[directWs.Id] = wsNode;
                        Resources.Add(wsNode);
                    }
                    wsNode.Children.Add(unitNode);
                }
                else
                {
                    Resources.Add(unitNode);
                }
            }
            unitNode.Children.Add(CreateChannelNode(channel, checkedIds));
        }

        if (unassigned.Count > 0)
        {
            var unassignedNode = new ResourceNode("未分配组织");
            foreach (var channel in unassigned.OrderBy(c => c.DeviceName).ThenBy(c => c.DeviceChannel))
                unassignedNode.Children.Add(CreateChannelNode(channel, checkedIds));
            Resources.Add(unassignedNode);
        }
    }

    private static ResourceNode CreateChannelNode(Channel channel, HashSet<long> checkedIds) =>
        new(channel.DisplayName, channel) { IsChecked = checkedIds.Contains(channel.Id), IsExpanded = false };
    public string OnlineStatsLabel => $"在线 {_channels.Values.Count(c => c.Online)}/{_channels.Count}";
    private IEnumerable<Channel> CheckedChannels() => Resources.SelectMany(n => n.Flatten()).Where(n => n.IsChecked && n.Channel is not null).Select(n => n.Channel!);
    partial void OnSearchChanged(string value) => Filter();
    partial void OnFavoritesOnlyChanged(bool value) => Filter();
    [RelayCommand] private void ClearSearch() => Search = "";
    [RelayCommand] private void ExpandAllResources() { foreach (var root in Resources) root.SetExpandedRecursive(true); }
    [RelayCommand] private void CollapseAllResources() { foreach (var root in Resources) root.SetExpandedRecursive(false); }
    partial void OnSelectedLayoutChanged(LayoutDto? value)
    {
        if (value is null) return;
        LayoutName = value.Name; SaveAsPatrol = value.Kind == "patrol"; ShareLayout = value.Shared; IntervalSeconds = value.IntervalSeconds;
    }
    private void Filter() { foreach (var node in Resources) node.Filter(Search.Trim(), FavoritesOnly ? _favoriteIds : null); }
}
