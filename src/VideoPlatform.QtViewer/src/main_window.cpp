#include "main_window.h"

#include "apiclient.h"
#include "app_dialog.h"
#include "camera_tree_widget.h"
#include "change_password_dialog.h"
#include "crash_reporter.h"
#include "dock_layout_controller.h"
#include "icon_provider.h"
#include "playback_controller.h"
#include "preview_controller.h"
#include "ptz_controller.h"
#include "recording_timeline_widget.h"
#include "video_tile_widget.h"
#include "viewer_action_registry.h"
#include "viewer_logic.h"
#include "viewer_ui_settings.h"
#include "window_title_bar.h"
#include "workspace_controller.h"

#include <algorithm>
#include <cmath>
#include <utility>
#include <QAction>
#include <QActionGroup>
#include <QAbstractSpinBox>
#include <QApplication>
#include <QCalendarWidget>
#include <QColor>
#include <QComboBox>
#include <QCloseEvent>
#include <QCryptographicHash>
#include <QDateTimeEdit>
#include <QDialog>
#include <QDialogButtonBox>
#include <QEvent>
#include <QFrame>
#include <QFormLayout>
#include <QGridLayout>
#include <QGuiApplication>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QHash>
#include <QIcon>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLabel>
#include <QLineEdit>
#include <QMenu>
#include <QPainter>
#include <QPushButton>
#include <QPixmap>
#include <QSettings>
#include <QScreen>
#include <QScrollArea>
#include <QResizeEvent>
#include <QSignalBlocker>
#include <QSizePolicy>
#include <QSpinBox>
#include <QStackedWidget>
#include <QStatusBar>
#include <QSlider>
#include <QTabBar>
#include <QToolButton>
#include <QTimer>
#include <QTreeWidget>
#include <QTreeWidgetItemIterator>
#include <QTimeZone>
#include <QTextCharFormat>
#include <QVBoxLayout>

namespace {
QString favoriteSettingsKey(const QString &username) {
    return QStringLiteral("viewerFavorites/%1")
        .arg(QString::fromLatin1(QCryptographicHash::hash(username.toUtf8(), QCryptographicHash::Sha256).toHex()));
}

QString accountSettingsPrefix(const QString &username) {
    return ViewerUiSettings::accountSettingsPrefix(username);
}

QString connectivityLabel(int connectivity) {
    switch (connectivity) {
        case 1: return QStringLiteral("在线");
        case 2: return QStringLiteral("疑似离线");
        case 3: return QStringLiteral("离线");
        case 4: return QStringLiteral("恢复中");
        default: return QStringLiteral("状态未知");
    }
}

QColor connectivityColor(int connectivity) {
    switch (connectivity) {
        case 1: return QColor(QStringLiteral("#3BA477"));
        case 2: return QColor(QStringLiteral("#D39A36"));
        case 3: return QColor(QStringLiteral("#D15D5D"));
        case 4: return QColor(QStringLiteral("#4D86B8"));
        default: return QColor(QStringLiteral("#7C878C"));
    }
}

QIcon statusIcon(int connectivity) {
    QPixmap pixmap(12, 12);
    pixmap.fill(Qt::transparent);
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setPen(Qt::NoPen);
    painter.setBrush(connectivityColor(connectivity));
    painter.drawEllipse(2, 2, 8, 8);
    return QIcon(pixmap);
}

QString cameraStatusSummaryText(const ViewerLogic::CameraConnectivitySummary &summary) {
    QStringList parts{QStringLiteral("在线 %1").arg(summary.online)};
    if (summary.unavailable > 0) parts.append(QStringLiteral("离线或异常 %1").arg(summary.unavailable));
    if (summary.recovering > 0) parts.append(QStringLiteral("恢复中 %1").arg(summary.recovering));
    if (summary.unknown > 0) parts.append(QStringLiteral("未检测 %1").arg(summary.unknown));
    parts.append(QStringLiteral("共 %1 路").arg(summary.total));
    return parts.join(QStringLiteral(" · "));
}

QString cameraToolTip(const CameraInfo &camera) {
    const QStringList capabilities{
        camera.canLiveView ? QStringLiteral("可预览") : QStringLiteral("无预览权限"),
        camera.canPlayback ? QStringLiteral("可回放") : QStringLiteral("无回放权限"),
        camera.canControlPtz ? QStringLiteral("可控制云台") : QStringLiteral("无云台控制权限或能力")};
    return QStringLiteral("%1\n编号：%2\n状态：%3\n%4")
        .arg(camera.alias, camera.code, connectivityLabel(camera.connectivity), capabilities.join(QStringLiteral("；")));
}

QString playbackActionLabel(const QString &action) {
    if (action == QStringLiteral("Pause")) return QStringLiteral("暂停");
    if (action == QStringLiteral("Resume")) return QStringLiteral("继续");
    if (action == QStringLiteral("Seek")) return QStringLiteral("定位");
    if (action == QStringLiteral("SetSpeed")) return QStringLiteral("倍速");
    return QStringLiteral("控制");
}

void syncLayoutMenuSelection(QToolButton *button, int count) {
    if (button == nullptr || button->menu() == nullptr) {
        return;
    }
    for (QAction *action : button->menu()->actions()) {
        if (action == nullptr || !action->isCheckable()) {
            continue;
        }
        const QSignalBlocker blocker(action);
        action->setChecked(action->data().toInt() == count);
    }
}

void setEnabledWithReason(QWidget *widget, bool enabled, const QString &reason) {
    if (widget == nullptr) {
        return;
    }
    constexpr auto BaseToolTipProperty = "viewerBaseToolTip";
    if (!widget->property(BaseToolTipProperty).isValid()) {
        widget->setProperty(BaseToolTipProperty, widget->toolTip());
    }
    widget->setEnabled(enabled);
    widget->setToolTip(enabled ? widget->property(BaseToolTipProperty).toString() : reason);
}

void setEnabledWithReason(QAction *action, bool enabled, const QString &reason) {
    if (action == nullptr) {
        return;
    }
    constexpr auto BaseToolTipProperty = "viewerBaseToolTip";
    if (!action->property(BaseToolTipProperty).isValid()) {
        action->setProperty(BaseToolTipProperty, action->toolTip());
    }
    action->setEnabled(enabled);
    action->setToolTip(enabled ? action->property(BaseToolTipProperty).toString() : reason);
}

QString workspaceName(WorkspaceMode mode) {
    return mode == WorkspaceMode::Preview ? QStringLiteral("实时预览") : QStringLiteral("录像回放");
}

std::optional<QString> promptText(
    QWidget *parent,
    const QString &title,
    const QString &labelText,
    const QString &initialValue,
    int maximumLength) {
    AppDialog dialog(title, parent);
    dialog.setModal(true);
    dialog.setMinimumWidth(440);

    auto *label = new QLabel(labelText, &dialog);
    auto *editor = new QLineEdit(initialValue, &dialog);
    editor->setMaxLength(maximumLength);
    editor->selectAll();
    auto *buttons = new QDialogButtonBox(
        QDialogButtonBox::Ok | QDialogButtonBox::Cancel,
        &dialog);
    if (auto *confirmButton = buttons->button(QDialogButtonBox::Ok)) {
        confirmButton->setText(QStringLiteral("保存"));
        confirmButton->setDefault(true);
    }
    if (auto *cancelButton = buttons->button(QDialogButtonBox::Cancel)) {
        cancelButton->setText(QStringLiteral("取消"));
    }
    QObject::connect(buttons, &QDialogButtonBox::accepted, &dialog, &QDialog::accept);
    QObject::connect(buttons, &QDialogButtonBox::rejected, &dialog, &QDialog::reject);
    dialog.contentLayout()->addWidget(label);
    dialog.contentLayout()->addWidget(editor);
    dialog.contentLayout()->addWidget(buttons);
    editor->setFocus();

    if (dialog.exec() != QDialog::Accepted) {
        return std::nullopt;
    }
    return editor->text().trimmed().left(maximumLength);
}
}

MainWindow::MainWindow(ApiClient *apiClient, ViewerStartupMode startupMode, QWidget *parent)
    : QMainWindow(parent), apiClient_(apiClient), startupMode_(startupMode) {
    WindowTitleBar::applyFramelessWindow(this);
    setWindowTitle(QStringLiteral("企业视频统一查看端"));
    resize(1500, 920);
    setMinimumSize(1080, 680);
    canvasPresentationTimer_ = new QTimer(this);
    canvasPresentationTimer_->setSingleShot(true);
    canvasPresentationTimer_->setInterval(1800);
    connect(canvasPresentationTimer_, &QTimer::timeout,
            this, &MainWindow::handleCanvasPresentationTimeout);
    actionRegistry_ = new ViewerActionRegistry(this);
    initializeActions();
    workspaceController_ = new WorkspaceController(this);
    previewController_ = new PreviewController(this);
    playbackController_ = new PlaybackController(this);
    ptzController_ = new PtzController(this);
    connect(workspaceController_, &WorkspaceController::stateChanged,
            this, &MainWindow::refreshControllerActionStates);
    connect(previewController_, &PreviewController::stateChanged,
            this, &MainWindow::refreshControllerActionStates);
    connect(playbackController_, &PlaybackController::stateChanged,
            this, &MainWindow::refreshControllerActionStates);
    connect(ptzController_, &PtzController::stateChanged,
            this, &MainWindow::refreshControllerActionStates);
    connect(ptzController_, &PtzController::ptzCommandRequested,
            apiClient_, &ApiClient::requestPtz);
    connect(ptzController_, &PtzController::availabilityChanged,
            this, [this](bool, PtzAvailabilityReason, const QString &) {
        updatePtzControlState();
    });
    connect(ptzController_, &PtzController::pulseStateChanged,
            this, [this](const PtzPulseState &) {
        updatePtzControlState();
    });
    connect(workspaceController_, &WorkspaceController::operationRejected,
            this, [this](const QString &message) {
        showOperationFeedback(message, activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback);
    });
    connect(previewController_, &PreviewController::operationRejected,
            this, [this](const QString &message) {
        showOperationFeedback(message, WorkspaceMode::Preview);
    });
    connect(playbackController_, &PlaybackController::operationRejected,
            this, [this](const QString &message) {
        showOperationFeedback(message, WorkspaceMode::Playback);
    });
    connect(ptzController_, &PtzController::operationRejected,
            this, [this](const QString &message) {
        showOperationFeedback(message, WorkspaceMode::Preview);
    });
    titleBar_ = new WindowTitleBar(this);
    titleBar_->setTargetWindow(this);
    titleBar_->setMaximizeRestoreAction(actionRegistry_->action(ViewerActionId::RestoreWindow));
    titleBar_->setAccountName(apiClient_->username());
    titleBar_->setConnectionState(ViewerConnectionState::Connected, QStringLiteral("中心已连接"));
    connect(titleBar_, &WindowTitleBar::workspaceModeRequested, this, [this](WorkspaceMode mode) {
        switchWorkspace(mode == WorkspaceMode::Preview ? 0 : 1);
    });
    setMenuWidget(titleBar_);
    uiSettings_ = new ViewerUiSettings(apiClient_->username());
    if (startupMode_ != ViewerStartupMode::Normal) {
        // 安全启动只隔离容易导致窗口初始化失败的布局数据，不影响收藏、视图和播放偏好。
        uiSettings_->backupAndClearLayoutForRecovery();
    }
    loadPreferences();

    DockLayoutController::PanelWidgets dockPanels;
    dockPanels.resourceCatalog = buildSidebar();
    QWidget *centralWorkspace = buildWorkspace();
    dockPanels.ptz = buildPtzPanel();
    dockPanels.playbackSearch = buildPlaybackSearchPanel();
    dockPanels.recordingTimeline = buildRecordingTimelinePanel();

    dockLayoutController_ = new DockLayoutController(this, this);
    dockLayoutController_->initialize(centralWorkspace, dockPanels, ptzPanelVisible_);
    dockLayoutController_->setStoredState(
        WorkspaceMode::Preview,
        uiSettings_->dockState(WorkspaceMode::Preview));
    dockLayoutController_->setStoredState(
        WorkspaceMode::Playback,
        uiSettings_->dockState(WorkspaceMode::Playback));
    connect(dockLayoutController_, &DockLayoutController::panelVisibilityChanged,
            this, [this](DockPanelId panelId, bool visible) {
        if (panelId == DockPanelId::Ptz) {
            ptzPanelVisible_ = visible;
            if (ptzPanelButton_ != nullptr) {
                const QSignalBlocker blocker(ptzPanelButton_);
                ptzPanelButton_->setChecked(visible);
            }
        }
        refreshControllerActionStates();
    });
    connect(dockLayoutController_, &DockLayoutController::lockedChanged,
            this, [this](bool locked) {
        Q_UNUSED(locked)
        refreshControllerActionStates();
    });

    if (dockPanelsMenu_ != nullptr) {
        dockPanelsMenu_->addAction(actionRegistry_->action(ViewerActionId::ShowResourceCatalog));
        dockPanelsMenu_->addAction(actionRegistry_->action(ViewerActionId::ShowPtz));
        dockPanelsMenu_->addAction(actionRegistry_->action(ViewerActionId::ShowPlaybackSearch));
        dockPanelsMenu_->addAction(actionRegistry_->action(ViewerActionId::ShowRecordingTimeline));
        dockPanelsMenu_->addSeparator();
        dockLayoutLockAction_ = actionRegistry_->action(ViewerActionId::LockDockLayout);
        dockPanelsMenu_->addAction(dockLayoutLockAction_);
        dockPanelsMenu_->addAction(actionRegistry_->action(ViewerActionId::RestoreDefaultLayout));
    }
    titleBar_->setPanelsMenu(dockPanelsMenu_);
    if (QMenu *accountMenu = titleBar_->accountMenu()) {
        accountMenu->clear();
        accountMenu->addAction(actionRegistry_->action(ViewerActionId::ChangePassword));
        accountMenu->addSeparator();
        accountMenu->addAction(actionRegistry_->action(ViewerActionId::Logout));
        accountMenu->addAction(actionRegistry_->action(ViewerActionId::ExitApplication));
    }

    dockLayoutController_->switchWorkspace(
        WorkspaceMode::Preview,
        uiSettings_->dockState(WorkspaceMode::Preview));
    setDockLayoutLocked(uiSettings_->dockLocked());
    restoreWindowGeometry();

    // 面板菜单与 Qt ADS 原生切换动作共用同一可用性和勾选状态，避免两个入口漂移。
    actionRegistry_->bindAction(
        ViewerActionId::ShowResourceCatalog,
        dockLayoutController_->dockPanelAction(DockPanelId::ResourceCatalog));
    actionRegistry_->bindAction(
        ViewerActionId::ShowPtz,
        dockLayoutController_->dockPanelAction(DockPanelId::Ptz));
    actionRegistry_->bindAction(
        ViewerActionId::ShowPlaybackSearch,
        dockLayoutController_->dockPanelAction(DockPanelId::PlaybackSearch));
    actionRegistry_->bindAction(
        ViewerActionId::ShowRecordingTimeline,
        dockLayoutController_->dockPanelAction(DockPanelId::RecordingTimeline));

    for (int index = 0; index < 64; ++index) {
        auto *tile = new VideoTileWidget(index, videoGrid_->parentWidget());
        tile->hide();
        tiles_.append(tile);
        connect(tile, &VideoTileWidget::activated, this, &MainWindow::selectTile);
        connect(tile, &VideoTileWidget::maximizeRequested, this, &MainWindow::toggleTileMaximized);
        connect(tile, &VideoTileWidget::restartRequested, this, &MainWindow::restartTile);
        connect(tile, &VideoTileWidget::profileChangeRequested, this, [this](VideoTileWidget *target, const QString &profile) {
            if (!ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("切换码流"))) {
                return;
            }
            if (target != nullptr && target->profile() == profile) {
                return;
            }
            if (target != nullptr && !target->requestId().isNull()) {
                if (statusLabel_ != nullptr) {
                    statusLabel_->setText(QStringLiteral("当前窗格正在建立播放会话，请稍后再切换码流。"));
                }
                return;
            }
            if (target != nullptr && target->camera().has_value()) {
                requestCamera(target, *target->camera(), profile);
            }
        });
        connect(tile, &VideoTileWidget::instantPlaybackRequested, this, &MainWindow::openInstantPlayback);
        connect(tile, &VideoTileWidget::cameraIdsDropped, this, [this](VideoTileWidget *target, const QList<QUuid> &cameraIds) {
            assignCameraIds(target, cameraIds, cameraIds.size() > 1);
        });
        connect(tile, &VideoTileWidget::clearRequested, this, [this](VideoTileWidget *target) {
            if (target != nullptr && ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("清空预览窗格"))) {
                target->clearTile();
            }
        });
        connect(tile, &VideoTileWidget::tileCleared, this, [this](const QUuid &, const QUuid &) {
            if (previewController_ != nullptr) {
                for (VideoTileWidget *candidate : std::as_const(tiles_)) {
                    if (candidate != nullptr && candidate->isEmpty()) {
                        previewController_->setTileAssigned(candidate->index(), false);
                    }
                }
            }
            updatePtzState();
            syncPreviewSessionState();
            updateCatalogSummary();
        });
        connect(tile, &VideoTileWidget::sessionRequestShouldBeCanceled, apiClient_, &ApiClient::cancelStreamSessionRequest);
        connect(tile, &VideoTileWidget::sessionShouldBeRevoked, apiClient_, &ApiClient::revokeSession);
        connect(tile, &VideoTileWidget::sessionReconnectRequested, this,
                [this, tile](const QUuid &sessionId, const QUuid &requestId) {
            if (canInteractWithWorkspace(WorkspaceMode::Preview)) {
                apiClient_->requestReconnectTicket(sessionId, requestId);
            }
        });
    }
    selectedTile_ = tiles_.first();
    selectedTile_->setSelected(true);

    for (int index = 0; index < 4; ++index) {
        auto *tile = new VideoTileWidget(index, playbackGrid_->parentWidget());
        tile->hide();
        playbackTiles_.append(tile);
        connect(tile, &VideoTileWidget::activated, this, &MainWindow::selectPlaybackTile);
        connect(tile, &VideoTileWidget::maximizeRequested, this, &MainWindow::toggleTileMaximized);
        connect(tile, &VideoTileWidget::restartRequested, this, &MainWindow::restartTile);
        connect(tile, &VideoTileWidget::syncMembershipChanged, this, [this](VideoTileWidget *, bool) {
            updatePlaybackTimeline();
            updatePlaybackControlState();
        });
        connect(tile, &VideoTileWidget::syncMembershipChangeRequested, this, [this](VideoTileWidget *target, bool synchronized) {
            if (target != nullptr && ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("切换同步组"))) {
                target->setSyncMember(synchronized);
            }
        });
        connect(tile, &VideoTileWidget::cameraIdsDropped, this, [this](VideoTileWidget *target, const QList<QUuid> &cameraIds) {
            assignCameraIds(target, cameraIds, cameraIds.size() > 1);
        });
        connect(tile, &VideoTileWidget::clearRequested, this, [this](VideoTileWidget *target) {
            if (target != nullptr && ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("清空回放窗格"))) {
                target->clearTile();
            }
        });
        connect(tile, &VideoTileWidget::playbackStateChanged, this, &MainWindow::handleTilePlaybackState);
        connect(tile, &VideoTileWidget::playbackMediaPositionChanged, this, &MainWindow::handleTileMediaPosition);
        connect(tile, &VideoTileWidget::tileCleared, this, [this](const QUuid &sessionId, const QUuid &cameraId) {
            playbackTransport_.remove(sessionId);
            playbackControlsInFlight_.remove(sessionId);
            playbackAdvancingSessions_.remove(sessionId);
            playbackClockAnchoredAt_.remove(sessionId);
            playbackMediaOriginSeconds_.remove(sessionId);
            playbackMediaLastSeconds_.remove(sessionId);
            playbackMediaOriginPositions_.remove(sessionId);
            apiClient_->cancelRecordingSearch(cameraId);
            playbackSegments_.remove(cameraId);
            playbackSearchStates_.remove(cameraId);
            playbackSearchRequests_.remove(cameraId);
            if (playbackController_ != nullptr) {
                for (VideoTileWidget *candidate : std::as_const(playbackTiles_)) {
                    if (candidate != nullptr && candidate->isEmpty()) {
                        playbackController_->clearTile(candidate->index());
                    }
                }
            }
            updatePlaybackTimeline();
            updatePlaybackControlState();
        });
        connect(tile, &VideoTileWidget::sessionRequestShouldBeCanceled, apiClient_, &ApiClient::cancelStreamSessionRequest);
        connect(tile, &VideoTileWidget::sessionShouldBeRevoked, apiClient_, &ApiClient::revokeSession);
        connect(tile, &VideoTileWidget::sessionReconnectRequested, this,
                [this, tile](const QUuid &sessionId, const QUuid &requestId) {
            if (canInteractWithWorkspace(WorkspaceMode::Playback)) {
                apiClient_->requestReconnectTicket(sessionId, requestId);
            }
        });
    }
    selectedPlaybackTile_ = playbackTiles_.first();
    selectedPlaybackTile_->setSelected(true);
    // 控制器状态变更会同步刷新动作与控件，必须在所有工作台控件创建完成后执行。
    previewController_->setLayoutCount(layoutCount_);
    previewController_->setStreamMode(streamProfileMode_);
    playbackController_->setLayoutCount(playbackLayoutCount_);
    changeLayout(layoutCount_);
    changePlaybackLayout(playbackLayoutCount_);

    playbackMonotonicClock_.start();

    tourTimer_ = new QTimer(this);
    connect(tourTimer_, &QTimer::timeout, this, &MainWindow::advanceTour);
    playbackCursorTimer_ = new QTimer(this);
    playbackCursorTimer_->setInterval(250);
    connect(playbackCursorTimer_, &QTimer::timeout, this, [this]() {
        if (activeWorkspace_ != 1 || !playbackCursor_.isValid() || playbackStartedAt_ == nullptr || playbackEndedAt_ == nullptr) {
            return;
        }
        if (selectedPlaybackTile_ == nullptr || selectedPlaybackTile_->sessionId().isNull()) {
            playbackCursorTimer_->stop();
            return;
        }
        const QUuid sessionId = selectedPlaybackTile_->sessionId();
        const PlaybackTransportInfo transport = playbackTransport_.value(sessionId);
        if (transport.isPaused || playbackControlsInFlight_.contains(sessionId)) return;
        playbackCursor_ = estimatedPlaybackPosition(sessionId);
        if (playbackCursor_ > playbackEndedAt_->dateTime()) {
            playbackCursor_ = playbackEndedAt_->dateTime();
            playbackCursorTimer_->stop();
        }
        recordingTimeline_->setCursor(playbackCursor_);
    });
    playbackTransportRefreshTimer_ = new QTimer(this);
    playbackTransportRefreshTimer_->setInterval(3000);
    connect(playbackTransportRefreshTimer_, &QTimer::timeout, this, [this]() {
        if (!canInteractWithWorkspace(WorkspaceMode::Playback)) return;
        for (VideoTileWidget *tile : activePlaybackTiles()) {
            if (tile != nullptr && !tile->sessionId().isNull() && !playbackControlsInFlight_.contains(tile->sessionId())) {
                apiClient_->requestPlaybackTransport(tile->sessionId());
            }
        }
    });
    playbackTransportRefreshTimer_->start();
    cameraStatusRefreshTimer_ = new QTimer(this);
    cameraStatusRefreshTimer_->setInterval(15000);
    connect(cameraStatusRefreshTimer_, &QTimer::timeout, apiClient_, &ApiClient::refreshCameraStatuses);
    cameraStatusRefreshTimer_->start();
    loadFavorites();
    loadSavedViews();

    connect(apiClient_, &ApiClient::catalogLoaded, this, &MainWindow::populateCatalog);
    connect(apiClient_, &ApiClient::cameraStatusesLoaded, this, &MainWindow::applyCameraStatuses);
    connect(apiClient_, &ApiClient::forcedPasswordChangeRequired,
            this, &MainWindow::handleForcedPasswordChangeRequired, Qt::QueuedConnection);
    connect(apiClient_, &ApiClient::requestFailed, this, [this](const QString &operation, const QString &message) {
        statusLabel_->setText(QStringLiteral("%1失败：%2").arg(operation, message));
        if (titleBar_ != nullptr && operation.contains(QStringLiteral("目录"))) {
            titleBar_->setConnectionState(ViewerConnectionState::Error, QStringLiteral("中心连接异常"));
        }
    });
    connect(apiClient_, &ApiClient::streamSessionCreated, this, &MainWindow::handleSessionCreated);
    connect(apiClient_, &ApiClient::streamSessionFailed, this, &MainWindow::handleSessionFailed);
    connect(apiClient_, &ApiClient::streamSessionRenewalFailed, this, [this](const QUuid &sessionId, const QString &message) {
        if (auto *tile = findTileBySession(sessionId)) {
            const bool playback = playbackTiles_.contains(tile);
            tile->failSession(QStringLiteral("播放租约续期失败：%1").arg(message));
            if (playback) {
                playbackTransport_.remove(sessionId);
                playbackControlsInFlight_.remove(sessionId);
                playbackAdvancingSessions_.remove(sessionId);
                playbackClockAnchoredAt_.remove(sessionId);
                playbackMediaOriginSeconds_.remove(sessionId);
                playbackMediaLastSeconds_.remove(sessionId);
                playbackMediaOriginPositions_.remove(sessionId);
                playbackStatusLabel_->setText(QStringLiteral("回放租约续期失败：%1").arg(message));
                updatePlaybackControlState();
            }
        }
    });
    connect(apiClient_, &ApiClient::ptzRequestFailed, this, [this](const QString &message) {
        statusLabel_->setText(QStringLiteral("云台控制请求失败：%1").arg(message));
    });
    connect(apiClient_, &ApiClient::recordingSearchCompleted, this, &MainWindow::handleRecordingSearchCompleted);
    connect(apiClient_, &ApiClient::recordingSearchFailed, this, &MainWindow::handleRecordingSearchFailed);
    connect(apiClient_, &ApiClient::playbackControlQueued, this, &MainWindow::handlePlaybackControlQueued);
    connect(apiClient_, &ApiClient::playbackControlFailed, this, &MainWindow::handlePlaybackControlFailed);
    connect(apiClient_, &ApiClient::playbackTransportRefreshed, this,
            [this](const QUuid &sessionId, const PlaybackTransportInfo &transport) {
        VideoTileWidget *tile = findTileBySession(sessionId);
        if (tile == nullptr || !playbackTiles_.contains(tile) || playbackControlsInFlight_.contains(sessionId)) return;
        PlaybackTransportInfo refreshed = transport;
        const PlaybackTransportInfo current = playbackTransport_.value(sessionId);
        const bool reportsNewCommand = !transport.commandId.isNull() && transport.commandId != current.commandId;
        if (!reportsNewCommand && current.position.isValid()) {
            refreshed.position = estimatedPlaybackPosition(sessionId);
        }
        anchorPlaybackClock(sessionId, refreshed, tile->isPlaying() && !refreshed.isPaused);
        if (tile == selectedPlaybackTile_) {
            playbackCursor_ = estimatedPlaybackPosition(sessionId);
            recordingTimeline_->setCursor(playbackCursor_);
        }
        updatePlaybackControlState();
    });
    connect(apiClient_, &ApiClient::sessionRevocationFailed, this, [this](const QUuid &, const QString &message) {
        statusBar()->showMessage(message, 8000);
    });

    auto *escapeAction = new QAction(this);
    escapeAction->setShortcut(QKeySequence(Qt::Key_Escape));
    connect(escapeAction, &QAction::triggered, this, [this]() {
        if (isCanvasPresentationBusy()) exitCanvasFullScreen();
        else if (activeWorkspace_ == 0 && maximizedPreviewTile_ != nullptr) restoreTileLayout(false);
        else if (activeWorkspace_ == 1 && maximizedPlaybackTile_ != nullptr) restoreTileLayout(true);
    });
    addAction(escapeAction);
    auto *playPauseAction = new QAction(this);
    playPauseAction->setShortcut(QKeySequence(Qt::Key_Space));
    connect(playPauseAction, &QAction::triggered, this, [this]() {
        QWidget *focused = QApplication::focusWidget();
        if (activeWorkspace_ != 1 || qobject_cast<QLineEdit *>(focused) != nullptr || qobject_cast<QAbstractSpinBox *>(focused) != nullptr) return;
        const PlaybackTransportInfo transport = selectedPlaybackTile_ == nullptr
            ? PlaybackTransportInfo{}
            : playbackTransport_.value(selectedPlaybackTile_->sessionId());
        controlPlayback(transport.isPaused ? QStringLiteral("Resume") : QStringLiteral("Pause"));
    });
    addAction(playPauseAction);

    statusBar()->setSizeGripEnabled(false);
    auto *clockLabel = new QLabel;
    clockLabel->setObjectName(QStringLiteral("mutedLabel"));
    statusBar()->addPermanentWidget(clockLabel);
    auto *clockTimer = new QTimer(this);
    clockTimer->setInterval(1000);
    connect(clockTimer, &QTimer::timeout, this, [clockLabel]() {
        clockLabel->setText(QStringLiteral("本地时间  %1").arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss"))));
    });
    clockLabel->setText(QStringLiteral("本地时间  %1").arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss"))));
    clockTimer->start();
    updateResponsiveToolbar();
    refreshControllerActionStates();
    if (startupMode_ != ViewerStartupMode::Normal && statusLabel_ != nullptr) {
        statusLabel_->setText(startupMode_ == ViewerStartupMode::SafeUi
                                  ? QStringLiteral("已按安全界面模式启动，原窗口与面板布局已备份。")
                                  : QStringLiteral("检测到上次异常退出，已使用默认窗口与面板布局恢复启动。"));
    }
    apiClient_->loadCatalog();
}

MainWindow::~MainWindow() {
    delete uiSettings_;
}

void MainWindow::initializeActions() {
    const QList<ViewerActionDescriptor> descriptors{
        {ViewerActionId::WorkspacePreview, QStringLiteral("实时预览"), ViewerIcon::Video, {}, true},
        {ViewerActionId::WorkspacePlayback, QStringLiteral("录像回放"), ViewerIcon::CalendarSearch, {}, true},
        {ViewerActionId::RefreshCatalog, QStringLiteral("刷新资源目录"), ViewerIcon::Refresh},
        {ViewerActionId::FocusSearch, QStringLiteral("搜索资源"), ViewerIcon::Search, QKeySequence::Find},
        {ViewerActionId::ToggleFavorite, QStringLiteral("收藏当前摄像头"), ViewerIcon::Star},
        {ViewerActionId::SaveView, QStringLiteral("保存当前视图"), ViewerIcon::Save},
        {ViewerActionId::ChangePreviewLayout, QStringLiteral("切换实时预览分屏"), ViewerIcon::LayoutGrid},
        {ViewerActionId::ChangePlaybackLayout, QStringLiteral("切换录像回放分屏"), ViewerIcon::LayoutGrid},
        {ViewerActionId::StopAllPreview, QStringLiteral("停止全部实时预览"), ViewerIcon::Stop},
        {ViewerActionId::StopAllPlayback, QStringLiteral("停止全部录像回放"), ViewerIcon::Stop},
        {ViewerActionId::ToggleFullScreen, QStringLiteral("切换视频画布全屏"), ViewerIcon::Maximize, QKeySequence(Qt::Key_F11)},
        {ViewerActionId::RestoreWindow, QStringLiteral("最大化或还原窗口"), ViewerIcon::Maximize},
        {ViewerActionId::ClearSelectedTile, QStringLiteral("清空当前窗格"), ViewerIcon::Close, QKeySequence(Qt::Key_Delete)},
        {ViewerActionId::ToggleTour, QStringLiteral("轮巡"), ViewerIcon::Play, {}, true},
        {ViewerActionId::TourPrevious, QStringLiteral("轮巡上一页"), ViewerIcon::SkipBack},
        {ViewerActionId::TourNext, QStringLiteral("轮巡下一页"), ViewerIcon::SkipForward},
        {ViewerActionId::PlaybackPause, QStringLiteral("暂停回放"), ViewerIcon::Pause},
        {ViewerActionId::PlaybackResume, QStringLiteral("继续回放"), ViewerIcon::Play},
        {ViewerActionId::PlaybackSync, QStringLiteral("同步组"), ViewerIcon::None, {}, true},
        {ViewerActionId::ShowResourceCatalog, QStringLiteral("资源面板"), ViewerIcon::PanelLeft, {}, true},
        {ViewerActionId::ShowPtz, QStringLiteral("云台控制面板"), ViewerIcon::PanelRight, {}, true},
        {ViewerActionId::ShowPlaybackSearch, QStringLiteral("回放检索面板"), ViewerIcon::CalendarSearch, {}, true},
        {ViewerActionId::ShowRecordingTimeline, QStringLiteral("录像时间轴面板"), ViewerIcon::PanelBottom, {}, true},
        {ViewerActionId::LockDockLayout, QStringLiteral("锁定面板布局"), ViewerIcon::Lock, {}, true},
        {ViewerActionId::RestoreDefaultLayout, QStringLiteral("恢复当前工作区默认布局"), ViewerIcon::Restore},
        {ViewerActionId::ChangePassword, QStringLiteral("修改密码"), ViewerIcon::Password},
        {ViewerActionId::Logout, QStringLiteral("退出登录"), ViewerIcon::Logout},
        {ViewerActionId::ExitApplication, QStringLiteral("退出应用"), ViewerIcon::Close},
    };

    for (const ViewerActionDescriptor &descriptor : descriptors) {
        QAction *action = actionRegistry_->registerAction(descriptor);
        if (!descriptor.shortcut.isEmpty()) {
            addAction(action);
        }
    }

    auto *workspaceActionGroup = new QActionGroup(this);
    workspaceActionGroup->setExclusive(true);
    workspaceActionGroup->addAction(actionRegistry_->action(ViewerActionId::WorkspacePreview));
    workspaceActionGroup->addAction(actionRegistry_->action(ViewerActionId::WorkspacePlayback));

    connect(actionRegistry_, &ViewerActionRegistry::actionTriggered, this,
            [this](ViewerActionId id, bool checked) {
        switch (id) {
            case ViewerActionId::WorkspacePreview:
                switchWorkspace(0);
                break;
            case ViewerActionId::WorkspacePlayback:
                switchWorkspace(1);
                break;
            case ViewerActionId::RefreshCatalog:
                if (!ensureWorkspaceInteraction(
                        activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback,
                        QStringLiteral("刷新资源目录"))) {
                    break;
                }
                if (catalogSummaryLabel_ != nullptr) {
                    catalogSummaryLabel_->setText(QStringLiteral("正在刷新授权目录…"));
                }
                apiClient_->loadCatalog();
                break;
            case ViewerActionId::FocusSearch:
                if (!ensureWorkspaceInteraction(
                        activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback,
                        QStringLiteral("搜索资源"))) {
                    break;
                }
                if (searchEdit_ != nullptr) searchEdit_->setFocus();
                break;
            case ViewerActionId::ToggleFavorite:
                if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("收藏"))) {
                    toggleSelectedFavorite();
                }
                break;
            case ViewerActionId::SaveView:
                if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("保存视图"))) {
                    saveCurrentView();
                }
                break;
            case ViewerActionId::ChangePreviewLayout:
            case ViewerActionId::ChangePlaybackLayout:
                break;
            case ViewerActionId::StopAllPreview:
                if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("停止实时预览"))) {
                    stopAllPreview(true);
                }
                break;
            case ViewerActionId::StopAllPlayback:
                if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("停止录像回放"))) {
                    stopAllPlayback(true);
                }
                break;
            case ViewerActionId::ToggleFullScreen:
                toggleCanvasFullScreen();
                break;
            case ViewerActionId::RestoreWindow:
                if (isMaximized()) showNormal();
                else showMaximized();
                QTimer::singleShot(0, this, &MainWindow::refreshControllerActionStates);
                break;
            case ViewerActionId::ClearSelectedTile: {
                QWidget *focused = QApplication::focusWidget();
                if (qobject_cast<QLineEdit *>(focused) != nullptr ||
                    qobject_cast<QAbstractSpinBox *>(focused) != nullptr) {
                    break;
                }
                const WorkspaceMode mode = activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback;
                if (!ensureWorkspaceInteraction(mode, QStringLiteral("清空窗格"))) {
                    break;
                }
                VideoTileWidget *tile = activeWorkspace_ == 0 ? selectedTile_ : selectedPlaybackTile_;
                if (tile != nullptr && !tile->isEmpty()) tile->clearTile();
                break;
            }
            case ViewerActionId::ToggleTour:
                if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("轮巡"))) {
                    toggleTour(checked);
                }
                break;
            case ViewerActionId::TourPrevious: {
                if (!ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("轮巡翻页"))) {
                    break;
                }
                const int visibleCount = std::max(1, layoutCount_);
                tourCursor_ = std::max(0, tourCursor_ - visibleCount * 2);
                advanceTour();
                break;
            }
            case ViewerActionId::TourNext:
                if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("轮巡翻页"))) {
                    advanceTour();
                }
                break;
            case ViewerActionId::PlaybackPause:
                if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("暂停回放"))) {
                    controlPlayback(QStringLiteral("Pause"));
                }
                break;
            case ViewerActionId::PlaybackResume:
                if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("继续回放"))) {
                    controlPlayback(QStringLiteral("Resume"));
                }
                break;
            case ViewerActionId::PlaybackSync:
                if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("切换同步组")) &&
                    playbackController_ != nullptr) {
                    playbackController_->setSyncEnabled(checked);
                }
                updatePlaybackControlState();
                savePreferences();
                break;
            case ViewerActionId::ShowResourceCatalog:
                if (ensureWorkspaceInteraction(
                        activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback,
                        QStringLiteral("显示资源面板"))) {
                    showDockPanel(DockPanelId::ResourceCatalog, checked);
                }
                break;
            case ViewerActionId::ShowPtz:
                if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("显示云台控制面板"))) {
                    showDockPanel(DockPanelId::Ptz, checked);
                }
                break;
            case ViewerActionId::ShowPlaybackSearch:
                if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("显示回放检索面板"))) {
                    showDockPanel(DockPanelId::PlaybackSearch, checked);
                }
                break;
            case ViewerActionId::ShowRecordingTimeline:
                if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("显示录像时间轴面板"))) {
                    showDockPanel(DockPanelId::RecordingTimeline, checked);
                }
                break;
            case ViewerActionId::LockDockLayout:
                if (ensureWorkspaceInteraction(
                        activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback,
                        QStringLiteral("锁定面板布局"))) {
                    setDockLayoutLocked(checked);
                }
                break;
            case ViewerActionId::RestoreDefaultLayout:
                if (ensureWorkspaceInteraction(
                        activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback,
                        QStringLiteral("恢复面板布局"))) {
                    resetDockLayout();
                }
                break;
            case ViewerActionId::ChangePassword:
                openChangePasswordDialog();
                break;
            case ViewerActionId::Logout:
                requestLogout();
                break;
            case ViewerActionId::ExitApplication:
                close();
                break;
            default:
                break;
        }
    });
}

void MainWindow::refreshControllerActionStates() {
    if (actionRegistry_ == nullptr || workspaceController_ == nullptr ||
        previewController_ == nullptr || playbackController_ == nullptr) {
        return;
    }

    const auto isPreviewAction = [](ViewerActionId id) {
        return id == ViewerActionId::ToggleFavorite || id == ViewerActionId::SaveView ||
               id == ViewerActionId::ChangePreviewLayout || id == ViewerActionId::StopAllPreview ||
               id == ViewerActionId::ToggleTour || id == ViewerActionId::TourPrevious ||
               id == ViewerActionId::TourNext;
    };
    const auto isPlaybackAction = [](ViewerActionId id) {
        return id == ViewerActionId::ChangePlaybackLayout || id == ViewerActionId::StopAllPlayback ||
               id == ViewerActionId::PlaybackPause ||
               id == ViewerActionId::PlaybackResume || id == ViewerActionId::PlaybackSync;
    };

    const bool canvasPresentationBusy = isCanvasPresentationBusy();
    const bool globalInteractionEnabled = !sessionEnding_ && !workspaceTransition_.has_value() &&
                                          !canvasPresentationBusy && workspaceController_->interactionEnabled();
    for (ViewerActionId id : WorkspaceController::allActionIds()) {
        if (!actionRegistry_->contains(id)) {
            continue;
        }
        const bool isWindowUtilityAction = id == ViewerActionId::RestoreWindow;
        const bool isCanvasAction = id == ViewerActionId::ToggleFullScreen;
        bool enabled = false;
        if (isWindowUtilityAction) {
            enabled = !sessionEnding_ && !canvasPresentationBusy;
        } else if (isCanvasAction) {
            enabled = !sessionEnding_ &&
                ((canvasPresentationState_ == CanvasPresentationState::Entering ||
                  canvasPresentationState_ == CanvasPresentationState::Active) ||
                 (canvasPresentationState_ == CanvasPresentationState::Idle &&
                  globalInteractionEnabled && workspaceController_->isActionEnabled(id)));
        } else {
            enabled = globalInteractionEnabled && workspaceController_->isActionEnabled(id);
        }
        if (isPreviewAction(id)) enabled = enabled && previewController_->isActionEnabled(id);
        if (isPlaybackAction(id)) enabled = enabled && playbackController_->isActionEnabled(id);
        if (id == ViewerActionId::ShowPtz) {
            enabled = globalInteractionEnabled && workspaceController_->mode() == WorkspaceMode::Preview;
        } else if (id == ViewerActionId::ShowPlaybackSearch ||
                   id == ViewerActionId::ShowRecordingTimeline) {
            enabled = globalInteractionEnabled && workspaceController_->mode() == WorkspaceMode::Playback;
        }
        if (id == ViewerActionId::ClearSelectedTile) {
            enabled = enabled && (workspaceController_->mode() == WorkspaceMode::Preview
                ? previewController_->isActionEnabled(id)
                : selectedPlaybackTile_ != nullptr && selectedPlaybackTile_->camera().has_value());
        }

        bool checked = false;
        if (id == ViewerActionId::WorkspacePreview || id == ViewerActionId::WorkspacePlayback) {
            checked = workspaceController_->isActionChecked(id);
        } else if (id == ViewerActionId::ToggleTour) {
            checked = previewController_->tourActive();
        } else if (id == ViewerActionId::PlaybackSync) {
            checked = playbackController_->syncEnabled();
        } else if (id == ViewerActionId::LockDockLayout) {
            checked = dockLayoutController_ != nullptr && dockLayoutController_->isLocked();
        } else if (dockLayoutController_ != nullptr) {
            switch (id) {
                case ViewerActionId::ShowResourceCatalog:
                    checked = dockLayoutController_->isPanelVisible(DockPanelId::ResourceCatalog);
                    break;
                case ViewerActionId::ShowPtz:
                    checked = dockLayoutController_->isPanelVisible(DockPanelId::Ptz);
                    break;
                case ViewerActionId::ShowPlaybackSearch:
                    checked = dockLayoutController_->isPanelVisible(DockPanelId::PlaybackSearch);
                    break;
                case ViewerActionId::ShowRecordingTimeline:
                    checked = dockLayoutController_->isPanelVisible(DockPanelId::RecordingTimeline);
                    break;
                default:
                    break;
            }
        }

        QString reason;
        if (!enabled) {
            if (sessionEnding_) {
                reason = QStringLiteral("当前会话正在结束。");
            } else if (canvasPresentationBusy) {
                reason = canvasPresentationState_ == CanvasPresentationState::Exiting
                    ? QStringLiteral("正在退出视频画布全屏，请稍候。")
                    : QStringLiteral("视频画布全屏显示期间不可使用该操作。");
            } else if (workspaceTransition_.has_value()) {
                reason = QStringLiteral("正在切换工作区，请稍候。");
            } else if (id == ViewerActionId::ShowPtz) {
                reason = QStringLiteral("仅可在实时预览工作区显示云台控制面板。");
            } else if (id == ViewerActionId::ShowPlaybackSearch) {
                reason = QStringLiteral("请先切换到录像回放工作区，再显示回放检索面板。");
            } else if (id == ViewerActionId::ShowRecordingTimeline) {
                reason = QStringLiteral("请先切换到录像回放工作区，再显示录像时间轴面板。");
            } else if (isPreviewAction(id)) {
                reason = QStringLiteral("仅可在实时预览工作区执行，且需要满足当前窗格条件。");
            } else if (isPlaybackAction(id)) {
                reason = QStringLiteral("仅可在录像回放工作区执行，且需要已就绪的回放窗格。");
            } else if (id == ViewerActionId::ClearSelectedTile) {
                reason = QStringLiteral("请先选择已分配摄像头的窗格。");
            } else {
                reason = QStringLiteral("当前操作暂不可用。");
            }
        }
        actionRegistry_->applyState(id, ViewerActionState{enabled, checked, reason});
    }

    if (QAction *windowAction = actionRegistry_->action(ViewerActionId::RestoreWindow)) {
        const bool maximized = isMaximized();
        windowAction->setText(maximized ? QStringLiteral("还原窗口") : QStringLiteral("最大化窗口"));
        windowAction->setToolTip(windowAction->text());
        windowAction->setIcon(IconProvider::instance().icon(maximized ? ViewerIcon::Minimize : ViewerIcon::Maximize));
    }
    if (QAction *tourAction = actionRegistry_->action(ViewerActionId::ToggleTour)) {
        tourAction->setIcon(IconProvider::instance().icon(
            previewController_->tourActive() ? ViewerIcon::Pause : ViewerIcon::Play));
    }
    const auto syncLayoutMenuActions = [this](ViewerActionId id, const QList<QAction *> &actions) {
        const ViewerActionState state = actionRegistry_->state(id);
        for (QAction *action : actions) {
            setEnabledWithReason(action, state.enabled, state.unavailableReason);
        }
    };
    syncLayoutMenuActions(ViewerActionId::ChangePreviewLayout, previewLayoutActions_);
    syncLayoutMenuActions(ViewerActionId::ChangePlaybackLayout, playbackLayoutActions_);
    updateWorkspaceInteractionControls();
}

bool MainWindow::canInteractWithWorkspace(WorkspaceMode mode) const {
    if (sessionEnding_ || workspaceTransition_.has_value() || isCanvasPresentationBusy() || workspaceController_ == nullptr ||
        !workspaceController_->interactionEnabled()) {
        return false;
    }
    const WorkspaceMode activeMode = activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback;
    return activeMode == mode && workspaceController_->mode() == mode;
}

bool MainWindow::ensureWorkspaceInteraction(WorkspaceMode mode, const QString &operation) {
    if (canInteractWithWorkspace(mode)) {
        return true;
    }
    QString reason;
    if (sessionEnding_) {
        reason = QStringLiteral("当前会话正在结束。");
    } else if (isCanvasPresentationBusy()) {
        reason = QStringLiteral("视频画布全屏切换中，请先退出全屏。");
    } else if (workspaceTransition_.has_value()) {
        reason = QStringLiteral("正在切换工作区，请稍候。");
    } else {
        reason = QStringLiteral("请先切换到%1工作区。").arg(workspaceName(mode));
    }
    refreshControllerActionStates();
    showOperationFeedback(QStringLiteral("%1暂不可用：%2").arg(operation, reason), mode);
    return false;
}

void MainWindow::showOperationFeedback(const QString &message, WorkspaceMode preferredWorkspace) {
    const WorkspaceMode visibleWorkspace = activeWorkspace_ == 1
        ? WorkspaceMode::Playback
        : WorkspaceMode::Preview;
    QLabel *label = visibleWorkspace == WorkspaceMode::Playback ? playbackStatusLabel_ : statusLabel_;
    if (label == nullptr) {
        label = preferredWorkspace == WorkspaceMode::Playback ? playbackStatusLabel_ : statusLabel_;
    }
    if (label != nullptr) {
        label->setText(message);
    }
}

void MainWindow::updateWorkspaceInteractionControls() {
    const bool globalEnabled = !sessionEnding_ && !workspaceTransition_.has_value() && !isCanvasPresentationBusy() &&
                               workspaceController_ != nullptr && workspaceController_->interactionEnabled();
    const QString globalReason = sessionEnding_
        ? QStringLiteral("当前会话正在结束。")
        : isCanvasPresentationBusy()
            ? QStringLiteral("视频画布全屏切换中，请稍候。")
        : QStringLiteral("正在切换工作区，请稍候。");
    if (titleBar_ != nullptr) {
        titleBar_->setUtilityMenusEnabled(globalEnabled);
    }
    setEnabledWithReason(searchEdit_, globalEnabled, globalReason);
    setEnabledWithReason(resourceTabs_, globalEnabled, globalReason);
    setEnabledWithReason(catalogTree_, globalEnabled, globalReason);
    const bool playbackInteractionEnabled = canInteractWithWorkspace(WorkspaceMode::Playback);
    const QString playbackReason = globalEnabled
        ? QStringLiteral("请先切换到录像回放工作区。")
        : globalReason;
    setEnabledWithReason(playbackSearchPanel_, playbackInteractionEnabled, playbackReason);
    setEnabledWithReason(recordingTimelinePanel_, playbackInteractionEnabled, playbackReason);
}

QList<QAction *> MainWindow::dockPanelActions() const {
    return dockLayoutController_ != nullptr
        ? dockLayoutController_->dockPanelActions()
        : QList<QAction *>{};
}

bool MainWindow::isDockLayoutLocked() const {
    return dockLayoutController_ != nullptr && dockLayoutController_->isLocked();
}

bool MainWindow::isCanvasFullScreen() const {
    return canvasPresentationState_ != CanvasPresentationState::Idle;
}

void MainWindow::showDockPanel(DockPanelId panelId, bool visible) {
    if (dockLayoutController_ != nullptr) {
        dockLayoutController_->showDockPanel(panelId, visible);
    }
}

void MainWindow::setDockLayoutLocked(bool locked) {
    if (dockLayoutController_ == nullptr || dockLayoutController_->isInteractionFrozen()) {
        return;
    }
    dockLayoutController_->setLocked(locked);
    if (uiSettings_ != nullptr) {
        uiSettings_->setDockLocked(locked);
    }
}

void MainWindow::resetDockLayout() {
    if (dockLayoutController_ == nullptr) {
        return;
    }
    dockLayoutController_->resetCurrentLayout();
    if (statusLabel_ != nullptr && activeWorkspace_ == 0) {
        statusLabel_->setText(QStringLiteral("已恢复实时预览的默认面板布局。"));
    } else if (playbackStatusLabel_ != nullptr) {
        playbackStatusLabel_->setText(QStringLiteral("已恢复录像回放的默认面板布局。"));
    }
    savePreferences();
}

void MainWindow::closeEvent(QCloseEvent *event) {
    if (sessionEnding_) {
        event->accept();
        return;
    }
    event->ignore();
    prepareForSessionEnd();
    hide();
    connect(apiClient_, &ApiClient::shutdownFinished, qApp, &QCoreApplication::quit, Qt::SingleShotConnection);
    QTimer::singleShot(2000, qApp, &QCoreApplication::quit);
    apiClient_->logout();
}

void MainWindow::changeEvent(QEvent *event) {
    QMainWindow::changeEvent(event);
    if (event != nullptr && event->type() == QEvent::WindowStateChange) {
        if (canvasPresentationState_ == CanvasPresentationState::Entering && isFullScreen()) {
            // 原生窗口状态稳定后才隐藏外围界面，避免 Qt ADS 与窗口状态切换重入。
            QTimer::singleShot(0, this, &MainWindow::finishCanvasFullScreenEntry);
        } else if (canvasPresentationState_ == CanvasPresentationState::Active && !isFullScreen()) {
            // 处理系统级快捷键或窗口管理器主动退出全屏，避免外围界面一直保持隐藏。
            canvasPresentationState_ = CanvasPresentationState::Exiting;
            QTimer::singleShot(0, this, [this]() {
                finishCanvasFullScreenExit();
            });
        } else if (canvasPresentationState_ == CanvasPresentationState::Exiting && !isFullScreen()) {
            QTimer::singleShot(0, this, [this]() {
                finishCanvasFullScreenExit();
            });
        }
        if (titleBar_ != nullptr) {
            titleBar_->setWindowMaximized(isMaximized());
        }
        refreshControllerActionStates();
    }
}

bool MainWindow::nativeEvent(const QByteArray &eventType, void *message, qintptr *result) {
    if (titleBar_ != nullptr && titleBar_->handleNativeEvent(eventType, message, result)) {
        return true;
    }
    return QMainWindow::nativeEvent(eventType, message, result);
}

void MainWindow::resizeEvent(QResizeEvent *event) {
    QMainWindow::resizeEvent(event);
    updateResponsiveToolbar();
}

void MainWindow::updateResponsiveToolbar() {
    const bool compact = width() < 1420;
    for (QWidget *widget : std::as_const(previewCompactWidgets_)) {
        if (widget != nullptr) widget->setVisible(!compact);
    }
    if (previewOverflowButton_ != nullptr) {
        previewOverflowButton_->setVisible(compact);
    }
    if (tourButton_ != nullptr) {
        tourButton_->setToolButtonStyle(compact ? Qt::ToolButtonIconOnly : Qt::ToolButtonTextBesideIcon);
    }
    if (ptzPanelButton_ != nullptr) {
        ptzPanelButton_->setToolButtonStyle(compact ? Qt::ToolButtonIconOnly : Qt::ToolButtonTextOnly);
    }
}

QWidget *MainWindow::buildSidebar() {
    auto *sidebar = new QFrame;
    sidebar->setObjectName(QStringLiteral("sidebar"));
    sidebar->setMinimumWidth(248);
    sidebar->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Expanding);
    auto *layout = new QVBoxLayout(sidebar);
    layout->setContentsMargins(12, 13, 12, 11);
    layout->setSpacing(9);

    auto *title = new QLabel(QStringLiteral("资源目录"));
    title->setObjectName(QStringLiteral("brandTitle"));

    resourceTabs_ = new QTabBar;
    resourceTabs_->setObjectName(QStringLiteral("resourceTabs"));
    resourceTabs_->setExpanding(true);
    resourceTabs_->addTab(QStringLiteral("监控点"));
    resourceTabs_->addTab(QStringLiteral("收藏"));
    resourceTabs_->addTab(QStringLiteral("我的视图"));
    resourceTabs_->setCurrentIndex(catalogMode_);
    resourceTabs_->setAccessibleName(QStringLiteral("设备资源类型"));

    searchEdit_ = new QLineEdit;
    searchEdit_->setPlaceholderText(QStringLiteral("搜索区域、编号或别名"));
    searchEdit_->setClearButtonEnabled(true);
    searchEdit_->setAccessibleName(QStringLiteral("搜索设备资源"));
    auto *refreshButton = new QToolButton;
    refreshButton->setDefaultAction(actionRegistry_->action(ViewerActionId::RefreshCatalog));
    refreshButton->setToolTip(QStringLiteral("刷新授权设备目录"));
    refreshButton->setAccessibleName(QStringLiteral("刷新设备目录"));
    refreshButton->setFixedSize(34, 34);
    auto *searchLayout = new QHBoxLayout;
    searchLayout->setContentsMargins(0, 0, 0, 0);
    searchLayout->setSpacing(6);
    searchLayout->addWidget(searchEdit_, 1);
    searchLayout->addWidget(refreshButton);

    catalogTree_ = new CameraTreeWidget;
    catalogTree_->setObjectName(QStringLiteral("catalogTree"));
    catalogTree_->setHeaderHidden(true);
    catalogTree_->setIndentation(17);
    catalogTree_->setUniformRowHeights(true);
    catalogTree_->setContextMenuPolicy(Qt::CustomContextMenu);
    catalogTree_->setAccessibleName(QStringLiteral("授权设备资源树"));
    catalogSummaryLabel_ = new QLabel(QStringLiteral("正在加载授权目录…"));
    catalogSummaryLabel_->setObjectName(QStringLiteral("mutedLabel"));
    catalogSummaryLabel_->setWordWrap(true);

    layout->addWidget(title);
    layout->addWidget(resourceTabs_);
    layout->addLayout(searchLayout);
    layout->addWidget(catalogTree_, 1);
    layout->addWidget(catalogSummaryLabel_);
    connect(searchEdit_, &QLineEdit::textChanged, this, &MainWindow::filterCatalog);
    connect(resourceTabs_, &QTabBar::currentChanged, this, [this](int index) {
        catalogMode_ = index;
        rebuildCatalog();
        filterCatalog(searchEdit_->text());
        savePreferences();
    });
    connect(catalogTree_, &QTreeWidget::itemActivated, this, &MainWindow::assignCameraFromTree);
    connect(catalogTree_, &QTreeWidget::itemClicked, this, [this](QTreeWidgetItem *item, int) {
        if (item == nullptr) return;
        const QString kind = item->data(0, CatalogRoles::ResourceKind).toString();
        if (kind == QStringLiteral("region")) {
            currentRegionId_ = QUuid(item->data(0, CatalogRoles::ResourceId).toString());
        } else if (kind == QStringLiteral("camera")) {
            const QUuid cameraId(item->data(0, CatalogRoles::ResourceId).toString());
            const auto iterator = std::find_if(cameras_.cbegin(), cameras_.cend(), [cameraId](const CameraInfo &camera) {
                return camera.id == cameraId;
            });
            if (iterator != cameras_.cend()) currentRegionId_ = iterator->regionId;
        }
    });
    connect(catalogTree_, &QWidget::customContextMenuRequested, this, &MainWindow::handleCatalogContextMenu);
    return sidebar;
}

QWidget *MainWindow::buildWorkspace() {
    auto *workspace = new QWidget;
    auto *layout = new QVBoxLayout(workspace);
    workspaceLayout_ = layout;
    layout->setContentsMargins(14, 12, 14, 12);
    layout->setSpacing(10);

    dockPanelsMenu_ = titleBar_ != nullptr ? titleBar_->panelsMenu() : new QMenu(workspace);
    if (dockPanelsMenu_ != nullptr) {
        dockPanelsMenu_->clear();
    }

    workspaceStack_ = new QStackedWidget;
    workspaceStack_->addWidget(buildPreviewWorkspace());
    workspaceStack_->addWidget(buildPlaybackWorkspace());
    layout->addWidget(workspaceStack_, 1);
    return workspace;
}

QWidget *MainWindow::buildPreviewWorkspace() {
    auto *workspace = new QWidget;
    auto *layout = new QVBoxLayout(workspace);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(10);
    previewToolbar_ = buildToolbar();
    layout->addWidget(previewToolbar_);

    auto *videoHost = new QFrame;
    videoHost->setObjectName(QStringLiteral("videoHost"));
    videoGrid_ = new QGridLayout(videoHost);
    videoGrid_->setContentsMargins(3, 3, 3, 3);
    videoGrid_->setSpacing(3);
    layout->addWidget(videoHost, 1);
    previewControlStrip_ = buildControlStrip();
    layout->addWidget(previewControlStrip_);
    return workspace;
}

QWidget *MainWindow::buildPlaybackWorkspace() {
    auto *workspace = new QWidget;
    auto *layout = new QVBoxLayout(workspace);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(10);

    auto *toolbar = new QFrame;
    toolbar->setObjectName(QStringLiteral("toolbar"));
    toolbar->setMinimumHeight(50);
    toolbar->setMaximumHeight(64);
    toolbar->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    playbackToolbar_ = toolbar;
    playbackSearchPanel_ = toolbar;
    auto *toolbarLayout = new QHBoxLayout(toolbar);
    toolbarLayout->setContentsMargins(8, 6, 8, 6);
    toolbarLayout->setSpacing(6);
    auto *title = new QLabel(QStringLiteral("回放检索"));
    title->setObjectName(QStringLiteral("toolbarTitle"));
    toolbarLayout->addWidget(title);

    const QDateTime now = QDateTime::currentDateTime();
    playbackStartedAt_ = new QDateTimeEdit(QDateTime(now.date(), QTime(0, 0)), toolbar);
    playbackEndedAt_ = new QDateTimeEdit(now, toolbar);
    playbackStartedAt_->setObjectName(QStringLiteral("playbackStartedAt"));
    playbackEndedAt_->setObjectName(QStringLiteral("playbackEndedAt"));
    for (auto *editor : {playbackStartedAt_, playbackEndedAt_}) {
        editor->setDisplayFormat(QStringLiteral("yyyy-MM-dd HH:mm:ss"));
        editor->setCalendarPopup(true);
        editor->setFixedWidth(162);
    }
    auto *fromLabel = new QLabel(QStringLiteral("从"));
    fromLabel->setObjectName(QStringLiteral("mutedLabel"));
    toolbarLayout->addWidget(fromLabel);
    toolbarLayout->addWidget(playbackStartedAt_);
    auto *toLabel = new QLabel(QStringLiteral("至"));
    toLabel->setObjectName(QStringLiteral("mutedLabel"));
    toolbarLayout->addWidget(toLabel);
    toolbarLayout->addWidget(playbackEndedAt_);
    auto *todayButton = new QToolButton;
    todayButton->setObjectName(QStringLiteral("playbackPresetToday"));
    todayButton->setText(QStringLiteral("今天"));
    todayButton->setAccessibleName(QStringLiteral("检索今天录像"));
    todayButton->setToolTip(QStringLiteral("检索今天的录像"));
    connect(todayButton, &QToolButton::clicked, this, [this]() {
        if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("设置检索时间"))) {
            return;
        }
        const QDateTime current = QDateTime::currentDateTime();
        playbackStartedAt_->setDateTime(QDateTime(current.date(), QTime(0, 0)));
        playbackEndedAt_->setDateTime(current);
        refreshPlaybackSearches();
    });
    toolbarLayout->addWidget(todayButton);
    auto *yesterdayButton = new QToolButton;
    yesterdayButton->setObjectName(QStringLiteral("playbackPresetYesterday"));
    yesterdayButton->setText(QStringLiteral("昨天"));
    yesterdayButton->setAccessibleName(QStringLiteral("检索昨天录像"));
    yesterdayButton->setToolTip(QStringLiteral("检索昨天全天录像"));
    connect(yesterdayButton, &QToolButton::clicked, this, [this]() {
        if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("设置检索时间"))) {
            return;
        }
        const QDate yesterday = QDate::currentDate().addDays(-1);
        playbackStartedAt_->setDateTime(QDateTime(yesterday, QTime(0, 0)));
        playbackEndedAt_->setDateTime(QDateTime(yesterday, QTime(23, 59, 59)));
        refreshPlaybackSearches();
    });
    toolbarLayout->addWidget(yesterdayButton);
    auto *recentButton = new QToolButton;
    recentButton->setObjectName(QStringLiteral("playbackPresetRecentHour"));
    recentButton->setText(QStringLiteral("近一小时"));
    recentButton->setAccessibleName(QStringLiteral("检索最近一小时录像"));
    recentButton->setToolTip(QStringLiteral("检索最近一小时录像"));
    connect(recentButton, &QToolButton::clicked, this, [this]() {
        if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("设置检索时间"))) {
            return;
        }
        const QDateTime current = QDateTime::currentDateTime();
        playbackStartedAt_->setDateTime(current.addSecs(-3600));
        playbackEndedAt_->setDateTime(current);
        refreshPlaybackSearches();
    });
    toolbarLayout->addWidget(recentButton);
    auto *searchButton = new QPushButton(QStringLiteral("检索"));
    searchButton->setObjectName(QStringLiteral("playbackSearchButton"));
    searchButton->setIcon(IconProvider::instance().icon(ViewerIcon::Search));
    searchButton->setAccessibleName(QStringLiteral("检索录像"));
    connect(searchButton, &QPushButton::clicked, this, &MainWindow::refreshPlaybackSearches);
    toolbarLayout->addWidget(searchButton);
    toolbarLayout->addStretch();

    playbackLayoutButton_ = new QToolButton;
    playbackLayoutButton_->setObjectName(QStringLiteral("playbackLayoutButton"));
    playbackLayoutButton_->setIcon(IconProvider::instance().icon(ViewerIcon::LayoutGrid));
    playbackLayoutButton_->setText(QStringLiteral("%1 分屏").arg(playbackLayoutCount_));
    playbackLayoutButton_->setPopupMode(QToolButton::InstantPopup);
    playbackLayoutButton_->setToolTip(QStringLiteral("选择录像回放分屏"));
    playbackLayoutButton_->setAccessibleName(QStringLiteral("录像回放分屏"));
    actionRegistry_->bindWidget(ViewerActionId::ChangePlaybackLayout, playbackLayoutButton_);
    auto *playbackLayoutMenu = new QMenu(playbackLayoutButton_);
    auto *playbackLayoutGroup = new QActionGroup(playbackLayoutMenu);
    playbackLayoutGroup->setExclusive(true);
    for (const int count : {1, 4}) {
        auto *action = playbackLayoutMenu->addAction(QStringLiteral("%1 分屏回放").arg(count));
        action->setCheckable(true);
        action->setChecked(count == playbackLayoutCount_);
        action->setData(count);
        action->setObjectName(QStringLiteral("playbackLayout.%1").arg(count));
        action->setToolTip(QStringLiteral("%1 分屏回放").arg(count));
        playbackLayoutGroup->addAction(action);
        playbackLayoutActions_.append(action);
    }
    connect(playbackLayoutGroup, &QActionGroup::triggered, this, [this](QAction *action) {
        changePlaybackLayout(action->data().toInt());
    });
    playbackLayoutButton_->setMenu(playbackLayoutMenu);
    toolbarLayout->addWidget(playbackLayoutButton_);
    auto *stopAllButton = new QToolButton;
    stopAllButton->setDefaultAction(actionRegistry_->action(ViewerActionId::StopAllPlayback));
    stopAllButton->setToolTip(QStringLiteral("停止并清空全部录像回放"));
    stopAllButton->setAccessibleName(QStringLiteral("停止全部录像回放"));
    stopAllButton->setFixedSize(34, 31);
    toolbarLayout->addWidget(stopAllButton);
    auto *fullScreenButton = new QToolButton;
    fullScreenButton->setDefaultAction(actionRegistry_->action(ViewerActionId::ToggleFullScreen));
    fullScreenButton->setToolTip(QStringLiteral("切换视频画布全屏，按 Esc 或 F11 退出"));
    fullScreenButton->setAccessibleName(QStringLiteral("切换视频画布全屏"));
    fullScreenButton->setFixedSize(34, 31);
    toolbarLayout->addWidget(fullScreenButton);
    layout->addWidget(toolbar);

    auto *videoHost = new QFrame;
    videoHost->setObjectName(QStringLiteral("videoHost"));
    playbackGrid_ = new QGridLayout(videoHost);
    playbackGrid_->setContentsMargins(3, 3, 3, 3);
    playbackGrid_->setSpacing(3);
    layout->addWidget(videoHost, 1);

    auto *controlStrip = new QFrame;
    controlStrip->setObjectName(QStringLiteral("controlStrip"));
    auto *controlLayout = new QHBoxLayout(controlStrip);
    controlLayout->setContentsMargins(10, 7, 10, 7);
    playbackStatusLabel_ = new QLabel(QStringLiteral("选择摄像头后检索录像片段。"));
    playbackStatusLabel_->setObjectName(QStringLiteral("mutedLabel"));
    controlLayout->addWidget(playbackStatusLabel_, 1);
    playbackSyncButton_ = new QToolButton;
    playbackSyncButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::PlaybackSync));
    actionRegistry_->setChecked(
        ViewerActionId::PlaybackSync,
        playbackController_ != nullptr && playbackController_->syncEnabled());
    playbackSyncButton_->setToolTip(QStringLiteral("同步控制已加入同步组的窗格；独立窗格仅控制自身"));
    playbackSyncButton_->setAccessibleName(QStringLiteral("同步组控制"));
    controlLayout->addWidget(playbackSyncButton_);
    playbackPauseButton_ = new QToolButton;
    playbackPauseButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::PlaybackPause));
    playbackPauseButton_->setToolTip(QStringLiteral("暂停当前回放"));
    playbackPauseButton_->setAccessibleName(QStringLiteral("暂停回放"));
    playbackPauseButton_->setFixedSize(34, 31);
    controlLayout->addWidget(playbackPauseButton_);
    playbackResumeButton_ = new QToolButton;
    playbackResumeButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::PlaybackResume));
    playbackResumeButton_->setToolTip(QStringLiteral("继续当前回放"));
    playbackResumeButton_->setAccessibleName(QStringLiteral("继续回放"));
    playbackResumeButton_->setFixedSize(34, 31);
    controlLayout->addWidget(playbackResumeButton_);
    playbackSpeedCombo_ = new QComboBox;
    playbackSpeedCombo_->setObjectName(QStringLiteral("playbackSpeedCombo"));
    playbackSpeedCombo_->setToolTip(QStringLiteral("设置回放速度"));
    playbackSpeedCombo_->addItem(QStringLiteral("0.5x"), 0.5);
    playbackSpeedCombo_->addItem(QStringLiteral("1x"), 1.0);
    playbackSpeedCombo_->addItem(QStringLiteral("2x"), 2.0);
    playbackSpeedCombo_->addItem(QStringLiteral("4x"), 4.0);
    playbackSpeedCombo_->setCurrentIndex(1);
    connect(playbackSpeedCombo_, &QComboBox::activated, this, [this](int) {
        if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("设置回放倍速"))) {
            updatePlaybackControlState();
            return;
        }
        controlPlayback(QStringLiteral("SetSpeed"), playbackSpeedCombo_->currentData().toDouble());
    });
    controlLayout->addWidget(playbackSpeedCombo_);

    auto *timelineSeparator = new QFrame;
    timelineSeparator->setFrameShape(QFrame::VLine);
    timelineSeparator->setObjectName(QStringLiteral("toolbarSeparator"));
    controlLayout->addWidget(timelineSeparator);
    auto *zoomOutButton = new QToolButton;
    zoomOutButton->setObjectName(QStringLiteral("timelineZoomOut"));
    zoomOutButton->setText(QStringLiteral("−"));
    zoomOutButton->setToolTip(QStringLiteral("缩小时间轴"));
    zoomOutButton->setAccessibleName(QStringLiteral("缩小时间轴"));
    zoomOutButton->setFixedSize(31, 31);
    controlLayout->addWidget(zoomOutButton);
    timelineZoomLabel_ = new QLabel(QStringLiteral("1.0x"));
    timelineZoomLabel_->setObjectName(QStringLiteral("mutedLabel"));
    timelineZoomLabel_->setAlignment(Qt::AlignCenter);
    timelineZoomLabel_->setFixedWidth(48);
    controlLayout->addWidget(timelineZoomLabel_);
    auto *zoomInButton = new QToolButton;
    zoomInButton->setObjectName(QStringLiteral("timelineZoomIn"));
    zoomInButton->setText(QStringLiteral("+"));
    zoomInButton->setToolTip(QStringLiteral("放大时间轴"));
    zoomInButton->setAccessibleName(QStringLiteral("放大时间轴"));
    zoomInButton->setFixedSize(31, 31);
    controlLayout->addWidget(zoomInButton);
    auto *zoomResetButton = new QToolButton;
    zoomResetButton->setObjectName(QStringLiteral("timelineZoomReset"));
    zoomResetButton->setText(QStringLiteral("适应"));
    zoomResetButton->setToolTip(QStringLiteral("显示完整检索时间范围"));
    zoomResetButton->setAccessibleName(QStringLiteral("时间轴适应范围"));
    controlLayout->addWidget(zoomResetButton);
    recordingTimelinePanel_ = new QWidget;
    recordingTimelinePanel_->setObjectName(QStringLiteral("recordingTimelinePanel"));
    recordingTimelinePanel_->setMinimumHeight(220);
    recordingTimelinePanel_->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Minimum);
    auto *timelinePanelLayout = new QVBoxLayout(recordingTimelinePanel_);
    timelinePanelLayout->setContentsMargins(0, 0, 0, 0);
    timelinePanelLayout->setSpacing(6);
    timelinePanelLayout->addWidget(controlStrip);

    recordingTimeline_ = new RecordingTimelineWidget;
    connect(recordingTimeline_, &RecordingTimelineWidget::positionSelected, this, &MainWindow::seekPlayback);
    connect(recordingTimeline_, &RecordingTimelineWidget::positionUnavailable, this, [this](const QDateTime &position) {
        playbackStatusLabel_->setText(QStringLiteral("%1 附近没有可用录像片段。").arg(position.toString(QStringLiteral("yyyy-MM-dd HH:mm:ss"))));
    });
    connect(recordingTimeline_, &RecordingTimelineWidget::trackSelected, this, [this](int trackIndex) {
        if (trackIndex >= 0 && trackIndex < playbackLayoutCount_ && trackIndex < playbackTiles_.size()) {
            selectPlaybackTile(playbackTiles_.at(trackIndex));
        }
    });
    connect(recordingTimeline_, &RecordingTimelineWidget::zoomChanged, this, [this](double factor) {
        if (timelineZoomLabel_ != nullptr) timelineZoomLabel_->setText(QStringLiteral("%1x").arg(factor, 0, 'f', factor < 10.0 ? 1 : 0));
    });
    connect(zoomOutButton, &QToolButton::clicked, this, [this]() {
        if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("缩小时间轴"))) {
            recordingTimeline_->zoomOut();
        }
    });
    connect(zoomInButton, &QToolButton::clicked, this, [this]() {
        if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("放大时间轴"))) {
            recordingTimeline_->zoomIn();
        }
    });
    connect(zoomResetButton, &QToolButton::clicked, this, [this]() {
        if (ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("适应时间轴范围"))) {
            recordingTimeline_->resetZoom();
        }
    });
    timelinePanelLayout->addWidget(recordingTimeline_);
    layout->addWidget(recordingTimelinePanel_);
    return workspace;
}

QWidget *MainWindow::buildPlaybackSearchPanel() {
    playbackSearchPanel_->setParent(nullptr);
    playbackSearchPanel_->setObjectName(QStringLiteral("playbackSearchPanel"));
    return playbackSearchPanel_;
}

QWidget *MainWindow::buildRecordingTimelinePanel() {
    recordingTimelinePanel_->setParent(nullptr);
    return recordingTimelinePanel_;
}

void MainWindow::switchWorkspace(int index) {
    if (index < 0 || index > 1 || workspaceStack_ == nullptr) {
        return;
    }
    if (isCanvasPresentationBusy()) {
        // 全屏退出完成后再切工作区，不能在 Qt ADS 临时隐藏状态中恢复另一套透视。
        pendingCanvasWorkspaceIndex_ = index;
        exitCanvasFullScreen();
        return;
    }
    if (index == activeWorkspace_ || workspaceTransition_.has_value()) {
        refreshControllerActionStates();
        return;
    }
    const WorkspaceMode nextMode = index == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback;
    if (workspaceController_ != nullptr && !workspaceController_->setMode(nextMode)) {
        return;
    }
    const bool sourcePlayback = activeWorkspace_ == 1;
    restoreTileLayout(sourcePlayback);
    if (!sourcePlayback) {
        stopTour();
        stopActivePtzPulse();
    } else if (playbackCursorTimer_ != nullptr) {
        playbackCursorTimer_->stop();
    }

    WorkspaceTransition transition;
    transition.targetIndex = index;
    transition.sourcePlayback = sourcePlayback;
    const QList<VideoTileWidget *> &sourceTiles = sourcePlayback ? playbackTiles_ : tiles_;
    for (VideoTileWidget *tile : sourceTiles) {
        if (tile == nullptr) {
            continue;
        }
        if (sourcePlayback && tile->camera().has_value()) {
            apiClient_->cancelRecordingSearch(tile->camera()->id);
        }
        if (!tile->requestId().isNull() || !tile->sessionId().isNull() || tile->hasAllocatedPlayer()) {
            transition.pendingTiles.append(tile);
        }
    }
    if (sourcePlayback) {
        playbackControlBatch_.reset();
    }
    workspaceTransition_ = std::move(transition);
    setWorkspaceInteractionEnabled(false);
    QTimer::singleShot(0, this, &MainWindow::processWorkspaceReleaseStep);
}

void MainWindow::releaseWorkspaceTile(bool playbackWorkspace, VideoTileWidget *tile) {
    if (tile == nullptr) {
        return;
    }
    if (playbackWorkspace && !tile->sessionId().isNull()) {
        freezePlaybackClock(tile->sessionId());
        playbackTransport_.remove(tile->sessionId());
        playbackControlsInFlight_.remove(tile->sessionId());
        playbackAdvancingSessions_.remove(tile->sessionId());
        playbackClockAnchoredAt_.remove(tile->sessionId());
        playbackMediaOriginSeconds_.remove(tile->sessionId());
        playbackMediaLastSeconds_.remove(tile->sessionId());
        playbackMediaOriginPositions_.remove(tile->sessionId());
    }
    tile->suspendSession();
}

void MainWindow::processWorkspaceReleaseStep() {
    if (!workspaceTransition_.has_value() || sessionEnding_) {
        return;
    }
    WorkspaceTransition &transition = *workspaceTransition_;
    if (transition.nextTileIndex < transition.pendingTiles.size()) {
        VideoTileWidget *tile = transition.pendingTiles.at(transition.nextTileIndex++);
        releaseWorkspaceTile(transition.sourcePlayback, tile);
        QTimer::singleShot(0, this, &MainWindow::processWorkspaceReleaseStep);
        return;
    }
    finishWorkspaceTransition();
}

void MainWindow::finishWorkspaceTransition() {
    if (!workspaceTransition_.has_value() || sessionEnding_) {
        return;
    }
    const int index = workspaceTransition_->targetIndex;
    workspaceTransition_.reset();
    const WorkspaceMode nextMode = index == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback;
    std::optional<int> pendingSavedView;
    if (index == 0) {
        pendingSavedView = pendingSavedViewIndex_;
        pendingSavedViewIndex_.reset();
    }
    std::optional<PendingInstantPlayback> pendingInstantPlayback;
    if (index == 1) {
        pendingInstantPlayback = pendingInstantPlayback_;
        pendingInstantPlayback_.reset();
    }

    activeWorkspace_ = index;
    if (titleBar_ != nullptr) {
        titleBar_->confirmWorkspaceMode(nextMode);
    }
    workspaceStack_->setCurrentIndex(index);
    if (dockLayoutController_ != nullptr) {
        dockLayoutController_->switchWorkspace(
            nextMode,
            uiSettings_ != nullptr ? uiSettings_->dockState(nextMode) : QByteArray{});
    }
    if (ptzController_ != nullptr) {
        ptzController_->setWorkspaceMode(nextMode);
    }
    if (index == 0) {
        if (!pendingSavedView.has_value()) {
            for (VideoTileWidget *tile : tiles_) {
                if (tile->isVisible() && tile->camera().has_value() && tile->camera()->canLiveView) {
                    requestCamera(tile, *tile->camera());
                }
            }
        }
        statusLabel_->setText(QStringLiteral("已切换到实时预览。"));
    } else {
        VideoTileWidget *previouslySelected = selectedPlaybackTile_;
        VideoTileWidget *tileToSkip = playbackTileToSkipOnNextRestore_;
        playbackTileToSkipOnNextRestore_ = nullptr;
        for (VideoTileWidget *tile : playbackTiles_) {
            if (tile != tileToSkip && tile->isVisible() && tile->camera().has_value()) {
                requestPlayback(tile, *tile->camera(), playbackStartedAt_->dateTime(), playbackEndedAt_->dateTime());
            }
        }
        if (pendingInstantPlayback.has_value() && pendingInstantPlayback->target != nullptr) {
            selectPlaybackTile(pendingInstantPlayback->target);
            requestPlayback(
                pendingInstantPlayback->target,
                pendingInstantPlayback->camera,
                pendingInstantPlayback->startedAt,
                pendingInstantPlayback->endedAt);
            playbackStatusLabel_->setText(QStringLiteral("已打开 %1 的即时回放。")
                                               .arg(pendingInstantPlayback->camera.alias));
        } else if (previouslySelected != nullptr && previouslySelected->isVisible()) {
            selectPlaybackTile(previouslySelected);
        }
        updatePlaybackTimeline();
    }
    updateCatalogSummary();
    updatePtzState();
    setWorkspaceInteractionEnabled(true);
    updatePlaybackControlState();
    refreshControllerActionStates();
    savePreferences();

    if (pendingSavedView.has_value()) {
        applySavedView(*pendingSavedView);
    }
}

void MainWindow::setWorkspaceInteractionEnabled(bool enabled) {
    if (workspaceController_ != nullptr) {
        workspaceController_->setInteractionEnabled(enabled);
    }
    if (titleBar_ != nullptr) {
        titleBar_->setWorkspaceSwitchEnabled(enabled);
        titleBar_->setUtilityMenusEnabled(enabled);
    }
    // 工作区切换期间保留窗口级全屏命令；其余业务控件由统一动作状态和下列直接控件门控。
    if (workspaceStack_ != nullptr) workspaceStack_->setEnabled(true);
    const QString directControlReason = enabled ? QString{} : QStringLiteral("正在切换工作区，请稍候。");
    setEnabledWithReason(ptzPanelButton_, enabled, directControlReason);
    setEnabledWithReason(previewOverflowButton_, enabled, directControlReason);
    setEnabledWithReason(tourSourceCombo_, enabled, directControlReason);
    setEnabledWithReason(tourIntervalSpin_, enabled, directControlReason);
    setEnabledWithReason(streamProfileCombo_, enabled, directControlReason);
    if (catalogTree_ != nullptr) {
        catalogTree_->setEnabled(enabled);
    }
    if (ptzPanel_ != nullptr) {
        ptzPanel_->setEnabled(enabled);
    }
    if (dockLayoutController_ != nullptr) {
        dockLayoutController_->setInteractionFrozen(!enabled);
    }
    const QString tileReason = enabled ? QString{} : QStringLiteral("正在切换工作区，请稍候。");
    for (VideoTileWidget *tile : std::as_const(tiles_)) {
        if (tile != nullptr) {
            tile->setCommandInteractionEnabled(enabled, tileReason);
        }
    }
    for (VideoTileWidget *tile : std::as_const(playbackTiles_)) {
        if (tile != nullptr) {
            tile->setCommandInteractionEnabled(enabled, tileReason);
        }
    }
    updateWorkspaceInteractionControls();
    updatePtzControlState();
}

void MainWindow::changePlaybackLayout(int count) {
    if (playbackGrid_ == nullptr || !PlaybackController::isSupportedLayout(count)) {
        return;
    }
    const bool needsInitialGrid = playbackGrid_->count() == 0;
    if (!needsInitialGrid &&
        !ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("切换录像回放分屏"))) {
        return;
    }
    if (count == playbackLayoutCount_ && !needsInitialGrid) {
        syncLayoutMenuSelection(playbackLayoutButton_, count);
        return;
    }
    if (playbackController_ != nullptr && !needsInitialGrid && !playbackController_->setLayoutCount(count)) {
        syncLayoutMenuSelection(playbackLayoutButton_, playbackLayoutCount_);
        return;
    }
    const bool restoreSessions = !suppressLayoutSessionRestore_;
    suppressLayoutSessionRestore_ = false;
    restoreTileLayout(true);
    const int previousCount = playbackLayoutCount_;
    while (auto *item = playbackGrid_->takeAt(0)) {
        delete item;
    }
    playbackLayoutCount_ = count;
    const int dimension = count == 1 ? 1 : 2;
    for (int index = 0; index < playbackTiles_.size(); ++index) {
        auto *tile = playbackTiles_.at(index);
        if (index < count) {
            playbackGrid_->addWidget(tile, index / dimension, index % dimension);
            tile->setCompact(false);
            tile->show();
        } else {
            if (!tile->sessionId().isNull()) {
                freezePlaybackClock(tile->sessionId());
                playbackTransport_.remove(tile->sessionId());
                playbackControlsInFlight_.remove(tile->sessionId());
                playbackAdvancingSessions_.remove(tile->sessionId());
                playbackClockAnchoredAt_.remove(tile->sessionId());
                playbackMediaOriginSeconds_.remove(tile->sessionId());
                playbackMediaLastSeconds_.remove(tile->sessionId());
                playbackMediaOriginPositions_.remove(tile->sessionId());
            }
            if (tile->camera().has_value()) apiClient_->cancelRecordingSearch(tile->camera()->id);
            tile->suspendSession();
            tile->hide();
        }
    }
    if (selectedPlaybackTile_ == nullptr || !selectedPlaybackTile_->isVisible()) {
        selectPlaybackTile(playbackTiles_.first());
    }
    if (restoreSessions && activeWorkspace_ == 1 && count > previousCount) {
        for (int index = previousCount; index < count; ++index) {
            VideoTileWidget *tile = playbackTiles_.at(index);
            if (tile->camera().has_value()) {
                requestPlayback(tile, *tile->camera(), playbackStartedAt_->dateTime(), playbackEndedAt_->dateTime());
            }
        }
    }
    if (playbackLayoutButton_ != nullptr) {
        playbackLayoutButton_->setText(QStringLiteral("%1 分屏").arg(count));
    }
    syncLayoutMenuSelection(playbackLayoutButton_, count);
    updatePlaybackTimeline();
    updatePlaybackControlState();
    savePreferences();
}

QList<VideoTileWidget *> MainWindow::activePlaybackTiles() const {
    QList<VideoTileWidget *> result;
    for (int index = 0; index < playbackLayoutCount_ && index < playbackTiles_.size(); ++index) {
        auto *tile = playbackTiles_.at(index);
        if (tile->camera().has_value()) {
            result.append(tile);
        }
    }
    return result;
}

void MainWindow::selectPlaybackTile(VideoTileWidget *tile) {
    if (tile == nullptr) {
        return;
    }
    if (selectedPlaybackTile_ != nullptr) {
        selectedPlaybackTile_->setSelected(false);
    }
    selectedPlaybackTile_ = tile;
    selectedPlaybackTile_->setSelected(true);
    if (playbackController_ != nullptr) playbackController_->selectTile(tile->index());
    const PlaybackTransportInfo transport = playbackTransport_.value(tile->sessionId());
    if (transport.position.isValid()) {
        playbackCursor_ = estimatedPlaybackPosition(tile->sessionId());
    }
    updatePlaybackTimeline();
    updatePlaybackControlState();
}

void MainWindow::refreshPlaybackSearches() {
    if (playbackStartedAt_ == nullptr || playbackEndedAt_ == nullptr) {
        return;
    }
    if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("录像检索"))) {
        return;
    }
    const QDateTime startedAt = playbackStartedAt_->dateTime();
    const QDateTime endedAt = playbackEndedAt_->dateTime();
    QString rangeError;
    if (!PlaybackController::validateTimeRange(startedAt, endedAt, &rangeError)) {
        playbackStatusLabel_->setText(rangeError);
        return;
    }
    playbackCursor_ = startedAt;
    QSet<QUuid> activeCameraIds;
    for (auto *tile : activePlaybackTiles()) {
        if (tile->camera().has_value()) activeCameraIds.insert(tile->camera()->id);
    }
    for (const QUuid &cameraId : playbackSegments_.keys()) {
        if (!activeCameraIds.contains(cameraId)) playbackSegments_.remove(cameraId);
    }
    for (const QUuid &cameraId : playbackSearchRequests_.keys()) {
        if (!activeCameraIds.contains(cameraId)) playbackSearchRequests_.remove(cameraId);
    }
    for (const QUuid &cameraId : playbackSearchStates_.keys()) {
        if (!activeCameraIds.contains(cameraId)) playbackSearchStates_.remove(cameraId);
    }
    const auto playbackTiles = activePlaybackTiles();
    auto *previouslySelected = selectedPlaybackTile_;
    for (auto *tile : playbackTiles) {
        const CameraInfo camera = *tile->camera();
        playbackSegments_.remove(camera.id);
        requestPlayback(tile, camera, startedAt, endedAt);
    }
    if (previouslySelected != nullptr && previouslySelected->isVisible()) {
        selectPlaybackTile(previouslySelected);
    }
    updatePlaybackTimeline();
    updatePlaybackCalendarMarks();
    playbackStatusLabel_->setText(playbackTiles.isEmpty()
                                      ? QStringLiteral("请先为回放窗格分配摄像头。")
                                      : QStringLiteral("正在按新时间范围重建回放并检索录像片段…"));
}

void MainWindow::handleRecordingSearchCompleted(
    const QUuid &requestId,
    const QUuid &cameraId,
    const QList<RecordingSegment> &segments) {
    if (playbackSearchRequests_.value(cameraId) != requestId) {
        return;
    }
    playbackSegments_.insert(cameraId, segments);
    playbackSearchStates_.insert(cameraId, segments.isEmpty()
                                               ? QStringLiteral("无录像")
                                               : QStringLiteral("%1 段").arg(segments.size()));
    updatePlaybackTimeline();
    updatePlaybackCalendarMarks();
    updatePlaybackSearchSummary();
}

void MainWindow::handleRecordingSearchFailed(const QUuid &requestId, const QUuid &cameraId, const QString &message) {
    if (playbackSearchRequests_.value(cameraId) != requestId) {
        return;
    }
    playbackSegments_.remove(cameraId);
    playbackSearchStates_.insert(cameraId, QStringLiteral("检索失败"));
    updatePlaybackTimeline();
    playbackStatusLabel_->setText(QStringLiteral("录像检索失败：%1").arg(message));
}

void MainWindow::updatePlaybackSearchSummary() {
    if (playbackStatusLabel_ == nullptr) return;
    int searchingCount = 0;
    int readyCount = 0;
    int emptyCount = 0;
    int failedCount = 0;
    int segmentCount = 0;
    for (VideoTileWidget *tile : activePlaybackTiles()) {
        if (!tile->camera().has_value()) continue;
        const QUuid cameraId = tile->camera()->id;
        const QString state = playbackSearchStates_.value(cameraId);
        if (state == QStringLiteral("检索中")) ++searchingCount;
        else if (state == QStringLiteral("无录像")) ++emptyCount;
        else if (state == QStringLiteral("检索失败")) ++failedCount;
        else if (!state.isEmpty()) ++readyCount;
        segmentCount += playbackSegments_.value(cameraId).size();
    }
    if (searchingCount > 0) {
        playbackStatusLabel_->setText(QStringLiteral("正在检索 %1 路录像，已完成 %2 路…").arg(searchingCount).arg(readyCount + emptyCount + failedCount));
    } else if (readyCount + emptyCount + failedCount > 0) {
        playbackStatusLabel_->setText(QStringLiteral("检索完成：%1 个片段，%2 路无录像，%3 路失败。")
                                          .arg(segmentCount)
                                          .arg(emptyCount)
                                          .arg(failedCount));
    }
}

void MainWindow::updatePlaybackCalendarMarks() {
    if (playbackStartedAt_ == nullptr || playbackEndedAt_ == nullptr) return;
    const QList<QCalendarWidget *> calendars{
        playbackStartedAt_->calendarWidget(),
        playbackEndedAt_->calendarWidget()};
    const QTextCharFormat emptyFormat;
    for (QCalendarWidget *calendar : calendars) {
        if (calendar == nullptr) continue;
        for (const QDate &date : markedRecordingDates_) calendar->setDateTextFormat(date, emptyFormat);
    }
    markedRecordingDates_.clear();
    for (const QList<RecordingSegment> &segments : playbackSegments_) {
        for (const RecordingSegment &segment : segments) {
            QDate date = segment.startedAt.toLocalTime().date();
            const QDate endDate = segment.endedAt.toLocalTime().date();
            while (date.isValid() && date <= endDate && markedRecordingDates_.size() < 64) {
                markedRecordingDates_.insert(date);
                date = date.addDays(1);
            }
        }
    }
    QTextCharFormat recordingFormat;
    recordingFormat.setForeground(QColor(QStringLiteral("#F4F6F7")));
    recordingFormat.setBackground(QColor(QStringLiteral("#2E6D52")));
    for (QCalendarWidget *calendar : calendars) {
        if (calendar == nullptr) continue;
        for (const QDate &date : markedRecordingDates_) calendar->setDateTextFormat(date, recordingFormat);
    }
}

void MainWindow::updatePlaybackTimeline() {
    if (recordingTimeline_ == nullptr || playbackStartedAt_ == nullptr || playbackEndedAt_ == nullptr) {
        return;
    }
    recordingTimeline_->setRange(playbackStartedAt_->dateTime(), playbackEndedAt_->dateTime());
    QList<RecordingTimelineTrack> tracks;
    for (int index = 0; index < playbackLayoutCount_ && index < playbackTiles_.size(); ++index) {
        auto *tile = playbackTiles_.at(index);
        const auto camera = tile->camera();
        tracks.append({
            camera.has_value() ? QStringLiteral("%1 %2").arg(camera->code, camera->alias) : QStringLiteral("未分配"),
            camera.has_value() ? playbackSearchStates_.value(camera->id, QStringLiteral("未检索")) : QString{},
            camera.has_value() ? playbackSegments_.value(camera->id) : QList<RecordingSegment>{},
            tile == selectedPlaybackTile_});
    }
    recordingTimeline_->setTracks(tracks);
    recordingTimeline_->setCursor(playbackCursor_.isValid() ? playbackCursor_ : playbackStartedAt_->dateTime());
}

void MainWindow::seekPlayback(const QDateTime &position) {
    if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("定位回放"))) {
        return;
    }
    const QDateTime previousPosition = playbackCursor_.isValid()
        ? playbackCursor_
        : playbackStartedAt_->dateTime();
    playbackCursor_ = position;
    if (!controlPlayback(QStringLiteral("Seek"))) {
        playbackCursor_ = previousPosition;
        recordingTimeline_->setCursor(previousPosition);
    }
}

bool MainWindow::controlPlayback(const QString &action, double speed) {
    if (!ensureWorkspaceInteraction(WorkspaceMode::Playback, QStringLiteral("回放控制"))) {
        return false;
    }
    if (playbackControlBatch_.has_value() && !playbackControlBatch_->pending.isEmpty()) {
        playbackStatusLabel_->setText(QStringLiteral("上一批回放控制仍在处理中，请等待完成后再操作。"));
        updatePlaybackControlState();
        return false;
    }
    updatePlaybackControlState();
    const QList<VideoTileWidget *> targets = playbackControlTargets();
    if (targets.isEmpty()) {
        playbackStatusLabel_->setText(QStringLiteral("请先为回放窗格分配摄像头。"));
        return false;
    }
    const PlaybackControlState summary = playbackController_ != nullptr
        ? playbackController_->controlState()
        : PlaybackControlState{};
    const bool capabilityAvailable =
        (action == QStringLiteral("Seek") && summary.seekEnabled) ||
        (action == QStringLiteral("SetSpeed") && summary.speedEnabled) ||
        (action == QStringLiteral("Pause") && summary.pauseEnabled) ||
        (action == QStringLiteral("Resume") && summary.resumeEnabled);
    if (!summary.ready || summary.pending || !capabilityAvailable) {
        playbackStatusLabel_->setText(targets.size() > 1
                                          ? QStringLiteral("同步组中存在尚未就绪、正在处理或不支持此控制的窗格。")
                                          : QStringLiteral("当前回放会话尚未就绪、正在处理或不支持此控制。"));
        updatePlaybackControlState();
        return false;
    }

    PlaybackControlBatch batch;
    batch.action = action;
    for (auto *tile : targets) {
        freezePlaybackClock(tile->sessionId());
        const QDateTime position = action == QStringLiteral("Seek") ? playbackCursor_ : QDateTime{};
        apiClient_->controlPlayback(tile->sessionId(), action, position, speed);
        playbackControlsInFlight_.insert(tile->sessionId());
        batch.pending.insert(tile->sessionId());
    }
    playbackControlBatch_ = batch;
    playbackStatusLabel_->setText(targets.size() > 1
                                      ? QStringLiteral("正在向 %1 个同步窗格发送%2命令…").arg(targets.size()).arg(playbackActionLabel(action))
                                      : QStringLiteral("正在发送回放%1命令…").arg(playbackActionLabel(action)));
    updatePlaybackControlState();
    return true;
}

void MainWindow::handlePlaybackControlQueued(const QUuid &sessionId, const PlaybackTransportInfo &transport) {
    playbackControlsInFlight_.remove(sessionId);
    const QString completedAction = playbackControlBatch_.has_value() ? playbackControlBatch_->action : QString{};
    const bool belongsToBatch = playbackControlBatch_.has_value() && playbackControlBatch_->pending.remove(sessionId);
    if (belongsToBatch) playbackControlBatch_->succeeded.insert(sessionId);
    auto *tile = findTileBySession(sessionId);
    if (tile == nullptr || !playbackTiles_.contains(tile)) {
        if (belongsToBatch) finishPlaybackControlBatchIfReady();
        else updatePlaybackControlState();
        return;
    }
    if (completedAction == QStringLiteral("Seek")) {
        playbackMediaOriginSeconds_.remove(sessionId);
        playbackMediaLastSeconds_.remove(sessionId);
        playbackMediaOriginPositions_.remove(sessionId);
    }
    anchorPlaybackClock(sessionId, transport, tile->isPlaying() && !transport.isPaused);
    if (!transport.isPaused && !tile->isPlaying() && canInteractWithWorkspace(WorkspaceMode::Playback)) {
        tile->refreshPlaybackStream();
    }
    if (tile == selectedPlaybackTile_ && transport.position.isValid()) {
        playbackCursor_ = transport.position.toLocalTime();
    }
    if (tile == selectedPlaybackTile_ && playbackSpeedCombo_ != nullptr && transport.speed > 0.0) {
        const int index = playbackSpeedCombo_->findData(transport.speed);
        if (index >= 0) {
            QSignalBlocker blocker(playbackSpeedCombo_);
            playbackSpeedCombo_->setCurrentIndex(index);
        }
    }
    updatePlaybackControlState();
    updatePlaybackTimeline();
    if (belongsToBatch) {
        finishPlaybackControlBatchIfReady();
    } else {
        playbackStatusLabel_->setText(transport.detail.isEmpty() ? QStringLiteral("回放控制命令已完成。") : transport.detail);
    }
}

void MainWindow::handlePlaybackControlFailed(const QUuid &sessionId, const QString &message) {
    playbackControlsInFlight_.remove(sessionId);
    const bool belongsToBatch = playbackControlBatch_.has_value() && playbackControlBatch_->pending.remove(sessionId);
    if (belongsToBatch) playbackControlBatch_->failed.insert(sessionId, message);
    auto *tile = findTileBySession(sessionId);
    if (tile == nullptr || !playbackTiles_.contains(tile)) {
        if (belongsToBatch) finishPlaybackControlBatchIfReady();
        else updatePlaybackControlState();
        return;
    }
    const PlaybackTransportInfo transport = playbackTransport_.value(sessionId);
    anchorPlaybackClock(sessionId, transport, tile->isPlaying() && !transport.isPaused);
    if (tile == selectedPlaybackTile_ && transport.position.isValid()) {
        playbackCursor_ = transport.position.toLocalTime();
        recordingTimeline_->setCursor(playbackCursor_);
    }
    updatePlaybackControlState();
    if (belongsToBatch) {
        finishPlaybackControlBatchIfReady();
    } else {
        playbackStatusLabel_->setText(QStringLiteral("回放控制失败：%1").arg(message));
    }
}

void MainWindow::updatePlaybackControlState() {
    if (playbackController_ != nullptr) {
        playbackController_->setBatchPending(
            playbackControlBatch_.has_value() && !playbackControlBatch_->pending.isEmpty());
        for (int index = 0; index < playbackTiles_.size(); ++index) {
            VideoTileWidget *tile = playbackTiles_.at(index);
            if (tile == nullptr || !tile->camera().has_value()) {
                playbackController_->clearTile(index);
                continue;
            }
            PlaybackTileState state;
            state.cameraId = tile->camera()->id;
            state.sessionId = tile->sessionId();
            state.syncMember = tile->isSyncMember();
            state.commandPending = playbackControlsInFlight_.contains(tile->sessionId());
            state.transport = playbackTransport_.value(tile->sessionId());
            playbackController_->setTileState(index, state);
        }
    }
    const bool hasSession = selectedPlaybackTile_ != nullptr && !selectedPlaybackTile_->sessionId().isNull();
    const PlaybackTransportInfo transport = hasSession ? playbackTransport_.value(selectedPlaybackTile_->sessionId()) : PlaybackTransportInfo{};
    const PlaybackControlState summary = playbackController_ != nullptr
        ? playbackController_->controlState()
        : PlaybackControlState{};
    const bool playbackInteractionsEnabled = canInteractWithWorkspace(WorkspaceMode::Playback);
    const QString speedReason = playbackInteractionsEnabled
        ? QStringLiteral("当前回放会话尚未就绪或不支持倍速控制。")
        : QStringLiteral("请先切换到录像回放工作区。");
    setEnabledWithReason(
        playbackSpeedCombo_,
        playbackInteractionsEnabled && summary.speedEnabled,
        speedReason);
    if (playbackSpeedCombo_ != nullptr) {
        const int speedIndex = playbackSpeedCombo_->findData(transport.speed > 0.0 ? transport.speed : 1.0);
        if (speedIndex >= 0) {
            const QSignalBlocker blocker(playbackSpeedCombo_);
            playbackSpeedCombo_->setCurrentIndex(speedIndex);
        }
    }
    if (playbackCursorTimer_ != nullptr) {
        if (hasSession && playbackAdvancingSessions_.contains(selectedPlaybackTile_->sessionId()) && !summary.pending) {
            playbackCursorTimer_->start();
        } else {
            playbackCursorTimer_->stop();
        }
    }
    refreshControllerActionStates();
}

QList<VideoTileWidget *> MainWindow::playbackControlTargets() const {
    QList<VideoTileWidget *> targets;
    if (selectedPlaybackTile_ == nullptr || !selectedPlaybackTile_->camera().has_value()) {
        return targets;
    }
    const bool useSyncGroup = playbackController_ != nullptr && playbackController_->syncEnabled() &&
                              selectedPlaybackTile_->isSyncMember();
    if (!useSyncGroup) {
        targets.append(selectedPlaybackTile_);
        return targets;
    }
    for (VideoTileWidget *tile : activePlaybackTiles()) {
        if (tile->isSyncMember()) targets.append(tile);
    }
    return targets;
}

QDateTime MainWindow::estimatedPlaybackPosition(const QUuid &sessionId) const {
    PlaybackTransportInfo transport = playbackTransport_.value(sessionId);
    QDateTime position = transport.position.isValid()
        ? transport.position.toLocalTime()
        : (playbackStartedAt_ != nullptr ? playbackStartedAt_->dateTime() : QDateTime{});
    if (!position.isValid()) return position;
    if (playbackAdvancingSessions_.contains(sessionId) && playbackClockAnchoredAt_.contains(sessionId)) {
        const qint64 elapsedMilliseconds = std::max<qint64>(0, playbackMonotonicClock_.elapsed() - playbackClockAnchoredAt_.value(sessionId));
        position = position.addMSecs(static_cast<qint64>(elapsedMilliseconds * std::max(0.0, transport.speed)));
    }
    if (playbackStartedAt_ != nullptr && position < playbackStartedAt_->dateTime()) position = playbackStartedAt_->dateTime();
    if (playbackEndedAt_ != nullptr && position > playbackEndedAt_->dateTime()) position = playbackEndedAt_->dateTime();
    return position;
}

void MainWindow::anchorPlaybackClock(const QUuid &sessionId, const PlaybackTransportInfo &transport, bool advancing) {
    if (sessionId.isNull()) return;
    PlaybackTransportInfo anchored = transport;
    if (!anchored.position.isValid()) {
        anchored.position = playbackStartedAt_ != nullptr ? playbackStartedAt_->dateTime() : QDateTime{};
    }
    playbackTransport_.insert(sessionId, anchored);
    playbackClockAnchoredAt_.insert(sessionId, playbackMonotonicClock_.elapsed());
    if (advancing && !anchored.isPaused && !playbackControlsInFlight_.contains(sessionId)) {
        playbackAdvancingSessions_.insert(sessionId);
    } else {
        playbackAdvancingSessions_.remove(sessionId);
    }
}

void MainWindow::freezePlaybackClock(const QUuid &sessionId) {
    if (sessionId.isNull() || !playbackTransport_.contains(sessionId)) return;
    PlaybackTransportInfo transport = playbackTransport_.value(sessionId);
    transport.position = estimatedPlaybackPosition(sessionId);
    playbackTransport_.insert(sessionId, transport);
    playbackClockAnchoredAt_.insert(sessionId, playbackMonotonicClock_.elapsed());
    playbackAdvancingSessions_.remove(sessionId);
}

void MainWindow::handleTilePlaybackState(VideoTileWidget *tile, bool playing) {
    if (tile == nullptr || !playbackTiles_.contains(tile) || tile->sessionId().isNull()) return;
    const QUuid sessionId = tile->sessionId();
    if (!playing) {
        freezePlaybackClock(sessionId);
    } else {
        const PlaybackTransportInfo transport = playbackTransport_.value(sessionId);
        anchorPlaybackClock(sessionId, transport, !transport.isPaused);
    }
    if (tile == selectedPlaybackTile_) {
        playbackCursor_ = estimatedPlaybackPosition(sessionId);
        updatePlaybackTimeline();
    }
    updatePlaybackControlState();
}

void MainWindow::handleTileMediaPosition(VideoTileWidget *tile, double seconds) {
    if (tile == nullptr || !playbackTiles_.contains(tile) || tile->sessionId().isNull() ||
        !std::isfinite(seconds) || seconds < 0.0) {
        return;
    }
    const QUuid sessionId = tile->sessionId();
    const bool mediaClockRestarted = playbackMediaLastSeconds_.contains(sessionId) &&
                                     seconds + 0.25 < playbackMediaLastSeconds_.value(sessionId);
    if (!playbackMediaOriginSeconds_.contains(sessionId) || mediaClockRestarted) {
        playbackMediaOriginSeconds_.insert(sessionId, seconds);
        playbackMediaOriginPositions_.insert(sessionId, estimatedPlaybackPosition(sessionId));
    }
    playbackMediaLastSeconds_.insert(sessionId, seconds);
    const QDateTime originPosition = playbackMediaOriginPositions_.value(sessionId);
    if (!originPosition.isValid()) return;
    const double elapsedMediaSeconds = std::max(0.0, seconds - playbackMediaOriginSeconds_.value(sessionId));
    QDateTime mediaPosition = originPosition.addMSecs(static_cast<qint64>(std::llround(elapsedMediaSeconds * 1000.0)));
    if (playbackStartedAt_ != nullptr && mediaPosition < playbackStartedAt_->dateTime()) mediaPosition = playbackStartedAt_->dateTime();
    if (playbackEndedAt_ != nullptr && mediaPosition > playbackEndedAt_->dateTime()) mediaPosition = playbackEndedAt_->dateTime();

    PlaybackTransportInfo transport = playbackTransport_.value(sessionId);
    transport.position = mediaPosition;
    playbackTransport_.insert(sessionId, transport);
    playbackClockAnchoredAt_.insert(sessionId, playbackMonotonicClock_.elapsed());
    if (tile->isPlaying() && !transport.isPaused && !playbackControlsInFlight_.contains(sessionId)) {
        playbackAdvancingSessions_.insert(sessionId);
    } else {
        playbackAdvancingSessions_.remove(sessionId);
    }
    if (tile == selectedPlaybackTile_) {
        playbackCursor_ = mediaPosition;
        recordingTimeline_->setCursor(playbackCursor_);
    }
}

void MainWindow::finishPlaybackControlBatchIfReady() {
    if (!playbackControlBatch_.has_value() || !playbackControlBatch_->pending.isEmpty()) return;
    const int succeededCount = playbackControlBatch_->succeeded.size();
    const int failedCount = playbackControlBatch_->failed.size();
    const QString actionLabel = playbackActionLabel(playbackControlBatch_->action);
    if (failedCount == 0) {
        playbackStatusLabel_->setText(QStringLiteral("回放%1已在 %2 个窗格完成。").arg(actionLabel).arg(succeededCount));
    } else if (succeededCount == 0) {
        playbackStatusLabel_->setText(QStringLiteral("回放%1失败：%2")
                                          .arg(actionLabel, playbackControlBatch_->failed.constBegin().value()));
    } else {
        playbackStatusLabel_->setText(QStringLiteral("回放%1部分完成：%2 个成功，%3 个失败。失败窗格保持原状态。")
                                          .arg(actionLabel)
                                          .arg(succeededCount)
                                          .arg(failedCount));
    }
    playbackControlBatch_.reset();
    updatePlaybackControlState();
    updatePlaybackTimeline();
}

QWidget *MainWindow::buildToolbar() {
    auto *toolbar = new QFrame;
    toolbar->setObjectName(QStringLiteral("toolbar"));
    auto *layout = new QHBoxLayout(toolbar);
    layout->setContentsMargins(8, 6, 8, 6);
    layout->setSpacing(6);
    auto *title = new QLabel(QStringLiteral("预览控制"));
    title->setObjectName(QStringLiteral("toolbarTitle"));
    layout->addWidget(title);

    auto *saveViewButton = new QToolButton;
    saveViewButton->setDefaultAction(actionRegistry_->action(ViewerActionId::SaveView));
    saveViewButton->setToolTip(QStringLiteral("保存当前分屏和窗格映射为我的视图"));
    saveViewButton->setAccessibleName(QStringLiteral("保存当前视图"));
    saveViewButton->setFixedSize(34, 31);
    layout->addWidget(saveViewButton);

    favoriteButton_ = new QToolButton;
    favoriteButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::ToggleFavorite));
    favoriteButton_->setFixedSize(35, 31);
    favoriteButton_->setToolTip(QStringLiteral("收藏或取消收藏当前摄像头"));
    favoriteButton_->setEnabled(false);
    layout->addWidget(favoriteButton_);

    streamProfileCombo_ = new QComboBox;
    streamProfileCombo_->setObjectName(QStringLiteral("previewStreamProfile"));
    streamProfileCombo_->setAccessibleName(QStringLiteral("预览码流策略"));
    streamProfileCombo_->setToolTip(QStringLiteral("自动模式在九分屏及以上使用子码流"));
    streamProfileCombo_->addItem(QStringLiteral("自动码流"), QStringLiteral("auto"));
    streamProfileCombo_->addItem(QStringLiteral("主码流"), QStringLiteral("main"));
    streamProfileCombo_->addItem(QStringLiteral("子码流"), QStringLiteral("sub"));
    streamProfileCombo_->setCurrentIndex(std::max(0, streamProfileCombo_->findData(streamProfileMode_)));
    connect(streamProfileCombo_, &QComboBox::currentIndexChanged, this, [this](int) {
        setStreamProfileMode(streamProfileCombo_->currentData().toString());
    });
    layout->addWidget(streamProfileCombo_);

    auto *separator = new QFrame;
    separator->setFrameShape(QFrame::VLine);
    separator->setObjectName(QStringLiteral("toolbarSeparator"));
    layout->addWidget(separator);

    tourSourceCombo_ = new QComboBox;
    tourSourceCombo_->setObjectName(QStringLiteral("tourSourceCombo"));
    tourSourceCombo_->setAccessibleName(QStringLiteral("轮巡资源范围"));
    tourSourceCombo_->addItem(QStringLiteral("收藏轮巡"), QStringLiteral("favorites"));
    tourSourceCombo_->addItem(QStringLiteral("全部在线"), QStringLiteral("online"));
    tourSourceCombo_->addItem(QStringLiteral("当前区域"), QStringLiteral("region"));
    tourSourceCombo_->setCurrentIndex(std::max(0, tourSourceCombo_->findData(tourSourceMode_)));
    connect(tourSourceCombo_, &QComboBox::currentIndexChanged, this, [this](int) {
        tourSourceMode_ = tourSourceCombo_->currentData().toString();
        if (previewController_ != nullptr && previewController_->tourActive()) {
            tourCursor_ = 0;
            advanceTour();
        }
        savePreferences();
    });
    layout->addWidget(tourSourceCombo_);

    tourPreviousButton_ = new QToolButton;
    tourPreviousButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::TourPrevious));
    tourPreviousButton_->setToolTip(QStringLiteral("轮巡上一页"));
    tourPreviousButton_->setAccessibleName(QStringLiteral("轮巡上一页"));
    tourPreviousButton_->setFixedSize(34, 31);
    tourPreviousButton_->setEnabled(false);
    layout->addWidget(tourPreviousButton_);

    tourIntervalSpin_ = new QSpinBox;
    tourIntervalSpin_->setObjectName(QStringLiteral("tourIntervalSpin"));
    tourIntervalSpin_->setRange(5, 300);
    tourIntervalSpin_->setValue(tourIntervalSeconds_);
    tourIntervalSpin_->setSuffix(QStringLiteral(" 秒"));
    tourIntervalSpin_->setToolTip(QStringLiteral("收藏摄像头的轮巡间隔"));
    tourIntervalSpin_->setFixedWidth(78);
    connect(tourIntervalSpin_, &QSpinBox::valueChanged, this, [this](int seconds) {
        tourIntervalSeconds_ = seconds;
        if (previewController_ != nullptr && previewController_->tourActive() && tourTimer_ != nullptr) {
            tourTimer_->start(seconds * 1000);
        }
        savePreferences();
    });
    layout->addWidget(tourIntervalSpin_);

    tourButton_ = new QToolButton;
    tourButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::ToggleTour));
    tourButton_->setToolTip(QStringLiteral("按收藏摄像头自动轮巡"));
    tourButton_->setToolButtonStyle(Qt::ToolButtonTextBesideIcon);
    layout->addWidget(tourButton_);

    tourNextButton_ = new QToolButton;
    tourNextButton_->setDefaultAction(actionRegistry_->action(ViewerActionId::TourNext));
    tourNextButton_->setToolTip(QStringLiteral("轮巡下一页"));
    tourNextButton_->setAccessibleName(QStringLiteral("轮巡下一页"));
    tourNextButton_->setFixedSize(34, 31);
    tourNextButton_->setEnabled(false);
    layout->addWidget(tourNextButton_);

    previewOverflowButton_ = new QToolButton;
    previewOverflowButton_->setIcon(IconProvider::instance().icon(ViewerIcon::More));
    previewOverflowButton_->setIconSize(QSize(19, 19));
    previewOverflowButton_->setPopupMode(QToolButton::InstantPopup);
    previewOverflowButton_->setToolTip(QStringLiteral("更多预览命令"));
    previewOverflowButton_->setAccessibleName(QStringLiteral("预览工具栏溢出菜单"));
    previewOverflowButton_->setFixedSize(34, 31);
    auto *overflowMenu = new QMenu(previewOverflowButton_);
    overflowMenu->addAction(actionRegistry_->action(ViewerActionId::SaveView));
    overflowMenu->addAction(actionRegistry_->action(ViewerActionId::ToggleFavorite));
    overflowMenu->addSeparator();
    auto *tourSourceMenu = overflowMenu->addMenu(QStringLiteral("轮巡范围"));
    auto *tourSourceGroup = new QActionGroup(tourSourceMenu);
    tourSourceGroup->setExclusive(true);
    const QList<QPair<QString, QString>> tourSources{
        {QStringLiteral("收藏轮巡"), QStringLiteral("favorites")},
        {QStringLiteral("全部在线"), QStringLiteral("online")},
        {QStringLiteral("当前区域"), QStringLiteral("region")},
    };
    for (const auto &[text, value] : tourSources) {
        QAction *action = tourSourceMenu->addAction(text);
        action->setCheckable(true);
        action->setChecked(value == tourSourceMode_);
        action->setData(value);
        tourSourceGroup->addAction(action);
    }
    connect(tourSourceGroup, &QActionGroup::triggered, this, [this](QAction *action) {
        const int index = tourSourceCombo_->findData(action->data());
        if (index >= 0) tourSourceCombo_->setCurrentIndex(index);
    });
    auto *tourIntervalMenu = overflowMenu->addMenu(QStringLiteral("轮巡间隔"));
    for (const int seconds : {5, 15, 30, 60}) {
        QAction *action = tourIntervalMenu->addAction(QStringLiteral("%1 秒").arg(seconds));
        action->setData(seconds);
        connect(action, &QAction::triggered, this, [this, seconds]() {
            tourIntervalSpin_->setValue(seconds);
        });
    }
    overflowMenu->addSeparator();
    overflowMenu->addAction(actionRegistry_->action(ViewerActionId::ToggleTour));
    overflowMenu->addAction(actionRegistry_->action(ViewerActionId::TourPrevious));
    overflowMenu->addAction(actionRegistry_->action(ViewerActionId::TourNext));
    previewOverflowButton_->setMenu(overflowMenu);
    previewOverflowButton_->hide();
    layout->addWidget(previewOverflowButton_);
    previewCompactWidgets_ = {
        saveViewButton,
        tourSourceCombo_,
        tourPreviousButton_,
        tourIntervalSpin_,
        tourNextButton_,
    };

    layout->addStretch();

    ptzPanelButton_ = new QToolButton;
    ptzPanelButton_->setObjectName(QStringLiteral("ptzPanelToggle"));
    ptzPanelButton_->setIcon(IconProvider::instance().icon(ViewerIcon::PanelRight));
    ptzPanelButton_->setText(QStringLiteral("云台"));
    ptzPanelButton_->setCheckable(true);
    ptzPanelButton_->setChecked(ptzPanelVisible_);
    ptzPanelButton_->setToolTip(QStringLiteral("显示或隐藏云台控制面板"));
    connect(ptzPanelButton_, &QToolButton::toggled, this, [this](bool visible) {
        if (!ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("显示云台控制面板"))) {
            if (ptzPanelButton_ != nullptr) {
                const QSignalBlocker blocker(ptzPanelButton_);
                ptzPanelButton_->setChecked(ptzPanelVisible_);
            }
            return;
        }
        ptzPanelVisible_ = visible;
        showDockPanel(DockPanelId::Ptz, visible);
        savePreferences();
    });
    layout->addWidget(ptzPanelButton_);

    previewLayoutButton_ = new QToolButton;
    previewLayoutButton_->setObjectName(QStringLiteral("previewLayoutButton"));
    previewLayoutButton_->setIcon(IconProvider::instance().icon(ViewerIcon::LayoutGrid));
    previewLayoutButton_->setText(QStringLiteral("%1 分屏").arg(layoutCount_));
    previewLayoutButton_->setPopupMode(QToolButton::InstantPopup);
    previewLayoutButton_->setToolTip(QStringLiteral("选择实时预览分屏"));
    previewLayoutButton_->setAccessibleName(QStringLiteral("实时预览分屏"));
    actionRegistry_->bindWidget(ViewerActionId::ChangePreviewLayout, previewLayoutButton_);
    auto *layoutMenu = new QMenu(previewLayoutButton_);
    auto *layoutGroup = new QActionGroup(layoutMenu);
    layoutGroup->setExclusive(true);
    for (const int count : {1, 4, 9, 16, 25, 36, 64}) {
        auto *action = layoutMenu->addAction(QStringLiteral("%1 分屏").arg(count));
        action->setCheckable(true);
        action->setChecked(count == layoutCount_);
        action->setData(count);
        action->setObjectName(QStringLiteral("previewLayout.%1").arg(count));
        action->setToolTip(QStringLiteral("%1 分屏").arg(count));
        layoutGroup->addAction(action);
        previewLayoutActions_.append(action);
    }
    connect(layoutGroup, &QActionGroup::triggered, this, [this](QAction *action) {
        changeLayout(action->data().toInt());
    });
    previewLayoutButton_->setMenu(layoutMenu);
    layout->addWidget(previewLayoutButton_);

    auto *stopAllButton = new QToolButton;
    stopAllButton->setDefaultAction(actionRegistry_->action(ViewerActionId::StopAllPreview));
    stopAllButton->setToolTip(QStringLiteral("停止并清空全部实时预览"));
    stopAllButton->setAccessibleName(QStringLiteral("停止全部实时预览"));
    stopAllButton->setFixedSize(34, 31);
    layout->addWidget(stopAllButton);

    auto *fullScreenButton = new QToolButton;
    fullScreenButton->setDefaultAction(actionRegistry_->action(ViewerActionId::ToggleFullScreen));
    fullScreenButton->setToolTip(QStringLiteral("切换视频画布全屏，按 Esc 或 F11 退出"));
    fullScreenButton->setAccessibleName(QStringLiteral("切换视频画布全屏"));
    fullScreenButton->setFixedSize(34, 31);
    layout->addWidget(fullScreenButton);
    return toolbar;
}

QWidget *MainWindow::buildControlStrip() {
    auto *strip = new QFrame;
    strip->setObjectName(QStringLiteral("controlStrip"));
    auto *layout = new QHBoxLayout(strip);
    layout->setContentsMargins(10, 7, 10, 7);
    statusLabel_ = new QLabel(QStringLiteral("正在加载授权摄像头目录…"));
    statusLabel_->setObjectName(QStringLiteral("mutedLabel"));
    layout->addWidget(statusLabel_, 1);
    return strip;
}

QWidget *MainWindow::buildPtzPanel() {
    ptzPanel_ = new QFrame;
    ptzPanel_->setObjectName(QStringLiteral("ptzPanel"));
    ptzPanel_->setMinimumWidth(205);
    ptzPanel_->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Minimum);
    auto *layout = new QVBoxLayout(ptzPanel_);
    layout->setContentsMargins(12, 10, 12, 10);
    layout->setSpacing(9);
    auto *title = new QLabel(QStringLiteral("云台控制"));
    title->setObjectName(QStringLiteral("panelTitle"));
    layout->addWidget(title);
    ptzStatusLabel_ = new QLabel(QStringLiteral("选择支持云台控制且在线的摄像头"));
    ptzStatusLabel_->setObjectName(QStringLiteral("mutedLabel"));
    ptzStatusLabel_->setWordWrap(true);
    layout->addWidget(ptzStatusLabel_);

    const auto createPulseButton = [this](ViewerIcon icon, const QString &accessibleName, int action) {
        auto *button = new QToolButton;
        button->setObjectName(QStringLiteral("ptzAction.%1").arg(action));
        button->setIcon(IconProvider::instance().icon(icon));
        button->setIconSize(QSize(19, 19));
        button->setEnabled(false);
        button->setFixedSize(46, 40);
        button->setAccessibleName(accessibleName);
        button->setToolTip(QStringLiteral("按住时持续控制云台，松开即停止"));
        connect(button, &QToolButton::pressed, this, [this, action]() { beginPtzPulse(action); });
        connect(button, &QToolButton::released, this, [this, action]() { endPtzPulse(action); });
        ptzMotionButtons_.append(button);
        return button;
    };

    auto *directionGrid = new QGridLayout;
    directionGrid->setContentsMargins(0, 0, 0, 0);
    directionGrid->setHorizontalSpacing(5);
    directionGrid->setVerticalSpacing(5);
    directionGrid->addWidget(createPulseButton(ViewerIcon::MoveUpLeft, QStringLiteral("云台左上"), 4), 0, 0);
    directionGrid->addWidget(createPulseButton(ViewerIcon::ArrowUp, QStringLiteral("云台向上"), 2), 0, 1);
    directionGrid->addWidget(createPulseButton(ViewerIcon::MoveUpRight, QStringLiteral("云台右上"), 5), 0, 2);
    directionGrid->addWidget(createPulseButton(ViewerIcon::ArrowLeft, QStringLiteral("云台向左"), 0), 1, 0);
    auto *stopButton = new QToolButton;
    stopButton->setObjectName(QStringLiteral("ptzAction.stop"));
    stopButton->setIcon(IconProvider::instance().icon(ViewerIcon::Stop));
    stopButton->setIconSize(QSize(18, 18));
    stopButton->setFixedSize(46, 40);
    stopButton->setToolTip(QStringLiteral("立即停止当前云台动作"));
    stopButton->setAccessibleName(QStringLiteral("停止云台动作"));
    stopButton->setEnabled(false);
    connect(stopButton, &QToolButton::clicked, this, &MainWindow::stopActivePtzPulse);
    ptzStopButton_ = stopButton;
    directionGrid->addWidget(stopButton, 1, 1);
    directionGrid->addWidget(createPulseButton(ViewerIcon::ArrowRight, QStringLiteral("云台向右"), 1), 1, 2);
    directionGrid->addWidget(createPulseButton(ViewerIcon::MoveDownLeft, QStringLiteral("云台左下"), 6), 2, 0);
    directionGrid->addWidget(createPulseButton(ViewerIcon::ArrowDown, QStringLiteral("云台向下"), 3), 2, 1);
    directionGrid->addWidget(createPulseButton(ViewerIcon::MoveDownRight, QStringLiteral("云台右下"), 7), 2, 2);
    layout->addLayout(directionGrid);

    const auto addPairControl = [this, layout, &createPulseButton](const QString &label, int decreaseAction, int increaseAction) {
        auto *row = new QHBoxLayout;
        row->setContentsMargins(0, 0, 0, 0);
        auto *name = new QLabel(label);
        name->setObjectName(QStringLiteral("mutedLabel"));
        row->addWidget(name, 1);
        auto *decreaseButton = createPulseButton(ViewerIcon::Minus, QStringLiteral("%1减小").arg(label), decreaseAction);
        auto *increaseButton = createPulseButton(ViewerIcon::Plus, QStringLiteral("%1增大").arg(label), increaseAction);
        decreaseButton->setFixedSize(42, 31);
        increaseButton->setFixedSize(42, 31);
        row->addWidget(decreaseButton);
        row->addWidget(increaseButton);
        layout->addLayout(row);
    };
    addPairControl(QStringLiteral("变倍"), 9, 8);
    addPairControl(QStringLiteral("焦距"), 10, 11);
    addPairControl(QStringLiteral("光圈"), 13, 12);

    auto *speedLabel = new QLabel(QStringLiteral("速度  4 / 7"));
    speedLabel->setObjectName(QStringLiteral("mutedLabel"));
    ptzSpeedSlider_ = new QSlider(Qt::Horizontal);
    ptzSpeedSlider_->setObjectName(QStringLiteral("ptzSpeedSlider"));
    ptzSpeedSlider_->setRange(1, 7);
    ptzSpeedSlider_->setValue(4);
    ptzSpeedSlider_->setTickPosition(QSlider::TicksBelow);
    ptzSpeedSlider_->setTickInterval(1);
    ptzSpeedSlider_->setAccessibleName(QStringLiteral("云台移动速度"));
    connect(ptzSpeedSlider_, &QSlider::valueChanged, this, [speedLabel](int speed) {
        speedLabel->setText(QStringLiteral("速度  %1 / 7").arg(speed));
    });
    layout->addWidget(speedLabel);
    layout->addWidget(ptzSpeedSlider_);
    layout->addStretch();
    ptzPanel_->setMinimumHeight(ptzPanel_->sizeHint().height());

    auto *scrollArea = new QScrollArea;
    scrollArea->setObjectName(QStringLiteral("ptzScrollArea"));
    scrollArea->setWidgetResizable(true);
    scrollArea->setFrameShape(QFrame::NoFrame);
    scrollArea->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea->setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    scrollArea->setMinimumSize(220, 260);
    scrollArea->setWidget(ptzPanel_);
    return scrollArea;
}

void MainWindow::populateCatalog(const QList<RegionInfo> &regions, const QList<CameraInfo> &cameras) {
    regions_ = regions;
    cameras_ = cameras;
    syncAssignedCameraStatuses();
    QSet<QUuid> visibleCameraIds;
    for (const auto &camera : cameras_) {
        visibleCameraIds.insert(camera.id);
    }
    const auto previousFavorites = favoriteCameraIds_;
    favoriteCameraIds_.intersect(visibleCameraIds);
    if (favoriteCameraIds_ != previousFavorites) {
        saveFavorites();
    }
    rebuildCatalog();
    updateFavoriteState();
    updateCatalogSummary();
    if (previewController_ != nullptr) {
        previewController_->setTourCandidateCount(camerasForTour().size());
    }
    if (titleBar_ != nullptr) {
        titleBar_->setConnectionState(ViewerConnectionState::Connected, QStringLiteral("中心已连接"));
    }
    if (previewController_ != nullptr && previewController_->tourActive() && camerasForTour().isEmpty()) {
        stopTour();
        statusLabel_->setText(QStringLiteral("轮巡已停止：收藏摄像头已不在当前授权范围。"));
        return;
    }
    statusLabel_->setText(QStringLiteral("已加载授权摄像头：%1，当前 %2 分屏。")
                              .arg(cameraStatusSummaryText(ViewerLogic::summarizeCameraConnectivity(cameras_)))
                              .arg(layoutCount_));
    updatePtzState();
}

void MainWindow::applyCameraStatuses(const QList<CameraStatusInfo> &statuses) {
    if (!ViewerLogic::mergeCameraStatuses(cameras_, statuses)) {
        return;
    }
    syncAssignedCameraStatuses();
    refreshCatalogStatusPresentation();
    updateCatalogSummary();
    if (previewController_ != nullptr) {
        previewController_->setTourCandidateCount(camerasForTour().size());
    }
    updatePtzState();
}

void MainWindow::rebuildCatalog() {
    catalogTree_->clear();
    if (resourceTabs_ != nullptr) {
        resourceTabs_->setTabText(0, QStringLiteral("监控点 %1").arg(cameras_.size()));
        resourceTabs_->setTabText(1, QStringLiteral("收藏 %1").arg(favoriteCameraIds_.size()));
        resourceTabs_->setTabText(2, QStringLiteral("我的视图 %1").arg(savedViews_.size()));
    }

    const auto addCameraItem = [this](QTreeWidgetItem *parent, const CameraInfo &camera) {
        auto *item = new QTreeWidgetItem(QStringList{QStringLiteral("%1  [%2]").arg(camera.alias, camera.code)});
        item->setData(0, CatalogRoles::ResourceId, camera.id.toString(QUuid::WithoutBraces));
        item->setData(0, CatalogRoles::ResourceKind, QStringLiteral("camera"));
        item->setIcon(0, statusIcon(camera.connectivity));
        item->setToolTip(0, cameraToolTip(camera));
        if (parent != nullptr) parent->addChild(item);
        else catalogTree_->addTopLevelItem(item);
        return item;
    };

    if (catalogMode_ == 2) {
        for (int index = 0; index < savedViews_.size(); ++index) {
            const SavedView &view = savedViews_.at(index);
            auto *viewItem = new QTreeWidgetItem(QStringList{QStringLiteral("%1  ·  %2 分屏").arg(view.name).arg(view.layout)});
            viewItem->setData(0, CatalogRoles::ResourceKind, QStringLiteral("view"));
            viewItem->setData(0, CatalogRoles::ViewIndex, index);
            viewItem->setIcon(0, IconProvider::instance().icon(ViewerIcon::LayoutGrid, QSize(16, 16)));
            for (const SavedViewSlot &slot : view.assignments) {
                const auto camera = std::find_if(cameras_.cbegin(), cameras_.cend(), [&slot](const CameraInfo &item) {
                    return item.id == slot.cameraId;
                });
                auto *slotItem = new QTreeWidgetItem(QStringList{
                    camera == cameras_.cend()
                        ? QStringLiteral("窗格 %1：设备已不可见").arg(slot.index + 1)
                        : QStringLiteral("窗格 %1：%2").arg(slot.index + 1).arg(camera->alias)});
                slotItem->setData(0, CatalogRoles::ResourceKind, QStringLiteral("viewSlot"));
                slotItem->setData(0, CatalogRoles::ResourceId, slot.cameraId.toString(QUuid::WithoutBraces));
                if (camera != cameras_.cend()) slotItem->setIcon(0, statusIcon(camera->connectivity));
                viewItem->addChild(slotItem);
            }
            catalogTree_->addTopLevelItem(viewItem);
        }
        updateCatalogSummary();
        return;
    }

    if (catalogMode_ == 1) {
        for (const CameraInfo &camera : cameras_) {
            if (favoriteCameraIds_.contains(camera.id)) addCameraItem(nullptr, camera);
        }
        updateCatalogSummary();
        return;
    }

    QHash<QUuid, QTreeWidgetItem *> regionItems;
    for (const auto &region : regions_) {
        auto *item = new QTreeWidgetItem(QStringList{region.name});
        item->setData(0, CatalogRoles::ResourceId, region.id.toString(QUuid::WithoutBraces));
        item->setData(0, CatalogRoles::ResourceKind, QStringLiteral("region"));
        item->setIcon(0, IconProvider::instance().icon(ViewerIcon::Folder, QSize(16, 16)));
        regionItems.insert(region.id, item);
    }
    for (const auto &region : regions_) {
        auto *item = regionItems.value(region.id);
        if (!region.parentId.isNull() && regionItems.contains(region.parentId)) {
            regionItems.value(region.parentId)->addChild(item);
        } else {
            catalogTree_->addTopLevelItem(item);
        }
    }
    for (const CameraInfo &camera : cameras_) {
        if (regionItems.contains(camera.regionId)) {
            addCameraItem(regionItems.value(camera.regionId), camera);
        } else {
            addCameraItem(nullptr, camera);
        }
    }
    for (int index = 0; index < catalogTree_->topLevelItemCount(); ++index) {
        catalogTree_->topLevelItem(index)->setExpanded(true);
    }
    updateCatalogSummary();
}

void MainWindow::assignCameraFromTree(QTreeWidgetItem *item, int) {
    if (item == nullptr) return;
    const QString kind = item->data(0, CatalogRoles::ResourceKind).toString();
    if (kind == QStringLiteral("view")) {
        applySavedView(item->data(0, CatalogRoles::ViewIndex).toInt());
        return;
    }
    const QList<QUuid> cameraIds = cameraIdsForItem(item);
    if (cameraIds.isEmpty()) {
        item->setExpanded(!item->isExpanded());
        return;
    }
    VideoTileWidget *target = activeWorkspace_ == 1 ? selectedPlaybackTile_ : selectedTile_;
    assignCameraIds(target, cameraIds, kind != QStringLiteral("camera"));
}

QList<QUuid> MainWindow::cameraIdsForItem(QTreeWidgetItem *item) const {
    QList<QUuid> result;
    if (item == nullptr) return result;
    const QString kind = item->data(0, CatalogRoles::ResourceKind).toString();
    if (kind == QStringLiteral("camera") || kind == QStringLiteral("viewSlot")) {
        const QUuid cameraId(item->data(0, CatalogRoles::ResourceId).toString());
        if (!cameraId.isNull()) result.append(cameraId);
        return result;
    }
    for (int index = 0; index < item->childCount(); ++index) {
        for (const QUuid &cameraId : cameraIdsForItem(item->child(index))) {
            if (!result.contains(cameraId)) result.append(cameraId);
        }
    }
    return result;
}

void MainWindow::assignCameraIds(VideoTileWidget *startTile, const QList<QUuid> &cameraIds, bool adaptLayout) {
    const WorkspaceMode mode = activeWorkspace_ == 1 ? WorkspaceMode::Playback : WorkspaceMode::Preview;
    if (!ensureWorkspaceInteraction(mode, QStringLiteral("分配摄像头"))) {
        return;
    }
    QList<CameraInfo> selectedCameras;
    QSet<QUuid> selectedCameraIds;
    for (const QUuid &cameraId : cameraIds) {
        if (selectedCameraIds.contains(cameraId)) continue;
        const auto iterator = std::find_if(cameras_.cbegin(), cameras_.cend(), [cameraId](const CameraInfo &camera) {
            return camera.id == cameraId;
        });
        if (iterator == cameras_.cend()) continue;
        if (activeWorkspace_ == 0 && !iterator->canLiveView) continue;
        if (activeWorkspace_ == 1 && !iterator->canPlayback) continue;
        selectedCameraIds.insert(cameraId);
        selectedCameras.append(*iterator);
    }
    if (selectedCameras.isEmpty()) {
        const QString message = activeWorkspace_ == 1
            ? QStringLiteral("所选资源没有录像回放权限，请联系管理员。")
            : QStringLiteral("所选资源没有实时预览权限，请联系管理员。");
        if (activeWorkspace_ == 1) playbackStatusLabel_->setText(message);
        else statusLabel_->setText(message);
        return;
    }

    stopTour();
    if (adaptLayout) {
        const int targetLayout = ViewerLogic::bestLayoutForCameraCount(selectedCameras.size(), activeWorkspace_ == 1);
        suppressLayoutSessionRestore_ = true;
        if (activeWorkspace_ == 1) changePlaybackLayout(targetLayout);
        else changeLayout(targetLayout);
    }

    const int visibleCount = activeWorkspace_ == 1 ? playbackLayoutCount_ : layoutCount_;
    const auto &workspaceTiles = activeWorkspace_ == 1 ? playbackTiles_ : tiles_;
    const int startIndex = startTile == nullptr ? 0 : std::clamp(startTile->index(), 0, visibleCount - 1);
    QSet<QUuid> alreadyAssignedIds;
    QSet<int> reservedIndices;
    for (int index = 0; index < workspaceTiles.size(); ++index) {
        VideoTileWidget *existing = workspaceTiles.at(index);
        if (!existing->camera().has_value() || !selectedCameraIds.contains(existing->camera()->id)) continue;
        if (index >= visibleCount) {
            existing->clearTile();
            continue;
        }
        alreadyAssignedIds.insert(existing->camera()->id);
        reservedIndices.insert(index);
    }

    if (selectedCameras.size() == 1 && alreadyAssignedIds.contains(selectedCameras.first().id)) {
        const CameraInfo &camera = selectedCameras.first();
        for (int index = 0; index < visibleCount; ++index) {
            VideoTileWidget *existing = workspaceTiles.at(index);
            if (existing->camera().has_value() && existing->camera()->id == camera.id) {
                if (activeWorkspace_ == 1) selectPlaybackTile(existing);
                else selectTile(existing);
                if (existing->sessionId().isNull() && existing->requestId().isNull()) restartTile(existing);
                return;
            }
        }
    }

    QList<CameraInfo> pendingCameras;
    for (const CameraInfo &camera : selectedCameras) {
        if (!alreadyAssignedIds.contains(camera.id)) pendingCameras.append(camera);
    }
    if (pendingCameras.isEmpty()) {
        for (const int index : reservedIndices) {
            VideoTileWidget *existing = workspaceTiles.at(index);
            if (existing->sessionId().isNull() && existing->requestId().isNull()) restartTile(existing);
        }
        const QString message = QStringLiteral("所选 %1 路摄像头已在当前%2中打开。")
                                    .arg(alreadyAssignedIds.size())
                                    .arg(activeWorkspace_ == 1 ? QStringLiteral("回放分屏") : QStringLiteral("预览分屏"));
        if (activeWorkspace_ == 1) playbackStatusLabel_->setText(message);
        else statusLabel_->setText(message);
        return;
    }

    QList<int> targetIndices;
    for (const int index : ViewerLogic::orderedWindowIndices(startIndex, visibleCount)) {
        if (!reservedIndices.contains(index)) targetIndices.append(index);
    }
    const int assignmentCount = std::min(pendingCameras.size(), targetIndices.size());

    for (int index = 0; index < assignmentCount; ++index) {
        VideoTileWidget *target = activeWorkspace_ == 1
            ? playbackTiles_.at(targetIndices.at(index))
            : tiles_.at(targetIndices.at(index));
        assignSingleCamera(target, pendingCameras.at(index));
    }
    for (int index = 0; index < visibleCount; ++index) {
        VideoTileWidget *tile = workspaceTiles.at(index);
        if (tile->camera().has_value() && tile->sessionId().isNull() && tile->requestId().isNull()) restartTile(tile);
    }
    const int skippedCount = cameraIds.size() - alreadyAssignedIds.size() - assignmentCount;
    const QString message = QStringLiteral("已保留 %1 路已打开摄像头，并将 %2 路分配到当前%3。%4")
                                .arg(alreadyAssignedIds.size())
                                .arg(assignmentCount)
                                .arg(activeWorkspace_ == 1 ? QStringLiteral("回放分屏") : QStringLiteral("预览分屏"))
                                .arg(skippedCount > 0 ? QStringLiteral("另有 %1 路因权限或分屏容量限制未分配。").arg(skippedCount) : QString{});
    if (activeWorkspace_ == 1) playbackStatusLabel_->setText(message);
    else statusLabel_->setText(message);
}

void MainWindow::assignSingleCamera(VideoTileWidget *tile, const CameraInfo &camera) {
    if (tile == nullptr) return;
    const WorkspaceMode mode = activeWorkspace_ == 1 ? WorkspaceMode::Playback : WorkspaceMode::Preview;
    if (!ensureWorkspaceInteraction(mode, QStringLiteral("分配摄像头"))) {
        return;
    }
    if (activeWorkspace_ == 1) {
        if (!camera.canPlayback) {
            playbackStatusLabel_->setText(QStringLiteral("当前账号没有 %1 的录像回放权限。").arg(camera.alias));
            return;
        }
        requestPlayback(tile, camera, playbackStartedAt_->dateTime(), playbackEndedAt_->dateTime());
    } else {
        if (!camera.canLiveView) {
            statusLabel_->setText(QStringLiteral("当前账号没有 %1 的实时预览权限。").arg(camera.alias));
            return;
        }
        requestCamera(tile, camera);
    }
}

void MainWindow::handleCatalogContextMenu(const QPoint &position) {
    QTreeWidgetItem *item = catalogTree_->itemAt(position);
    if (item == nullptr) return;
    const QString kind = item->data(0, CatalogRoles::ResourceKind).toString();
    QMenu menu(this);
    if (kind == QStringLiteral("view")) {
        auto *applyAction = menu.addAction(QStringLiteral("应用此视图"));
        auto *deleteAction = menu.addAction(QStringLiteral("删除此视图"));
        const QAction *selected = menu.exec(catalogTree_->viewport()->mapToGlobal(position));
        const int viewIndex = item->data(0, CatalogRoles::ViewIndex).toInt();
        if (selected == applyAction) applySavedView(viewIndex);
        else if (selected == deleteAction) deleteSavedView(viewIndex);
        return;
    }
    const QList<QUuid> cameraIds = cameraIdsForItem(item);
    if (cameraIds.isEmpty()) return;
    auto *openAction = menu.addAction(cameraIds.size() > 1
                                          ? QStringLiteral("打开此区域全部监控点")
                                          : QStringLiteral("在当前窗格打开"));
    QAction *favoriteAction = nullptr;
    if (kind == QStringLiteral("camera")) {
        const QUuid cameraId(item->data(0, CatalogRoles::ResourceId).toString());
        favoriteAction = menu.addAction(favoriteCameraIds_.contains(cameraId)
                                            ? QStringLiteral("取消收藏")
                                            : QStringLiteral("加入收藏"));
    }
    const QAction *selected = menu.exec(catalogTree_->viewport()->mapToGlobal(position));
    if (selected == openAction) {
        assignCameraIds(activeWorkspace_ == 1 ? selectedPlaybackTile_ : selectedTile_, cameraIds, cameraIds.size() > 1);
    } else if (favoriteAction != nullptr && selected == favoriteAction) {
        if (!ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("收藏摄像头"))) {
            return;
        }
        const QUuid cameraId(item->data(0, CatalogRoles::ResourceId).toString());
        if (favoriteCameraIds_.contains(cameraId)) favoriteCameraIds_.remove(cameraId);
        else favoriteCameraIds_.insert(cameraId);
        saveFavorites();
        rebuildCatalog();
        updateFavoriteState();
    }
}

void MainWindow::updateCatalogSummary() {
    if (catalogSummaryLabel_ == nullptr) return;
    if (catalogMode_ == 1) {
        catalogSummaryLabel_->setText(QStringLiteral("收藏：%1")
                                          .arg(cameraStatusSummaryText(ViewerLogic::summarizeCameraConnectivity(favoriteCameras()))));
    } else if (catalogMode_ == 2) {
        catalogSummaryLabel_->setText(QStringLiteral("已保存 %1 个本地视图").arg(savedViews_.size()));
    } else {
        catalogSummaryLabel_->setText(QStringLiteral("%1 · 可拖放到窗格")
                                          .arg(cameraStatusSummaryText(ViewerLogic::summarizeCameraConnectivity(cameras_))));
    }
}

void MainWindow::syncAssignedCameraStatuses() {
    QHash<QUuid, int> connectivityById;
    for (const CameraInfo &camera : cameras_) connectivityById.insert(camera.id, camera.connectivity);
    const auto syncTiles = [&connectivityById](const QList<VideoTileWidget *> &tiles) {
        for (VideoTileWidget *tile : tiles) {
            const auto camera = tile->camera();
            if (camera.has_value() && connectivityById.contains(camera->id)) {
                tile->updateCameraConnectivity(connectivityById.value(camera->id));
            }
        }
    };
    syncTiles(tiles_);
    syncTiles(playbackTiles_);
}

void MainWindow::refreshCatalogStatusPresentation() {
    QHash<QUuid, CameraInfo> camerasById;
    for (const CameraInfo &camera : cameras_) camerasById.insert(camera.id, camera);
    QTreeWidgetItemIterator iterator(catalogTree_);
    while (*iterator != nullptr) {
        QTreeWidgetItem *item = *iterator;
        const QString kind = item->data(0, CatalogRoles::ResourceKind).toString();
        if (kind == QStringLiteral("camera") || kind == QStringLiteral("viewSlot")) {
            const QUuid cameraId(item->data(0, CatalogRoles::ResourceId).toString());
            const auto camera = camerasById.constFind(cameraId);
            if (camera != camerasById.cend()) {
                item->setIcon(0, statusIcon(camera->connectivity));
                if (kind == QStringLiteral("camera")) item->setToolTip(0, cameraToolTip(camera.value()));
            }
        }
        ++iterator;
    }
}

void MainWindow::changeLayout(int count) {
    if (!PreviewController::isSupportedLayout(count)) return;
    const bool needsInitialGrid = videoGrid_ == nullptr || videoGrid_->count() == 0;
    if (!needsInitialGrid &&
        !ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("切换实时预览分屏"))) {
        return;
    }
    if (count == layoutCount_ && !needsInitialGrid) {
        syncLayoutMenuSelection(previewLayoutButton_, count);
        return;
    }
    if (previewController_ != nullptr && !needsInitialGrid && !previewController_->setLayoutCount(count)) {
        syncLayoutMenuSelection(previewLayoutButton_, layoutCount_);
        return;
    }
    const bool restoreSessions = !suppressLayoutSessionRestore_;
    suppressLayoutSessionRestore_ = false;
    restoreTileLayout(false);
    const int previousCount = layoutCount_;
    const bool profileChanged = streamProfileMode_ == QStringLiteral("auto") && (layoutCount_ >= 9) != (count >= 9);
    while (auto *item = videoGrid_->takeAt(0)) {
        delete item;
    }
    layoutCount_ = count;
    const int dimension = PreviewController::gridDimension(count);
    for (int index = 0; index < tiles_.size(); ++index) {
        auto *tile = tiles_.at(index);
        if (index < count) {
            videoGrid_->addWidget(tile, index / dimension, index % dimension);
            tile->setCompact(count >= 25);
            tile->show();
        } else {
            tile->suspendSession();
            tile->hide();
        }
    }
    if (previewController_ != nullptr && previewController_->tourActive()) {
        advanceTour();
    } else if (restoreSessions && activeWorkspace_ == 0) {
        for (int index = 0; index < count; ++index) {
            const bool newlyVisible = index >= previousCount;
            if ((newlyVisible || profileChanged) && tiles_.at(index)->camera().has_value() &&
                tiles_.at(index)->camera()->canLiveView) {
                const auto camera = tiles_.at(index)->camera();
                requestCamera(tiles_.at(index), *camera);
            }
        }
    }
    if (selectedTile_ == nullptr || !selectedTile_->isVisible()) selectTile(tiles_.first());
    if (previewLayoutButton_ != nullptr) previewLayoutButton_->setText(QStringLiteral("%1 分屏").arg(count));
    syncLayoutMenuSelection(previewLayoutButton_, count);
    const QString profile = ViewerLogic::previewProfileForMode(streamProfileMode_, count) == QStringLiteral("main")
        ? QStringLiteral("主码流")
        : QStringLiteral("子码流");
    statusLabel_->setText(QStringLiteral("当前 %1 分屏，默认使用%2；隐藏窗格映射已保留。").arg(count).arg(profile));
    savePreferences();
}

void MainWindow::filterCatalog(const QString &query) {
    const QString normalized = query.trimmed().toLower();
    for (int index = 0; index < catalogTree_->topLevelItemCount(); ++index) {
        filterItem(catalogTree_->topLevelItem(index), normalized);
    }
}

bool MainWindow::filterItem(QTreeWidgetItem *item, const QString &query) {
    bool childVisible = false;
    for (int index = 0; index < item->childCount(); ++index) {
        childVisible = filterItem(item->child(index), query) || childVisible;
    }
    const bool selfVisible = query.isEmpty() || item->text(0).toLower().contains(query);
    const bool visible = selfVisible || childVisible;
    item->setHidden(!visible);
    if (childVisible && !query.isEmpty()) item->setExpanded(true);
    return visible;
}

void MainWindow::loadPreferences() {
    QSettings settings;
    const QString prefix = accountSettingsPrefix(apiClient_->username());
    const int savedLayout = settings.value(prefix + QStringLiteral("previewLayout"), 4).toInt();
    layoutCount_ = QList<int>{1, 4, 9, 16, 25, 36, 64}.contains(savedLayout) ? savedLayout : 4;
    const int savedPlaybackLayout = settings.value(prefix + QStringLiteral("playbackLayout"), 1).toInt();
    playbackLayoutCount_ = QList<int>{1, 4}.contains(savedPlaybackLayout) ? savedPlaybackLayout : 1;
    tourIntervalSeconds_ = std::clamp(settings.value(prefix + QStringLiteral("tourInterval"), 15).toInt(), 5, 300);
    streamProfileMode_ = ViewerLogic::normalizedStreamMode(settings.value(prefix + QStringLiteral("streamMode"), QStringLiteral("auto")).toString());
    const QString tourSource = settings.value(prefix + QStringLiteral("tourSource"), QStringLiteral("favorites")).toString();
    tourSourceMode_ = QList<QString>{QStringLiteral("favorites"), QStringLiteral("online"), QStringLiteral("region")}.contains(tourSource)
        ? tourSource
        : QStringLiteral("favorites");
    ptzPanelVisible_ = settings.value(prefix + QStringLiteral("ptzPanelVisible"), true).toBool();
    if (playbackController_ != nullptr) {
        playbackController_->setSyncEnabled(settings.value(prefix + QStringLiteral("playbackSync"), true).toBool());
    }
    catalogMode_ = std::clamp(settings.value(prefix + QStringLiteral("catalogMode"), 0).toInt(), 0, 2);
}

void MainWindow::savePreferences() const {
    QSettings settings;
    const QString prefix = accountSettingsPrefix(apiClient_->username());
    settings.setValue(prefix + QStringLiteral("previewLayout"), layoutCount_);
    settings.setValue(prefix + QStringLiteral("playbackLayout"), playbackLayoutCount_);
    settings.setValue(prefix + QStringLiteral("tourInterval"), tourIntervalSeconds_);
    settings.setValue(prefix + QStringLiteral("streamMode"), streamProfileMode_);
    settings.setValue(prefix + QStringLiteral("tourSource"), tourSourceMode_);
    settings.setValue(prefix + QStringLiteral("ptzPanelVisible"), ptzPanelVisible_);
    settings.setValue(
        prefix + QStringLiteral("playbackSync"),
        playbackController_ == nullptr || playbackController_->syncEnabled());
    settings.setValue(prefix + QStringLiteral("catalogMode"), catalogMode_);
    if (uiSettings_ != nullptr && !isCanvasPresentationBusy()) {
        uiSettings_->setWindowGeometry(saveGeometry());
        uiSettings_->setDockLocked(isDockLayoutLocked());
        if (dockLayoutController_ != nullptr) {
            uiSettings_->setDockState(
                WorkspaceMode::Preview,
                dockLayoutController_->stateFor(WorkspaceMode::Preview));
            uiSettings_->setDockState(
                WorkspaceMode::Playback,
                dockLayoutController_->stateFor(WorkspaceMode::Playback));
        }
    }
}

void MainWindow::restoreWindowGeometry() {
    const QByteArray geometry = uiSettings_ != nullptr ? uiSettings_->windowGeometry() : QByteArray{};
    if (geometry.isEmpty() || !restoreGeometry(geometry)) {
        resize(1500, 920);
    }
    ensureWindowOnAvailableScreen();
}

void MainWindow::ensureWindowOnAvailableScreen() {
    const QRect windowFrame = isMaximized() ? normalGeometry() : frameGeometry();
    for (const QScreen *screen : QGuiApplication::screens()) {
        const QRect visiblePart = windowFrame.intersected(screen->availableGeometry());
        if (visiblePart.width() >= 96 && visiblePart.height() >= 64) {
            return;
        }
    }

    QScreen *screen = QGuiApplication::primaryScreen();
    if (screen == nullptr) {
        return;
    }
    showNormal();
    const QRect available = screen->availableGeometry();
    const QSize targetSize(
        std::min(width(), available.width()),
        std::min(height(), available.height()));
    resize(targetSize.expandedTo(minimumSize()));
    move(available.center() - rect().center());
}

void MainWindow::loadSavedViews() {
    QSettings settings;
    const QString key = accountSettingsPrefix(apiClient_->username()) + QStringLiteral("savedViews");
    const QJsonArray values = QJsonDocument::fromJson(settings.value(key).toByteArray()).array();
    savedViews_.clear();
    for (const QJsonValue &value : values) {
        const QJsonObject object = value.toObject();
        SavedView view;
        view.name = object.value(QStringLiteral("name")).toString().trimmed().left(32);
        view.layout = object.value(QStringLiteral("layout")).toInt(4);
        view.streamMode = ViewerLogic::normalizedStreamMode(object.value(QStringLiteral("streamMode")).toString());
        if (view.name.isEmpty() || !QList<int>{1, 4, 9, 16, 25, 36, 64}.contains(view.layout)) continue;
        for (const QJsonValue &slotValue : object.value(QStringLiteral("slots")).toArray()) {
            const QJsonObject slotObject = slotValue.toObject();
            const int slotIndex = slotObject.value(QStringLiteral("index")).toInt(-1);
            const QUuid cameraId(slotObject.value(QStringLiteral("cameraId")).toString());
            const QString profile = slotObject.value(QStringLiteral("profile")).toString();
            if (slotIndex >= 0 && slotIndex < view.layout && !cameraId.isNull()) {
                view.assignments.append({slotIndex, cameraId, profile == QStringLiteral("sub") ? QStringLiteral("sub") : QStringLiteral("main")});
            }
        }
        savedViews_.append(view);
        if (savedViews_.size() >= 20) break;
    }
}

void MainWindow::saveSavedViews() const {
    QJsonArray values;
    for (const SavedView &view : savedViews_) {
        QJsonArray slotValues;
        for (const SavedViewSlot &slot : view.assignments) {
            slotValues.append(QJsonObject{
                {QStringLiteral("index"), slot.index},
                {QStringLiteral("cameraId"), slot.cameraId.toString(QUuid::WithoutBraces)},
                {QStringLiteral("profile"), slot.profile}});
        }
        values.append(QJsonObject{
            {QStringLiteral("name"), view.name},
            {QStringLiteral("layout"), view.layout},
            {QStringLiteral("streamMode"), view.streamMode},
            {QStringLiteral("slots"), slotValues}});
    }
    QSettings settings;
    settings.setValue(
        accountSettingsPrefix(apiClient_->username()) + QStringLiteral("savedViews"),
        QJsonDocument(values).toJson(QJsonDocument::Compact));
}

void MainWindow::saveCurrentView() {
    SavedView view;
    view.layout = layoutCount_;
    view.streamMode = streamProfileMode_;
    for (int index = 0; index < layoutCount_; ++index) {
        VideoTileWidget *tile = tiles_.at(index);
        if (tile->camera().has_value()) view.assignments.append({index, tile->camera()->id, tile->profile()});
    }
    if (view.assignments.isEmpty()) {
        statusLabel_->setText(QStringLiteral("当前预览没有可保存的摄像头窗格。"));
        return;
    }
    const QString defaultName = QStringLiteral("视图 %1").arg(savedViews_.size() + 1);
    const std::optional<QString> requestedName = promptText(
        this,
        QStringLiteral("保存我的视图"),
        QStringLiteral("视图名称："),
        defaultName,
        32);
    if (!requestedName.has_value() || requestedName->isEmpty()) return;
    view.name = *requestedName;
    const auto existing = std::find_if(savedViews_.begin(), savedViews_.end(), [&view](const SavedView &item) {
        return item.name.compare(view.name, Qt::CaseInsensitive) == 0;
    });
    if (existing != savedViews_.end()) {
        if (AppDialog::question(
                this,
                QStringLiteral("覆盖我的视图"),
                QStringLiteral("已存在同名视图，是否覆盖？")) != QDialogButtonBox::Yes) {
            return;
        }
        *existing = view;
    } else {
        if (savedViews_.size() >= 20) {
            statusLabel_->setText(QStringLiteral("最多保存 20 个本地视图，请先删除不再使用的视图。"));
            return;
        }
        savedViews_.append(view);
    }
    saveSavedViews();
    catalogMode_ = 2;
    if (resourceTabs_ != nullptr) resourceTabs_->setCurrentIndex(2);
    rebuildCatalog();
    statusLabel_->setText(QStringLiteral("已保存视图“%1”。").arg(view.name));
}

void MainWindow::applySavedView(int viewIndex) {
    if (viewIndex < 0 || viewIndex >= savedViews_.size()) return;
    const WorkspaceMode sourceMode = activeWorkspace_ == 1 ? WorkspaceMode::Playback : WorkspaceMode::Preview;
    if (!ensureWorkspaceInteraction(sourceMode, QStringLiteral("应用我的视图"))) {
        return;
    }
    if (activeWorkspace_ != 0) {
        pendingSavedViewIndex_ = viewIndex;
        switchWorkspace(0);
        if (!workspaceTransition_.has_value()) {
            pendingSavedViewIndex_.reset();
        }
        return;
    }
    const SavedView view = savedViews_.at(viewIndex);
    stopAllPreview(true);
    setStreamProfileMode(view.streamMode);
    changeLayout(view.layout);
    int restoredCount = 0;
    for (const SavedViewSlot &slot : view.assignments) {
        const auto camera = std::find_if(cameras_.cbegin(), cameras_.cend(), [&slot](const CameraInfo &item) {
            return item.id == slot.cameraId;
        });
        if (camera == cameras_.cend() || !camera->canLiveView || slot.index < 0 || slot.index >= layoutCount_) continue;
        requestCamera(tiles_.at(slot.index), *camera, slot.profile);
        ++restoredCount;
    }
    statusLabel_->setText(QStringLiteral("已应用视图“%1”，恢复 %2 路摄像头。").arg(view.name).arg(restoredCount));
}

void MainWindow::deleteSavedView(int viewIndex) {
    if (viewIndex < 0 || viewIndex >= savedViews_.size()) return;
    const WorkspaceMode sourceMode = activeWorkspace_ == 1 ? WorkspaceMode::Playback : WorkspaceMode::Preview;
    if (!ensureWorkspaceInteraction(sourceMode, QStringLiteral("删除我的视图"))) {
        return;
    }
    const QString name = savedViews_.at(viewIndex).name;
    if (AppDialog::question(
            this,
            QStringLiteral("删除我的视图"),
            QStringLiteral("确定删除视图“%1”吗？").arg(name)) != QDialogButtonBox::Yes) {
        return;
    }
    savedViews_.removeAt(viewIndex);
    saveSavedViews();
    rebuildCatalog();
    statusLabel_->setText(QStringLiteral("已删除视图“%1”。").arg(name));
}

void MainWindow::loadFavorites() {
    QSettings settings;
    const auto storedIds = settings.value(favoriteSettingsKey(apiClient_->username())).toStringList();
    for (const auto &value : storedIds) {
        const QUuid cameraId(value);
        if (!cameraId.isNull()) {
            favoriteCameraIds_.insert(cameraId);
        }
    }
}

void MainWindow::saveFavorites() const {
    QSettings settings;
    QStringList storedIds;
    for (const auto &cameraId : favoriteCameraIds_) {
        storedIds.append(cameraId.toString(QUuid::WithoutBraces));
    }
    storedIds.sort();
    settings.setValue(favoriteSettingsKey(apiClient_->username()), storedIds);
}

QList<CameraInfo> MainWindow::favoriteCameras() const {
    QList<CameraInfo> result;
    for (const auto &camera : cameras_) {
        if (favoriteCameraIds_.contains(camera.id)) {
            result.append(camera);
        }
    }
    return result;
}

QList<CameraInfo> MainWindow::camerasForTour() const {
    QList<CameraInfo> result;
    for (const CameraInfo &camera : cameras_) {
        if (!camera.canLiveView || camera.connectivity != 1) continue;
        if (tourSourceMode_ == QStringLiteral("favorites") && !favoriteCameraIds_.contains(camera.id)) continue;
        if (tourSourceMode_ == QStringLiteral("region") &&
            (currentRegionId_.isNull() || !isRegionWithin(camera.regionId, currentRegionId_))) continue;
        result.append(camera);
    }
    return result;
}

bool MainWindow::isRegionWithin(const QUuid &candidateRegionId, const QUuid &ancestorRegionId) const {
    if (candidateRegionId.isNull() || ancestorRegionId.isNull()) return false;
    QUuid current = candidateRegionId;
    QSet<QUuid> visited;
    while (!current.isNull() && !visited.contains(current)) {
        if (current == ancestorRegionId) return true;
        visited.insert(current);
        const auto iterator = std::find_if(regions_.cbegin(), regions_.cend(), [current](const RegionInfo &region) {
            return region.id == current;
        });
        if (iterator == regions_.cend()) break;
        current = iterator->parentId;
    }
    return false;
}

void MainWindow::updateFavoriteState() {
    const bool hasCamera = activeWorkspace_ == 0 && selectedTile_ != nullptr && selectedTile_->camera().has_value();
    const bool isFavorite = hasCamera && favoriteCameraIds_.contains(selectedTile_->camera()->id);
    QAction *favoriteAction = actionRegistry_->action(ViewerActionId::ToggleFavorite);
    if (favoriteAction == nullptr) {
        return;
    }
    favoriteAction->setText(isFavorite ? QStringLiteral("取消收藏当前摄像头") : QStringLiteral("收藏当前摄像头"));
    favoriteAction->setIcon(IconProvider::instance().icon(
        ViewerIcon::Star,
        isFavorite ? QColor(QStringLiteral("#D4B26A")) : QColor(QStringLiteral("#C5CDD0"))));
    refreshControllerActionStates();
}

void MainWindow::toggleSelectedFavorite() {
    if (selectedTile_ == nullptr || !selectedTile_->camera().has_value()) {
        return;
    }
    const CameraInfo camera = *selectedTile_->camera();
    if (favoriteCameraIds_.contains(camera.id)) {
        favoriteCameraIds_.remove(camera.id);
        statusLabel_->setText(QStringLiteral("已取消收藏 %1。").arg(camera.alias));
    } else {
        favoriteCameraIds_.insert(camera.id);
        statusLabel_->setText(QStringLiteral("已收藏 %1。").arg(camera.alias));
    }
    saveFavorites();
    rebuildCatalog();
    updateFavoriteState();
    updateCatalogSummary();
    if (previewController_ != nullptr && previewController_->tourActive() && camerasForTour().isEmpty()) {
        stopTour();
    }
}

void MainWindow::toggleTour(bool enabled) {
    if (!enabled) {
        stopTour();
        return;
    }
    const QList<CameraInfo> candidates = camerasForTour();
    if (previewController_ != nullptr) previewController_->setTourCandidateCount(candidates.size());
    if (candidates.isEmpty()) {
        statusLabel_->setText(tourSourceMode_ == QStringLiteral("region") && currentRegionId_.isNull()
                                  ? QStringLiteral("请先在设备树中选择一个区域，再启动当前区域轮巡。")
                                  : QStringLiteral("当前轮巡范围内没有在线摄像头。"));
        const QSignalBlocker blocker(actionRegistry_->action(ViewerActionId::ToggleTour));
        actionRegistry_->setChecked(ViewerActionId::ToggleTour, false);
        return;
    }
    if (previewController_ != nullptr) previewController_->setTourActive(true);
    tourCursor_ = 0;
    advanceTour();
    tourTimer_->start(tourIntervalSpin_->value() * 1000);
}

void MainWindow::stopTour() {
    const bool active = previewController_ != nullptr && previewController_->tourActive();
    if (!active && (tourButton_ == nullptr || !tourButton_->isChecked())) {
        return;
    }
    if (previewController_ != nullptr) previewController_->setTourActive(false);
    tourCursor_ = 0;
    if (tourTimer_ != nullptr) {
        tourTimer_->stop();
    }
    refreshControllerActionStates();
}

bool MainWindow::prepareForSessionEnd() {
    if (sessionEnding_) {
        return false;
    }
    if (isCanvasPresentationBusy()) {
        forceExitCanvasFullScreenForShutdown();
    }
    sessionEnding_ = true;
    workspaceTransition_.reset();
    pendingInstantPlayback_.reset();
    pendingSavedViewIndex_.reset();
    setWorkspaceInteractionEnabled(false);
    savePreferences();
    stopTour();
    stopActivePtzPulse();
    for (auto *tile : tiles_) {
        tile->releaseForShutdown();
    }
    for (auto *tile : playbackTiles_) {
        tile->releaseForShutdown();
    }
    if (playbackCursorTimer_ != nullptr) {
        playbackCursorTimer_->stop();
    }
    if (cameraStatusRefreshTimer_ != nullptr) {
        cameraStatusRefreshTimer_->stop();
    }
    return true;
}

void MainWindow::handleForcedPasswordChangeRequired() {
    if (sessionEnding_ || forcedPasswordDialogOpen_) {
        return;
    }

    forcedPasswordDialogOpen_ = true;
    ChangePasswordDialog dialog(apiClient_, this, true);
    const int result = dialog.exec();
    forcedPasswordDialogOpen_ = false;
    if (!prepareForSessionEnd()) {
        return;
    }

    if (result == QDialog::Accepted) {
        AppDialog::success(
            this,
            QStringLiteral("密码修改成功"),
            QStringLiteral("请使用新密码重新登录。"));
        hide();
        emit reauthenticationRequested();
        return;
    }

    hide();
    emit logoutRequested();
}

void MainWindow::openChangePasswordDialog() {
    if (sessionEnding_) {
        return;
    }

    ChangePasswordDialog dialog(apiClient_, this);
    if (dialog.exec() != QDialog::Accepted || !prepareForSessionEnd()) {
        return;
    }

    AppDialog::success(
        this,
        QStringLiteral("密码修改成功"),
        QStringLiteral("密码已修改，请使用新密码重新登录。"));
    hide();
    emit reauthenticationRequested();
}

void MainWindow::requestLogout() {
    if (sessionEnding_) {
        return;
    }

    if (AppDialog::question(
            this,
            QStringLiteral("退出登录"),
            QStringLiteral("确定要退出当前账号吗？")) != QDialogButtonBox::Yes ||
        !prepareForSessionEnd()) {
        return;
    }

    hide();
    emit logoutRequested();
}

void MainWindow::advanceTour() {
    if (previewController_ == nullptr || !previewController_->tourActive()) {
        return;
    }
    const auto cameras = camerasForTour();
    if (cameras.isEmpty()) {
        stopTour();
        return;
    }
    const int visibleCount = std::min(layoutCount_, static_cast<int>(tiles_.size()));
    if (tourCursor_ >= cameras.size()) tourCursor_ = 0;
    int assignedCount = 0;
    for (int index = 0; index < visibleCount; ++index) {
        const int cameraIndex = tourCursor_ + index;
        if (cameraIndex < cameras.size()) {
            requestCamera(tiles_.at(index), cameras.at(cameraIndex));
            ++assignedCount;
        } else {
            tiles_.at(index)->clearTile();
        }
    }
    tourCursor_ = (tourCursor_ + visibleCount) % cameras.size();
    statusLabel_->setText(QStringLiteral("轮巡已切换 %1 路在线摄像头，停留 %2 秒。").arg(assignedCount).arg(tourIntervalSpin_->value()));
}

void MainWindow::selectTile(VideoTileWidget *tile) {
    if (tile == nullptr) return;
    if (tile != selectedTile_) {
        stopActivePtzPulse();
    }
    if (selectedTile_ != nullptr) selectedTile_->setSelected(false);
    selectedTile_ = tile;
    selectedTile_->setSelected(true);
    if (previewController_ != nullptr) previewController_->selectTile(tile->index());
    updatePtzState();
}

void MainWindow::toggleTileMaximized(VideoTileWidget *tile) {
    if (tile == nullptr || tile->isEmpty()) return;
    const bool playback = playbackTiles_.contains(tile);
    if (!ensureWorkspaceInteraction(
            playback ? WorkspaceMode::Playback : WorkspaceMode::Preview,
            QStringLiteral("切换单窗放大"))) {
        return;
    }
    if ((playback && maximizedPlaybackTile_ == tile) || (!playback && maximizedPreviewTile_ == tile)) {
        restoreTileLayout(playback);
        return;
    }
    restoreTileLayout(playback);
    QGridLayout *grid = playback ? playbackGrid_ : videoGrid_;
    const QList<VideoTileWidget *> &workspaceTiles = playback ? playbackTiles_ : tiles_;
    const int visibleCount = playback ? playbackLayoutCount_ : layoutCount_;
    while (auto *item = grid->takeAt(0)) delete item;
    for (int index = 0; index < visibleCount; ++index) workspaceTiles.at(index)->hide();
    grid->addWidget(tile, 0, 0);
    tile->show();
    tile->setCompact(false);
    if (playback) {
        maximizedPlaybackTile_ = tile;
        selectPlaybackTile(tile);
        playbackStatusLabel_->setText(QStringLiteral("已放大回放窗格 %1，双击或按 Esc 恢复分屏。").arg(tile->index() + 1));
    } else {
        maximizedPreviewTile_ = tile;
        selectTile(tile);
        statusLabel_->setText(QStringLiteral("已放大预览窗格 %1，双击或按 Esc 恢复分屏。").arg(tile->index() + 1));
    }
}

void MainWindow::restoreTileLayout(bool playback) {
    VideoTileWidget *&maximizedTile = playback ? maximizedPlaybackTile_ : maximizedPreviewTile_;
    if (maximizedTile == nullptr) return;
    QGridLayout *grid = playback ? playbackGrid_ : videoGrid_;
    const QList<VideoTileWidget *> &workspaceTiles = playback ? playbackTiles_ : tiles_;
    const int count = playback ? playbackLayoutCount_ : layoutCount_;
    const int dimension = playback
        ? (count == 1 ? 1 : 2)
        : (count == 1 ? 1 : count == 4 ? 2 : count == 9 ? 3 : count == 16 ? 4 : count == 25 ? 5 : count == 36 ? 6 : 8);
    while (auto *item = grid->takeAt(0)) delete item;
    for (int index = 0; index < workspaceTiles.size(); ++index) {
        VideoTileWidget *tile = workspaceTiles.at(index);
        if (index < count) {
            grid->addWidget(tile, index / dimension, index % dimension);
            tile->setCompact(!playback && count >= 25);
            tile->show();
        } else {
            tile->hide();
        }
    }
    maximizedTile = nullptr;
}

void MainWindow::stopAllPreview(bool clearAssignments) {
    restoreTileLayout(false);
    stopTour();
    stopActivePtzPulse();
    for (VideoTileWidget *tile : tiles_) {
        if (clearAssignments) tile->clearTile();
        else tile->suspendSession();
    }
    if (clearAssignments && previewController_ != nullptr) previewController_->clearAssignments();
    syncPreviewSessionState();
    selectTile(tiles_.first());
    statusLabel_->setText(clearAssignments
                              ? QStringLiteral("已停止并清空全部实时预览。")
                              : QStringLiteral("已暂停全部实时预览，会保留窗格映射。"));
}

void MainWindow::stopAllPlayback(bool clearAssignments) {
    restoreTileLayout(true);
    for (VideoTileWidget *tile : playbackTiles_) {
        if (tile->camera().has_value()) apiClient_->cancelRecordingSearch(tile->camera()->id);
        if (clearAssignments) tile->clearTile();
        else tile->suspendSession();
    }
    playbackTransport_.clear();
    playbackControlsInFlight_.clear();
    playbackAdvancingSessions_.clear();
    playbackClockAnchoredAt_.clear();
    playbackMediaOriginSeconds_.clear();
    playbackMediaLastSeconds_.clear();
    playbackMediaOriginPositions_.clear();
    playbackControlBatch_.reset();
    if (clearAssignments && playbackController_ != nullptr) playbackController_->clearAllTiles();
    if (clearAssignments) {
        playbackSegments_.clear();
        playbackSearchStates_.clear();
        playbackSearchRequests_.clear();
    }
    if (playbackCursorTimer_ != nullptr) playbackCursorTimer_->stop();
    selectPlaybackTile(playbackTiles_.first());
    updatePlaybackTimeline();
    updatePlaybackCalendarMarks();
    playbackStatusLabel_->setText(clearAssignments
                                      ? QStringLiteral("已停止并清空全部录像回放。")
                                      : QStringLiteral("已暂停全部录像回放，会保留窗格映射。"));
}

void MainWindow::restartTile(VideoTileWidget *tile) {
    if (tile == nullptr || !tile->camera().has_value()) return;
    const WorkspaceMode mode = playbackTiles_.contains(tile) ? WorkspaceMode::Playback : WorkspaceMode::Preview;
    if (!ensureWorkspaceInteraction(mode, QStringLiteral("重新连接"))) {
        return;
    }
    if (playbackTiles_.contains(tile)) {
        requestPlayback(tile, *tile->camera(), playbackStartedAt_->dateTime(), playbackEndedAt_->dateTime());
    } else {
        requestCamera(tile, *tile->camera(), tile->profile());
    }
}

void MainWindow::openInstantPlayback(VideoTileWidget *tile, int seconds) {
    if (tile == nullptr || !tile->camera().has_value() || seconds < 1) return;
    if (!ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("即时回放"))) {
        return;
    }
    const CameraInfo camera = *tile->camera();
    if (!camera.canPlayback) {
        statusLabel_->setText(QStringLiteral("当前账号没有 %1 的录像回放权限。").arg(camera.alias));
        return;
    }
    const QDateTime endedAt = QDateTime::currentDateTime();
    playbackStartedAt_->setDateTime(endedAt.addSecs(-seconds));
    playbackEndedAt_->setDateTime(endedAt);
    VideoTileWidget *target = nullptr;
    for (int index = 0; index < playbackTiles_.size(); ++index) {
        VideoTileWidget *candidate = playbackTiles_.at(index);
        if (candidate->camera().has_value() && candidate->camera()->id == camera.id) {
            if (index < playbackLayoutCount_ && target == nullptr) target = candidate;
            else candidate->clearTile();
        }
    }
    if (target == nullptr) target = selectedPlaybackTile_ != nullptr ? selectedPlaybackTile_ : playbackTiles_.first();
    if (activeWorkspace_ != 1) {
        playbackTileToSkipOnNextRestore_ = target;
        pendingInstantPlayback_ = PendingInstantPlayback{
            target,
            camera,
            playbackStartedAt_->dateTime(),
            playbackEndedAt_->dateTime()};
        switchWorkspace(1);
        if (!workspaceTransition_.has_value()) {
            pendingInstantPlayback_.reset();
        }
        return;
    }
    selectPlaybackTile(target);
    requestPlayback(target, camera, playbackStartedAt_->dateTime(), playbackEndedAt_->dateTime());
    playbackStatusLabel_->setText(QStringLiteral("已打开 %1 的即时回放。").arg(camera.alias));
}

void MainWindow::toggleCanvasFullScreen() {
    if (canvasPresentationState_ == CanvasPresentationState::Entering ||
        canvasPresentationState_ == CanvasPresentationState::Active) {
        exitCanvasFullScreen();
        return;
    }
    if (canvasPresentationState_ == CanvasPresentationState::Exiting) {
        return;
    }

    const WorkspaceMode mode = activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback;
    if (!ensureWorkspaceInteraction(mode, QStringLiteral("切换视频画布全屏"))) {
        return;
    }
    enterCanvasFullScreen();
}

void MainWindow::enterCanvasFullScreen() {
    if (canvasPresentationState_ != CanvasPresentationState::Idle ||
        workspaceLayout_ == nullptr || dockLayoutController_ == nullptr) {
        return;
    }

    // 保持视频网格及其 QOpenGLWidget 的父子关系不变，避免 libmpv 渲染上下文重建。
    canvasPresentationSnapshot_.wasMaximized = isMaximized();
    canvasPresentationSnapshot_.titleBarVisible = titleBar_ != nullptr && titleBar_->isVisible();
    canvasPresentationSnapshot_.statusBarVisible = statusBar() != nullptr && statusBar()->isVisible();
    canvasPresentationSnapshot_.previewToolbarVisible = previewToolbar_ != nullptr && previewToolbar_->isVisible();
    canvasPresentationSnapshot_.previewControlStripVisible = previewControlStrip_ != nullptr && previewControlStrip_->isVisible();
    canvasPresentationSnapshot_.playbackToolbarVisible = playbackToolbar_ != nullptr && playbackToolbar_->isVisible();
    canvasPresentationSnapshot_.workspaceMargins = workspaceLayout_->contentsMargins();
    canvasPresentationSnapshot_.workspaceSpacing = workspaceLayout_->spacing();
    canvasPresentationShellApplied_ = false;
    canvasPresentationState_ = CanvasPresentationState::Entering;
    CrashReporter::updateContext(QStringLiteral("进入全屏"), false);

    // 进入原生全屏前先冻结业务入口，但不移动或重建任何播放器窗口。
    setWorkspaceInteractionEnabled(false);
    if (canvasPresentationTimer_ != nullptr) {
        canvasPresentationTimer_->start();
    }
    showFullScreen();
    if (isFullScreen()) {
        QTimer::singleShot(0, this, &MainWindow::finishCanvasFullScreenEntry);
    }
    refreshControllerActionStates();
}

void MainWindow::exitCanvasFullScreen() {
    if (canvasPresentationState_ == CanvasPresentationState::Idle ||
        canvasPresentationState_ == CanvasPresentationState::Exiting) {
        return;
    }

    canvasPresentationState_ = CanvasPresentationState::Exiting;
    if (canvasPresentationTimer_ != nullptr) {
        canvasPresentationTimer_->start();
    }
    if (isFullScreen()) {
        if (canvasPresentationSnapshot_.wasMaximized) {
            showMaximized();
        } else {
            showNormal();
        }
    } else {
        QTimer::singleShot(0, this, [this]() {
            finishCanvasFullScreenExit();
        });
    }
    refreshControllerActionStates();
}

void MainWindow::finishCanvasFullScreenEntry() {
    if (canvasPresentationState_ != CanvasPresentationState::Entering || !isFullScreen()) {
        return;
    }

    applyCanvasPresentationShell();
    canvasPresentationState_ = CanvasPresentationState::Active;
    CrashReporter::updateContext(QStringLiteral("视频画布全屏"), true);
    if (canvasPresentationTimer_ != nullptr) {
        canvasPresentationTimer_->stop();
    }
    refreshControllerActionStates();
}

void MainWindow::finishCanvasFullScreenExit(bool forced) {
    if (canvasPresentationState_ != CanvasPresentationState::Exiting ||
        (!forced && isFullScreen())) {
        return;
    }

    if (canvasPresentationTimer_ != nullptr) {
        canvasPresentationTimer_->stop();
    }
    restoreCanvasPresentationShell();
    canvasPresentationState_ = CanvasPresentationState::Idle;
    canvasPresentationSnapshot_ = CanvasPresentationSnapshot{};
    CrashReporter::updateContext(QStringLiteral("普通工作台"), false);

    const bool hasPendingWorkspaceSwitch = pendingCanvasWorkspaceIndex_.has_value();
    if (!sessionEnding_) {
        setWorkspaceInteractionEnabled(!workspaceTransition_.has_value() && !hasPendingWorkspaceSwitch);
    }
    refreshControllerActionStates();
    resumePendingWorkspaceSwitch();
}

void MainWindow::applyCanvasPresentationShell() {
    if (canvasPresentationShellApplied_) {
        return;
    }
    if (dockLayoutController_ != nullptr) {
        dockLayoutController_->setCanvasOnly(true);
    }
    if (titleBar_ != nullptr) {
        titleBar_->hide();
    }
    if (statusBar() != nullptr) {
        statusBar()->hide();
    }
    if (previewToolbar_ != nullptr) {
        previewToolbar_->hide();
    }
    if (previewControlStrip_ != nullptr) {
        previewControlStrip_->hide();
    }
    if (playbackToolbar_ != nullptr) {
        playbackToolbar_->hide();
    }
    if (workspaceLayout_ != nullptr) {
        workspaceLayout_->setContentsMargins(0, 0, 0, 0);
        workspaceLayout_->setSpacing(0);
    }
    canvasPresentationShellApplied_ = true;
}

void MainWindow::restoreCanvasPresentationShell() {
    if (dockLayoutController_ != nullptr) {
        dockLayoutController_->setCanvasOnly(false);
    }
    if (workspaceLayout_ != nullptr) {
        workspaceLayout_->setContentsMargins(canvasPresentationSnapshot_.workspaceMargins);
        workspaceLayout_->setSpacing(canvasPresentationSnapshot_.workspaceSpacing);
    }
    if (titleBar_ != nullptr) {
        titleBar_->setVisible(canvasPresentationSnapshot_.titleBarVisible);
    }
    if (statusBar() != nullptr) {
        statusBar()->setVisible(canvasPresentationSnapshot_.statusBarVisible);
    }
    if (previewToolbar_ != nullptr) {
        previewToolbar_->setVisible(canvasPresentationSnapshot_.previewToolbarVisible);
    }
    if (previewControlStrip_ != nullptr) {
        previewControlStrip_->setVisible(canvasPresentationSnapshot_.previewControlStripVisible);
    }
    if (playbackToolbar_ != nullptr) {
        playbackToolbar_->setVisible(canvasPresentationSnapshot_.playbackToolbarVisible);
    }
    canvasPresentationShellApplied_ = false;
}

void MainWindow::handleCanvasPresentationTimeout() {
    if (canvasPresentationState_ == CanvasPresentationState::Entering) {
        if (isFullScreen()) {
            finishCanvasFullScreenEntry();
            return;
        }
        canvasPresentationState_ = CanvasPresentationState::Exiting;
        finishCanvasFullScreenExit(true);
        showOperationFeedback(QStringLiteral("无法进入视频画布全屏，已恢复工作台界面。"),
                              activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback);
        return;
    }
    if (canvasPresentationState_ == CanvasPresentationState::Exiting) {
        if (isFullScreen()) {
            showNormal();
        }
        QTimer::singleShot(120, this, [this]() {
            finishCanvasFullScreenExit(true);
        });
    }
}

void MainWindow::forceExitCanvasFullScreenForShutdown() {
    if (canvasPresentationState_ == CanvasPresentationState::Idle) {
        return;
    }
    if (canvasPresentationTimer_ != nullptr) {
        canvasPresentationTimer_->stop();
    }
    pendingCanvasWorkspaceIndex_.reset();
    if (isFullScreen()) {
        showNormal();
    }
    restoreCanvasPresentationShell();
    canvasPresentationState_ = CanvasPresentationState::Idle;
    canvasPresentationSnapshot_ = CanvasPresentationSnapshot{};
    CrashReporter::updateContext(QStringLiteral("正在关闭"), false);
}

void MainWindow::resumePendingWorkspaceSwitch() {
    if (sessionEnding_ || !pendingCanvasWorkspaceIndex_.has_value()) {
        return;
    }
    const int targetIndex = *pendingCanvasWorkspaceIndex_;
    pendingCanvasWorkspaceIndex_.reset();
    QTimer::singleShot(0, this, [this, targetIndex]() {
        if (!sessionEnding_) {
            switchWorkspace(targetIndex);
        }
    });
}

bool MainWindow::isCanvasPresentationBusy() const {
    return canvasPresentationState_ != CanvasPresentationState::Idle;
}

void MainWindow::setStreamProfileMode(const QString &mode) {
    const QString normalized = ViewerLogic::normalizedStreamMode(mode);
    if (streamProfileMode_ == normalized) return;
    if (!ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("切换码流策略"))) {
        if (streamProfileCombo_ != nullptr) {
            const QSignalBlocker blocker(streamProfileCombo_);
            streamProfileCombo_->setCurrentIndex(std::max(0, streamProfileCombo_->findData(streamProfileMode_)));
        }
        return;
    }
    streamProfileMode_ = normalized;
    if (previewController_ != nullptr) previewController_->setStreamMode(normalized);
    if (streamProfileCombo_ != nullptr && streamProfileCombo_->currentData().toString() != normalized) {
        const QSignalBlocker blocker(streamProfileCombo_);
        streamProfileCombo_->setCurrentIndex(std::max(0, streamProfileCombo_->findData(normalized)));
    }
    stopTour();
    if (activeWorkspace_ == 0) {
        for (int index = 0; index < layoutCount_; ++index) {
            VideoTileWidget *tile = tiles_.at(index);
            if (tile->camera().has_value() && tile->camera()->canLiveView) requestCamera(tile, *tile->camera());
        }
    }
    statusLabel_->setText(QStringLiteral("预览码流策略已切换为%1。")
                              .arg(normalized == QStringLiteral("main")
                                       ? QStringLiteral("主码流")
                                       : normalized == QStringLiteral("sub") ? QStringLiteral("子码流") : QStringLiteral("自动")));
    savePreferences();
}

void MainWindow::requestCamera(VideoTileWidget *tile, const CameraInfo &camera, const QString &profileOverride) {
    if (tile == nullptr || !camera.canLiveView) {
        if (statusLabel_ != nullptr) {
            statusLabel_->setText(QStringLiteral("当前账号没有 %1 的实时预览权限。").arg(camera.alias));
        }
        return;
    }
    selectTile(tile);
    const QString profile = profileOverride == QStringLiteral("main") || profileOverride == QStringLiteral("sub")
        ? profileOverride
        : ViewerLogic::previewProfileForMode(streamProfileMode_, layoutCount_);
    tile->assignCamera(camera, profile);
    if (previewController_ != nullptr) previewController_->setTileAssigned(tile->index(), true);
    updatePtzState();
    refreshControllerActionStates();
    const QUuid requestId = QUuid::createUuid();
    tile->markSessionRequested(requestId);
    syncPreviewSessionState();
    apiClient_->createLiveSession(camera.id, profile, tile->index(), requestId);
    statusLabel_->setText(QStringLiteral("正在为 %1 申请授权播放会话…").arg(camera.alias));
}

void MainWindow::requestPlayback(
    VideoTileWidget *tile,
    const CameraInfo &camera,
    const QDateTime &startedAt,
    const QDateTime &endedAt) {
    QString rangeError;
    if (tile == nullptr || !camera.canPlayback ||
        !PlaybackController::validateTimeRange(startedAt, endedAt, &rangeError)) {
        if (playbackStatusLabel_ != nullptr) {
            playbackStatusLabel_->setText(!camera.canPlayback
                                              ? QStringLiteral("当前账号没有 %1 的录像回放权限。").arg(camera.alias)
                                              : rangeError);
        }
        return;
    }
    const auto previousCamera = tile->camera();
    if (!tile->sessionId().isNull()) {
        freezePlaybackClock(tile->sessionId());
        playbackTransport_.remove(tile->sessionId());
        playbackControlsInFlight_.remove(tile->sessionId());
        playbackAdvancingSessions_.remove(tile->sessionId());
        playbackClockAnchoredAt_.remove(tile->sessionId());
        playbackMediaOriginSeconds_.remove(tile->sessionId());
        playbackMediaLastSeconds_.remove(tile->sessionId());
        playbackMediaOriginPositions_.remove(tile->sessionId());
    }
    if (previousCamera.has_value() && previousCamera->id != camera.id) {
        apiClient_->cancelRecordingSearch(previousCamera->id);
        playbackSegments_.remove(previousCamera->id);
        playbackSearchStates_.remove(previousCamera->id);
        playbackSearchRequests_.remove(previousCamera->id);
    }
    playbackSegments_.remove(camera.id);
    selectPlaybackTile(tile);
    tile->assignCamera(camera, QStringLiteral("playback"));
    playbackCursor_ = startedAt;
    updatePlaybackTimeline();
    const QUuid requestId = QUuid::createUuid();
    tile->markSessionRequested(requestId);
    apiClient_->createPlaybackSession(camera.id, startedAt, endedAt, tile->index(), requestId);
    if (playbackStatusLabel_ != nullptr) {
        playbackStatusLabel_->setText(QStringLiteral("正在启动 %1 的回放中继…").arg(camera.alias));
    }
    const QUuid searchId = QUuid::createUuid();
    playbackSearchRequests_.insert(camera.id, searchId);
    playbackSearchStates_.insert(camera.id, QStringLiteral("检索中"));
    apiClient_->searchRecordings(camera.id, startedAt, endedAt, searchId);
    updatePlaybackSearchSummary();
    updatePlaybackControlState();
}

void MainWindow::beginPtzPulse(int action) {
    if (ensureWorkspaceInteraction(WorkspaceMode::Preview, QStringLiteral("云台控制")) &&
        ptzController_ != nullptr) {
        ptzController_->beginPulse(action, ptzSpeedSlider_ != nullptr ? ptzSpeedSlider_->value() : 4);
    }
}

void MainWindow::endPtzPulse(int action) {
    if (ptzController_ != nullptr) {
        ptzController_->endPulse(action);
    }
}

void MainWindow::stopActivePtzPulse() {
    if (ptzController_ != nullptr && ptzController_->pulseState().active) {
        ptzController_->stopPulse();
    }
    updatePtzControlState();
}

void MainWindow::syncPreviewSessionState() {
    if (previewController_ == nullptr) {
        return;
    }
    int activeCount = 0;
    for (const VideoTileWidget *tile : std::as_const(tiles_)) {
        if (tile != nullptr && (!tile->requestId().isNull() || !tile->sessionId().isNull())) {
            ++activeCount;
        }
    }
    previewController_->setActiveSessionCount(activeCount);
}

void MainWindow::handleSessionCreated(const QUuid &requestId, const StreamSessionInfo &session) {
    if (auto *tile = findTileByRequest(requestId)) {
        if (!tile->startSession(session)) {
            if (playbackTiles_.contains(tile)) {
                playbackTransport_.remove(session.id);
                playbackStatusLabel_->setText(QStringLiteral("播放器无法启动回放会话。"));
                updatePlaybackControlState();
            } else {
                statusLabel_->setText(QStringLiteral("播放器无法启动实时预览会话。"));
                syncPreviewSessionState();
            }
            return;
        }
        if (playbackTiles_.contains(tile)) {
            playbackMediaOriginSeconds_.remove(session.id);
            playbackMediaLastSeconds_.remove(session.id);
            playbackMediaOriginPositions_.remove(session.id);
            PlaybackTransportInfo transport = session.hasPlaybackTransport
                ? session.playbackTransport
                : PlaybackTransportInfo{};
            if (!transport.position.isValid()) {
                transport.position = playbackStartedAt_ != nullptr ? playbackStartedAt_->dateTime() : QDateTime{};
            }
            anchorPlaybackClock(session.id, transport, false);
            playbackStatusLabel_->setText(QStringLiteral("回放会话已建立，正在接入录像流。"));
            updatePlaybackControlState();
        } else {
            statusLabel_->setText(QStringLiteral("播放会话已建立。"));
            syncPreviewSessionState();
        }
    } else {
        apiClient_->revokeSession(session.id);
    }
}

void MainWindow::handleSessionFailed(const QUuid &requestId, const QString &message) {
    auto *tile = findTileByRequest(requestId);
    if (tile == nullptr) {
        return;
    }
    const bool isPlayback = tile != nullptr && playbackTiles_.contains(tile);
    const QUuid previousSessionId = tile->sessionId();
    if (tile->sessionId().isNull()) {
        tile->failSession(message);
    } else {
        tile->handleReconnectFailure(message);
    }
    if (isPlayback) {
        if (!previousSessionId.isNull() && tile->sessionId().isNull()) {
            playbackTransport_.remove(previousSessionId);
            playbackControlsInFlight_.remove(previousSessionId);
            playbackAdvancingSessions_.remove(previousSessionId);
            playbackClockAnchoredAt_.remove(previousSessionId);
            playbackMediaOriginSeconds_.remove(previousSessionId);
            playbackMediaLastSeconds_.remove(previousSessionId);
            playbackMediaOriginPositions_.remove(previousSessionId);
        }
        playbackStatusLabel_->setText(message);
        updatePlaybackControlState();
    } else {
        statusLabel_->setText(message);
        syncPreviewSessionState();
    }
}

VideoTileWidget *MainWindow::findTileByRequest(const QUuid &requestId) const {
    for (auto *tile : tiles_) if (tile->requestId() == requestId) return tile;
    for (auto *tile : playbackTiles_) if (tile->requestId() == requestId) return tile;
    return nullptr;
}

VideoTileWidget *MainWindow::findTileBySession(const QUuid &sessionId) const {
    for (auto *tile : tiles_) if (tile->sessionId() == sessionId) return tile;
    for (auto *tile : playbackTiles_) if (tile->sessionId() == sessionId) return tile;
    return nullptr;
}

VideoTileWidget *MainWindow::firstAvailableTile() const {
    for (auto *tile : tiles_) if (tile->isVisible() && tile->isEmpty()) return tile;
    return selectedTile_ != nullptr && selectedTile_->isVisible() ? selectedTile_ : nullptr;
}

VideoTileWidget *MainWindow::firstAvailablePlaybackTile() const {
    for (auto *tile : playbackTiles_) if (tile->isVisible() && tile->isEmpty()) return tile;
    return selectedPlaybackTile_ != nullptr && selectedPlaybackTile_->isVisible() ? selectedPlaybackTile_ : nullptr;
}

void MainWindow::updatePtzState() {
    const bool hasCamera = activeWorkspace_ == 0 && selectedTile_ != nullptr && selectedTile_->camera().has_value();
    if (ptzController_ != nullptr) {
        ptzController_->setWorkspaceMode(activeWorkspace_ == 0 ? WorkspaceMode::Preview : WorkspaceMode::Playback);
        if (hasCamera) ptzController_->setSelectedCamera(*selectedTile_->camera());
        else ptzController_->clearSelectedCamera();
    }
    updatePtzControlState();
    updateFavoriteState();
}

void MainWindow::updatePtzControlState() {
    const bool workspaceReady = canInteractWithWorkspace(WorkspaceMode::Preview);
    const bool supported = workspaceReady && ptzController_ != nullptr && ptzController_->available();
    const bool pulseActive = supported && ptzController_->pulseState().active;
    QString reason;
    if (ptzController_ != nullptr) {
        reason = ptzController_->statusText();
    }
    if (!workspaceReady && workspaceTransition_.has_value()) {
        reason = QStringLiteral("正在切换工作区，请稍候。");
    }
    if (reason.isEmpty()) {
        reason = QStringLiteral("选择支持云台控制且在线的摄像头。");
    }
    for (QToolButton *button : std::as_const(ptzMotionButtons_)) {
        if (button == nullptr) {
            continue;
        }
        button->setProperty("cameraSupportsPtz", supported);
        setEnabledWithReason(button, supported, reason);
    }
    if (ptzStopButton_ != nullptr) {
        ptzStopButton_->setProperty("cameraSupportsPtz", supported);
        setEnabledWithReason(
            ptzStopButton_,
            pulseActive,
            supported ? QStringLiteral("当前没有进行中的云台动作。") : reason);
    }
    setEnabledWithReason(ptzSpeedSlider_, supported, reason);
    if (ptzStatusLabel_ != nullptr && ptzController_ != nullptr) {
        ptzStatusLabel_->setText(ptzController_->statusText());
    }
}
