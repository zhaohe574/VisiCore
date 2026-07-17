#include "apiclient.h"
#include "change_password_dialog.h"
#include "login_dialog.h"
#include "main_window.h"
#include "mpv_player_widget.h"
#include "recording_timeline_widget.h"
#include "theme_manager.h"
#include "video_tile_widget.h"
#include "viewer_action_registry.h"
#include "viewer_ui_settings.h"
#include "viewer_ui_types.h"
#include "window_title_bar.h"

#include <QAction>
#include <QApplication>
#include <QCoreApplication>
#include <QContextMenuEvent>
#include <QEnterEvent>
#include <QGuiApplication>
#include <QLabel>
#include <QMenu>
#include <QPushButton>
#include <QSettings>
#include <QSignalSpy>
#include <QTemporaryDir>
#include <QTest>
#include <QTimer>
#include <QToolButton>
#include <QTreeWidget>
#include <QUrl>

#include <algorithm>
#include <memory>

class MainWindowInteractionTests final : public QObject {
    Q_OBJECT

private slots:
    void initTestCase();
    void init();
    void cleanup();
    void allViewerActionsAreRegisteredAndNamed();
    void unavailableActionsExposeReason();
    void workspaceAndLayoutActionsStaySynchronized();
    void repeatedCurrentWorkspaceActionKeepsSelection();
    void workspaceTransitionFreezesInteractiveSurfaces();
    void panelsMenuTogglesDockPanel();
    void previewLayoutMenuRespondsToMouse();
    void fullScreenButtonRespondsToMouse();
    void dockLockStateSurvivesWorkspaceRefresh();
    void cameraAssignmentRefreshesFavoriteAndPtzState();
    void cloudControlUsesChineseUserFacingText();
    void playbackSearchAndTimelineControlsRespondAfterSwitch();
    void videoTileContextMenuOnlyEmitsRequests();
    void canceledPreviewContextMenuDoesNotDispatchNullAction();
    void profileSwitchKeepsExistingPlayerCanvas();
    void videoTileHeaderOnlyAppearsOnPointerHover();
    void authenticationDialogsKeepButtonsResponsive();
    void sixtyFourPreviewLayoutCreatesStableWorkspace();

private:
    [[nodiscard]] ViewerActionRegistry *registry() const;
    [[nodiscard]] QAction *action(ViewerActionId id) const;
    [[nodiscard]] WindowTitleBar *titleBar() const;
    [[nodiscard]] QToolButton *titleBarButton(const QString &accessibleName) const;

    QTemporaryDir settingsDirectory_;
    std::unique_ptr<ApiClient> apiClient_;
    std::unique_ptr<MainWindow> window_;
};

void MainWindowInteractionTests::initTestCase() {
    QVERIFY(settingsDirectory_.isValid());
    ThemeManager::instance().apply(*qApp);
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsDirectory_.path());
    QCoreApplication::setOrganizationName(QStringLiteral("VideoPlatformQtViewerTests"));
    QCoreApplication::setApplicationName(QStringLiteral("MainWindowInteractionTests"));
}

void MainWindowInteractionTests::init() {
    QSettings settings;
    settings.clear();
    settings.sync();
    // 使用未知本地协议，避免窗口构造时的目录刷新触及真实网络。
    apiClient_ = std::make_unique<ApiClient>(QUrl(QStringLiteral("test://viewer/")), true);
    window_ = std::make_unique<MainWindow>(apiClient_.get());
    window_->resize(1440, 900);
    window_->show();
    QTRY_VERIFY(window_->isVisible());
}

void MainWindowInteractionTests::cleanup() {
    if (window_ != nullptr) {
        window_->hide();
        QCoreApplication::processEvents();
        window_.reset();
    }
    apiClient_.reset();
    QSettings settings;
    settings.clear();
    settings.sync();
    QCoreApplication::processEvents();
}

void MainWindowInteractionTests::allViewerActionsAreRegisteredAndNamed() {
    ViewerActionRegistry *actionRegistry = registry();
    QVERIFY(actionRegistry != nullptr);

    for (int value = static_cast<int>(ViewerActionId::WorkspacePreview);
         value <= static_cast<int>(ViewerActionId::ExitApplication);
         ++value) {
        const ViewerActionId id = static_cast<ViewerActionId>(value);
        QAction *viewerAction = actionRegistry->action(id);
        QVERIFY2(viewerAction != nullptr, qPrintable(QStringLiteral("动作 %1 未注册。").arg(value)));
        QCOMPARE(viewerAction->objectName(), QStringLiteral("viewerAction_%1").arg(value));
        QVERIFY2(!viewerAction->text().isEmpty(), qPrintable(QStringLiteral("动作 %1 未提供用户可见名称。").arg(value)));
    }
}

void MainWindowInteractionTests::unavailableActionsExposeReason() {
    ViewerActionRegistry *actionRegistry = registry();
    QVERIFY(actionRegistry != nullptr);
    QAction *favoriteAction = action(ViewerActionId::ToggleFavorite);
    QVERIFY(favoriteAction != nullptr);

    const ViewerActionState state = actionRegistry->state(ViewerActionId::ToggleFavorite);
    QVERIFY(!state.enabled);
    QVERIFY2(!state.unavailableReason.isEmpty(), "未选择已分配摄像头时必须说明收藏操作不可用的原因。");
    QVERIFY(!favoriteAction->isEnabled());
    QVERIFY(favoriteAction->toolTip().contains(state.unavailableReason));
    QVERIFY(favoriteAction->statusTip().contains(state.unavailableReason));
}

void MainWindowInteractionTests::workspaceAndLayoutActionsStaySynchronized() {
    QAction *previewAction = action(ViewerActionId::WorkspacePreview);
    QAction *playbackAction = action(ViewerActionId::WorkspacePlayback);
    QAction *previewLayoutAction = action(ViewerActionId::ChangePreviewLayout);
    QAction *playbackLayoutAction = action(ViewerActionId::ChangePlaybackLayout);
    WindowTitleBar *windowTitleBar = titleBar();
    QVERIFY(previewAction != nullptr);
    QVERIFY(playbackAction != nullptr);
    QVERIFY(previewLayoutAction != nullptr);
    QVERIFY(playbackLayoutAction != nullptr);
    QVERIFY(windowTitleBar != nullptr);

    QCOMPARE(static_cast<int>(windowTitleBar->workspaceMode()), static_cast<int>(WorkspaceMode::Preview));
    QVERIFY(previewAction->isChecked());
    QVERIFY(!playbackAction->isChecked());
    QVERIFY(previewLayoutAction->isEnabled());
    QVERIFY(!playbackLayoutAction->isEnabled());

    playbackAction->trigger();
    QTRY_COMPARE(static_cast<int>(windowTitleBar->workspaceMode()), static_cast<int>(WorkspaceMode::Playback));
    QTRY_VERIFY(playbackAction->isChecked());
    QTRY_VERIFY(!previewAction->isChecked());
    QTRY_VERIFY(!previewLayoutAction->isEnabled());
    QTRY_VERIFY(playbackLayoutAction->isEnabled());

    previewAction->trigger();
    QTRY_COMPARE(static_cast<int>(windowTitleBar->workspaceMode()), static_cast<int>(WorkspaceMode::Preview));
    QTRY_VERIFY(previewAction->isChecked());
    QTRY_VERIFY(!playbackAction->isChecked());
    QTRY_VERIFY(previewLayoutAction->isEnabled());
    QTRY_VERIFY(!playbackLayoutAction->isEnabled());
}

void MainWindowInteractionTests::repeatedCurrentWorkspaceActionKeepsSelection() {
    QAction *previewAction = action(ViewerActionId::WorkspacePreview);
    QAction *playbackAction = action(ViewerActionId::WorkspacePlayback);
    QVERIFY(previewAction != nullptr);
    QVERIFY(playbackAction != nullptr);
    QVERIFY(previewAction->isChecked());

    previewAction->trigger();

    QVERIFY2(previewAction->isChecked(), "重复选择当前工作区后，实时预览勾选状态不得被清除。");
    QVERIFY(!playbackAction->isChecked());
    QCOMPARE(static_cast<int>(titleBar()->workspaceMode()), static_cast<int>(WorkspaceMode::Preview));
}

void MainWindowInteractionTests::workspaceTransitionFreezesInteractiveSurfaces() {
    QAction *playbackAction = action(ViewerActionId::WorkspacePlayback);
    QAction *previewLayoutAction = action(ViewerActionId::ChangePreviewLayout);
    QAction *resourcePanelAction = action(ViewerActionId::ShowResourceCatalog);
    QAction *fullScreenAction = action(ViewerActionId::ToggleFullScreen);
    QVERIFY(playbackAction != nullptr);
    QVERIFY(previewLayoutAction != nullptr);
    QVERIFY(resourcePanelAction != nullptr);
    QVERIFY(fullScreenAction != nullptr);

    const QList<VideoTileWidget *> tiles = window_->findChildren<VideoTileWidget *>();
    QVERIFY(!tiles.isEmpty());
    const QList<QAction *> dockActions = window_->dockPanelActions();
    QVERIFY(!dockActions.isEmpty());

    playbackAction->trigger();

    QVERIFY2(!previewLayoutAction->isEnabled(), "工作区切换期间不得继续切换实时预览分屏。");
    QVERIFY2(!resourcePanelAction->isEnabled(), "工作区切换期间不得继续切换面板。");
    QVERIFY2(!fullScreenAction->isEnabled(), "工作区切换期间不得进入画布全屏，以免污染两套停靠布局。");
    QVERIFY2(!tiles.first()->commandInteractionEnabled(), "工作区切换期间视频窗格右键与拖放操作必须冻结。");
    QVERIFY2(!dockActions.first()->isEnabled(), "工作区切换期间必须冻结 Qt ADS 自带面板操作。");

    QTRY_COMPARE(static_cast<int>(titleBar()->workspaceMode()), static_cast<int>(WorkspaceMode::Playback));
    QTRY_VERIFY(tiles.first()->commandInteractionEnabled());
    QVERIFY(!action(ViewerActionId::ShowPtz)->isEnabled());
    QVERIFY(action(ViewerActionId::ShowPlaybackSearch)->isEnabled());
}

void MainWindowInteractionTests::sixtyFourPreviewLayoutCreatesStableWorkspace() {
    window_.reset();
    QSettings settings;
    const QString prefix = ViewerUiSettings::accountSettingsPrefix(QString{});
    settings.setValue(prefix + QStringLiteral("previewLayout"), 64);
    settings.sync();

    window_ = std::make_unique<MainWindow>(apiClient_.get());
    QVERIFY(window_ != nullptr);

    const QList<VideoTileWidget *> tiles = window_->findChildren<VideoTileWidget *>();
    QCOMPARE(tiles.size(), 68);
    window_->resize(1920, 1080);
    window_->show();
    QTRY_VERIFY(window_->isVisible());
    QTest::qWait(150);
    QVERIFY(window_->isVisible());
    window_->resetDockLayout();
    QTest::qWait(50);
    QVERIFY(window_->isVisible());
}

void MainWindowInteractionTests::panelsMenuTogglesDockPanel() {
    WindowTitleBar *windowTitleBar = titleBar();
    QVERIFY(windowTitleBar != nullptr);
    QMenu *menu = windowTitleBar->panelsMenu();
    QVERIFY(menu != nullptr);
    QToolButton *panelsButton = titleBarButton(QStringLiteral("面板"));
    QVERIFY(panelsButton != nullptr);

    QAction *ptzAction = action(ViewerActionId::ShowPtz);
    QVERIFY(ptzAction != nullptr);
    QVERIFY(ptzAction->isEnabled());
    QVERIFY(ptzAction->isChecked());
    QAction *nativePtzAction = nullptr;
    for (QAction *dockAction : window_->dockPanelActions()) {
        if (dockAction != nullptr && dockAction->data().toInt() == static_cast<int>(DockPanelId::Ptz)) {
            nativePtzAction = dockAction;
            break;
        }
    }
    QVERIFY(nativePtzAction != nullptr);
    QVERIFY(nativePtzAction->isEnabled());
    QVERIFY(nativePtzAction->isChecked());

    QTest::mouseClick(panelsButton, Qt::LeftButton);
    QTRY_VERIFY(menu->isVisible());
    const QRect actionGeometry = menu->actionGeometry(ptzAction);
    QVERIFY(actionGeometry.isValid());
    QTest::mouseClick(menu, Qt::LeftButton, Qt::NoModifier, actionGeometry.center());
    QTRY_VERIFY(!ptzAction->isChecked());
    QTRY_VERIFY(!nativePtzAction->isChecked());
    QTRY_VERIFY(!menu->isVisible());

    QTest::mouseClick(panelsButton, Qt::LeftButton);
    QTRY_VERIFY(menu->isVisible());
    QTest::mouseClick(menu, Qt::LeftButton, Qt::NoModifier, menu->actionGeometry(ptzAction).center());
    QTRY_VERIFY(ptzAction->isChecked());
    QTRY_VERIFY(nativePtzAction->isChecked());
}

void MainWindowInteractionTests::previewLayoutMenuRespondsToMouse() {
    auto *layoutButton = window_->findChild<QToolButton *>(QStringLiteral("previewLayoutButton"));
    QVERIFY(layoutButton != nullptr);
    QMenu *menu = layoutButton->menu();
    QVERIFY(menu != nullptr);

    QAction *nineLayoutAction = menu->findChild<QAction *>(QStringLiteral("previewLayout.9"));
    QVERIFY(nineLayoutAction != nullptr);
    QVERIFY(nineLayoutAction->isEnabled());

    // InstantPopup 在鼠标按下时进入同步菜单循环；测试直接弹出同一菜单后验证菜单项鼠标命中。
    menu->popup(layoutButton->mapToGlobal(QPoint(0, layoutButton->height())));
    QTRY_VERIFY(menu->isVisible());
    const QRect actionGeometry = menu->actionGeometry(nineLayoutAction);
    QVERIFY(actionGeometry.isValid());
    QTest::mouseClick(menu, Qt::LeftButton, Qt::NoModifier, actionGeometry.center());

    QTRY_COMPARE(layoutButton->text(), QStringLiteral("9 分屏"));
    const QList<VideoTileWidget *> tiles = window_->findChildren<VideoTileWidget *>();
    const int visibleTiles = std::count_if(tiles.cbegin(), tiles.cend(), [](VideoTileWidget *tile) {
        return tile != nullptr && tile->isVisible();
    });
    QCOMPARE(visibleTiles, 9);
}

void MainWindowInteractionTests::fullScreenButtonRespondsToMouse() {
    if (QGuiApplication::platformName() == QStringLiteral("offscreen")) {
        QSKIP("离屏平台不支持稳定验证窗口全屏状态，改由 Windows 原生验收覆盖。");
    }

    QToolButton *fullScreenButton = nullptr;
    for (QToolButton *button : window_->findChildren<QToolButton *>()) {
        if (button->accessibleName() == QStringLiteral("切换视频画布全屏") && button->isVisible()) {
            fullScreenButton = button;
            break;
        }
    }
    QVERIFY(fullScreenButton != nullptr);
    QVERIFY(fullScreenButton->isEnabled());

    QTest::mouseClick(fullScreenButton, Qt::LeftButton);
    QTRY_VERIFY(window_->isCanvasFullScreen());
    QTRY_VERIFY(window_->isFullScreen());
    QVERIFY(!titleBar()->isVisible());
    QTest::keyClick(window_.get(), Qt::Key_Escape);
    QTRY_VERIFY(!window_->isCanvasFullScreen());
    QTRY_VERIFY(!window_->isFullScreen());
    QVERIFY(titleBar()->isVisible());
}

void MainWindowInteractionTests::dockLockStateSurvivesWorkspaceRefresh() {
    QAction *lockAction = action(ViewerActionId::LockDockLayout);
    QAction *playbackAction = action(ViewerActionId::WorkspacePlayback);
    QVERIFY(lockAction != nullptr);
    QVERIFY(playbackAction != nullptr);
    QVERIFY(!window_->isDockLayoutLocked());

    lockAction->trigger();
    QTRY_VERIFY(window_->isDockLayoutLocked());
    QVERIFY(lockAction->isChecked());

    playbackAction->trigger();
    QTRY_COMPARE(static_cast<int>(titleBar()->workspaceMode()), static_cast<int>(WorkspaceMode::Playback));
    QVERIFY(window_->isDockLayoutLocked());
    QVERIFY2(lockAction->isChecked(), "工作区状态刷新不得覆盖面板布局锁定状态。");
}

void MainWindowInteractionTests::cameraAssignmentRefreshesFavoriteAndPtzState() {
    CameraInfo camera;
    camera.id = QUuid::createUuid();
    camera.alias = QStringLiteral("测试球机");
    camera.code = QStringLiteral("PTZ-001");
    camera.canLiveView = true;
    camera.supportsPtz = true;
    camera.canControlPtz = true;
    camera.connectivity = 1;
    apiClient_->catalogLoaded({}, {camera});

    auto *catalogTree = window_->findChild<QTreeWidget *>(QStringLiteral("catalogTree"));
    QVERIFY(catalogTree != nullptr);
    QTRY_VERIFY(catalogTree->topLevelItemCount() == 1);
    QTreeWidgetItem *cameraItem = catalogTree->topLevelItem(0);
    QVERIFY(cameraItem != nullptr);
    catalogTree->itemActivated(cameraItem, 0);

    QAction *favoriteAction = action(ViewerActionId::ToggleFavorite);
    QVERIFY(favoriteAction != nullptr);
    QTRY_VERIFY(favoriteAction->isEnabled());

    QToolButton *upButton = nullptr;
    QToolButton *stopButton = nullptr;
    for (QToolButton *button : window_->findChildren<QToolButton *>()) {
        if (button->accessibleName() == QStringLiteral("云台向上")) {
            upButton = button;
        } else if (button->accessibleName() == QStringLiteral("停止云台动作")) {
            stopButton = button;
        }
    }
    QVERIFY(upButton != nullptr);
    QVERIFY(stopButton != nullptr);
    QTRY_VERIFY(upButton->isEnabled());
    QVERIFY(!stopButton->isEnabled());

    QTest::mousePress(upButton, Qt::LeftButton);
    QTRY_VERIFY(stopButton->isEnabled());
    QTest::mouseRelease(upButton, Qt::LeftButton);
    QTRY_VERIFY(!stopButton->isEnabled());
}

void MainWindowInteractionTests::cloudControlUsesChineseUserFacingText() {
    QAction *cloudControlAction = action(ViewerActionId::ShowPtz);
    QVERIFY(cloudControlAction != nullptr);
    QCOMPARE(cloudControlAction->text(), QStringLiteral("云台控制面板"));

    auto *toggleButton = window_->findChild<QToolButton *>(QStringLiteral("ptzPanelToggle"));
    QVERIFY(toggleButton != nullptr);
    QCOMPARE(toggleButton->text(), QStringLiteral("云台"));
    QVERIFY(!toggleButton->toolTip().contains(QStringLiteral("PTZ")));

    QAction *nativeCloudControlAction = nullptr;
    for (QAction *dockAction : window_->dockPanelActions()) {
        if (dockAction != nullptr && dockAction->data().toInt() == static_cast<int>(DockPanelId::Ptz)) {
            nativeCloudControlAction = dockAction;
            break;
        }
    }
    QVERIFY(nativeCloudControlAction != nullptr);
    QCOMPARE(nativeCloudControlAction->text(), QStringLiteral("云台控制"));

    apiClient_->ptzRequestFailed(QStringLiteral("模拟故障"));
    const QList<QLabel *> labels = window_->findChildren<QLabel *>();
    QTRY_VERIFY(std::any_of(
        labels.cbegin(),
        labels.cend(),
        [](const QLabel *label) {
            return label != nullptr && label->text() == QStringLiteral("云台控制请求失败：模拟故障");
        }));
}

void MainWindowInteractionTests::playbackSearchAndTimelineControlsRespondAfterSwitch() {
    QAction *playbackAction = action(ViewerActionId::WorkspacePlayback);
    QVERIFY(playbackAction != nullptr);
    playbackAction->trigger();
    QTRY_COMPARE(static_cast<int>(titleBar()->workspaceMode()), static_cast<int>(WorkspaceMode::Playback));

    auto *searchButton = window_->findChild<QPushButton *>(QStringLiteral("playbackSearchButton"));
    auto *zoomInButton = window_->findChild<QToolButton *>(QStringLiteral("timelineZoomIn"));
    auto *timeline = window_->findChild<RecordingTimelineWidget *>();
    QVERIFY(searchButton != nullptr);
    QVERIFY(zoomInButton != nullptr);
    QVERIFY(timeline != nullptr);
    QVERIFY(searchButton->isEnabled());
    QVERIFY(zoomInButton->isEnabled());

    QSignalSpy searchClickSpy(searchButton, &QPushButton::clicked);
    QTest::mouseClick(searchButton, Qt::LeftButton);
    QCOMPARE(searchClickSpy.count(), 1);

    QCOMPARE(timeline->zoomFactor(), 1.0);
    QTest::mouseClick(zoomInButton, Qt::LeftButton);
    QTRY_COMPARE(timeline->zoomFactor(), 1.5);
}

void MainWindowInteractionTests::videoTileContextMenuOnlyEmitsRequests() {
    CameraInfo camera;
    camera.id = QUuid::createUuid();
    camera.alias = QStringLiteral("右键菜单测试摄像头");
    camera.canLiveView = true;
    camera.canPlayback = true;

    VideoTileWidget previewTile(0);
    previewTile.assignCamera(camera, QStringLiteral("main"));
    previewTile.resize(320, 180);
    previewTile.show();
    QTRY_VERIFY(previewTile.isVisible());
    QSignalSpy clearRequestedSpy(&previewTile, &VideoTileWidget::clearRequested);
    previewTile.requestClear();
    QCOMPARE(clearRequestedSpy.count(), 1);
    QVERIFY2(!previewTile.isEmpty(), "右键清空命令必须先由主窗口门控，窗格本身不得直接清空。");

    VideoTileWidget playbackTile(1);
    playbackTile.assignCamera(camera, QStringLiteral("playback"));
    playbackTile.resize(320, 180);
    playbackTile.show();
    QTRY_VERIFY(playbackTile.isVisible());
    QSignalSpy syncRequestedSpy(&playbackTile, &VideoTileWidget::syncMembershipChangeRequested);
    playbackTile.requestSyncMembershipChange(false);
    QCOMPARE(syncRequestedSpy.count(), 1);
    QVERIFY2(playbackTile.isSyncMember(), "右键同步组命令必须先由主窗口门控，窗格本身不得直接修改同步状态。");
}

void MainWindowInteractionTests::canceledPreviewContextMenuDoesNotDispatchNullAction() {
    CameraInfo camera;
    camera.id = QUuid::createUuid();
    camera.alias = QStringLiteral("取消右键菜单测试摄像头");
    camera.canLiveView = true;

    VideoTileWidget tile(0);
    tile.assignCamera(camera, QStringLiteral("main"));
    tile.resize(320, 180);
    tile.show();
    QTRY_VERIFY(tile.isVisible());
    QSignalSpy profileChangeSpy(&tile, &VideoTileWidget::profileChangeRequested);
    QSignalSpy syncChangeSpy(&tile, &VideoTileWidget::syncMembershipChangeRequested);

    QTimer closer;
    closer.setInterval(5);
    connect(&closer, &QTimer::timeout, []() {
        if (QWidget *popup = QApplication::activePopupWidget()) {
            popup->close();
        }
    });
    closer.start();
    const QPoint localPosition(16, 16);
    QContextMenuEvent event(QContextMenuEvent::Mouse, localPosition, tile.mapToGlobal(localPosition));
    QApplication::sendEvent(&tile, &event);
    closer.stop();

    QCOMPARE(profileChangeSpy.count(), 0);
    QCOMPARE(syncChangeSpy.count(), 0);
}

void MainWindowInteractionTests::profileSwitchKeepsExistingPlayerCanvas() {
    CameraInfo camera;
    camera.id = QUuid::createUuid();
    camera.alias = QStringLiteral("码流切换测试摄像头");
    camera.canLiveView = true;

    VideoTileWidget tile(0);
    tile.assignCamera(camera, QStringLiteral("main"));
    StreamSessionInfo mainSession;
    mainSession.id = QUuid::createUuid();
    mainSession.gatewayUri = QUrl(QStringLiteral("http://127.0.0.1:18889/preview-main/index.m3u8"));
    QVERIFY(tile.startSession(mainSession));
    const QList<MpvPlayerWidget *> initialPlayers = tile.findChildren<MpvPlayerWidget *>();
    QCOMPARE(initialPlayers.size(), 1);
    MpvPlayerWidget *const player = initialPlayers.constFirst();

    tile.assignCamera(camera, QStringLiteral("sub"));
    QCOMPARE(tile.profile(), QStringLiteral("sub"));
    QCOMPARE(tile.findChildren<MpvPlayerWidget *>().size(), 1);
    QCOMPARE(tile.findChild<MpvPlayerWidget *>(), player);

    StreamSessionInfo subSession;
    subSession.id = QUuid::createUuid();
    subSession.gatewayUri = QUrl(QStringLiteral("http://127.0.0.1:18889/preview-sub/index.m3u8"));
    QVERIFY(tile.startSession(subSession));
    QCOMPARE(tile.findChildren<MpvPlayerWidget *>().size(), 1);
    QCOMPARE(tile.findChild<MpvPlayerWidget *>(), player);
}

void MainWindowInteractionTests::videoTileHeaderOnlyAppearsOnPointerHover() {
    CameraInfo camera;
    camera.id = QUuid::createUuid();
    camera.alias = QStringLiteral("悬停信息测试摄像头");
    camera.code = QStringLiteral("HOVER-001");
    camera.canLiveView = true;

    VideoTileWidget tile(0);
    tile.assignCamera(camera, QStringLiteral("main"));
    tile.resize(320, 180);
    tile.show();
    QTRY_VERIFY(tile.isVisible());

    auto *titleLabel = tile.findChild<QLabel *>(QStringLiteral("tileTitle"));
    auto *profileLabel = tile.findChild<QLabel *>(QStringLiteral("tileProfile"));
    auto *stateLabel = tile.findChild<QLabel *>(QStringLiteral("tileState"));
    auto *statusDot = tile.findChild<QFrame *>(QStringLiteral("tileStatusDot"));
    QVERIFY(titleLabel != nullptr);
    QVERIFY(profileLabel != nullptr);
    QVERIFY(stateLabel != nullptr);
    QVERIFY(statusDot != nullptr);

    QEvent leaveEvent(QEvent::Leave);
    QApplication::sendEvent(&tile, &leaveEvent);
    QVERIFY(!titleLabel->isVisible());
    QVERIFY(!profileLabel->isVisible());
    QVERIFY(!statusDot->isVisible());
    QVERIFY2(stateLabel->isVisible(), "连接状态提示不能跟随顶部信息一起隐藏。");

    const QPointF localPosition(12, 12);
    QEnterEvent enterEvent(localPosition, localPosition, tile.mapToGlobal(localPosition.toPoint()));
    QApplication::sendEvent(&tile, &enterEvent);
    QVERIFY(titleLabel->isVisible());
    QVERIFY(profileLabel->isVisible());
    QVERIFY(statusDot->isVisible());
    QCOMPARE(profileLabel->text(), QStringLiteral("主码流"));

    QEvent secondLeaveEvent(QEvent::Leave);
    QApplication::sendEvent(&tile, &secondLeaveEvent);
    tile.assignCamera(camera, QStringLiteral("sub"));
    QVERIFY(!titleLabel->isVisible());
    QVERIFY(!profileLabel->isVisible());
    QVERIFY(!statusDot->isVisible());
    QVERIFY(stateLabel->isVisible());
}

void MainWindowInteractionTests::authenticationDialogsKeepButtonsResponsive() {
    LoginDialog loginDialog(apiClient_.get());
    loginDialog.show();
    QTRY_VERIFY(loginDialog.isVisible());
    auto *loginButton = loginDialog.findChild<QPushButton *>(QStringLiteral("loginButton"));
    if (loginButton == nullptr) {
        for (QPushButton *candidate : loginDialog.findChildren<QPushButton *>()) {
            if (candidate->text() == QStringLiteral("登录")) {
                loginButton = candidate;
                break;
            }
        }
    }
    QVERIFY(loginButton != nullptr);
    QVERIFY(loginButton->isEnabled());
    QTest::mouseClick(loginButton, Qt::LeftButton);
    QTRY_VERIFY(loginButton->isEnabled());
    loginDialog.hide();

    ChangePasswordDialog changePasswordDialog(apiClient_.get(), nullptr, true);
    changePasswordDialog.show();
    QTRY_VERIFY(changePasswordDialog.isVisible());
    QPushButton *submitButton = nullptr;
    QPushButton *cancelButton = nullptr;
    for (QPushButton *candidate : changePasswordDialog.findChildren<QPushButton *>()) {
        if (candidate->text() == QStringLiteral("确认修改")) {
            submitButton = candidate;
        } else if (candidate->text() == QStringLiteral("返回登录")) {
            cancelButton = candidate;
        }
    }
    QVERIFY(submitButton != nullptr);
    QVERIFY(cancelButton != nullptr);
    QVERIFY(submitButton->isEnabled());
    QVERIFY(cancelButton->isEnabled());
    QTest::mouseClick(submitButton, Qt::LeftButton);
    QTRY_VERIFY(submitButton->isEnabled());
    changePasswordDialog.hide();
}

ViewerActionRegistry *MainWindowInteractionTests::registry() const {
    return window_ != nullptr ? window_->findChild<ViewerActionRegistry *>() : nullptr;
}

QAction *MainWindowInteractionTests::action(ViewerActionId id) const {
    ViewerActionRegistry *actionRegistry = registry();
    return actionRegistry != nullptr ? actionRegistry->action(id) : nullptr;
}

WindowTitleBar *MainWindowInteractionTests::titleBar() const {
    return window_ != nullptr ? window_->findChild<WindowTitleBar *>(QStringLiteral("windowTitleBar")) : nullptr;
}

QToolButton *MainWindowInteractionTests::titleBarButton(const QString &accessibleName) const {
    WindowTitleBar *windowTitleBar = titleBar();
    if (windowTitleBar == nullptr) {
        return nullptr;
    }
    for (QToolButton *button : windowTitleBar->findChildren<QToolButton *>()) {
        if (button != nullptr && button->accessibleName() == accessibleName) {
            return button;
        }
    }
    return nullptr;
}

QTEST_MAIN(MainWindowInteractionTests)

#include "main_window_interaction_tests.moc"
