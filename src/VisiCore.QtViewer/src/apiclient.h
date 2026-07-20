#pragma once

#include "models.h"

#include <QNetworkAccessManager>
#include <QNetworkRequest>
#include <QObject>
#include <QHash>
#include <QJsonObject>
#include <QQueue>
#include <QSet>
#include <QUrl>

class QNetworkReply;
class QTimer;

class ApiClient final : public QObject {
    Q_OBJECT

public:
    explicit ApiClient(QUrl baseUrl, bool allowInsecureHttp, QObject *parent = nullptr);

    [[nodiscard]] bool isBaseUrlValid() const;
    [[nodiscard]] QUrl baseUrl() const;
    [[nodiscard]] bool allowsInsecureHttp() const;
    bool setBaseUrl(QUrl baseUrl);
    [[nodiscard]] QString username() const;
    [[nodiscard]] bool passwordChangeRequired() const;

    void login(const QString &username, const QString &password);
    void changePassword(const QString &currentPassword, const QString &newPassword);
    void logout();
    void loadCatalog();
    void refreshCameraStatuses();
    void createLiveSession(const QUuid &cameraId, const QString &profile, int slotNumber, const QUuid &requestId);
    void createPlaybackSession(
        const QUuid &cameraId,
        const QDateTime &startedAt,
        const QDateTime &endedAt,
        int slotNumber,
        const QUuid &requestId);
    void cancelStreamSessionRequest(const QUuid &requestId);
    void searchRecordings(
        const QUuid &cameraId,
        const QDateTime &startedAt,
        const QDateTime &endedAt,
        const QUuid &requestId);
    void cancelRecordingSearch(const QUuid &cameraId);
    void controlPlayback(
        const QUuid &sessionId,
        const QString &action,
        const QDateTime &position = {},
        double speed = 0.0);
    void requestPlaybackTransport(const QUuid &sessionId);
    void loadPlaybackExports();
    void createPlaybackExport(const QUuid &cameraId, const QDateTime &startedAt, const QDateTime &endedAt);
    void cancelPlaybackExport(const QUuid &exportId);
    QNetworkReply *downloadPlaybackExport(const QUuid &exportId);
    void requestReconnectTicket(const QUuid &sessionId, const QUuid &requestId);
    void revokeSession(const QUuid &sessionId);
    void requestPtz(const QUuid &cameraId, int action, int motion, int speed = 4);

signals:
    void loginSucceeded(const QString &username);
    void loginFailed(const QString &message);
    void forcedPasswordChangeRequired();
    void passwordChangeSucceeded();
    void passwordChangeFailed(const QString &message);
    void catalogLoaded(const QList<RegionInfo> &regions, const QList<CameraInfo> &cameras);
    void cameraStatusesLoaded(const QList<CameraStatusInfo> &statuses);
    void requestFailed(const QString &operation, const QString &message);
    void streamSessionCreated(const QUuid &requestId, const StreamSessionInfo &session);
    void streamSessionFailed(const QUuid &requestId, const QString &message);
    void streamSessionRenewalFailed(const QUuid &sessionId, const QString &message);
    void recordingSearchCompleted(const QUuid &requestId, const QUuid &cameraId, const QList<RecordingSegment> &segments);
    void recordingSearchFailed(const QUuid &requestId, const QUuid &cameraId, const QString &message);
    void playbackControlQueued(const QUuid &sessionId, const PlaybackTransportInfo &transport);
    void playbackControlFailed(const QUuid &sessionId, const QString &message);
    void playbackTransportRefreshed(const QUuid &sessionId, const PlaybackTransportInfo &transport);
    void playbackExportsLoaded(const QList<PlaybackExportInfo> &exports);
    void playbackExportsFailed(const QString &message);
    void playbackExportCreated(const PlaybackExportInfo &exportInfo);
    void playbackExportCreateFailed(const QString &message);
    void playbackExportCancelled(const QUuid &exportId);
    void playbackExportCancelFailed(const QUuid &exportId, const QString &message);
    void ptzRequestFailed(const QString &message);
    void sessionRevoked(const QUuid &sessionId);
    void sessionRevocationFailed(const QUuid &sessionId, const QString &message);
    void shutdownFinished();

private:
    QNetworkRequest makeRequest(const QString &path) const;
    QString errorMessage(QNetworkReply *reply, const QByteArray &body);
    void handleStreamSessionReply(
        QNetworkReply *reply,
        const QUuid &requestId,
        bool allowReconnect,
        quint64 authGeneration);
    void requestReconnectTicketForGeneration(const QUuid &sessionId, const QUuid &requestId, quint64 authGeneration);
    void pollPlaybackSessionForGeneration(const QUuid &sessionId, const QUuid &requestId, quint64 authGeneration);
    void handlePlaybackSessionState(const QJsonObject &object, const QUuid &requestId, quint64 authGeneration);
    void pollRecordingSearchForGeneration(
        const QUuid &searchId,
        const QUuid &cameraId,
        const QUuid &requestId,
        quint64 authGeneration,
        int attempt);
    void pollPlaybackTransportForGeneration(
        const QUuid &sessionId,
        const QUuid &commandId,
        quint64 authGeneration,
        int attempt,
        const QDateTime &deadline);
    void revokeSessionForGeneration(const QUuid &sessionId, quint64 authGeneration, int attempt);
    void dispatchPtzQueue(const QUuid &cameraId, quint64 authGeneration);
    void acquirePtzLease(const QUuid &cameraId, quint64 authGeneration);
    void sendNextPtzCommand(const QUuid &cameraId, quint64 authGeneration);
    void clearPtzCameraState(const QUuid &cameraId);
    void clearPtzState();
    void clearAuthenticationState();
    void sendLogout(const QNetworkRequest &request, int attempt, quint64 authGeneration);
    void renewActiveSessions();
    PlaybackExportInfo parsePlaybackExport(const QJsonObject &object) const;

    struct ActiveLease {
        QDateTime expiresAt;
        QDateTime renewAt;
    };

    struct PtzLease {
        QUuid id;
        QString token;
        QDateTime expiresAt;
        qint64 sequence = 0;
    };

    struct PtzCommandRequest {
        int action = 0;
        int motion = 0;
        int speed = 4;
    };

    QUrl baseUrl_;
    bool allowInsecureHttp_ = false;
    QNetworkAccessManager network_;
    QString token_;
    QString username_;
    bool passwordChangeRequired_ = false;
    quint64 authGeneration_ = 0;
    QHash<QUuid, ActiveLease> activeLeases_;
    QSet<QUuid> renewalsInFlight_;
    QHash<QUuid, PtzLease> ptzLeases_;
    QHash<QUuid, QQueue<PtzCommandRequest>> ptzCommandQueues_;
    QHash<QUuid, PtzCommandRequest> ptzCommandsInFlight_;
    QSet<QUuid> ptzLeaseRequestsInFlight_;
    QHash<QUuid, QUuid> recordingSearchRequestsByCamera_;
    QHash<QUuid, PlaybackTransportInfo> pendingPlaybackTransports_;
    QSet<QUuid> playbackTransportRefreshesInFlight_;
    QHash<QUuid, QUuid> pendingSessionIdsByRequest_;
    QSet<QUuid> canceledSessionRequests_;
    bool cameraStatusRefreshInFlight_ = false;
    QTimer *leaseTimer_ = nullptr;
};
