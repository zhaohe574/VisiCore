#include "viewer_startup_state.h"

#include <QDateTime>
#include <QSettings>
#include <QUuid>

namespace {
const QString ActiveSessionKey = QStringLiteral("viewerRuntime/startup/activeSessionId");
const QString LastStartedAtUtcKey = QStringLiteral("viewerRuntime/startup/lastStartedAtUtc");
const QString LastCleanExitAtUtcKey = QStringLiteral("viewerRuntime/startup/lastCleanExitAtUtc");
}

ViewerStartupState ViewerStartupState::begin(bool safeUiRequested) {
    QSettings settings;
    const bool previousRunExitedUnexpectedly =
        !settings.value(ActiveSessionKey).toString().isEmpty();
    const QString sessionId = QUuid::createUuid().toString(QUuid::WithoutBraces);
    settings.setValue(ActiveSessionKey, sessionId);
    settings.setValue(
        LastStartedAtUtcKey,
        QDateTime::currentDateTimeUtc().toString(Qt::ISODateWithMs));
    settings.sync();
    return ViewerStartupState(safeUiRequested, previousRunExitedUnexpectedly, sessionId);
}

ViewerStartupState::ViewerStartupState(
    bool safeUiRequested,
    bool previousRunExitedUnexpectedly,
    QString sessionId)
    : safeUiRequested_(safeUiRequested)
    , previousRunExitedUnexpectedly_(previousRunExitedUnexpectedly)
    , sessionId_(sessionId) {
}

ViewerStartupMode ViewerStartupState::mode() const {
    if (safeUiRequested_) {
        return ViewerStartupMode::SafeUi;
    }
    return previousRunExitedUnexpectedly_
        ? ViewerStartupMode::RecoverAfterUnexpectedExit
        : ViewerStartupMode::Normal;
}

bool ViewerStartupState::previousRunExitedUnexpectedly() const {
    return previousRunExitedUnexpectedly_;
}

bool ViewerStartupState::shouldRecoverLayout() const {
    return safeUiRequested_ || previousRunExitedUnexpectedly_;
}

void ViewerStartupState::markCleanShutdown() const {
    if (sessionId_.isEmpty()) {
        return;
    }

    QSettings settings;
    if (settings.value(ActiveSessionKey).toString() != sessionId_) {
        return;
    }
    settings.remove(ActiveSessionKey);
    settings.setValue(
        LastCleanExitAtUtcKey,
        QDateTime::currentDateTimeUtc().toString(Qt::ISODateWithMs));
    settings.sync();
}
