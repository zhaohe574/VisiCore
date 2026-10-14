using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using System.Net.Http;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [RelayCommand] private Task SaveLayoutAsync() => SaveLayoutCoreAsync(false);
    [RelayCommand] private Task UpdateLayoutAsync() => SaveLayoutCoreAsync(true);
    private Task SaveLayoutCoreAsync(bool update) => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(LayoutName)) throw new ArgumentException("请输入布局名称。");
        if (IntervalSeconds is < 10 or > 300) throw new ArgumentException("轮巡停留时间必须在 10 至 300 秒之间。");
        if (ShareLayout && !CanShareLayouts) throw new InvalidOperationException("当前账号无权发布共享轮巡。");
        var slots = Tiles.Take(LayoutCount).Select(t => t.Channel?.Id).ToArray();
        var ids = SaveAsPatrol ? CheckedChannels().Select(c => (long?)c.Id).Distinct().ToArray() : slots;
        if (!ids.Any(id => id is > 0)) throw new InvalidOperationException("请先选择轮巡通道或打开视频窗口。");
        if (SaveAsPatrol && ids.Any(id => id is null)) throw new InvalidOperationException("轮巡方案不能包含空窗口。");
        var request = new LayoutRequest(LayoutName.Trim(), SaveAsPatrol ? "patrol" : "layout", ShareLayout, LayoutCount, IntervalSeconds, ids);
        if (update)
        {
            if (SelectedLayout is null) throw new InvalidOperationException("请选择要更新的方案。");
            await _api.SendAsync(HttpMethod.Put, $"layouts/{SelectedLayout.Id}", request);
        }
        else await _api.PostAsync<LayoutDto>("layouts", request);
        await RefreshAsync(); Status = "布局已保存并同步至平台。";
    });
    [RelayCommand] private Task DeleteLayoutAsync() => RunAsync(async () =>
    {
        if (SelectedLayout is null) return;
        if (!_dialogs.Confirm($"确定删除“{SelectedLayout.Name}”？")) return;
        await _api.SendAsync(HttpMethod.Delete, $"layouts/{SelectedLayout.Id}"); await RefreshAsync();
    });
    /// <summary>iVMS-4200 主预览“新建视图”：把当前分屏窗口安排保存为新的视图方案。</summary>
    [RelayCommand] private Task NewViewAsync() => RunAsync(async () =>
    {
        if (IsPlayback) throw new InvalidOperationException("请在主预览模式下新建视图。");
        LayoutName = $"视图 {Layouts.Count(l => l.Kind == "layout") + 1}";
        SaveAsPatrol = false; ShareLayout = false; SelectedLayout = null;
        await SaveLayoutCoreAsync(false);
        SelectedLayout = Layouts.FirstOrDefault(l => l.Kind == "layout" && l.Name == LayoutName);
        Status = $"视图“{LayoutName}”已保存。";
    });
    /// <summary>iVMS-4200 主预览“保存视图”：更新当前选中的视图，或按当前名称另存为新的视图。</summary>
    [RelayCommand] private Task SaveViewAsync() => RunAsync(async () =>
    {
        if (IsPlayback) throw new InvalidOperationException("请在主预览模式下保存视图。");
        if (SelectedLayout is { Kind: "layout" } current)
        {
            SaveAsPatrol = false;
            await SaveLayoutCoreAsync(true);
            Status = $"视图“{current.Name}”已更新。";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(LayoutName) || LayoutName == "我的值守布局") LayoutName = $"视图 {Layouts.Count(l => l.Kind == "layout") + 1}";
            SaveAsPatrol = false; ShareLayout = false;
            await SaveLayoutCoreAsync(false);
            SelectedLayout = Layouts.FirstOrDefault(l => l.Kind == "layout" && l.Name == LayoutName);
            Status = $"视图“{LayoutName}”已保存。";
        }
    });
    /// <summary>iVMS-4200 主预览工具栏轮巡开关：按选中/首个轮巡方案启动，正在轮巡时停止。</summary>
    [RelayCommand] private Task TogglePatrolAsync() => RunAsync(async () =>
    {
        if (IsPatrolling) { StopPatrol(); Status = "轮巡已停止。"; return; }
        if (IsPlayback) throw new InvalidOperationException("请在主预览模式下启动轮巡。");
        var patrol = SelectedLayout is { Kind: "patrol" } selected ? selected : Layouts.FirstOrDefault(l => l.Kind == "patrol");
        if (patrol is null) throw new InvalidOperationException("没有可用的轮巡方案：请在“方案设置”中勾选“设为巡视轮巡”并保存。");
        if (!CanLive) throw new InvalidOperationException("当前账号没有预览权限。");
        SelectedLayout = patrol;
        await ApplyLayoutCommand.ExecuteAsync(null);
    });
    [RelayCommand] private Task ApplyLayoutAsync() => RunAsync(async () =>
    {
        var layout = SelectedLayout ?? throw new InvalidOperationException("请选择布局或轮巡方案。");
        if (!LayoutOptions.Contains(layout.Layout)) throw new InvalidOperationException("此布局的分屏数量无效。");
        if (layout.Layout == 25 && !CanSplit25) throw new InvalidOperationException("当前账号未被分配 25 路分屏权限。");
        if (layout.Kind == "patrol")
        {
            if (!CanLive) throw new InvalidOperationException("当前账号没有预览权限。");
            await SetModeAsync(false);
        }
        await StopAllCoreAsync();
        _awaitingRestore = false;
        var preset = LayoutPreset.Find(AllPresets, layout.Layout.ToString());
        if (preset is not null)
        {
            await ApplyLayoutCountAsync(preset.Count, preset.Columns, preset.Rows, preset);
        }
        else
        {
            var cols = (int)Math.Max(1, Math.Round(Math.Sqrt(layout.Layout)));
            var rows = (int)Math.Ceiling((double)layout.Layout / cols);
            await ApplyLayoutCountAsync(layout.Layout, cols, rows);
        }
        _ = ApplyLayoutTiersAsync();
        if (layout.Kind == "patrol")
        {
            if (!CanLive) throw new InvalidOperationException("当前账号没有预览权限。");
            _patrol = layout; _patrolOffset = 0; IsPatrolling = true;
            await AdvancePatrolAsync(DateTimeOffset.UtcNow);
        }
        else
        {
            var limit = Math.Min(layout.ChannelIds.Length, LayoutCount);
            var generation = _generation;
            var tasks = new List<Task>();
            for (var i = 0; i < limit; ++i)
            {
                var channelId = layout.ChannelIds[i];
                var tile = Tiles[i];
                if (channelId is { } id && _channels.TryGetValue(id, out var channel) && channel.Online)
                {
                    tasks.Add(OpenChannelGatedAsync(channel, tile, generation));
                }
                else
                {
                    tile.StateLabel = channelId is null ? "空闲" : "通道离线或无权访问";
                }
            }
            for (var i = limit; i < LayoutCount; ++i)
            {
                Tiles[i].StateLabel = "空闲";
            }
            if (tasks.Count > 0)
            {
                Status = $"正在同时加载 {tasks.Count} 路视频...";
                await Task.WhenAll(tasks);
                if (generation == _generation) Status = $"已加载 {tasks.Count} 路分屏视频。";
            }
        }
    });
    [RelayCommand] private void StopPatrol() { IsPatrolling = false; _patrol = null; }
    private async Task AdvancePatrolAsync(DateTimeOffset now)
    {
        if (!await _patrolGate.WaitAsync(0)) return;
        var generation = _generation;
        try
        {
            var patrol = _patrol;
            if (patrol is null || patrol.ChannelIds.Length == 0) { StopPatrol(); return; }
            var tasks = new List<Task>();
            for (var i = 0; i < LayoutCount && IsPatrolling && generation == _generation; ++i)
            {
                var id = patrol.ChannelIds[(_patrolOffset + i) % patrol.ChannelIds.Length];
                var tile = Tiles[i];
                if (id is { } channelId && _channels.TryGetValue(channelId, out var channel) && channel.Online)
                {
                    tasks.Add(OpenChannelGatedAsync(channel, tile, generation));
                }
                else
                {
                    tasks.Add(Task.Run(async () => { await tile.StopAsync(); tile.StateLabel = "通道离线或无权访问"; }));
                }
            }
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
            _patrolOffset = (_patrolOffset + LayoutCount) % patrol.ChannelIds.Length;
            _nextPatrol = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(patrol.IntervalSeconds, 10, 300));
        }
        finally { _patrolGate.Release(); }
    }
}
