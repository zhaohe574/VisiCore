#pragma once

#include <QDateTime>
#include <QString>
#include <QUrl>
#include <QUuid>

struct RegionInfo {
    QUuid id;
    QUuid parentId;
    QString code;
    QString name;
};

struct CameraInfo {
    QUuid id;
    QUuid regionId;
    QString code;
    QString alias;
    bool supportsPtz = false;
    int connectivity = 0;
    bool canLiveView = false;
    bool canPlayback = false;
    bool canControlPtz = false;
};

struct CameraStatusInfo {
    QUuid id;
    int connectivity = 0;
};

struct RecordingSegment {
    QDateTime startedAt;
    QDateTime endedAt;
    qint64 sizeBytes = 0;
    bool locked = false;
    QString fileType;
    bool approximate = false;
};

struct PlaybackTransportInfo {
    QString status;
    QUuid commandId;
    bool isPaused = false;
    QDateTime position;
    double speed = 1.0;
    bool canPause = false;
    bool canSeek = false;
    bool canChangeSpeed = false;
    QString detail;
};

struct StreamSessionInfo {
    QUuid id;
    QUrl gatewayUri;
    QDateTime ticketExpiresAt;
    QDateTime leaseExpiresAt;
    int renewAfterSeconds = 0;
    bool hasPlaybackTransport = false;
    PlaybackTransportInfo playbackTransport;
};
