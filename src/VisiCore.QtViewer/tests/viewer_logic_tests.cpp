#include "viewer_logic.h"
#include "recording_timeline_widget.h"
#include "viewer_startup_state.h"
#include "viewer_ui_settings.h"

#include <QJsonArray>
#include <QJsonObject>
#include <QSignalSpy>
#include <QSettings>
#include <QTemporaryDir>
#include <QTest>

class ViewerLogicTests final : public QObject {
    Q_OBJECT

private slots:
    void passwordChangeRequirementUsesStructuredErrorCode();
    void ptzCommandAcknowledgementUsesLeaseExpiryContract();
    void streamModeIsNormalized();
    void previewProfileFollowsModeAndLayout();
    void layoutSelectionUsesSupportedCapacity();
    void windowOrderStartsAtSelectedTile();
    void recordingSegmentsSupportStandardAndLegacyShapes();
    void playbackCapabilitiesAreAggregatedConservatively();
    void timelineZoomKeepsAnchorAndBounds();
    void timelineLabelAndRecordingGapsDoNotSeek();
    void cameraStatusesMergeAndSummarizeWithoutInventingState();
    void uiSettingsMigrateAndRemainAccountIsolated();
    void uiSettingsRecoveryBackupOnlyIsolatesLayout();
    void startupStateDetectsUnexpectedExitAndSafeUi();
};

void ViewerLogicTests::passwordChangeRequirementUsesStructuredErrorCode() {
    QVERIFY(ViewerLogic::isPasswordChangeRequiredError(
        QJsonObject{{QStringLiteral("code"), QStringLiteral("password_change_required")}}));
    QVERIFY(!ViewerLogic::isPasswordChangeRequiredError(
        QJsonObject{{QStringLiteral("message"), QStringLiteral("首次登录或密码重置后必须修改密码。")}}));
    QVERIFY(!ViewerLogic::isPasswordChangeRequiredError(
        QJsonObject{{QStringLiteral("code"), QStringLiteral("forbidden")}}));
}

void ViewerLogicTests::ptzCommandAcknowledgementUsesLeaseExpiryContract() {
    const QDateTime expected = QDateTime::fromString(QStringLiteral("2026-07-16T16:30:00Z"), Qt::ISODate);
    QCOMPARE(
        ViewerLogic::parsePtzCommandLeaseExpiry(
            QJsonObject{{QStringLiteral("leaseExpiresAt"), QStringLiteral("2026-07-16T16:30:00Z")}}),
        expected);
    QVERIFY(!ViewerLogic::parsePtzCommandLeaseExpiry(
        QJsonObject{{QStringLiteral("expiresAt"), QStringLiteral("2026-07-16T16:30:00Z")}}).isValid());
}

void ViewerLogicTests::streamModeIsNormalized() {
    QCOMPARE(ViewerLogic::normalizedStreamMode(QStringLiteral(" MAIN ")), QStringLiteral("main"));
    QCOMPARE(ViewerLogic::normalizedStreamMode(QStringLiteral("Sub")), QStringLiteral("sub"));
    QCOMPARE(ViewerLogic::normalizedStreamMode(QStringLiteral("unknown")), QStringLiteral("auto"));
    QCOMPARE(ViewerLogic::normalizedStreamMode(QString{}), QStringLiteral("auto"));
}

void ViewerLogicTests::previewProfileFollowsModeAndLayout() {
    QCOMPARE(ViewerLogic::previewProfileForMode(QStringLiteral("auto"), 1), QStringLiteral("main"));
    QCOMPARE(ViewerLogic::previewProfileForMode(QStringLiteral("auto"), 8), QStringLiteral("main"));
    QCOMPARE(ViewerLogic::previewProfileForMode(QStringLiteral("auto"), 9), QStringLiteral("sub"));
    QCOMPARE(ViewerLogic::previewProfileForMode(QStringLiteral("main"), 64), QStringLiteral("main"));
    QCOMPARE(ViewerLogic::previewProfileForMode(QStringLiteral("sub"), 1), QStringLiteral("sub"));
}

void ViewerLogicTests::layoutSelectionUsesSupportedCapacity() {
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(0, false), 1);
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(2, false), 4);
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(10, false), 16);
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(65, false), 64);
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(1, true), 1);
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(2, true), 4);
    QCOMPARE(ViewerLogic::bestLayoutForCameraCount(5, true), 4);
}

void ViewerLogicTests::windowOrderStartsAtSelectedTile() {
    QCOMPARE(ViewerLogic::orderedWindowIndices(2, 4), QList<int>({2, 3, 0, 1}));
    QCOMPARE(ViewerLogic::orderedWindowIndices(-3, 4), QList<int>({0, 1, 2, 3}));
    QCOMPARE(ViewerLogic::orderedWindowIndices(99, 4), QList<int>({3, 0, 1, 2}));
    QVERIFY(ViewerLogic::orderedWindowIndices(0, 0).isEmpty());
}

void ViewerLogicTests::recordingSegmentsSupportStandardAndLegacyShapes() {
    const QJsonObject standardSegment{
        {QStringLiteral("startedAt"), QStringLiteral("2026-07-15T01:00:00Z")},
        {QStringLiteral("endedAt"), QStringLiteral("2026-07-15T01:05:00Z")},
        {QStringLiteral("sizeBytes"), 2048},
        {QStringLiteral("isLocked"), true},
        {QStringLiteral("segmentType"), QStringLiteral("motion")}};
    const QJsonObject invalidSegment{
        {QStringLiteral("startedAt"), QStringLiteral("2026-07-15T02:00:00Z")},
        {QStringLiteral("endedAt"), QStringLiteral("2026-07-15T02:00:00Z")}};
    const QJsonObject standardResponse{
        {QStringLiteral("coverageApproximate"), true},
        {QStringLiteral("segments"), QJsonArray{standardSegment, invalidSegment}}};

    const QList<RecordingSegment> standard = ViewerLogic::parseRecordingSegments(standardResponse);
    QCOMPARE(standard.size(), 1);
    QCOMPARE(standard.first().startedAt.toUTC(), QDateTime::fromString(QStringLiteral("2026-07-15T01:00:00Z"), Qt::ISODate));
    QCOMPARE(standard.first().endedAt.toUTC(), QDateTime::fromString(QStringLiteral("2026-07-15T01:05:00Z"), Qt::ISODate));
    QCOMPARE(standard.first().sizeBytes, qint64{2048});
    QVERIFY(standard.first().locked);
    QCOMPARE(standard.first().fileType, QStringLiteral("motion"));
    QVERIFY(standard.first().approximate);

    const QJsonObject legacySegment{
        {QStringLiteral("startedAt"), QStringLiteral("2026-07-15T03:00:00+08:00")},
        {QStringLiteral("endedAt"), QStringLiteral("2026-07-15T03:10:00+08:00")},
        {QStringLiteral("fileType"), QStringLiteral("schedule")},
        {QStringLiteral("coverageApproximate"), false}};
    const QJsonObject legacyResponse{{QStringLiteral("result"), QJsonObject{
        {QStringLiteral("coverageApproximate"), true},
        {QStringLiteral("segments"), QJsonArray{legacySegment}}}}};

    const QList<RecordingSegment> legacy = ViewerLogic::parseRecordingSegments(legacyResponse);
    QCOMPARE(legacy.size(), 1);
    QCOMPARE(legacy.first().fileType, QStringLiteral("schedule"));
    QVERIFY(!legacy.first().approximate);
}

void ViewerLogicTests::playbackCapabilitiesAreAggregatedConservatively() {
    PlaybackTransportInfo first;
    first.canPause = true;
    first.canSeek = true;
    first.canChangeSpeed = true;
    first.isPaused = true;
    PlaybackTransportInfo second;
    second.canPause = true;
    second.canSeek = false;
    second.canChangeSpeed = true;

    const ViewerLogic::PlaybackControlSummary summary = ViewerLogic::summarizePlaybackControls(
        QList<PlaybackTransportInfo>{first, second},
        QList<bool>{true, true},
        QList<bool>{false, true});
    QVERIFY(summary.hasTargets);
    QVERIFY(summary.ready);
    QVERIFY(summary.pending);
    QVERIFY(summary.canPause);
    QVERIFY(!summary.canSeek);
    QVERIFY(summary.canChangeSpeed);
    QVERIFY(summary.anyPaused);
    QVERIFY(!summary.allPaused);

    const ViewerLogic::PlaybackControlSummary notReady = ViewerLogic::summarizePlaybackControls(
        QList<PlaybackTransportInfo>{first, second},
        QList<bool>{true, false},
        QList<bool>{false, false});
    QVERIFY(notReady.hasTargets);
    QVERIFY(!notReady.ready);
    QVERIFY(!notReady.canPause);
    QVERIFY(!notReady.canSeek);
    QVERIFY(!notReady.canChangeSpeed);

    const ViewerLogic::PlaybackControlSummary mismatched = ViewerLogic::summarizePlaybackControls(
        QList<PlaybackTransportInfo>{first}, QList<bool>{}, QList<bool>{});
    QVERIFY(!mismatched.hasTargets);
}

void ViewerLogicTests::timelineZoomKeepsAnchorAndBounds() {
    const QDateTime startedAt = QDateTime::fromString(QStringLiteral("2026-07-15T00:00:00+08:00"), Qt::ISODate);
    const QDateTime endedAt = startedAt.addDays(1);

    const ViewerLogic::TimelineView centered = ViewerLogic::zoomedTimelineView(
        startedAt, endedAt, startedAt.addSecs(12 * 60 * 60), 0.5, 2.0);
    QCOMPARE(centered.startedAt, startedAt.addSecs(6 * 60 * 60));
    QCOMPARE(centered.endedAt, startedAt.addSecs(18 * 60 * 60));

    const ViewerLogic::TimelineView leftBounded = ViewerLogic::zoomedTimelineView(
        startedAt, endedAt, startedAt.addSecs(60 * 60), 0.5, 4.0);
    QCOMPARE(leftBounded.startedAt, startedAt);
    QCOMPARE(leftBounded.endedAt, startedAt.addSecs(6 * 60 * 60));

    const ViewerLogic::TimelineView unzoomed = ViewerLogic::zoomedTimelineView(
        startedAt, endedAt, QDateTime{}, 0.5, 0.25);
    QCOMPARE(unzoomed.startedAt, startedAt);
    QCOMPARE(unzoomed.endedAt, endedAt);

    const ViewerLogic::TimelineView invalid = ViewerLogic::zoomedTimelineView(
        endedAt, startedAt, startedAt, 0.5, 2.0);
    QVERIFY(!invalid.startedAt.isValid());
    QVERIFY(!invalid.endedAt.isValid());
}

void ViewerLogicTests::timelineLabelAndRecordingGapsDoNotSeek() {
    RecordingTimelineWidget timeline;
    timeline.resize(800, 110);
    const QDateTime startedAt = QDateTime::fromString(QStringLiteral("2026-07-15T08:00:00+08:00"), Qt::ISODate);
    const QDateTime endedAt = startedAt.addSecs(4 * 60 * 60);
    RecordingSegment segment;
    segment.startedAt = startedAt.addSecs(60 * 60);
    segment.endedAt = startedAt.addSecs(2 * 60 * 60);
    timeline.setRange(startedAt, endedAt);
    timeline.setTracks({RecordingTimelineTrack{
        QStringLiteral("窗格 1"), QStringLiteral("有录像"), {segment}, true}});
    timeline.show();

    QSignalSpy trackSpy(&timeline, &RecordingTimelineWidget::trackSelected);
    QSignalSpy positionSpy(&timeline, &RecordingTimelineWidget::positionSelected);
    QSignalSpy unavailableSpy(&timeline, &RecordingTimelineWidget::positionUnavailable);

    QTest::mouseClick(&timeline, Qt::LeftButton, Qt::NoModifier, QPoint(80, 49));
    QCOMPARE(trackSpy.count(), 1);
    QCOMPARE(positionSpy.count(), 0);
    QCOMPARE(unavailableSpy.count(), 0);

    QTest::mouseClick(&timeline, Qt::LeftButton, Qt::NoModifier, QPoint(408, 49));
    QCOMPARE(trackSpy.count(), 2);
    QCOMPARE(positionSpy.count(), 1);

    QTest::mouseClick(&timeline, Qt::LeftButton, Qt::NoModifier, QPoint(714, 49));
    QCOMPARE(trackSpy.count(), 3);
    QCOMPARE(positionSpy.count(), 1);
    QCOMPARE(unavailableSpy.count(), 1);
}

void ViewerLogicTests::cameraStatusesMergeAndSummarizeWithoutInventingState() {
    const QUuid firstId = QUuid::createUuid();
    const QUuid secondId = QUuid::createUuid();
    const QUuid thirdId = QUuid::createUuid();
    const QUuid fourthId = QUuid::createUuid();
    CameraInfo recoveringCamera{thirdId, {}, QStringLiteral("third"), QStringLiteral("第三路")};
    recoveringCamera.connectivity = 4;
    QList<CameraInfo> cameras{
        CameraInfo{firstId, {}, QStringLiteral("first"), QStringLiteral("第一路")},
        CameraInfo{secondId, {}, QStringLiteral("second"), QStringLiteral("第二路")},
        recoveringCamera,
        CameraInfo{fourthId, {}, QStringLiteral("fourth"), QStringLiteral("第四路")}};

    const bool changed = ViewerLogic::mergeCameraStatuses(
        cameras,
        QList<CameraStatusInfo>{{firstId, 1}, {secondId, 3}, {QUuid::createUuid(), 1}, {fourthId, 99}});

    QVERIFY(changed);
    QCOMPARE(cameras.at(0).connectivity, 1);
    QCOMPARE(cameras.at(1).connectivity, 3);
    QCOMPARE(cameras.at(2).connectivity, 4);
    QCOMPARE(cameras.at(3).connectivity, 0);
    const ViewerLogic::CameraConnectivitySummary summary = ViewerLogic::summarizeCameraConnectivity(cameras);
    QCOMPARE(summary.total, 4);
    QCOMPARE(summary.online, 1);
    QCOMPARE(summary.unavailable, 1);
    QCOMPARE(summary.unknown, 1);
    QCOMPARE(summary.recovering, 1);
    QVERIFY(!ViewerLogic::mergeCameraStatuses(cameras, QList<CameraStatusInfo>{{firstId, 1}, {secondId, 3}}));
}

void ViewerLogicTests::uiSettingsMigrateAndRemainAccountIsolated() {
    QTemporaryDir settingsDirectory;
    QVERIFY(settingsDirectory.isValid());
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsDirectory.path());
    QCoreApplication::setOrganizationName(QStringLiteral("VisiCoreTests"));
    QCoreApplication::setApplicationName(QStringLiteral("ViewerUiSettings"));

    const QString firstPrefix = ViewerUiSettings::accountSettingsPrefix(QStringLiteral("operator-a"));
    {
        QSettings legacy;
        legacy.clear();
        legacy.setValue(firstPrefix + QStringLiteral("geometry"), QByteArray("legacy-geometry"));
        legacy.setValue(firstPrefix + QStringLiteral("splitter"), QByteArray("legacy-splitter"));
        legacy.setValue(firstPrefix + QStringLiteral("previewLayout"), 16);
    }

    ViewerUiSettings first(QStringLiteral("operator-a"));
    QCOMPARE(first.schemaVersion(), ViewerUiSettings::CurrentSchemaVersion);
    QCOMPARE(first.windowGeometry(), QByteArray("legacy-geometry"));
    QVERIFY(QSettings().contains(firstPrefix + QStringLiteral("splitter")));
    QCOMPARE(QSettings().value(firstPrefix + QStringLiteral("previewLayout")).toInt(), 16);

    first.setDockState(WorkspaceMode::Preview, QByteArray("preview-state"));
    first.setDockState(WorkspaceMode::Playback, QByteArray("playback-state"));
    first.setDockLocked(true);

    ViewerUiSettings restored(QStringLiteral("operator-a"));
    QCOMPARE(restored.dockState(WorkspaceMode::Preview), QByteArray("preview-state"));
    QCOMPARE(restored.dockState(WorkspaceMode::Playback), QByteArray("playback-state"));
    QVERIFY(restored.dockLocked());

    ViewerUiSettings other(QStringLiteral("operator-b"));
    QVERIFY(other.dockState(WorkspaceMode::Preview).isEmpty());
    QVERIFY(!other.dockLocked());
}

void ViewerLogicTests::uiSettingsRecoveryBackupOnlyIsolatesLayout() {
    QTemporaryDir settingsDirectory;
    QVERIFY(settingsDirectory.isValid());
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsDirectory.path());
    QCoreApplication::setOrganizationName(QStringLiteral("VisiCoreTests"));
    QCoreApplication::setApplicationName(QStringLiteral("ViewerUiSettingsRecovery"));

    QSettings rawSettings;
    rawSettings.clear();
    const QString username = QStringLiteral("operator-recovery");
    const QString prefix = ViewerUiSettings::accountSettingsPrefix(username);
    ViewerUiSettings settings(username);
    settings.setWindowGeometry(QByteArray("window-geometry"));
    settings.setDockState(WorkspaceMode::Preview, QByteArray("preview-dock"));
    settings.setDockState(WorkspaceMode::Playback, QByteArray("playback-dock"));
    settings.setDockLocked(true);
    rawSettings.setValue(prefix + QStringLiteral("previewLayout"), 64);
    rawSettings.setValue(prefix + QStringLiteral("savedViews"), QByteArray("saved-views"));

    settings.backupAndClearLayoutForRecovery();
    QVERIFY(settings.hasRecoveryLayoutBackup());
    const ViewerUiLayoutSnapshot backup = settings.recoveryLayoutBackup();
    QCOMPARE(backup.windowGeometry, QByteArray("window-geometry"));
    QCOMPARE(backup.previewDockState, QByteArray("preview-dock"));
    QCOMPARE(backup.playbackDockState, QByteArray("playback-dock"));
    QVERIFY(settings.currentLayout().isEmpty());
    QVERIFY(settings.dockLocked());
    QCOMPARE(rawSettings.value(prefix + QStringLiteral("previewLayout")).toInt(), 64);
    QCOMPARE(rawSettings.value(prefix + QStringLiteral("savedViews")).toByteArray(), QByteArray("saved-views"));

    settings.setWindowGeometry(QByteArray("new-geometry"));
    settings.backupAndClearLayoutForRecovery();
    QCOMPARE(settings.recoveryLayoutBackup().windowGeometry, QByteArray("window-geometry"));
    QVERIFY(settings.currentLayout().isEmpty());

    QVERIFY(settings.restoreLayoutFromRecoveryBackup());
    QCOMPARE(settings.windowGeometry(), QByteArray("window-geometry"));
    QCOMPARE(settings.dockState(WorkspaceMode::Preview), QByteArray("preview-dock"));
    QCOMPARE(settings.dockState(WorkspaceMode::Playback), QByteArray("playback-dock"));
    settings.clearRecoveryLayoutBackup();
    QVERIFY(!settings.hasRecoveryLayoutBackup());
}

void ViewerLogicTests::startupStateDetectsUnexpectedExitAndSafeUi() {
    QTemporaryDir settingsDirectory;
    QVERIFY(settingsDirectory.isValid());
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsDirectory.path());
    QCoreApplication::setOrganizationName(QStringLiteral("VisiCoreTests"));
    QCoreApplication::setApplicationName(QStringLiteral("ViewerStartupState"));

    QSettings().clear();
    const ViewerStartupState first = ViewerStartupState::begin(false);
    QCOMPARE(static_cast<int>(first.mode()), static_cast<int>(ViewerStartupMode::Normal));
    QVERIFY(!first.previousRunExitedUnexpectedly());
    QVERIFY(!first.shouldRecoverLayout());

    const ViewerStartupState afterUnexpectedExit = ViewerStartupState::begin(false);
    QCOMPARE(
        static_cast<int>(afterUnexpectedExit.mode()),
        static_cast<int>(ViewerStartupMode::RecoverAfterUnexpectedExit));
    QVERIFY(afterUnexpectedExit.previousRunExitedUnexpectedly());
    QVERIFY(afterUnexpectedExit.shouldRecoverLayout());
    first.markCleanShutdown();
    afterUnexpectedExit.markCleanShutdown();

    const ViewerStartupState afterCleanExit = ViewerStartupState::begin(false);
    QCOMPARE(static_cast<int>(afterCleanExit.mode()), static_cast<int>(ViewerStartupMode::Normal));
    QVERIFY(!afterCleanExit.shouldRecoverLayout());
    afterCleanExit.markCleanShutdown();

    const ViewerStartupState safeUi = ViewerStartupState::begin(true);
    QCOMPARE(static_cast<int>(safeUi.mode()), static_cast<int>(ViewerStartupMode::SafeUi));
    QVERIFY(!safeUi.previousRunExitedUnexpectedly());
    QVERIFY(safeUi.shouldRecoverLayout());
    safeUi.markCleanShutdown();
}

QTEST_MAIN(ViewerLogicTests)

#include "viewer_logic_tests.moc"
