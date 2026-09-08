using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class ResourceNode(string name, Channel? channel = null) : ObservableObject
{
    public string Name { get; } = name;
    public Channel? Channel { get; } = channel;
    public ObservableCollection<ResourceNode> Children { get; } = [];
    public bool IsChannel => Channel is not null;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isExpanded = true;
    partial void OnIsCheckedChanged(bool value) { foreach (var child in Children) child.IsChecked = value; }
    public IEnumerable<ResourceNode> Flatten() => new[] { this }.Concat(Children.SelectMany(node => node.Flatten()));
    public bool Filter(string search, IReadOnlySet<long>? favorites)
    {
        var childMatch = false;
        foreach (var child in Children) childMatch |= child.Filter(search, favorites);
        IsVisible = Channel is { } channel
            ? (favorites is null || favorites.Contains(channel.Id)) && $"{Name} {channel.Detail}".Contains(search, StringComparison.OrdinalIgnoreCase)
            : childMatch;
        return IsVisible;
    }
}
