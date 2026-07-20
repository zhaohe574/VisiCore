#include "dock_layout_controller.h"

#include <DockAreaWidget.h>
#include <DockManager.h>
#include <DockSplitter.h>
#include <DockWidget.h>
#include <FloatingDockContainer.h>

#include <algorithm>
#include <numeric>
#include <QAction>
#include <QGuiApplication>
#include <QMainWindow>
#include <QRect>
#include <QScreen>
#include <QSplitter>
#include <QTimer>
#include <QWidget>

namespace {
constexpr int ResourcePanelWidth = 248;
constexpr int PtzPanelWidth = 220;
constexpr int SearchPanelHeight = 80;
constexpr int TimelinePanelHeight = 250;
constexpr int ExportTasksPanelHeight = 230;
constexpr int MinimumCentralWidth = 420;
constexpr int MinimumCentralHeight = 300;

int workspaceKey(WorkspaceMode mode) {
    return static_cast<int>(mode);
}

QWidget *directSplitterChild(QWidget *widget, QSplitter *splitter) {
    QWidget *candidate = widget;
    while (candidate != nullptr && candidate->parentWidget() != splitter) {
        candidate = candidate->parentWidget();
    }
    return candidate != nullptr && candidate->parentWidget() == splitter ? candidate : nullptr;
}

int dockIndexInSplitter(ads::CDockWidget *dock, QSplitter *splitter) {
    if (dock == nullptr || dock->dockAreaWidget() == nullptr || splitter == nullptr) {
        return -1;
    }
    QWidget *item = directSplitterChild(dock->dockAreaWidget(), splitter);
    return item != nullptr ? splitter->indexOf(item) : -1;
}
}

DockLayoutController::DockLayoutController(QMainWindow *window, QObject *parent)
    : QObject(parent), window_(window) {
}

void DockLayoutController::initialize(
    QWidget *centralCanvas,
    const PanelWidgets &panels,
    bool showPtzByDefault) {
    if (initialized_ || window_ == nullptr || centralCanvas == nullptr) {
        return;
    }

    ads::CDockManager::setConfigFlag(ads::CDockManager::HideSingleCentralWidgetTitleBar, true);
    ads::CDockManager::setConfigFlag(ads::CDockManager::FocusHighlighting, true);
    ads::CDockManager::setConfigFlag(ads::CDockManager::DockAreaHideDisabledButtons, true);
    ads::CDockManager::setAutoHideConfigFlags(ads::CDockManager::DefaultAutoHideConfig);

    dockManager_ = new ads::CDockManager(window_);
    dockManager_->setObjectName(QStringLiteral("workspaceDockManager"));
    dockManager_->setColorSchemeMode(ads::CDockManager::ColorSchemeMode::Dark);
    dockManager_->setStyleSheet(dockManager_->styleSheet() + QStringLiteral(R"(
        ads--CDockWidgetTab[activeTab="false"] QLabel#dockWidgetTabLabel {
            color: #B8C1C4;
        }
        ads--CDockWidgetTab[activeTab="false"]:hover QLabel#dockWidgetTabLabel {
            color: #F5F8F9;
        }
        ads--CDockWidgetTab[activeTab="true"] QLabel#dockWidgetTabLabel {
            color: #FFFFFF;
        }
    )"));

    centralDock_ = dockManager_->createDockWidget(QStringLiteral("视频画布"));
    centralDock_->setObjectName(QStringLiteral("dock.centralCanvas"));
    centralDock_->setWidget(centralCanvas, ads::CDockWidget::ForceNoScrollArea);
    dockManager_->setCentralWidget(centralDock_);

    resourceDock_ = createPanel(DockPanelId::ResourceCatalog, QStringLiteral("资源"), panels.resourceCatalog);
    ptzDock_ = createPanel(DockPanelId::Ptz, QStringLiteral("云台控制"), panels.ptz);
    playbackSearchDock_ = createPanel(DockPanelId::PlaybackSearch, QStringLiteral("回放检索"), panels.playbackSearch);
    recordingTimelineDock_ = createPanel(DockPanelId::RecordingTimeline, QStringLiteral("录像时间轴"), panels.recordingTimeline);
    exportTasksDock_ = createPanel(DockPanelId::ExportTasks, QStringLiteral("导出任务"), panels.exportTasks);

    dockManager_->addDockWidget(ads::LeftDockWidgetArea, resourceDock_);
    dockManager_->addDockWidget(ads::RightDockWidgetArea, ptzDock_);
    auto *searchArea = dockManager_->addDockWidget(ads::BottomDockWidgetArea, playbackSearchDock_);
    dockManager_->addDockWidget(ads::BottomDockWidgetArea, recordingTimelineDock_, searchArea);
    dockManager_->addDockWidget(ads::BottomDockWidgetArea, exportTasksDock_, searchArea);

    resourceDock_->setPreferredAutoHideSideBarLocation(ads::SideBarLeft);
    ptzDock_->setPreferredAutoHideSideBarLocation(ads::SideBarRight);
    playbackSearchDock_->setPreferredAutoHideSideBarLocation(ads::SideBarBottom);
    recordingTimelineDock_->setPreferredAutoHideSideBarLocation(ads::SideBarBottom);
    exportTasksDock_->setPreferredAutoHideSideBarLocation(ads::SideBarBottom);

    resourceDock_->toggleView(true);
    ptzDock_->toggleView(showPtzByDefault);
    playbackSearchDock_->toggleView(false);
    recordingTimelineDock_->toggleView(false);
    exportTasksDock_->toggleView(false);
    applyDefaultPanelSizes(WorkspaceMode::Preview);
    defaultStates_.insert(workspaceKey(WorkspaceMode::Preview), dockManager_->saveState(DockStateVersion));

    ptzDock_->toggleView(false);
    playbackSearchDock_->toggleView(true);
    recordingTimelineDock_->toggleView(true);
    exportTasksDock_->toggleView(true);
    applyDefaultPanelSizes(WorkspaceMode::Playback);
    defaultStates_.insert(workspaceKey(WorkspaceMode::Playback), dockManager_->saveState(DockStateVersion));

    initialized_ = true;
    applyDockFeatureLock();
    restoreStateOrDefault(WorkspaceMode::Preview, {});
}

void DockLayoutController::setStoredState(WorkspaceMode mode, const QByteArray &state) {
    if (!state.isEmpty()) {
        workspaceStates_.insert(workspaceKey(mode), state);
    }
}

void DockLayoutController::switchWorkspace(WorkspaceMode mode, const QByteArray &storedState) {
    if (!initialized_ || dockManager_ == nullptr) {
        workspaceMode_ = mode;
        return;
    }
    if (canvasOnly_) {
        setCanvasOnly(false);
    }
    if (mode == workspaceMode_ && storedState.isEmpty()) {
        return;
    }

    captureCurrentState();
    workspaceMode_ = mode;
    const int key = workspaceKey(mode);
    const QByteArray state = loadedWorkspaceStates_.contains(key)
        ? workspaceStates_.value(key)
        : !storedState.isEmpty() ? storedState : workspaceStates_.value(key);
    restoreStateOrDefault(mode, state);
    loadedWorkspaceStates_.insert(key);
}

WorkspaceMode DockLayoutController::workspaceMode() const {
    return workspaceMode_;
}

QByteArray DockLayoutController::stateFor(WorkspaceMode mode) const {
    // 画布全屏只是临时呈现状态，绝不能把“所有工具面板隐藏”写回用户布局。
    if (canvasOnly_ && mode == workspaceMode_ && !canvasOnlyState_.isEmpty()) {
        return canvasOnlyState_;
    }
    if (dockManager_ != nullptr && initialized_ && mode == workspaceMode_) {
        return dockManager_->saveState(DockStateVersion);
    }
    return workspaceStates_.value(workspaceKey(mode), defaultState(mode));
}

QList<QAction *> DockLayoutController::dockPanelActions() const {
    QList<QAction *> actions;
    for (const DockPanelId panelId : {
             DockPanelId::ResourceCatalog,
             DockPanelId::Ptz,
             DockPanelId::PlaybackSearch,
             DockPanelId::RecordingTimeline,
             DockPanelId::ExportTasks}) {
        if (QAction *action = dockPanelAction(panelId)) {
            actions.append(action);
        }
    }
    return actions;
}

QAction *DockLayoutController::dockPanelAction(DockPanelId panelId) const {
    ads::CDockWidget *dock = panel(panelId);
    return dock != nullptr ? dock->toggleViewAction() : nullptr;
}

bool DockLayoutController::isPanelVisible(DockPanelId panelId) const {
    const ads::CDockWidget *dock = panel(panelId);
    return dock != nullptr && !dock->isClosed();
}

bool DockLayoutController::isLocked() const {
    return locked_;
}

bool DockLayoutController::isInteractionFrozen() const {
    return interactionFrozen_;
}

bool DockLayoutController::isCanvasOnly() const {
    return canvasOnly_;
}

void DockLayoutController::showDockPanel(DockPanelId panelId, bool visible) {
    if (interactionFrozen_ || canvasOnly_) {
        return;
    }
    ads::CDockWidget *dock = panel(panelId);
    if (dock == nullptr) {
        return;
    }
    dock->toggleView(visible);
    if (visible) {
        dock->raise();
    }
}

void DockLayoutController::setLocked(bool locked) {
    if (interactionFrozen_ || locked_ == locked) {
        return;
    }
    locked_ = locked;
    applyDockFeatureLock();
    emit lockedChanged(locked_);
}

void DockLayoutController::setInteractionFrozen(bool frozen) {
    if (interactionFrozen_ == frozen) {
        return;
    }
    interactionFrozen_ = frozen;
    applyDockFeatureLock();
    syncDockPanelActionAvailability();
}

void DockLayoutController::setCanvasOnly(bool canvasOnly) {
    if (!initialized_ || dockManager_ == nullptr || canvasOnly_ == canvasOnly) {
        return;
    }

    if (canvasOnly) {
        // stateFor() 必须继续返回进入前的布局，不能把临时隐藏状态保存到用户设置。
        captureCurrentState();
        canvasOnlyState_ = workspaceStates_.value(
            workspaceKey(workspaceMode_),
            dockManager_->saveState(DockStateVersion));
        canvasOnlyPanelVisibility_.clear();
        canvasOnly_ = true;
        syncDockPanelActionAvailability();
        for (const DockPanelId panelId : {
                 DockPanelId::ResourceCatalog,
                 DockPanelId::Ptz,
                 DockPanelId::PlaybackSearch,
                 DockPanelId::RecordingTimeline,
                 DockPanelId::ExportTasks}) {
            ads::CDockWidget *dock = panel(panelId);
            if (dock != nullptr) {
                canvasOnlyPanelVisibility_.insert(static_cast<int>(panelId), !dock->isClosed());
                dock->toggleView(false);
            }
        }
        return;
    }

    // 不调用 restoreState()：全屏期间只改变了四个面板的可见性，恢复它们即可保持原停靠拓扑。
    const QHash<int, bool> panelVisibility = canvasOnlyPanelVisibility_;
    for (const DockPanelId panelId : {
             DockPanelId::ResourceCatalog,
             DockPanelId::Ptz,
             DockPanelId::PlaybackSearch,
             DockPanelId::RecordingTimeline,
             DockPanelId::ExportTasks}) {
        ads::CDockWidget *dock = panel(panelId);
        const auto visibility = panelVisibility.constFind(static_cast<int>(panelId));
        if (dock == nullptr || visibility == panelVisibility.cend() ||
            ((!dock->isClosed()) == visibility.value())) {
            continue;
        }
        dock->toggleView(visibility.value());
    }

    canvasOnly_ = false;
    canvasOnlyPanelVisibility_.clear();
    canvasOnlyState_.clear();
    syncDockPanelActionAvailability();
}

void DockLayoutController::resetCurrentLayout() {
    if (interactionFrozen_ || canvasOnly_ || !initialized_ || dockManager_ == nullptr) {
        return;
    }
    restoreStateOrDefault(workspaceMode_, {});
    workspaceStates_.insert(workspaceKey(workspaceMode_), dockManager_->saveState(DockStateVersion));
}

ads::CDockWidget *DockLayoutController::createPanel(
    DockPanelId panelId,
    const QString &title,
    QWidget *content) {
    auto *dock = dockManager_->createDockWidget(title);
    dock->setObjectName(QStringLiteral("dock.panel.%1").arg(static_cast<int>(panelId)));
    dock->setWidget(content, ads::CDockWidget::ForceNoScrollArea);
    dock->setMinimumSizeHintMode(ads::CDockWidget::MinimumSizeHintFromContentMinimumSize);
    dock->setFeatures(ads::CDockWidget::DefaultDockWidgetFeatures);
    QAction *toggleAction = dock->toggleViewAction();
    toggleAction->setObjectName(QStringLiteral("dock.panel.action.%1").arg(static_cast<int>(panelId)));
    toggleAction->setData(static_cast<int>(panelId));
    connect(dock, &ads::CDockWidget::viewToggled, this, [this, panelId](bool visible) {
        emit panelVisibilityChanged(panelId, visible);
        if (visible && initialized_) {
            schedulePanelSizeNormalization(workspaceMode_, false);
        }
    });
    return dock;
}

ads::CDockWidget *DockLayoutController::panel(DockPanelId panelId) const {
    switch (panelId) {
        case DockPanelId::ResourceCatalog: return resourceDock_;
        case DockPanelId::Ptz: return ptzDock_;
        case DockPanelId::PlaybackSearch: return playbackSearchDock_;
        case DockPanelId::RecordingTimeline: return recordingTimelineDock_;
        case DockPanelId::ExportTasks: return exportTasksDock_;
    }
    return nullptr;
}

QByteArray DockLayoutController::defaultState(WorkspaceMode mode) const {
    return defaultStates_.value(workspaceKey(mode));
}

bool DockLayoutController::restoreStateOrDefault(WorkspaceMode mode, const QByteArray &state) {
    const QByteArray fallback = defaultState(mode);
    bool restored = !state.isEmpty() && dockManager_->restoreState(state, DockStateVersion);
    if (restored && !floatingWindowsAreReachable()) {
        restored = false;
    }
    if (!restored) {
        dockManager_->restoreState(fallback, DockStateVersion);
        emit layoutRestoredFromDefault(mode);
    }
    schedulePanelSizeNormalization(mode, !restored);
    workspaceStates_.insert(workspaceKey(mode), dockManager_->saveState(DockStateVersion));
    return restored;
}

bool DockLayoutController::floatingWindowsAreReachable() const {
    const QList<QScreen *> screens = QGuiApplication::screens();
    for (const ads::CFloatingDockContainer *floating : dockManager_->floatingWidgets()) {
        if (floating == nullptr) {
            continue;
        }
        const QRect frame = floating->frameGeometry();
        if (!frame.isValid() || frame.isEmpty()) {
            continue;
        }
        bool reachable = false;
        for (const QScreen *screen : screens) {
            const QRect visiblePart = frame.intersected(screen->availableGeometry());
            if (visiblePart.width() >= 48 && visiblePart.height() >= 32) {
                reachable = true;
                break;
            }
        }
        if (!reachable) {
            return false;
        }
    }
    return true;
}

void DockLayoutController::captureCurrentState() {
    if (!canvasOnly_ && dockManager_ != nullptr && initialized_) {
        workspaceStates_.insert(workspaceKey(workspaceMode_), dockManager_->saveState(DockStateVersion));
    }
}

void DockLayoutController::applyDefaultPanelSizes(WorkspaceMode mode) {
    if (dockManager_ == nullptr || resourceDock_ == nullptr || centralDock_ == nullptr) {
        return;
    }

    QSplitter *horizontalSplitter = resourceDock_->dockAreaWidget() != nullptr
        ? resourceDock_->dockAreaWidget()->parentSplitter()
        : nullptr;
    if (horizontalSplitter != nullptr && horizontalSplitter->orientation() == Qt::Horizontal) {
        QList<int> sizes = horizontalSplitter->sizes();
        const int resourceIndex = dockIndexInSplitter(resourceDock_, horizontalSplitter);
        const int ptzIndex = dockIndexInSplitter(ptzDock_, horizontalSplitter);
        const int centralIndex = dockIndexInSplitter(centralDock_, horizontalSplitter);
        const int currentTotal = std::accumulate(sizes.cbegin(), sizes.cend(), 0);
        const int total = std::max({currentTotal, horizontalSplitter->width(), 1080});
        if (resourceIndex >= 0) sizes[resourceIndex] = 280;
        if (ptzIndex >= 0) sizes[ptzIndex] = ptzDock_->isClosed() ? 0 : 240;
        if (centralIndex >= 0) {
            const int occupied = std::accumulate(sizes.cbegin(), sizes.cend(), 0) - sizes[centralIndex];
            sizes[centralIndex] = std::max(MinimumCentralWidth, total - occupied);
        }
        horizontalSplitter->setSizes(sizes);
    }

    if (mode != WorkspaceMode::Playback || playbackSearchDock_ == nullptr || recordingTimelineDock_ == nullptr ||
        exportTasksDock_ == nullptr) {
        return;
    }

    QSplitter *verticalSplitter = playbackSearchDock_->dockAreaWidget() != nullptr
        ? playbackSearchDock_->dockAreaWidget()->parentSplitter()
        : nullptr;
    if (verticalSplitter == nullptr || verticalSplitter->orientation() != Qt::Vertical) {
        return;
    }
    QList<int> sizes = verticalSplitter->sizes();
    const int searchIndex = dockIndexInSplitter(playbackSearchDock_, verticalSplitter);
    const int timelineIndex = dockIndexInSplitter(recordingTimelineDock_, verticalSplitter);
    const int exportTasksIndex = dockIndexInSplitter(exportTasksDock_, verticalSplitter);
    const int centralIndex = dockIndexInSplitter(centralDock_, verticalSplitter);
    const int currentTotal = std::accumulate(sizes.cbegin(), sizes.cend(), 0);
    const int total = std::max({currentTotal, verticalSplitter->height(), 680});
    if (searchIndex >= 0) sizes[searchIndex] = SearchPanelHeight;
    if (timelineIndex >= 0) sizes[timelineIndex] = TimelinePanelHeight;
    if (exportTasksIndex >= 0) sizes[exportTasksIndex] = ExportTasksPanelHeight;
    if (centralIndex >= 0) {
        const int occupied = std::accumulate(sizes.cbegin(), sizes.cend(), 0) - sizes[centralIndex];
        sizes[centralIndex] = std::max(MinimumCentralHeight, total - occupied);
    }
    verticalSplitter->setSizes(sizes);
}

void DockLayoutController::normalizeVisiblePanelSizes(WorkspaceMode mode) {
    ensurePanelExtent(DockPanelId::ResourceCatalog, Qt::Horizontal, ResourcePanelWidth);
    ensurePanelExtent(DockPanelId::Ptz, Qt::Horizontal, PtzPanelWidth);
    if (mode == WorkspaceMode::Playback) {
        ensurePanelExtent(DockPanelId::PlaybackSearch, Qt::Vertical, SearchPanelHeight);
        ensurePanelExtent(DockPanelId::RecordingTimeline, Qt::Vertical, TimelinePanelHeight);
        ensurePanelExtent(DockPanelId::ExportTasks, Qt::Vertical, ExportTasksPanelHeight);
    }
}

void DockLayoutController::schedulePanelSizeNormalization(WorkspaceMode mode, bool applyDefaultSizes) {
    QTimer::singleShot(0, this, [this, mode, applyDefaultSizes]() {
        if (!initialized_ || dockManager_ == nullptr || canvasOnly_ || mode != workspaceMode_) {
            return;
        }
        if (applyDefaultSizes) {
            applyDefaultPanelSizes(mode);
        }
        normalizeVisiblePanelSizes(mode);
        workspaceStates_.insert(workspaceKey(mode), dockManager_->saveState(DockStateVersion));
    });
}

void DockLayoutController::ensurePanelExtent(
    DockPanelId panelId,
    Qt::Orientation orientation,
    int minimumExtent) {
    ads::CDockWidget *dock = panel(panelId);
    if (dock == nullptr || dock->isClosed() || dock->dockAreaWidget() == nullptr) {
        return;
    }
    QSplitter *splitter = dock->dockAreaWidget()->parentSplitter();
    if (splitter == nullptr || splitter->orientation() != orientation) {
        return;
    }
    QList<int> sizes = splitter->sizes();
    const int targetIndex = dockIndexInSplitter(dock, splitter);
    if (targetIndex < 0 || targetIndex >= sizes.size() || sizes[targetIndex] >= minimumExtent) {
        return;
    }

    const auto minimumForIndex = [this, splitter, orientation](int index) {
        QWidget *item = splitter->widget(index);
        const auto containsDock = [item, splitter](ads::CDockWidget *candidate) {
            return candidate != nullptr && candidate->dockAreaWidget() != nullptr &&
                directSplitterChild(candidate->dockAreaWidget(), splitter) == item;
        };
        if (containsDock(centralDock_)) {
            return orientation == Qt::Horizontal ? MinimumCentralWidth : MinimumCentralHeight;
        }
        if (orientation == Qt::Horizontal && containsDock(resourceDock_)) return ResourcePanelWidth;
        if (orientation == Qt::Horizontal && containsDock(ptzDock_)) return PtzPanelWidth;
        if (orientation == Qt::Vertical && containsDock(playbackSearchDock_)) return SearchPanelHeight;
        if (orientation == Qt::Vertical && containsDock(recordingTimelineDock_)) return TimelinePanelHeight;
        if (orientation == Qt::Vertical && containsDock(exportTasksDock_)) return ExportTasksPanelHeight;
        return 60;
    };

    int deficit = minimumExtent - sizes[targetIndex];
    while (deficit > 0) {
        int donorIndex = -1;
        int donorCapacity = 0;
        for (int index = 0; index < sizes.size(); ++index) {
            if (index == targetIndex) continue;
            const int capacity = sizes[index] - minimumForIndex(index);
            if (capacity > donorCapacity) {
                donorCapacity = capacity;
                donorIndex = index;
            }
        }
        if (donorIndex < 0 || donorCapacity <= 0) {
            break;
        }
        const int transfer = std::min(deficit, donorCapacity);
        sizes[donorIndex] -= transfer;
        sizes[targetIndex] += transfer;
        deficit -= transfer;
    }
    if (sizes[targetIndex] < minimumExtent) sizes[targetIndex] = minimumExtent;
    splitter->setSizes(sizes);
}

void DockLayoutController::applyDockFeatureLock() {
    if (dockManager_ == nullptr) {
        return;
    }
    dockManager_->setEnabled(!interactionFrozen_);
    for (ads::CFloatingDockContainer *floating : dockManager_->floatingWidgets()) {
        if (floating != nullptr) {
            floating->setEnabled(!interactionFrozen_);
        }
    }
    dockManager_->lockDockWidgetFeaturesGlobally(
        (locked_ || interactionFrozen_)
            ? ads::CDockWidget::GloballyLockableFeatures
            : ads::CDockWidget::NoDockWidgetFeatures);
}

void DockLayoutController::syncDockPanelActionAvailability() {
    const bool enabled = !interactionFrozen_ && !canvasOnly_;
    for (QAction *action : dockPanelActions()) {
        action->setEnabled(enabled);
    }
}
