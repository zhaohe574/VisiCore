using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private bool _isBusy;
    protected async Task RunAsync(Func<Task> action, string? success = null)
    {
        try { IsBusy = true; await action(); if (success is not null) Status = success; }
        catch (OperationCanceledException) { Status = "操作已取消。"; }
        catch (Exception ex)
        {
            Status = ex is PlatformException { TraceId: { Length: > 0 } trace } ? $"{ex.Message}（请求编号：{trace}）" : ex.Message;
            ClientFiles.Log(Status);
        }
        finally { IsBusy = false; }
    }
}
