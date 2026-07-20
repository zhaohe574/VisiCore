#pragma once

#include "models.h"
#include "bookmark_store.h"
#include "viewer_startup_state.h"
#include "viewer_ui_types.h"

#include <QElapsedTimer>
#include <QHash>
#include <QMainWindow>
#include <QMargins>
#include <QSet>
#include <optional>

class ApiClient;
class CameraTreeWidget;
class DockLayoutController;
class ExportController;
class ExportTasksPanel;
class PlaybackController;
class PreviewController;
class PtzController;
class QFrame;
class QGridLayout;
class QLabel;
class QLineEdit;
class QDateTimeEdit;
class QComboBox;
class QEvent;
class QAction;
class QMenu;
class QPushButton;
class QResizeEvent;
class QStackedWidget;
class QTimer;
class QTabBar;
class QSlider;
class QTreeWidgetItem;
class QSpinBox;
class QToolButton;
class QVBoxLayout;
class RecordingTimelineWidget;
class VideoTileWidget;
class ViewerUiSettings;
class ViewerActionRegistry;
class WindowTitleBar;
class WorkspaceController;

class MainWindow final : public QMainWindow {
    Q_OBJECT

public:
    explicit MainWindow(
        ApiClient *apiClient,
        ViewerStartupMode startupMode = ViewerStartupMode::Normal,
        QWidget *parent = nullptr);
    ~MainWindow() override;

    QList<QAction *> dockPanelActions() const;
    bool isDockLayoutLocked() const;
    bool isCanvasFullScreen() const;

public slots:
    void showDockPanel(DockPanelId panelId, bool visible = true);
    void setDockLayoutLocked(bool locked);
    void resetDockLayout();

signals:
    void logoutRequested();
    void reauthenticationRequested();

protected:
    void closeEvent(QCloseEvent *event) override;
    void changeEvent(QEvent *event) override;
    bool nativeEvent(const QByteArray &eventType, void *message, qintptr *result) override;
    void resizeEvent(QResizeEvent *event) override;

private slots:
    void populateCatalog(const QList<RegionInfo> &regions, const QList<CameraInfo> &cameras);
    void applyCameraStatuses(const QList<CameraStatusInfo> &statuses);
    void assignCameraFromTree(QTreeWidgetItem *item, int column);
    void changeLayout(int count);
    void filterCatalog(const QString &query);
    void selectTile(VideoTileWidget *tile);
    void handleSessionCreated(const QUuid &requestId, const StreamSessionInfo &session);
    void handleSessionFailed(const QUuid &requestId, const QString &message);
    void toggleSelectedFavorite();
    void toggleTour(bool enabled);
    void advanceTour();
    void handleForcedPasswordChangeRequired();
    void openChangePasswordDialog();
    void requestLogout();
    void switchWorkspace(int index);
    void changePlaybackLayout(int count);
    void selectPlaybackTile(VideoTileWidget *tile);
    void refreshPlaybackSearches();
    void handleRecordingSearchCompleted(const QUuid &requestId, const QUuid &cameraId, const QList<RecordingSegment> &segments);
    void handleRecordingSearchFailed(const QUuid &requestId, const QUuid &cameraId, const QString &message);
    void seekPlayback(const QDateTime &position);
    bool controlPlayback(const QString &action, double speed = 0.0);
    void handlePlaybackControlQueued(const QUuid &sessionId, const PlaybackTransportInfo &transport);
    void handlePlaybackControlFailed(const QUuid &sessionId, const QString &message);
    void handleCatalogContextMenu(const QPoint &position);

private:
    enum class CanvasPresentationState {
        Idle,
        Entering,
        Active,
        Exiting,
    };

    struct CanvasPresentationSnapshot {
        bool wasMaximized = false;
        bool titleBarVisible = true;
        bool statusBarVisible = true;
        bool previewToolbarVisible = true;
        bool previewControlStripVisible = true;
        bool playbackToolbarVisible = true;
        QMargins workspaceMargins;
        int workspaceSpacing = 0;
    };

    struct SavedViewSlot {
        int index = 0;
        QUuid cameraId;
        QString profile;
    };

    struct SavedView {
        QString name;
        int layout = 4;
        QString streamMode = QStringLiteral("auto");
        QList<SavedViewSlot> assignments;
    };

    struct PlaybackControlBatch {
        QString action;
        QSet<QUuid> pending;
        QSet<QUuid> succeeded;
        QHash<QUuid, QString> failed;
    };

    struct WorkspaceTransition {
        int targetIndex = 0;
        bool sourcePlayback = false;
        QList<VideoTileWidget *> pendingTiles;
        int nextTileIndex = 0;
    };

    struct PendingInstantPlayback {
        VideoTileWidget *target = nullptr;
        CameraInfo camera;
        QDateTime startedAt;
        QDateTime endedAt;
    };

    QWidget *buildSidebar();
    QWidget *buildWorkspace();
    QWidget *buildPreviewWorkspace();
    QWidget *buildPlaybackWorkspace();
    QWidget *buildPlaybackSearchPanel();
    QWidget *buildRecordingTimelinePanel();
    QWidget *buildExportTasksPanel();
    QWidget *buildToolbar();
    QWidget *buildControlStrip();
    QWidget *buildPtzPanel();
    void initializeActions();
    void refreshControllerActionStates();
    [[nodiscard]] bool canInteractWithWorkspace(WorkspaceMode mode) const;
    [[nodiscard]] bool ensureWorkspaceInteraction(WorkspaceMode mode, const QString &operation);
    void showOperationFeedback(const QString &message, WorkspaceMode preferredWorkspace);
    void updateWorkspaceInteractionControls();
    void updateResponsiveToolbar();
    void requestCamera(VideoTileWidget *tile, const CameraInfo &camera, const QString &profileOverride = {});
    void requestPlayback(VideoTileWidget *tile, const CameraInfo &camera, const QDateTime &startedAt, const QDateTime &endedAt);
    void assignCameraIds(VideoTileWidget *startTile, const QList<QUuid> &cameraIds, bool adaptLayout);
    void assignSingleCamera(VideoTileWidget *tile, const CameraInfo &camera);
    void restartTile(VideoTileWidget *tile);
    void openInstantPlayback(VideoTileWidget *tile, int seconds);
    void toggleTileMaximized(VideoTileWidget *tile);
    void restoreTileLayout(bool playback);
    void stopAllPreview(bool clearAssignments = true);
    void stopAllPlayback(bool clearAssignments = true);
    void toggleCanvasFullScreen();
    void enterCanvasFullScreen();
    void exitCanvasFullScreen();
    void finishCanvasFullScreenEntry();
    void finishCanvasFullScreenExit(bool forced = false);
    void applyCanvasPresentationShell();
    void restoreCanvasPresentationShell();
    void handleCanvasPresentationTimeout();
    void forceExitCanvasFullScreenForShutdown();
    void resumePendingWorkspaceSwitch();
    [[nodiscard]] bool isCanvasPresentationBusy() const;
    void setStreamProfileMode(const QString &mode);
    void updatePlaybackTimeline();
    void updatePlaybackControlState();
    void updatePlaybackCalendarMarks();
    void updatePlaybackSearchSummary();
    QList<VideoTileWidget *> playbackControlTargets() const;
    QDateTime estimatedPlaybackPosition(const QUuid &sessionId) const;
    void anchorPlaybackClock(const QUuid &sessionId, const PlaybackTransportInfo &transport, bool advancing);
    void freezePlaybackClock(const QUuid &sessionId);
    void handleTilePlaybackState(VideoTileWidget *tile, bool playing);
    void handleTileMediaPosition(VideoTileWidget *tile, double seconds);
    void finishPlaybackControlBatchIfReady();
    void releaseWorkspaceTile(bool playbackWorkspace, VideoTileWidget *tile);
    void processWorkspaceReleaseStep();
    void finishWorkspaceTransition();
    void setWorkspaceInteractionEnabled(bool enabled);
    void syncPreviewSessionState();
    void updatePtzControlState();
    void beginPtzPulse(int action);
    void endPtzPulse(int action);
    void stopActivePtzPulse();
    void captureTileScreenshot(VideoTileWidget *tile);
    void addPlaybackBookmark(VideoTileWidget *tile);
    void openPlaybackBookmark(int bookmarkIndex);
    void deletePlaybackBookmark(int bookmarkIndex);
    void loadPlaybackBookmarks();
    void savePlaybackBookmarks();
    void refreshPlaybackExports();
    void requestPlaybackExport();
    void downloadPlaybackExport(const QUuid &exportId);
    void loadFavorites();
    void saveFavorites() const;
    void rebuildCatalog();
    void updateCatalogSummary();
    void refreshCatalogStatusPresentation();
    void syncAssignedCameraStatuses();
    QList<QUuid> cameraIdsForItem(QTreeWidgetItem *item) const;
    QList<CameraInfo> camerasForTour() const;
    bool isRegionWithin(const QUuid &candidateRegionId, const QUuid &ancestorRegionId) const;
    void saveCurrentView();
    void applySavedView(int viewIndex);
    void deleteSavedView(int viewIndex);
    void loadSavedViews();
    void saveSavedViews() const;
    void loadPreferences();
    void savePreferences() const;
    void restoreWindowGeometry();
    void ensureWindowOnAvailableScreen();
    void updateFavoriteState();
    void stopTour();
    bool prepareForSessionEnd();
    QList<CameraInfo> favoriteCameras() const;
    QList<VideoTileWidget *> activePlaybackTiles() const;
    VideoTileWidget *findTileByRequest(const QUuid &requestId) const;
    VideoTileWidget *findTileBySession(const QUuid &sessionId) const;
    VideoTileWidget *firstAvailableTile() const;
    VideoTileWidget *firstAvailablePlaybackTile() const;
    void updatePtzState();
    bool filterItem(QTreeWidgetItem *item, const QString &query);

    ApiClient *apiClient_;
    ViewerStartupMode startupMode_ = ViewerStartupMode::Normal;
    CameraTreeWidget *catalogTree_ = nullptr;
    QLineEdit *searchEdit_ = nullptr;
    QTabBar *resourceTabs_ = nullptr;
    QLabel *catalogSummaryLabel_ = nullptr;
    QGridLayout *videoGrid_ = nullptr;
    QGridLayout *playbackGrid_ = nullptr;
    QLabel *statusLabel_ = nullptr;
    QLabel *playbackStatusLabel_ = nullptr;
    QToolButton *favoriteButton_ = nullptr;
    QToolButton *previewLayoutButton_ = nullptr;
    QToolButton *playbackLayoutButton_ = nullptr;
    QToolButton *ptzPanelButton_ = nullptr;
    QToolButton *previewOverflowButton_ = nullptr;
    QToolButton *tourButton_ = nullptr;
    QToolButton *tourPreviousButton_ = nullptr;
    QToolButton *tourNextButton_ = nullptr;
    QSpinBox *tourIntervalSpin_ = nullptr;
    QComboBox *tourSourceCombo_ = nullptr;
    QComboBox *streamProfileCombo_ = nullptr;
    QStackedWidget *workspaceStack_ = nullptr;
    QVBoxLayout *workspaceLayout_ = nullptr;
    QWidget *previewToolbar_ = nullptr;
    QWidget *previewControlStrip_ = nullptr;
    QWidget *playbackToolbar_ = nullptr;
    QDateTimeEdit *playbackStartedAt_ = nullptr;
    QDateTimeEdit *playbackEndedAt_ = nullptr;
    RecordingTimelineWidget *recordingTimeline_ = nullptr;
    QToolButton *playbackPauseButton_ = nullptr;
    QToolButton *playbackResumeButton_ = nullptr;
    QToolButton *playbackSyncButton_ = nullptr;
    QComboBox *playbackSpeedCombo_ = nullptr;
    QLabel *timelineZoomLabel_ = nullptr;
    QFrame *ptzPanel_ = nullptr;
    QWidget *playbackSearchPanel_ = nullptr;
    QWidget *recordingTimelinePanel_ = nullptr;
    ExportTasksPanel *exportTasksPanel_ = nullptr;
    QLabel *ptzStatusLabel_ = nullptr;
    QSlider *ptzSpeedSlider_ = nullptr;
    QMenu *dockPanelsMenu_ = nullptr;
    QAction *dockLayoutLockAction_ = nullptr;
    DockLayoutController *dockLayoutController_ = nullptr;
    ViewerUiSettings *uiSettings_ = nullptr;
    ViewerActionRegistry *actionRegistry_ = nullptr;
    WindowTitleBar *titleBar_ = nullptr;
    WorkspaceController *workspaceController_ = nullptr;
    PreviewController *previewController_ = nullptr;
    PlaybackController *playbackController_ = nullptr;
    PtzController *ptzController_ = nullptr;
    ExportController *exportController_ = nullptr;
    QList<QWidget *> previewCompactWidgets_;
    QList<RegionInfo> regions_;
    QList<CameraInfo> cameras_;
    QList<SavedView> savedViews_;
    QList<PlaybackBookmark> playbackBookmarks_;
    QSet<QUuid> favoriteCameraIds_;
    QList<VideoTileWidget *> tiles_;
    QList<VideoTileWidget *> playbackTiles_;
    VideoTileWidget *selectedTile_ = nullptr;
    VideoTileWidget *selectedPlaybackTile_ = nullptr;
    VideoTileWidget *maximizedPreviewTile_ = nullptr;
    VideoTileWidget *maximizedPlaybackTile_ = nullptr;
    QList<QToolButton *> ptzMotionButtons_;
    QToolButton *ptzStopButton_ = nullptr;
    QList<QAction *> previewLayoutActions_;
    QList<QAction *> playbackLayoutActions_;
    QList<QAction *> dockPanelToggleActions_;
    QTimer *tourTimer_ = nullptr;
    QUuid currentRegionId_;
    int layoutCount_ = 4;
    int playbackLayoutCount_ = 1;
    int tourCursor_ = 0;
    bool sessionEnding_ = false;
    bool forcedPasswordDialogOpen_ = false;
    int activeWorkspace_ = 0;
    int catalogMode_ = 0;
    int tourIntervalSeconds_ = 15;
    QString streamProfileMode_ = QStringLiteral("auto");
    QString tourSourceMode_ = QStringLiteral("favorites");
    bool ptzPanelVisible_ = true;
    CanvasPresentationState canvasPresentationState_ = CanvasPresentationState::Idle;
    CanvasPresentationSnapshot canvasPresentationSnapshot_;
    QTimer *canvasPresentationTimer_ = nullptr;
    bool canvasPresentationShellApplied_ = false;
    std::optional<int> pendingCanvasWorkspaceIndex_;
    QHash<QUuid, QUuid> playbackSearchRequests_;
    QHash<QUuid, QList<RecordingSegment>> playbackSegments_;
    QHash<QUuid, QString> playbackSearchStates_;
    QSet<QDate> markedRecordingDates_;
    QHash<QUuid, PlaybackTransportInfo> playbackTransport_;
    QSet<QUuid> playbackControlsInFlight_;
    QSet<QUuid> playbackAdvancingSessions_;
    QHash<QUuid, qint64> playbackClockAnchoredAt_;
    QHash<QUuid, double> playbackMediaOriginSeconds_;
    QHash<QUuid, double> playbackMediaLastSeconds_;
    QHash<QUuid, QDateTime> playbackMediaOriginPositions_;
    std::optional<PlaybackControlBatch> playbackControlBatch_;
    VideoTileWidget *playbackTileToSkipOnNextRestore_ = nullptr;
    bool suppressLayoutSessionRestore_ = false;
    std::optional<WorkspaceTransition> workspaceTransition_;
    std::optional<PendingInstantPlayback> pendingInstantPlayback_;
    std::optional<int> pendingSavedViewIndex_;
    QElapsedTimer playbackMonotonicClock_;
    QDateTime playbackCursor_;
    QTimer *playbackCursorTimer_ = nullptr;
    QTimer *playbackTransportRefreshTimer_ = nullptr;
    QTimer *cameraStatusRefreshTimer_ = nullptr;
    QTimer *exportRefreshTimer_ = nullptr;
    QDateTime pendingBookmarkSeekPosition_;
    VideoTileWidget *pendingBookmarkSeekTile_ = nullptr;
};
