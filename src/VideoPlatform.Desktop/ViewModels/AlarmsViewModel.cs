using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class AlarmsViewModel(IPlatformApi api) : ViewModelBase
{
    private long _generation;
    private long _detailGeneration;
    public ObservableCollection<Alarm> Items { get; } = [];
    public ObservableCollection<AlarmHistory> History { get; } = [];
    public Choice[] States { get; } = [new("", "全部状态"), new("new", "待处理"), new("processing", "处理中"), new("closed", "已关闭")];
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _eventType = "";
    [ObservableProperty] private DateTime? _from = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime? _to = DateTime.Today;
    [ObservableProperty] private int _pageNumber = 1;
    [ObservableProperty] private long _total;
    [ObservableProperty] private Alarm? _selected;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private BitmapSource? _image;
    [ObservableProperty] private bool _canRead;
    [ObservableProperty] private bool _canHandle;
    /// <summary>报警视频联动请求（iVMS-4200 事件中心使用逻辑）：参数为报警与联动方式 live／playback，由 Shell 切模块并打开通道。</summary>
    public event Action<Alarm, string>? VideoRequested;
    [RelayCommand] private void Video(string? mode)
    {
        if (Selected is not { } alarm || mode is not ("live" or "playback")) return;
        VideoRequested?.Invoke(alarm, mode);
    }
    public void SetAccess(User? user)
    {
        CanRead = user?.Can("alarm.read") == true;
        CanHandle = user?.Can("alarm.ack") == true || user?.Can("alarm.handle") == true;
        if (!CanRead) Clear();
    }
    public void Clear()
    {
        ++_generation; ++_detailGeneration;
        Items.Clear(); History.Clear(); Selected = null; Image = null; Note = ""; Total = 0;
    }
    public async Task RefreshAsync()
    {
        if (!CanRead) return;
        var generation = ++_generation;
        var query = $"alarms?page={PageNumber}&pageSize=50&state={Uri.EscapeDataString(State)}&search={Uri.EscapeDataString(Search.Trim())}&eventType={Uri.EscapeDataString(EventType.Trim())}";
        if (From is { } from) query += "&from=" + Uri.EscapeDataString(new DateTimeOffset(from.Date).ToString("O"));
        if (To is { } to) query += "&to=" + Uri.EscapeDataString(new DateTimeOffset(to.Date.AddDays(1).AddTicks(-1)).ToString("O"));
        var page = await api.GetAsync<Page<Alarm>>(query);
        if (generation != _generation || !CanRead) return;
        var selectedId = Selected?.Id;
        Items.Clear(); foreach (var item in page?.Items ?? []) Items.Add(item);
        Total = page?.Total ?? 0;
        Selected = Items.FirstOrDefault(a => a.Id == selectedId);
    }
    partial void OnSelectedChanged(Alarm? value) => _ = LoadDetailAsync(value);
    private Task LoadDetailAsync(Alarm? alarm) => RunAsync(async () =>
    {
        var generation = ++_detailGeneration;
        Image = null; History.Clear(); Note = "";
        if (alarm is null) return;
        var detail = await api.GetAsync<Alarm>($"alarms/{alarm.Id}");
        if (generation != _detailGeneration || detail is null || !CanRead) return;
        foreach (var item in detail.History ?? []) History.Add(item);
        if (detail.ImageAvailable)
        {
            var bytes = await api.GetBytesAsync($"alarms/{alarm.Id}/image");
            if (generation != _detailGeneration || !CanRead) return;
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            Image = image;
        }
    });
    [RelayCommand] private Task SearchAlarmsAsync() { PageNumber = 1; return RunAsync(RefreshAsync); }
    [RelayCommand] private Task RefreshAsyncCommand() => RunAsync(RefreshAsync);
    [RelayCommand] private Task NextPageAsync() => RunAsync(async () => { if (PageNumber * 50 < Total) { ++PageNumber; await RefreshAsync(); } });
    [RelayCommand] private Task PreviousPageAsync() => RunAsync(async () => { if (PageNumber > 1) { --PageNumber; await RefreshAsync(); } });
    [RelayCommand] private Task HandleAsync(string? action) => RunAsync(async () =>
    {
        if (!CanHandle) throw new InvalidOperationException("当前账号没有报警处理权限。");
        var alarm = Selected ?? throw new InvalidOperationException("请选择报警。");
        if (action is not ("claim" or "note" or "close" or "reopen")) throw new ArgumentException("报警操作无效。");
        if (action is "note" or "close" && string.IsNullOrWhiteSpace(Note)) throw new ArgumentException("请输入处理备注。");
        await api.SendAsync(HttpMethod.Post, $"alarms/{alarm.Id}/actions", new AlarmAction(action, Note.Trim()));
        await RefreshAsync(); Status = "报警处理已保存。";
    });
}
