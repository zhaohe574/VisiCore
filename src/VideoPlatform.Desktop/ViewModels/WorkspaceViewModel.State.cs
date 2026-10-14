using CommunityToolkit.Mvvm.Input;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private WorkspaceModeState? _liveState;
    private WorkspaceModeState? _playbackState;
    private bool _awaitingRestore;
    public WorkspaceViewModel CreateSibling() => new(_api, PlayerFactory, _ptz, _dispatcher, _dialogs, Catalog);

    private WorkspaceModeState CaptureMode() => new(CurrentPreset?.Key ?? LayoutCount.ToString(),
        SelectedTile?.Index ?? 0, Tiles.Select(tile => tile.CaptureState()).OfType<TileState>().ToArray(),
        RecordingDate, StartTime, EndTime, [.. Recordings], TimelineTracks, _patrol?.Id, _patrolOffset, IsPatrolling);

    private void SaveCurrentMode()
    {
        if (_awaitingRestore) return;
        if (IsPlayback) _playbackState = CaptureMode();
        else _liveState = CaptureMode();
    }

    public WorkspaceState CaptureState()
    {
        SaveCurrentMode();
        return new(Id, IsPlayback, _liveState, _playbackState);
    }

    public async Task RestoreStateAsync(WorkspaceState state)
    {
        await StopAllCoreAsync();
        Id = state.Id;
        _liveState = state.Live;
        _playbackState = state.Playback;
        IsPlayback = state.IsPlayback && CanPlayback;
        _awaitingRestore = true;
        await RestoreModeAsync(IsPlayback ? _playbackState : _liveState, startPlayback: false);
        Status = "工作区已恢复，点击“一键恢复播放”启动画面。";
    }

    [RelayCommand]
    private Task RestorePlaybackAsync() => RunAsync(async () =>
    {
        if (!_awaitingRestore) return;
        await RestoreModeAsync(IsPlayback ? _playbackState : _liveState, startPlayback: true);
        _awaitingRestore = false;
    });

    private async Task RestoreModeAsync(WorkspaceModeState? state, bool startPlayback)
    {
        Recordings.Clear();
        TimelineSegments = [];
        TimelineTracks = [];
        TimelineClipStart = TimelineClipEnd = null;
        if (state is null)
        {
            await SetLayoutAsync("4");
            return;
        }
        var preset = LayoutPreset.Find(AllPresets, state.Preset);
        await SetLayoutAsync(preset?.Count == 25 && !CanSplit25 ? "4" : state.Preset);
        SelectedTile = Tiles[Math.Clamp(state.SelectedIndex, 0, LayoutCount - 1)];
        RecordingDate = state.RecordingDate;
        StartTime = state.StartTime;
        EndTime = state.EndTime;
        foreach (var recording in state.Recordings) Recordings.Add(recording);
        TimelineTracks = state.Tracks;
        TimelineSegments = state.Recordings.Select(r => new RecordingSegment(r.Start, r.End)).ToArray();
        if (!startPlayback || (IsPlayback ? !CanPlayback : !CanLive)) return;
        foreach (var saved in state.Tiles.Where(t => t.Index >= 0 && t.Index < LayoutCount))
        {
            if (!_channels.TryGetValue(saved.ChannelId, out var channel) || (!IsPlayback && !channel.Online)) continue;
            var tile = Tiles[saved.Index];
            try
            {
                tile.IsMuted = saved.Muted;
                await tile.StartAsync(channel, IsPlayback, saved.StreamType, saved.Start ?? RangeStart, saved.End ?? RangeEnd);
                tile.ManualStreamOverride = saved.ManualStream;
                if (IsPlayback)
                {
                    if (saved.Position is { } position) await tile.ControlAsync(new("seek", Position: position));
                    if (saved.Speed != 1) await tile.ControlAsync(new("speed", Speed: saved.Speed));
                    if (saved.Paused) await tile.ControlAsync(new("pause"));
                }
            }
            catch (Exception ex) { Status = $"恢复通道“{channel.DisplayName}”失败：{ex.Message}"; }
        }
        if (!IsPlayback && state.WasPatrolling && Layouts.FirstOrDefault(l => l.Id == state.PatrolId) is { } patrol)
        {
            _patrol = patrol;
            _patrolOffset = state.PatrolOffset;
            IsPatrolling = true;
            _nextPatrol = DateTimeOffset.UtcNow.AddSeconds(patrol.IntervalSeconds);
        }
    }
}
