#include "playback_controller.h"
#include "preview_controller.h"
#include "ptz_controller.h"
#include "workspace_controller.h"

#include <QSignalSpy>
#include <QTest>

class WorkspaceControllersTests final : public QObject {
    Q_OBJECT

private slots:
    void workspaceSwitchUpdatesModeSpecificActions();
    void previewLayoutValidationPreservesHiddenAssignments();
    void playbackTargetsUseStableIndicesAndConservativeCapabilities();
    void playbackTimeRangeRejectsInvalidWindows();
    void ptzAvailabilityLossAlwaysRequestsStop();
};

void WorkspaceControllersTests::workspaceSwitchUpdatesModeSpecificActions() {
    WorkspaceController controller;
    QCOMPARE(static_cast<int>(controller.mode()), static_cast<int>(WorkspaceMode::Preview));
    QVERIFY(controller.isActionEnabled(ViewerActionId::ChangePreviewLayout));
    QVERIFY(!controller.isActionEnabled(ViewerActionId::ChangePlaybackLayout));
    QVERIFY(controller.isActionChecked(ViewerActionId::WorkspacePreview));

    QSignalSpy changingSpy(&controller, &WorkspaceController::workspaceAboutToChange);
    QSignalSpy changedSpy(&controller, &WorkspaceController::workspaceChanged);
    QVERIFY(controller.setMode(WorkspaceMode::Playback));
    QCOMPARE(changingSpy.count(), 1);
    QCOMPARE(changedSpy.count(), 1);
    QVERIFY(!controller.isActionEnabled(ViewerActionId::ChangePreviewLayout));
    QVERIFY(controller.isActionEnabled(ViewerActionId::ChangePlaybackLayout));
    QVERIFY(controller.isActionChecked(ViewerActionId::WorkspacePlayback));

    controller.setInteractionEnabled(false);
    QVERIFY(!controller.isActionEnabled(ViewerActionId::ExitApplication));
    QVERIFY(!controller.setMode(WorkspaceMode::Preview));
}

void WorkspaceControllersTests::previewLayoutValidationPreservesHiddenAssignments() {
    PreviewController controller;
    QSignalSpy rejectedSpy(&controller, &PreviewController::operationRejected);

    QVERIFY(!controller.setLayoutCount(49));
    QCOMPARE(controller.layoutCount(), 4);
    QCOMPARE(rejectedSpy.count(), 1);

    QVERIFY(controller.setLayoutCount(64));
    QCOMPARE(controller.gridDimension(controller.layoutCount()), 8);
    QVERIFY(controller.compactTiles());
    QCOMPARE(controller.effectiveStreamProfile(), QStringLiteral("sub"));
    QVERIFY(controller.selectTile(63));
    QVERIFY(controller.setTileAssigned(63, true));
    QVERIFY(controller.isActionEnabled(ViewerActionId::ToggleFavorite));

    QVERIFY(controller.setLayoutCount(4));
    QCOMPARE(controller.selectedTileIndex(), 0);
    QVERIFY(controller.isTileAssigned(63));
    QVERIFY(!controller.isTileVisible(63));
    QCOMPARE(controller.effectiveStreamProfile(), QStringLiteral("main"));
    QCOMPARE(PreviewController::bestLayoutForCameraCount(65), 64);
}

void WorkspaceControllersTests::playbackTargetsUseStableIndicesAndConservativeCapabilities() {
    PlaybackController controller;
    QVERIFY(controller.setLayoutCount(4));

    PlaybackTileState first;
    first.cameraId = QUuid::createUuid();
    first.sessionId = QUuid::createUuid();
    first.transport.canPause = true;
    first.transport.canSeek = true;
    first.transport.canChangeSpeed = true;

    PlaybackTileState second = first;
    second.cameraId = QUuid::createUuid();
    second.sessionId = QUuid::createUuid();
    second.transport.canSeek = false;
    second.transport.isPaused = true;

    PlaybackTileState third = first;
    third.cameraId = QUuid::createUuid();
    third.sessionId = QUuid::createUuid();
    third.syncMember = false;

    QVERIFY(controller.setTileState(0, first));
    QVERIFY(controller.setTileState(1, second));
    QVERIFY(controller.setTileState(2, third));
    QCOMPARE(controller.controlTargetIndices(), QList<int>({0, 1}));

    const PlaybackControlState synchronized = controller.controlState();
    QVERIFY(synchronized.hasTargets);
    QVERIFY(synchronized.ready);
    QVERIFY(synchronized.canPause);
    QVERIFY(!synchronized.canSeek);
    QVERIFY(synchronized.anyPaused);
    QVERIFY(!synchronized.allPaused);
    QVERIFY(synchronized.pauseEnabled);
    QVERIFY(synchronized.resumeEnabled);

    controller.setSyncEnabled(false);
    QCOMPARE(controller.controlTargetIndices(), QList<int>({0}));
    QVERIFY(controller.controlState().seekEnabled);

    controller.setSyncEnabled(true);
    QVERIFY(controller.selectTile(2));
    QCOMPARE(controller.controlTargetIndices(), QList<int>({2}));
    third.commandPending = true;
    QVERIFY(controller.setTileState(2, third));
    QVERIFY(controller.controlState().pending);
    QVERIFY(!controller.controlState().pauseEnabled);
}

void WorkspaceControllersTests::playbackTimeRangeRejectsInvalidWindows() {
    const QDateTime startedAt = QDateTime::fromString(QStringLiteral("2026-07-15T08:00:00+08:00"), Qt::ISODate);
    QString message;
    QVERIFY(PlaybackController::validateTimeRange(startedAt, startedAt.addDays(31), &message));
    QVERIFY(message.isEmpty());
    QVERIFY(!PlaybackController::validateTimeRange(startedAt, startedAt, &message));
    QVERIFY(!message.isEmpty());
    QVERIFY(!PlaybackController::validateTimeRange(startedAt, startedAt.addDays(31).addSecs(1), &message));
}

void WorkspaceControllersTests::ptzAvailabilityLossAlwaysRequestsStop() {
    PtzController controller;
    QCOMPARE(
        static_cast<int>(controller.availabilityReason()),
        static_cast<int>(PtzAvailabilityReason::NoCameraSelected));

    CameraInfo camera;
    camera.id = QUuid::createUuid();
    camera.alias = QStringLiteral("南门球机");
    camera.supportsPtz = true;
    camera.canControlPtz = true;
    camera.connectivity = 1;
    controller.setSelectedCamera(camera);
    QVERIFY(controller.available());

    QSignalSpy commandSpy(&controller, &PtzController::ptzCommandRequested);
    QVERIFY(controller.beginPulse(2, 4));
    QCOMPARE(commandSpy.count(), 1);
    QVERIFY(controller.pulseState().active);

    controller.setWorkspaceMode(WorkspaceMode::Playback);
    QCOMPARE(commandSpy.count(), 2);
    QCOMPARE(commandSpy.at(1).at(2).toInt(), 1);
    QVERIFY(!controller.pulseState().active);
    QCOMPARE(
        static_cast<int>(controller.availabilityReason()),
        static_cast<int>(PtzAvailabilityReason::PlaybackWorkspace));

    controller.setWorkspaceMode(WorkspaceMode::Preview);
    QVERIFY(controller.beginPulse(0, 7));
    camera.connectivity = 3;
    controller.setSelectedCamera(camera);
    QCOMPARE(commandSpy.last().at(2).toInt(), 1);
    QCOMPARE(
        static_cast<int>(controller.availabilityReason()),
        static_cast<int>(PtzAvailabilityReason::CameraOffline));
    QVERIFY(!controller.available());
    QVERIFY(!controller.beginPulse(99, 4));
}

QTEST_MAIN(WorkspaceControllersTests)

#include "workspace_controllers_tests.moc"
