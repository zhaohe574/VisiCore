namespace VideoPlatform.Desktop.Models;

public sealed record TileState(int Index, long ChannelId, int StreamType, DateTimeOffset? Start,
    DateTimeOffset? End, DateTimeOffset? Position, double Speed, bool Paused, bool Muted, int? ManualStream);
public sealed record WorkspaceModeState(string Preset, int SelectedIndex, TileState[] Tiles,
    DateTime RecordingDate, string StartTime, string EndTime, Recording[] Recordings,
    TimelineTrack[] Tracks, long? PatrolId, int PatrolOffset, bool WasPatrolling);
public sealed record WorkspaceState(Guid Id, bool IsPlayback, WorkspaceModeState? Live, WorkspaceModeState? Playback);
public sealed record WorkspacePlacement(string? Display, double Left, double Top, double Width, double Height, bool Maximized);
public sealed record SavedWorkspace(WorkspaceState State, WorkspacePlacement? Placement, bool Primary);
