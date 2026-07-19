#pragma once

#include "models.h"

#include <QDateTime>
#include <QJsonObject>
#include <QList>
#include <QString>

namespace ViewerLogic {

struct PlaybackControlSummary {
    bool hasTargets = false;
    bool ready = false;
    bool pending = false;
    bool canPause = false;
    bool canSeek = false;
    bool canChangeSpeed = false;
    bool anyPaused = false;
    bool allPaused = false;
};

struct TimelineView {
    QDateTime startedAt;
    QDateTime endedAt;
};

struct CameraConnectivitySummary {
    int total = 0;
    int online = 0;
    int unknown = 0;
    int unavailable = 0;
    int recovering = 0;
};

QString normalizedStreamMode(const QString &mode);
bool isPasswordChangeRequiredError(const QJsonObject &error);
QDateTime parsePtzCommandLeaseExpiry(const QJsonObject &object);
QString previewProfileForMode(const QString &mode, int layoutCount);
int bestLayoutForCameraCount(int cameraCount, bool playback);
QList<int> orderedWindowIndices(int selectedIndex, int visibleCount);
QList<RecordingSegment> parseRecordingSegments(const QJsonObject &object);
PlaybackControlSummary summarizePlaybackControls(
    const QList<PlaybackTransportInfo> &transports,
    const QList<bool> &sessionReady,
    const QList<bool> &commandsPending);
TimelineView zoomedTimelineView(
    const QDateTime &fullStartedAt,
    const QDateTime &fullEndedAt,
    const QDateTime &anchor,
    double anchorRatio,
    double zoomFactor);
bool mergeCameraStatuses(QList<CameraInfo> &cameras, const QList<CameraStatusInfo> &statuses);
CameraConnectivitySummary summarizeCameraConnectivity(const QList<CameraInfo> &cameras);

}
