using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using System.Net.Http;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public (DateTimeOffset Start, DateTimeOffset End) ReadRange()
    {
        if (!TimeSpan.TryParse(StartTime, out var start) || start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)
            || !TimeSpan.TryParse(EndTime, out var end) || end < TimeSpan.Zero || end >= TimeSpan.FromDays(1))
            throw new ArgumentException("请输入有效的开始和结束时间。");
        if (end <= start) throw new ArgumentException("结束时间必须晚于开始时间。");
        var date = RecordingDate.Date;
        var from = new DateTimeOffset(date.Add(start));
        var to = new DateTimeOffset(date.Add(end));
        RangeStart = new DateTimeOffset(date); // 当天 00:00:00（时间轴起点）
        RangeEnd = new DateTimeOffset(date.AddDays(1)); // 当天 24:00:00（时间轴终点，整整24小时）
        return (from, to);
    }
    [RelayCommand] private Task SearchRecordingsAsync() => RunAsync(async () =>
    {
        if (!CanPlayback) throw new InvalidOperationException("当前账号没有回放权限。");
        var primaryChannel = SelectedResource?.Channel ?? SelectedTile?.Channel;
        var checkedChannels = CheckedChannels().ToArray();
        var candidateChannels = checkedChannels.Length > 0
            ? checkedChannels
            : (primaryChannel is not null ? [primaryChannel] : Tiles.Where(t => t.Channel is not null).Select(t => t.Channel!).Distinct().ToArray());

        if (candidateChannels.Length == 0) throw new InvalidOperationException("请选择录像通道。");
        var range = ReadRange();
        var generation = _generation;

        var tracks = new List<TimelineTrack>();
        Recording[]? primaryRecordings = null;

        foreach (var ch in candidateChannels.Take(4))
        {
            try
            {
                var recs = await _api.PostAsync<Recording[]>("recordings/search", new RecordingRequest(ch.Id, range.Start, range.End));
                if (generation != _generation) return;
                var segs = recs.Select(r => new RecordingSegment(r.Start, r.End)).ToArray();
                var isActive = (primaryChannel is not null && ch.Id == primaryChannel.Id) || (SelectedTile?.Channel?.Id == ch.Id);
                tracks.Add(new TimelineTrack(ch.DisplayName, segs, isActive));
                if (primaryRecordings is null || ch.Id == primaryChannel?.Id)
                {
                    primaryRecordings = recs;
                }
            }
            catch (Exception ex)
            {
                ClientFiles.Log($"查询通道 {ch.DisplayName} 录像失败：{ex.Message}");
            }
        }

        if (generation != _generation) return;
        Recordings.Clear();
        if (primaryRecordings is not null)
        {
            foreach (var recording in primaryRecordings) Recordings.Add(recording);
            TimelineSegments = primaryRecordings.Select(r => new RecordingSegment(r.Start, r.End)).ToArray();
        }
        else
        {
            TimelineSegments = [];
        }

        TimelineTracks = [.. tracks];
        Status = $"已找到 {Recordings.Count} 段录像（共检索 {tracks.Count} 个通道）。";
    });
    [RelayCommand] private Task PlayRecordingAsync() => RunAsync(async () =>
    {
        if (SelectedRecording is null || !CanPlayback) return;
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        var target = SelectedTile ?? Tiles[0];
        await target.StartAsync(channel, true, target.DesiredStreamType, SelectedRecording.Start, SelectedRecording.End);
        _awaitingRestore = false;
        Playhead = SelectedRecording.Start;
    });
    [RelayCommand] private Task PlaybackActionAsync(string? action) => RunAsync(() => ControlAsync(new PlaybackControl(action ?? "pause")));
    [RelayCommand] private Task ApplySpeedAsync() => RunAsync(() => ControlAsync(new PlaybackControl("speed", Speed: PlaybackSpeed)));
    public Task SeekAsync(DateTimeOffset position) => RunAsync(() => ControlAsync(new PlaybackControl("seek", position)));

    [RelayCommand]
    private Task StepBackwardAsync() => RunAsync(async () =>
    {
        var current = Playhead ?? RangeStart;
        var target = current.AddSeconds(-10);
        if (target < RangeStart) target = RangeStart;
        await SeekAsync(target);
    });

    [RelayCommand]
    private Task StepForwardAsync() => RunAsync(async () =>
    {
        var current = Playhead ?? RangeStart;
        var target = current.AddSeconds(10);
        if (target > RangeEnd) target = RangeEnd;
        await SeekAsync(target);
    });

    [RelayCommand]
    private Task FrameStepAsync() => RunAsync(async () =>
    {
        await ControlAsync(new PlaybackControl("pause"));
        var tiles = UnifiedControl ? Tiles.Where(t => t.IsPlayback && t.SessionId is not null).ToArray() : SelectedTile is { } selected ? [selected] : Array.Empty<VideoTileViewModel>();
        foreach (var tile in tiles) tile.StepFrame();
    });

    [RelayCommand]
    private void ApplyQuickPreset(string? preset)
    {
        switch (preset)
        {
            case "today":
                RecordingDate = DateTime.Today;
                StartTime = "00:00:00";
                EndTime = "23:59:59";
                break;
            case "yesterday":
                RecordingDate = DateTime.Today.AddDays(-1);
                StartTime = "00:00:00";
                EndTime = "23:59:59";
                break;
        }
        ReadRange();
        _ = SearchRecordingsAsync();
    }

    [RelayCommand]
    private Task ExportTimelineClipAsync() => RunAsync(async () =>
    {
        if (!CanExport) throw new InvalidOperationException("当前账号没有录像导出权限。");
        var channel = SelectedResource?.Channel ?? SelectedTile?.Channel ?? throw new InvalidOperationException("请选择录像通道。");
        var (start, end) = HasTimelineClip ? (TimelineClipStart!.Value, TimelineClipEnd!.Value) : ReadRange();
        await _api.PostAsync<ExportJob>("exports", new ExportRequest(channel.Id, start, end));
        Status = $"已提交 {channel.DisplayName} 录像导出任务（{start.LocalDateTime:MM-dd HH:mm:ss} ~ {end.LocalDateTime:HH:mm:ss}）。";
        if (ExportCreated is not null) await ExportCreated();
    });

    public void SetTimelineClip(DateTimeOffset start, DateTimeOffset end)
    {
        TimelineClipStart = start;
        TimelineClipEnd = end;
    }

    private async Task ControlAsync(PlaybackControl command)
    {
        if (!CanPlayback) throw new InvalidOperationException("当前账号没有回放权限。");
        var tiles = UnifiedControl ? Tiles.Where(t => t.IsPlayback && t.SessionId is not null).ToArray() : SelectedTile is { } selected ? [selected] : Array.Empty<VideoTileViewModel>();
        var errors = new List<string>();
        foreach (var tile in tiles)
        {
            try { await tile.ControlAsync(command); }
            catch (Exception ex) { errors.Add($"窗口 {tile.Number}：{ex.Message}"); }
        }
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("；", errors));
    }
}
