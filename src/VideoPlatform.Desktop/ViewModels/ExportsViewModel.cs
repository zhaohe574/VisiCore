using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class ExportsViewModel(IPlatformApi api, IUserInteraction dialogs) : ViewModelBase
{
    private long _generation;
    private CancellationTokenSource? _download;
    public ObservableCollection<ExportJob> Items { get; } = [];
    [ObservableProperty] private ExportJob? _selected;
    [ObservableProperty] private long _total;
    [ObservableProperty] private int _pageNumber = 1;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _canExport;
    public void SetAccess(User? user)
    {
        CanExport = user?.Can("recording.export") == true || user?.Can("export.create") == true || user?.Can("playback.export") == true;
        if (!CanExport) Clear();
    }
    public void Clear() { ++_generation; _download?.Cancel(); Items.Clear(); Selected = null; Total = 0; }
    public async Task RefreshAsync()
    {
        if (!CanExport) return;
        var generation = ++_generation;
        var page = await api.GetAsync<Page<ExportJob>>($"exports?page={PageNumber}&pageSize=50");
        if (generation != _generation || !CanExport) return;
        var id = Selected?.Id;
        Items.Clear(); foreach (var item in page?.Items ?? []) Items.Add(item);
        Total = page?.Total ?? 0;
        Selected = Items.FirstOrDefault(i => i.Id == id);
    }
    [RelayCommand] private Task RefreshJobsAsync() => RunAsync(RefreshAsync);
    [RelayCommand] private Task NextPageAsync() => RunAsync(async () => { if (PageNumber * 50 < Total) { ++PageNumber; await RefreshAsync(); } });
    [RelayCommand] private Task PreviousPageAsync() => RunAsync(async () => { if (PageNumber > 1) { --PageNumber; await RefreshAsync(); } });
    [RelayCommand] private Task CancelJobAsync() => RunAsync(async () =>
    {
        if (Selected is not { State: "queued" or "running" } job) throw new InvalidOperationException("请选择排队中或导出中的任务。");
        await api.SendAsync(HttpMethod.Post, $"exports/{job.Id}/cancel"); await RefreshAsync();
    });
    [RelayCommand] private Task RetryAsync() => RunAsync(async () =>
    {
        if (Selected is not { State: "failed" or "cancelled" } job) throw new InvalidOperationException("请选择失败或已取消的任务。");
        await api.SendAsync(HttpMethod.Post, $"exports/{job.Id}/retry"); await RefreshAsync();
    });
    [RelayCommand] private Task DownloadAsync() => RunAsync(async () =>
    {
        if (Selected is not { State: "completed" } job) throw new InvalidOperationException("请选择已完成的导出任务。");
        if (job.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("导出文件已过期，请重新导出。");
        var destination = dialogs.SaveFile(job.FileName ?? "录像.zip", "录像文件|*.mp4;*.zip|全部文件|*.*");
        if (destination is null) return;
        _download = new(); IsDownloading = true; DownloadProgress = 0;
        try
        {
            await api.DownloadAsync($"exports/{job.Id}/download", destination, new Progress<double>(value => DownloadProgress = value), _download.Token);
            Status = $"录像已下载：{destination}";
        }
        finally { IsDownloading = false; _download.Dispose(); _download = null; }
    });
    [RelayCommand] private void CancelDownload() => _download?.Cancel();
}
