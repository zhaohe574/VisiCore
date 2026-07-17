#include "viewer_logic.h"

#include <QJsonArray>
#include <QJsonValue>
#include <QHash>

#include <algorithm>

namespace ViewerLogic {

bool isPasswordChangeRequiredError(const QJsonObject &error) {
    return error.value(QStringLiteral("code")).toString() == QStringLiteral("password_change_required");
}

QDateTime parsePtzCommandLeaseExpiry(const QJsonObject &object) {
    return QDateTime::fromString(
        object.value(QStringLiteral("leaseExpiresAt")).toString(),
        Qt::ISODate);
}

QString normalizedStreamMode(const QString &mode) {
    const QString normalized = mode.trimmed().toLower();
    return normalized == QStringLiteral("main") || normalized == QStringLiteral("sub")
        ? normalized
        : QStringLiteral("auto");
}

QString previewProfileForMode(const QString &mode, int layoutCount) {
    const QString normalized = normalizedStreamMode(mode);
    if (normalized != QStringLiteral("auto")) {
        return normalized;
    }
    return layoutCount >= 9 ? QStringLiteral("sub") : QStringLiteral("main");
}

int bestLayoutForCameraCount(int cameraCount, bool playback) {
    const QList<int> layouts = playback
        ? QList<int>{1, 4}
        : QList<int>{1, 4, 9, 16, 25, 36, 64};
    const int required = std::max(1, cameraCount);
    for (const int layout : layouts) {
        if (layout >= required) {
            return layout;
        }
    }
    return layouts.constLast();
}

QList<int> orderedWindowIndices(int selectedIndex, int visibleCount) {
    QList<int> result;
    if (visibleCount <= 0) {
        return result;
    }
    const int start = std::clamp(selectedIndex, 0, visibleCount - 1);
    result.reserve(visibleCount);
    for (int offset = 0; offset < visibleCount; ++offset) {
        result.append((start + offset) % visibleCount);
    }
    return result;
}

QList<RecordingSegment> parseRecordingSegments(const QJsonObject &object) {
    QList<RecordingSegment> result;
    QJsonArray values = object.value(QStringLiteral("segments")).toArray();
    const QJsonObject legacyResult = object.value(QStringLiteral("result")).toObject();
    if (values.isEmpty()) {
        values = legacyResult.value(QStringLiteral("segments")).toArray();
    }
    const bool resultApproximate = object.value(QStringLiteral("coverageApproximate")).toBool(
        legacyResult.value(QStringLiteral("coverageApproximate")).toBool(false));
    for (const QJsonValue &value : values) {
        const QJsonObject segment = value.toObject();
        const QDateTime startedAt = QDateTime::fromString(
            segment.value(QStringLiteral("startedAt")).toString(), Qt::ISODate);
        const QDateTime endedAt = QDateTime::fromString(
            segment.value(QStringLiteral("endedAt")).toString(), Qt::ISODate);
        if (!startedAt.isValid() || !endedAt.isValid() || startedAt >= endedAt) {
            continue;
        }
        result.append({
            startedAt.toLocalTime(),
            endedAt.toLocalTime(),
            segment.value(QStringLiteral("sizeBytes")).toVariant().toLongLong(),
            segment.value(QStringLiteral("isLocked")).toBool(),
            segment.value(QStringLiteral("segmentType")).toString(
                segment.value(QStringLiteral("fileType")).toVariant().toString()),
            segment.value(QStringLiteral("coverageApproximate")).toBool(resultApproximate)});
    }
    return result;
}

PlaybackControlSummary summarizePlaybackControls(
    const QList<PlaybackTransportInfo> &transports,
    const QList<bool> &sessionReady,
    const QList<bool> &commandsPending) {
    PlaybackControlSummary summary;
    if (transports.isEmpty() || transports.size() != sessionReady.size() || transports.size() != commandsPending.size()) {
        return summary;
    }
    summary.hasTargets = true;
    summary.ready = std::all_of(sessionReady.cbegin(), sessionReady.cend(), [](bool value) { return value; });
    summary.pending = std::any_of(commandsPending.cbegin(), commandsPending.cend(), [](bool value) { return value; });
    summary.canPause = summary.ready && std::all_of(transports.cbegin(), transports.cend(), [](const PlaybackTransportInfo &item) {
        return item.canPause;
    });
    summary.canSeek = summary.ready && std::all_of(transports.cbegin(), transports.cend(), [](const PlaybackTransportInfo &item) {
        return item.canSeek;
    });
    summary.canChangeSpeed = summary.ready && std::all_of(transports.cbegin(), transports.cend(), [](const PlaybackTransportInfo &item) {
        return item.canChangeSpeed;
    });
    summary.anyPaused = std::any_of(transports.cbegin(), transports.cend(), [](const PlaybackTransportInfo &item) {
        return item.isPaused;
    });
    summary.allPaused = std::all_of(transports.cbegin(), transports.cend(), [](const PlaybackTransportInfo &item) {
        return item.isPaused;
    });
    return summary;
}

TimelineView zoomedTimelineView(
    const QDateTime &fullStartedAt,
    const QDateTime &fullEndedAt,
    const QDateTime &anchor,
    double anchorRatio,
    double zoomFactor) {
    if (!fullStartedAt.isValid() || !fullEndedAt.isValid() || fullStartedAt >= fullEndedAt) {
        return {};
    }
    const qint64 fullDuration = fullStartedAt.msecsTo(fullEndedAt);
    const double boundedFactor = std::clamp(zoomFactor, 1.0, 64.0);
    const qint64 visibleDuration = std::clamp(
        static_cast<qint64>(static_cast<double>(fullDuration) / boundedFactor),
        std::min<qint64>(60000, fullDuration),
        fullDuration);
    const QDateTime boundedAnchor = anchor.isValid()
        ? std::clamp(anchor, fullStartedAt, fullEndedAt)
        : fullStartedAt.addMSecs(fullDuration / 2);
    const double boundedRatio = std::clamp(anchorRatio, 0.0, 1.0);
    QDateTime startedAt = boundedAnchor.addMSecs(-static_cast<qint64>(visibleDuration * boundedRatio));
    QDateTime endedAt = startedAt.addMSecs(visibleDuration);
    if (startedAt < fullStartedAt) {
        startedAt = fullStartedAt;
        endedAt = startedAt.addMSecs(visibleDuration);
    }
    if (endedAt > fullEndedAt) {
        endedAt = fullEndedAt;
        startedAt = endedAt.addMSecs(-visibleDuration);
    }
    return {startedAt, endedAt};
}

bool mergeCameraStatuses(QList<CameraInfo> &cameras, const QList<CameraStatusInfo> &statuses) {
    QHash<QUuid, int> connectivityById;
    for (const CameraStatusInfo &status : statuses) {
        if (!status.id.isNull() && status.connectivity >= 0 && status.connectivity <= 4) {
            connectivityById.insert(status.id, status.connectivity);
        }
    }
    bool changed = false;
    for (CameraInfo &camera : cameras) {
        const auto status = connectivityById.constFind(camera.id);
        if (status != connectivityById.cend() && camera.connectivity != status.value()) {
            camera.connectivity = status.value();
            changed = true;
        }
    }
    return changed;
}

CameraConnectivitySummary summarizeCameraConnectivity(const QList<CameraInfo> &cameras) {
    CameraConnectivitySummary summary;
    summary.total = cameras.size();
    for (const CameraInfo &camera : cameras) {
        switch (camera.connectivity) {
            case 1: ++summary.online; break;
            case 2:
            case 3: ++summary.unavailable; break;
            case 4: ++summary.recovering; break;
            default: ++summary.unknown; break;
        }
    }
    return summary;
}

}
