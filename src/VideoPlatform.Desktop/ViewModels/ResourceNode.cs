using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.ViewModels;

/// <summary>资源树节点。Channel 非空表示通道节点；否则为分组节点（录像机、组织或未分配分组）。</summary>
public sealed partial class ResourceNode(string name, Channel? channel = null) : ObservableObject
{
    public string Name { get; } = name;
    public Channel? Channel { get; } = channel;
    public ObservableCollection<ResourceNode> Children { get; } = [];
    public bool IsChannel => Channel is not null;
    public bool IsOnline => Channel?.Online == true;
    public bool IsDevice { get; init; }
    public long? DeviceId { get; init; }
    /// <summary>分组节点的在线统计（在线通道/总通道），通道节点为 null。</summary>
    public string? OnlineCountLabel
    {
        get
        {
            if (IsChannel) return null;
            var channelNodes = Flatten().Where(n => n.IsChannel).ToList();
            return channelNodes.Count == 0 ? null : $"在线 {channelNodes.Count(c => c.IsOnline)}/{channelNodes.Count}";
        }
    }
    /// <summary>通道节点在设备内的通道号，用于 iVMS-4200 风格的“通道 01”标识。</summary>
    public string? NumberLabel => Channel is { } channel ? channel.DeviceChannel.ToString("00") : null;
    public string DisplayName => IsChannel && !IsOnline ? $"{Name} (离线)" : Name;
    public string IconGlyph => !IsChannel ? "\uE838" : (Channel?.PtzCapable == true ? "\uE714" : "\uE722");
    public string SecondaryLabel => IsChannel ? Channel?.Detail ?? "" : "";
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isExpanded = true;
    public void SetExpandedRecursive(bool expanded)
    {
        IsExpanded = expanded;
        foreach (var child in Children) child.SetExpandedRecursive(expanded);
    }
    partial void OnIsCheckedChanged(bool value) { foreach (var child in Children) child.IsChecked = value; }
    public IEnumerable<ResourceNode> Flatten() => new[] { this }.Concat(Children.SelectMany(node => node.Flatten()));
    public bool Filter(string search, IReadOnlySet<long>? favorites)
    {
        var childMatch = false;
        foreach (var child in Children) childMatch |= child.Filter(search, favorites);
        IsVisible = Channel is { } channel
            ? (favorites is null || favorites.Contains(channel.Id)) && $"{Name} {channel.Name} {channel.Alias} {channel.Detail}".Contains(search, StringComparison.OrdinalIgnoreCase)
            : childMatch;
        return IsVisible;
    }
}
