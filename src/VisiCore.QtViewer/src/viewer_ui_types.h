#pragma once

#include <QMetaType>

enum class WorkspaceMode {
    Preview = 0,
    Playback = 1,
};

enum class DockPanelId {
    ResourceCatalog = 0,
    Ptz = 1,
    PlaybackSearch = 2,
    RecordingTimeline = 3,
};

enum class ViewerConnectionState {
    Connected = 0,
    Connecting = 1,
    Disconnected = 2,
    Error = 3,
};

enum class ViewerActionId {
    WorkspacePreview = 0,
    WorkspacePlayback,
    RefreshCatalog,
    FocusSearch,
    ToggleFavorite,
    SaveView,
    ChangePreviewLayout,
    ChangePlaybackLayout,
    StopAllPreview,
    StopAllPlayback,
    ToggleFullScreen,
    RestoreWindow,
    ClearSelectedTile,
    ToggleTour,
    TourPrevious,
    TourNext,
    PlaybackPause,
    PlaybackResume,
    PlaybackSync,
    ShowResourceCatalog,
    ShowPtz,
    ShowPlaybackSearch,
    ShowRecordingTimeline,
    LockDockLayout,
    RestoreDefaultLayout,
    ChangePassword,
    Logout,
    ExitApplication,
};

Q_DECLARE_METATYPE(WorkspaceMode)
Q_DECLARE_METATYPE(DockPanelId)
Q_DECLARE_METATYPE(ViewerConnectionState)
Q_DECLARE_METATYPE(ViewerActionId)
