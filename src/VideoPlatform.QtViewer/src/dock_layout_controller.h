#pragma once

#include "viewer_ui_types.h"

#include <QByteArray>
#include <QHash>
#include <QList>
#include <QObject>
#include <QSet>

class QAction;
class QMainWindow;
class QWidget;

namespace ads {
class CDockManager;
class CDockWidget;
}

class DockLayoutController final : public QObject {
    Q_OBJECT

public:
    struct PanelWidgets {
        QWidget *resourceCatalog = nullptr;
        QWidget *ptz = nullptr;
        QWidget *playbackSearch = nullptr;
        QWidget *recordingTimeline = nullptr;
    };

    explicit DockLayoutController(QMainWindow *window, QObject *parent = nullptr);

    void initialize(QWidget *centralCanvas, const PanelWidgets &panels, bool showPtzByDefault);
    void setStoredState(WorkspaceMode mode, const QByteArray &state);
    void switchWorkspace(WorkspaceMode mode, const QByteArray &storedState = {});

    WorkspaceMode workspaceMode() const;
    QByteArray stateFor(WorkspaceMode mode) const;
    QList<QAction *> dockPanelActions() const;
    QAction *dockPanelAction(DockPanelId panelId) const;

    bool isPanelVisible(DockPanelId panelId) const;
    bool isLocked() const;
    bool isInteractionFrozen() const;
    bool isCanvasOnly() const;

public slots:
    void showDockPanel(DockPanelId panelId, bool visible = true);
    void setLocked(bool locked);
    void setInteractionFrozen(bool frozen);
    void setCanvasOnly(bool canvasOnly);
    void resetCurrentLayout();

signals:
    void panelVisibilityChanged(DockPanelId panelId, bool visible);
    void lockedChanged(bool locked);
    void layoutRestoredFromDefault(WorkspaceMode mode);

private:
    static constexpr int DockStateVersion = 2;

    ads::CDockWidget *createPanel(DockPanelId panelId, const QString &title, QWidget *content);
    ads::CDockWidget *panel(DockPanelId panelId) const;
    QByteArray defaultState(WorkspaceMode mode) const;
    bool restoreStateOrDefault(WorkspaceMode mode, const QByteArray &state);
    bool floatingWindowsAreReachable() const;
    void captureCurrentState();
    void applyDefaultPanelSizes(WorkspaceMode mode);
    void normalizeVisiblePanelSizes(WorkspaceMode mode);
    void schedulePanelSizeNormalization(WorkspaceMode mode, bool applyDefaultSizes);
    void ensurePanelExtent(DockPanelId panelId, Qt::Orientation orientation, int minimumExtent);
    void applyDockFeatureLock();
    void syncDockPanelActionAvailability();

    QMainWindow *window_ = nullptr;
    ads::CDockManager *dockManager_ = nullptr;
    ads::CDockWidget *centralDock_ = nullptr;
    ads::CDockWidget *resourceDock_ = nullptr;
    ads::CDockWidget *ptzDock_ = nullptr;
    ads::CDockWidget *playbackSearchDock_ = nullptr;
    ads::CDockWidget *recordingTimelineDock_ = nullptr;
    QHash<int, QByteArray> workspaceStates_;
    QHash<int, QByteArray> defaultStates_;
    QSet<int> loadedWorkspaceStates_;
    WorkspaceMode workspaceMode_ = WorkspaceMode::Preview;
    bool initialized_ = false;
    bool locked_ = false;
    bool interactionFrozen_ = false;
    bool canvasOnly_ = false;
    // 画布模式仅临时隐藏工具面板，退出时按进入前的可见性恢复，绝不恢复 ADS 整体布局。
    QHash<int, bool> canvasOnlyPanelVisibility_;
    // 保存进入画布模式前的持久布局，供 stateFor() 在隐藏面板期间返回。
    QByteArray canvasOnlyState_;
};
