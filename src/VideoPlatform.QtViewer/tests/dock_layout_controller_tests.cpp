#include "dock_layout_controller.h"
#include "viewer_ui_settings.h"

#include <DockWidget.h>

#include <QAction>
#include <QMainWindow>
#include <QPushButton>
#include <QSettings>
#include <QSignalSpy>
#include <QTemporaryDir>
#include <QTest>
#include <QUuid>
#include <QVBoxLayout>
#include <QWidget>

class DockLayoutControllerTests final : public QObject {
    Q_OBJECT

private slots:
    void initTestCase();
    void defaultLayoutMatchesWorkspace();
    void inactiveDockTabsUseReadableTextColor();
    void workspaceStatesRemainIndependent();
    void corruptedStateFallsBackToDefault();
    void resetAndLockAreDeterministic();
    void panelActionsAndContentsRemainInteractive();
    void interactionFreezePreventsDockMutation();
    void canvasOnlyPreservesPersistentStateAndRestoresPanelVisibility();
    void canvasOnlyDoesNotPersistTemporaryVisibilityAcrossWorkspaceSwitch();
    void settingsMigrationPreservesLegacyPreferences();

private:
    static DockLayoutController::PanelWidgets createPanels();

    QTemporaryDir settingsDirectory_;
};

void DockLayoutControllerTests::initTestCase() {
    QVERIFY(settingsDirectory_.isValid());
    QCoreApplication::setOrganizationName(QStringLiteral("VideoPlatformDockTests"));
    QCoreApplication::setApplicationName(QStringLiteral("VideoPlatformDockTests"));
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsDirectory_.path());
    QSettings().clear();
}

void DockLayoutControllerTests::defaultLayoutMatchesWorkspace() {
    QMainWindow window;
    window.resize(1366, 768);
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);
    window.show();
    QCoreApplication::processEvents();

    QCOMPARE(controller.dockPanelActions().size(), 4);
    QVERIFY(controller.isPanelVisible(DockPanelId::ResourceCatalog));
    QVERIFY(controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(!controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(!controller.isPanelVisible(DockPanelId::RecordingTimeline));
    auto *resourceDock = window.findChild<QWidget *>(QStringLiteral("dock.panel.0"));
    auto *ptzDock = window.findChild<QWidget *>(QStringLiteral("dock.panel.1"));
    QVERIFY(resourceDock != nullptr);
    QVERIFY(ptzDock != nullptr);
    QVERIFY2(resourceDock->width() >= 240, "默认资源面板宽度不得退化为仅能显示标题的窄条。");
    QVERIFY2(ptzDock->width() >= 220, "默认 PTZ 面板宽度必须容纳完整控制按钮。");

    controller.switchWorkspace(WorkspaceMode::Playback);
    QCoreApplication::processEvents();
    QVERIFY(controller.isPanelVisible(DockPanelId::ResourceCatalog));
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(controller.isPanelVisible(DockPanelId::RecordingTimeline));
    auto *searchContent = window.findChild<QWidget *>(QStringLiteral("test.playbackSearch"));
    auto *timelineContent = window.findChild<QWidget *>(QStringLiteral("test.recordingTimeline"));
    QVERIFY(searchContent != nullptr);
    QVERIFY(timelineContent != nullptr);
    QVERIFY2(searchContent->height() >= 50, "回放检索内容区不得被压缩为标题栏。");
    QVERIFY2(timelineContent->height() >= 220, "录像时间轴必须保留完整操作高度。");
    const QRect searchRect(searchContent->mapToGlobal(QPoint(0, 0)), searchContent->size());
    const QRect timelineRect(timelineContent->mapToGlobal(QPoint(0, 0)), timelineContent->size());
    QVERIFY2(!searchRect.intersects(timelineRect), "回放检索与录像时间轴不得发生覆盖。");
}

void DockLayoutControllerTests::inactiveDockTabsUseReadableTextColor() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);

    auto *dockManager = window.findChild<QWidget *>(QStringLiteral("workspaceDockManager"));
    QVERIFY(dockManager != nullptr);
    const QString styleSheet = dockManager->styleSheet();
    QVERIFY2(
        styleSheet.contains(QStringLiteral("ads--CDockWidgetTab[activeTab=\"false\"] QLabel#dockWidgetTabLabel")),
        "Dock 管理器必须在自身局部样式中覆盖未激活标签，避免被第三方同色调色板吞没。");
    QVERIFY(styleSheet.contains(QStringLiteral("color: #B8C1C4")));

    auto *cloudControlDock = window.findChild<ads::CDockWidget *>(QStringLiteral("dock.panel.1"));
    QVERIFY(cloudControlDock != nullptr);
    QCOMPARE(cloudControlDock->windowTitle(), QStringLiteral("云台控制"));
    QCOMPARE(cloudControlDock->toggleViewAction()->text(), QStringLiteral("云台控制"));
}

void DockLayoutControllerTests::workspaceStatesRemainIndependent() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);

    controller.showDockPanel(DockPanelId::Ptz, false);
    controller.switchWorkspace(WorkspaceMode::Playback);
    controller.showDockPanel(DockPanelId::Ptz, true);
    controller.showDockPanel(DockPanelId::PlaybackSearch, false);

    controller.switchWorkspace(WorkspaceMode::Preview);
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(!controller.isPanelVisible(DockPanelId::PlaybackSearch));

    controller.switchWorkspace(WorkspaceMode::Playback);
    QVERIFY(controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(!controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(controller.isPanelVisible(DockPanelId::RecordingTimeline));
}

void DockLayoutControllerTests::corruptedStateFallsBackToDefault() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);
    controller.setStoredState(WorkspaceMode::Playback, QByteArrayLiteral("invalid-dock-state"));

    QSignalSpy fallbackSpy(&controller, &DockLayoutController::layoutRestoredFromDefault);
    controller.switchWorkspace(WorkspaceMode::Playback);

    QCOMPARE(fallbackSpy.count(), 1);
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(controller.isPanelVisible(DockPanelId::RecordingTimeline));
    QVERIFY(!controller.stateFor(WorkspaceMode::Playback).isEmpty());
}

void DockLayoutControllerTests::resetAndLockAreDeterministic() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);
    controller.switchWorkspace(WorkspaceMode::Playback);
    controller.showDockPanel(DockPanelId::Ptz, true);
    controller.showDockPanel(DockPanelId::RecordingTimeline, false);

    controller.resetCurrentLayout();
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(controller.isPanelVisible(DockPanelId::RecordingTimeline));

    QSignalSpy lockSpy(&controller, &DockLayoutController::lockedChanged);
    controller.setLocked(true);
    QVERIFY(controller.isLocked());
    QCOMPARE(lockSpy.count(), 1);
    controller.setLocked(false);
    QVERIFY(!controller.isLocked());
    QCOMPARE(lockSpy.count(), 2);
}

void DockLayoutControllerTests::panelActionsAndContentsRemainInteractive() {
    QMainWindow window;
    window.resize(1080, 680);
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);
    window.show();
    QCoreApplication::processEvents();

    QAction *ptzAction = controller.dockPanelAction(DockPanelId::Ptz);
    QVERIFY(ptzAction != nullptr);
    QVERIFY(ptzAction->isEnabled());
    QVERIFY(ptzAction->isCheckable());
    QVERIFY(ptzAction->isChecked());
    QCOMPARE(ptzAction->data().toInt(), static_cast<int>(DockPanelId::Ptz));

    auto *ptzDock = window.findChild<ads::CDockWidget *>(QStringLiteral("dock.panel.1"));
    QVERIFY(ptzDock != nullptr);
    QCOMPARE(
        ptzDock->minimumSizeHintMode(),
        ads::CDockWidget::MinimumSizeHintFromContentMinimumSize);

    QSignalSpy visibilitySpy(&controller, &DockLayoutController::panelVisibilityChanged);
    controller.setLocked(true);
    QVERIFY(ptzAction->isEnabled());
    ptzAction->trigger();
    QCoreApplication::processEvents();
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(!ptzAction->isChecked());

    ptzAction->trigger();
    QCoreApplication::processEvents();
    QVERIFY(controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(ptzAction->isChecked());
    QVERIFY2(ptzDock->width() >= 220, "从面板菜单重新打开 PTZ 后必须恢复可用宽度。");
    QCOMPARE(visibilitySpy.count(), 2);

    auto *probeButton = window.findChild<QPushButton *>(QStringLiteral("test.ptzProbe"));
    QVERIFY(probeButton != nullptr);
    QVERIFY(probeButton->isVisible());
    QSignalSpy clickSpy(probeButton, &QPushButton::clicked);
    QTest::mouseClick(probeButton, Qt::LeftButton);
    QCOMPARE(clickSpy.count(), 1);
}

void DockLayoutControllerTests::interactionFreezePreventsDockMutation() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);

    QAction *ptzAction = controller.dockPanelAction(DockPanelId::Ptz);
    QVERIFY(ptzAction != nullptr);
    QVERIFY(controller.isPanelVisible(DockPanelId::Ptz));

    controller.setInteractionFrozen(true);
    QVERIFY(controller.isInteractionFrozen());
    QVERIFY(!ptzAction->isEnabled());
    controller.showDockPanel(DockPanelId::Ptz, false);
    QVERIFY2(controller.isPanelVisible(DockPanelId::Ptz), "切换工作区期间不得改变停靠面板可见性。");

    controller.setInteractionFrozen(false);
    QVERIFY(!controller.isInteractionFrozen());
    QVERIFY(ptzAction->isEnabled());
    controller.showDockPanel(DockPanelId::Ptz, false);
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
}

void DockLayoutControllerTests::canvasOnlyPreservesPersistentStateAndRestoresPanelVisibility() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);
    controller.switchWorkspace(WorkspaceMode::Playback);
    controller.showDockPanel(DockPanelId::Ptz, true);
    controller.showDockPanel(DockPanelId::RecordingTimeline, false);
    const QByteArray stateBeforeCanvasOnly = controller.stateFor(WorkspaceMode::Playback);
    QAction *ptzAction = controller.dockPanelAction(DockPanelId::Ptz);
    QVERIFY(ptzAction != nullptr);

    controller.setCanvasOnly(true);
    QVERIFY(controller.isCanvasOnly());
    QVERIFY(!ptzAction->isEnabled());
    QVERIFY(!controller.isPanelVisible(DockPanelId::ResourceCatalog));
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(!controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(!controller.isPanelVisible(DockPanelId::RecordingTimeline));
    QCOMPARE(controller.stateFor(WorkspaceMode::Playback), stateBeforeCanvasOnly);

    // 画布模式下即使存在遗留菜单动作，也不能改写进入全屏前的面板快照。
    ptzAction->trigger();
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    controller.showDockPanel(DockPanelId::Ptz, true);
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QCOMPARE(controller.stateFor(WorkspaceMode::Playback), stateBeforeCanvasOnly);

    controller.setCanvasOnly(false);
    QVERIFY(!controller.isCanvasOnly());
    QVERIFY(ptzAction->isEnabled());
    QVERIFY(controller.isPanelVisible(DockPanelId::ResourceCatalog));
    QVERIFY(controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(!controller.isPanelVisible(DockPanelId::RecordingTimeline));
}

void DockLayoutControllerTests::canvasOnlyDoesNotPersistTemporaryVisibilityAcrossWorkspaceSwitch() {
    QMainWindow window;
    DockLayoutController controller(&window);
    controller.initialize(new QWidget, createPanels(), true);

    controller.showDockPanel(DockPanelId::Ptz, false);
    controller.switchWorkspace(WorkspaceMode::Playback);
    controller.showDockPanel(DockPanelId::Ptz, true);
    controller.showDockPanel(DockPanelId::RecordingTimeline, false);
    const QByteArray playbackStateBeforeCanvasOnly = controller.stateFor(WorkspaceMode::Playback);

    controller.setCanvasOnly(true);
    QVERIFY(controller.isCanvasOnly());
    QCOMPARE(controller.stateFor(WorkspaceMode::Playback), playbackStateBeforeCanvasOnly);

    // 工作区切换会先结束画布模式；必须恢复原面板可见性后再捕获工作区状态。
    controller.switchWorkspace(WorkspaceMode::Preview);
    QVERIFY(!controller.isCanvasOnly());
    QVERIFY(controller.isPanelVisible(DockPanelId::ResourceCatalog));
    QVERIFY(!controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(!controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(!controller.isPanelVisible(DockPanelId::RecordingTimeline));

    controller.switchWorkspace(WorkspaceMode::Playback);
    QVERIFY(controller.isPanelVisible(DockPanelId::ResourceCatalog));
    QVERIFY(controller.isPanelVisible(DockPanelId::Ptz));
    QVERIFY(controller.isPanelVisible(DockPanelId::PlaybackSearch));
    QVERIFY(!controller.isPanelVisible(DockPanelId::RecordingTimeline));
    QVERIFY(!controller.stateFor(WorkspaceMode::Playback).isEmpty());
}

void DockLayoutControllerTests::settingsMigrationPreservesLegacyPreferences() {
    const QString username = QUuid::createUuid().toString(QUuid::WithoutBraces);
    const QString prefix = ViewerUiSettings::accountSettingsPrefix(username);
    const QByteArray legacyGeometry("legacy-geometry");
    const QByteArray legacySplitter("legacy-splitter");
    QSettings rawSettings;
    rawSettings.setValue(prefix + QStringLiteral("geometry"), legacyGeometry);
    rawSettings.setValue(prefix + QStringLiteral("splitter"), legacySplitter);
    rawSettings.setValue(prefix + QStringLiteral("previewLayout"), 16);

    ViewerUiSettings settings(username);
    QCOMPARE(settings.schemaVersion(), ViewerUiSettings::CurrentSchemaVersion);
    QCOMPARE(settings.windowGeometry(), legacyGeometry);
    QCOMPARE(rawSettings.value(prefix + QStringLiteral("splitter")).toByteArray(), legacySplitter);
    QCOMPARE(rawSettings.value(prefix + QStringLiteral("previewLayout")).toInt(), 16);

    const QByteArray previewState("preview-state");
    const QByteArray playbackState("playback-state");
    settings.setDockState(WorkspaceMode::Preview, previewState);
    settings.setDockState(WorkspaceMode::Playback, playbackState);
    settings.setDockLocked(true);

    QCOMPARE(settings.dockState(WorkspaceMode::Preview), previewState);
    QCOMPARE(settings.dockState(WorkspaceMode::Playback), playbackState);
    QVERIFY(settings.dockLocked());
    QCOMPARE(rawSettings.value(prefix + QStringLiteral("dock/previewState")).toByteArray(), previewState);
    QCOMPARE(rawSettings.value(prefix + QStringLiteral("dock/playbackState")).toByteArray(), playbackState);
}

DockLayoutController::PanelWidgets DockLayoutControllerTests::createPanels() {
    auto *resource = new QWidget;
    resource->setObjectName(QStringLiteral("test.resource"));
    resource->setMinimumWidth(248);

    auto *ptz = new QWidget;
    ptz->setObjectName(QStringLiteral("test.ptz"));
    ptz->setMinimumSize(220, 260);
    auto *ptzLayout = new QVBoxLayout(ptz);
    auto *probeButton = new QPushButton(QStringLiteral("测试 PTZ 控件"), ptz);
    probeButton->setObjectName(QStringLiteral("test.ptzProbe"));
    ptzLayout->addWidget(probeButton);
    ptzLayout->addStretch();

    auto *playbackSearch = new QWidget;
    playbackSearch->setObjectName(QStringLiteral("test.playbackSearch"));
    playbackSearch->setMinimumHeight(50);

    auto *recordingTimeline = new QWidget;
    recordingTimeline->setObjectName(QStringLiteral("test.recordingTimeline"));
    recordingTimeline->setMinimumHeight(220);

    return {
        resource,
        ptz,
        playbackSearch,
        recordingTimeline,
    };
}

QTEST_MAIN(DockLayoutControllerTests)
#include "dock_layout_controller_tests.moc"
