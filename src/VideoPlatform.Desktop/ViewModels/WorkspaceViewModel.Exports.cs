using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using System.Net.Http;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [RelayCommand] private Task CreateExportAsync() => RunAsync(async () =>
    {
        if (!CanExport) throw new InvalidOperationException("当前账号没有录像导出权限。");
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        var range = ReadRange();
        await _api.PostAsync<ExportJob>("exports", new ExportRequest(channel.Id, range.Start, range.End));
        Status = "录像导出任务已提交。";
        if (ExportCreated is not null) await ExportCreated();
    });
    /// <summary>把选中的录像段直接提交导出（iVMS-4200 回放检索面板的“导出”语义）。</summary>
    [RelayCommand] private Task ExportRecordingAsync() => RunAsync(async () =>
    {
        if (!CanExport) throw new InvalidOperationException("当前账号没有录像导出权限。");
        var recording = SelectedRecording ?? throw new InvalidOperationException("请先选择录像段。");
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        await _api.PostAsync<ExportJob>("exports", new ExportRequest(channel.Id, recording.Start, recording.End));
        Status = $"录像段导出已提交：{recording.Label}";
        if (ExportCreated is not null) await ExportCreated();
    });
}
