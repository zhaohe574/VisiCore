#include "apiclient.h"

#include "viewer_logic.h"

#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QTimer>

#include <algorithm>
#include <utility>

namespace {
QUuid parseId(const QJsonValue &value) {
    return QUuid(value.toString());
}

QDateTime parseDateTime(const QJsonValue &value) {
    return QDateTime::fromString(value.toString(), Qt::ISODate);
}

bool containsChineseText(const QString &value) {
    for (const QChar character : value) {
        const ushort codePoint = character.unicode();
        if (codePoint >= 0x3400 && codePoint <= 0x9fff) {
            return true;
        }
    }
    return false;
}

QString friendlyFailureMessage(const QString &failureKind, const QString &operation) {
    const QString normalized = failureKind.trimmed().toLower();
    if (normalized.isEmpty()) {
        return QStringLiteral("%1未能完成，请稍后重试。").arg(operation);
    }
    if (containsChineseText(failureKind)) {
        return failureKind.trimmed();
    }
    if (normalized.contains(QStringLiteral("validation_required"))) {
        return QStringLiteral("%1暂不可用：设备尚未完成此功能验收，请联系管理员。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("disabled_by_policy"))) {
        return QStringLiteral("管理员尚未启用%1功能，请联系管理员。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("configuration"))) {
        return QStringLiteral("%1服务配置不完整，请联系管理员检查边缘服务。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("camera_missing"))) {
        return QStringLiteral("摄像头未正确登记或已被移除，请刷新设备列表。");
    }
    if (normalized.contains(QStringLiteral("support")) || normalized.contains(QStringLiteral("vendor"))) {
        return QStringLiteral("当前设备或边缘服务不支持%1。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("capacity")) || normalized.contains(QStringLiteral("concurrency")) ||
        normalized.contains(QStringLiteral("quota")) || normalized.contains(QStringLiteral("busy"))) {
        return QStringLiteral("%1资源已达到并发上限，请关闭其他回放窗口后重试。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("timeout")) || normalized.contains(QStringLiteral("deadline"))) {
        return QStringLiteral("%1等待设备响应超时，请稍后重试。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("network")) || normalized.contains(QStringLiteral("socket")) ||
        normalized.contains(QStringLiteral("http")) || normalized.contains(QStringLiteral("ioexception"))) {
        return QStringLiteral("%1暂时无法连接录像机，请检查设备网络后重试。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("invalid")) || normalized.contains(QStringLiteral("payload")) ||
        normalized.contains(QStringLiteral("json"))) {
        return QStringLiteral("%1请求无法处理，请确认客户端、中心和边缘服务版本一致。").arg(operation);
    }
    if (normalized.contains(QStringLiteral("cancel")) || normalized.contains(QStringLiteral("revoked")) ||
        normalized.contains(QStringLiteral("expired")) || normalized.contains(QStringLiteral("lease"))) {
        return QStringLiteral("%1会话已结束，请重新发起操作。").arg(operation);
    }
    return QStringLiteral("%1暂不可用，请稍后重试；若持续失败请联系管理员。").arg(operation);
}

PlaybackTransportInfo parsePlaybackTransport(const QJsonObject &object) {
    const QJsonObject transport = object.value(QStringLiteral("transport")).toObject();
    const QString detail = transport.value(QStringLiteral("detail")).toString();
    return {
        transport.value(QStringLiteral("status")).toString(),
        parseId(transport.value(QStringLiteral("commandId"))),
        transport.value(QStringLiteral("isPaused")).toBool(),
        parseDateTime(transport.value(QStringLiteral("position"))),
        transport.value(QStringLiteral("speed")).toDouble(1.0),
        transport.value(QStringLiteral("canPause")).toBool(false),
        transport.value(QStringLiteral("canSeek")).toBool(false),
        transport.value(QStringLiteral("canChangeSpeed")).toBool(false),
        detail.isEmpty() ? QString{} : friendlyFailureMessage(detail, QStringLiteral("回放控制"))};
}
}

ApiClient::ApiClient(QUrl baseUrl, bool allowInsecureHttp, QObject *parent)
    : QObject(parent), baseUrl_(std::move(baseUrl)), allowInsecureHttp_(allowInsecureHttp) {
    if (!baseUrl_.path().endsWith(u'/')) {
        baseUrl_.setPath(baseUrl_.path() + u'/');
    }
    leaseTimer_ = new QTimer(this);
    leaseTimer_->setInterval(5000);
    connect(leaseTimer_, &QTimer::timeout, this, &ApiClient::renewActiveSessions);
}

bool ApiClient::isBaseUrlValid() const {
    if (!baseUrl_.isValid() || baseUrl_.host().isEmpty()) {
        return false;
    }
    if (baseUrl_.scheme() == QStringLiteral("https")) {
        return true;
    }
    return allowInsecureHttp_ && baseUrl_.scheme() == QStringLiteral("http") &&
           (baseUrl_.host() == QStringLiteral("127.0.0.1") || baseUrl_.host() == QStringLiteral("localhost"));
}

QString ApiClient::username() const {
    return username_;
}

bool ApiClient::passwordChangeRequired() const {
    return passwordChangeRequired_;
}

void ApiClient::login(const QString &username, const QString &password) {
    clearAuthenticationState();
    const quint64 authGeneration = authGeneration_;
    auto request = makeRequest(QStringLiteral("api/v1/auth/login"));
    QJsonObject payload{{QStringLiteral("username"), username}, {QStringLiteral("password"), password}};
    auto *reply = network_.post(request, QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            const int statusCode = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
            if (statusCode == 401) {
                emit loginFailed(QStringLiteral("用户名或密码错误，请检查后重新输入。"));
            } else if (statusCode == 500 || statusCode == 502 || statusCode == 503 || statusCode == 504) {
                emit loginFailed(QStringLiteral("中心账号服务暂不可用，请确认平台数据库已启动后重试。"));
            } else {
                emit loginFailed(errorMessage(reply, body));
            }
            reply->deleteLater();
            return;
        }
        const QJsonObject result = QJsonDocument::fromJson(body).object();
        token_ = result.value(QStringLiteral("accessToken")).toString();
        username_ = result.value(QStringLiteral("username")).toString();
        passwordChangeRequired_ = result.value(QStringLiteral("requiresPasswordChange")).toBool(false);
        if (token_.isEmpty()) {
            emit loginFailed(QStringLiteral("中心 API 未返回会话令牌。"));
        } else {
            emit loginSucceeded(username_);
        }
        reply->deleteLater();
    });
}

void ApiClient::changePassword(const QString &currentPassword, const QString &newPassword) {
    if (token_.isEmpty()) {
        emit passwordChangeFailed(QStringLiteral("当前登录状态已失效，请重新登录。"));
        return;
    }
    if (currentPassword.isEmpty() || newPassword.size() < 12 || newPassword.size() > 256) {
        emit passwordChangeFailed(QStringLiteral("请输入当前密码，新密码长度必须为 12 至 256 位。"));
        return;
    }

    const quint64 authGeneration = authGeneration_;
    const QJsonObject payload{{QStringLiteral("currentPassword"), currentPassword},
                              {QStringLiteral("newPassword"), newPassword}};
    auto *reply = network_.put(
        makeRequest(QStringLiteral("api/v1/auth/password")),
        QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            emit passwordChangeFailed(errorMessage(reply, body));
            reply->deleteLater();
            return;
        }

        reply->deleteLater();
        clearAuthenticationState();
        emit passwordChangeSucceeded();
    });
}

void ApiClient::logout() {
    const bool hasToken = !token_.isEmpty();
    const QNetworkRequest logoutRequest = makeRequest(QStringLiteral("api/v1/auth/logout"));
    clearAuthenticationState();
    const quint64 authGeneration = authGeneration_;
    if (!hasToken) {
        emit shutdownFinished();
        return;
    }
    sendLogout(logoutRequest, 0, authGeneration);
}

void ApiClient::sendLogout(const QNetworkRequest &request, int attempt, quint64 authGeneration) {
    auto *reply = network_.post(request, QByteArray());
    connect(reply, &QNetworkReply::finished, this, [this, reply, request, attempt, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const int statusCode = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        const bool shouldRetry = reply->error() != QNetworkReply::NoError && attempt < 2 &&
                                 (statusCode == 0 || statusCode == 409 || statusCode >= 500);
        reply->deleteLater();
        if (shouldRetry) {
            QTimer::singleShot(150 * (attempt + 1), this, [this, request, attempt, authGeneration]() {
                if (authGeneration == authGeneration_) {
                    sendLogout(request, attempt + 1, authGeneration);
                }
            });
            return;
        }
        emit shutdownFinished();
    });
}

void ApiClient::loadCatalog() {
    const quint64 authGeneration = authGeneration_;
    auto *regionsReply = network_.get(makeRequest(QStringLiteral("api/v1/regions")));
    connect(regionsReply, &QNetworkReply::finished, this, [this, regionsReply, authGeneration]() {
        if (authGeneration != authGeneration_) {
            regionsReply->deleteLater();
            return;
        }
        const QByteArray body = regionsReply->readAll();
        if (regionsReply->error() != QNetworkReply::NoError) {
            emit requestFailed(QStringLiteral("加载区域"), errorMessage(regionsReply, body));
            regionsReply->deleteLater();
            return;
        }
        QList<RegionInfo> regions;
        for (const auto value : QJsonDocument::fromJson(body).array()) {
            const auto object = value.toObject();
            regions.append({parseId(object.value(QStringLiteral("id"))), parseId(object.value(QStringLiteral("parentId"))),
                            object.value(QStringLiteral("code")).toString(), object.value(QStringLiteral("name")).toString()});
        }
        regionsReply->deleteLater();

        auto *camerasReply = network_.get(makeRequest(QStringLiteral("api/v1/cameras")));
        connect(camerasReply, &QNetworkReply::finished, this, [this, camerasReply, regions, authGeneration]() {
            if (authGeneration != authGeneration_) {
                camerasReply->deleteLater();
                return;
            }
            const QByteArray cameraBody = camerasReply->readAll();
            if (camerasReply->error() != QNetworkReply::NoError) {
                emit requestFailed(QStringLiteral("加载摄像头"), errorMessage(camerasReply, cameraBody));
                camerasReply->deleteLater();
                return;
            }
            QList<CameraInfo> cameras;
            for (const auto value : QJsonDocument::fromJson(cameraBody).array()) {
                const auto object = value.toObject();
                cameras.append({parseId(object.value(QStringLiteral("id"))), parseId(object.value(QStringLiteral("regionId"))),
                                object.value(QStringLiteral("code")).toString(), object.value(QStringLiteral("alias")).toString(),
                                object.value(QStringLiteral("supportsPtz")).toBool(), object.value(QStringLiteral("connectivity")).toInt(),
                                object.value(QStringLiteral("canLiveView")).toBool(true),
                                object.value(QStringLiteral("canPlayback")).toBool(false),
                                object.value(QStringLiteral("canControlPtz")).toBool(false)});
            }
            emit catalogLoaded(regions, cameras);
            camerasReply->deleteLater();
        });
    });
}

void ApiClient::refreshCameraStatuses() {
    if (token_.isEmpty() || cameraStatusRefreshInFlight_) {
        return;
    }
    cameraStatusRefreshInFlight_ = true;
    const quint64 authGeneration = authGeneration_;
    auto *reply = network_.get(makeRequest(QStringLiteral("api/v1/cameras")));
    connect(reply, &QNetworkReply::finished, this, [this, reply, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        cameraStatusRefreshInFlight_ = false;
        const QByteArray body = reply->readAll();
        if (reply->error() == QNetworkReply::NoError) {
            QList<CameraStatusInfo> statuses;
            for (const QJsonValue value : QJsonDocument::fromJson(body).array()) {
                const QJsonObject object = value.toObject();
                const QUuid cameraId = parseId(object.value(QStringLiteral("id")));
                if (!cameraId.isNull()) {
                    statuses.append({cameraId, object.value(QStringLiteral("connectivity")).toInt()});
                }
            }
            emit cameraStatusesLoaded(statuses);
        }
        reply->deleteLater();
    });
}

void ApiClient::createLiveSession(const QUuid &cameraId, const QString &profile, int slotNumber, const QUuid &requestId) {
    const quint64 authGeneration = authGeneration_;
    canceledSessionRequests_.remove(requestId);
    pendingSessionIdsByRequest_.remove(requestId);
    const QString path = QStringLiteral("api/v1/cameras/%1/sessions").arg(cameraId.toString(QUuid::WithoutBraces));
    QJsonObject payload{{QStringLiteral("operation"), 1},
                        {QStringLiteral("profile"), profile},
                        {QStringLiteral("slotNumber"), slotNumber},
                        {QStringLiteral("clientRequestId"), requestId.toString(QUuid::WithoutBraces)}};
    auto *reply = network_.post(makeRequest(path), QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, requestId, authGeneration]() {
        handleStreamSessionReply(reply, requestId, true, authGeneration);
    });
}

void ApiClient::createPlaybackSession(
    const QUuid &cameraId,
    const QDateTime &startedAt,
    const QDateTime &endedAt,
    int slotNumber,
    const QUuid &requestId) {
    const quint64 authGeneration = authGeneration_;
    if (cameraId.isNull() || requestId.isNull() || !startedAt.isValid() || !endedAt.isValid() ||
        startedAt >= endedAt || startedAt.secsTo(endedAt) > 31 * 24 * 60 * 60 || slotNumber < 0 || slotNumber > 63) {
        emit streamSessionFailed(requestId, QStringLiteral("回放时间范围或窗格编号无效。"));
        return;
    }
    canceledSessionRequests_.remove(requestId);
    pendingSessionIdsByRequest_.remove(requestId);
    const QString path = QStringLiteral("api/v1/cameras/%1/playback-sessions").arg(cameraId.toString(QUuid::WithoutBraces));
    QJsonObject payload{{QStringLiteral("startedAt"), startedAt.toUTC().toString(Qt::ISODateWithMs)},
                        {QStringLiteral("endedAt"), endedAt.toUTC().toString(Qt::ISODateWithMs)},
                        {QStringLiteral("slotNumber"), slotNumber},
                        {QStringLiteral("clientRequestId"), requestId.toString(QUuid::WithoutBraces)}};
    auto *reply = network_.post(makeRequest(path), QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, requestId, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            if (canceledSessionRequests_.remove(requestId)) {
                pendingSessionIdsByRequest_.remove(requestId);
                pendingPlaybackTransports_.remove(requestId);
                reply->deleteLater();
                return;
            }
            emit streamSessionFailed(requestId, errorMessage(reply, body));
            reply->deleteLater();
            return;
        }
        handlePlaybackSessionState(QJsonDocument::fromJson(body).object(), requestId, authGeneration);
        reply->deleteLater();
    });
}

void ApiClient::cancelStreamSessionRequest(const QUuid &requestId) {
    if (requestId.isNull()) {
        return;
    }
    canceledSessionRequests_.insert(requestId);
    const QUuid sessionId = pendingSessionIdsByRequest_.take(requestId);
    pendingPlaybackTransports_.remove(requestId);
    if (!sessionId.isNull()) {
        revokeSession(sessionId);
    }
}

void ApiClient::searchRecordings(
    const QUuid &cameraId,
    const QDateTime &startedAt,
    const QDateTime &endedAt,
    const QUuid &requestId) {
    const quint64 authGeneration = authGeneration_;
    if (cameraId.isNull() || requestId.isNull() || !startedAt.isValid() || !endedAt.isValid() ||
        startedAt >= endedAt || startedAt.secsTo(endedAt) > 31 * 24 * 60 * 60) {
        emit recordingSearchFailed(requestId, cameraId, QStringLiteral("录像检索时间范围无效。"));
        return;
    }
    recordingSearchRequestsByCamera_.insert(cameraId, requestId);
    const QString path = QStringLiteral("api/v1/cameras/%1/recording-searches").arg(cameraId.toString(QUuid::WithoutBraces));
    QJsonObject payload{{QStringLiteral("startedAt"), startedAt.toUTC().toString(Qt::ISODateWithMs)},
                        {QStringLiteral("endedAt"), endedAt.toUTC().toString(Qt::ISODateWithMs)},
                        {QStringLiteral("maxResults"), 200},
                        {QStringLiteral("clientRequestId"), requestId.toString(QUuid::WithoutBraces)}};
    auto *reply = network_.post(makeRequest(path), QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, cameraId, requestId, authGeneration]() {
        if (authGeneration != authGeneration_ || recordingSearchRequestsByCamera_.value(cameraId) != requestId) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            recordingSearchRequestsByCamera_.remove(cameraId);
            emit recordingSearchFailed(requestId, cameraId, errorMessage(reply, body));
            reply->deleteLater();
            return;
        }
        const QUuid searchId = parseId(QJsonDocument::fromJson(body).object().value(QStringLiteral("id")));
        if (searchId.isNull()) {
            recordingSearchRequestsByCamera_.remove(cameraId);
            emit recordingSearchFailed(requestId, cameraId, QStringLiteral("中心 API 未返回录像检索编号。"));
        } else {
            pollRecordingSearchForGeneration(searchId, cameraId, requestId, authGeneration, 0);
        }
        reply->deleteLater();
    });
}

void ApiClient::cancelRecordingSearch(const QUuid &cameraId) {
    recordingSearchRequestsByCamera_.remove(cameraId);
}

void ApiClient::pollRecordingSearchForGeneration(
    const QUuid &searchId,
    const QUuid &cameraId,
    const QUuid &requestId,
    quint64 authGeneration,
    int attempt) {
    if (authGeneration != authGeneration_ || recordingSearchRequestsByCamera_.value(cameraId) != requestId) {
        return;
    }
    const QString path = QStringLiteral("api/v1/recording-searches/%1").arg(searchId.toString(QUuid::WithoutBraces));
    auto *reply = network_.get(makeRequest(path));
    connect(reply, &QNetworkReply::finished, this, [this, reply, searchId, cameraId, requestId, authGeneration, attempt]() {
        if (authGeneration != authGeneration_ || recordingSearchRequestsByCamera_.value(cameraId) != requestId) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            recordingSearchRequestsByCamera_.remove(cameraId);
            emit recordingSearchFailed(requestId, cameraId, errorMessage(reply, body));
            reply->deleteLater();
            return;
        }
        const QJsonObject object = QJsonDocument::fromJson(body).object();
        const QString status = object.value(QStringLiteral("status")).toString();
        const QDateTime expiresAt = parseDateTime(object.value(QStringLiteral("expiresAt")));
        if (expiresAt.isValid() && expiresAt <= QDateTime::currentDateTimeUtc()) {
            recordingSearchRequestsByCamera_.remove(cameraId);
            emit recordingSearchFailed(requestId, cameraId, QStringLiteral("录像检索结果已过期，请重新检索。"));
            reply->deleteLater();
            return;
        }
        if (status == QStringLiteral("Completed")) {
            const QList<RecordingSegment> segments = ViewerLogic::parseRecordingSegments(object);
            recordingSearchRequestsByCamera_.remove(cameraId);
            emit recordingSearchCompleted(requestId, cameraId, segments);
        } else if (status == QStringLiteral("Queued") || status == QStringLiteral("Running") || status == QStringLiteral("Pending")) {
            const int delayMilliseconds = std::min(1500, 450 + attempt * 50);
            QTimer::singleShot(delayMilliseconds, this, [this, searchId, cameraId, requestId, authGeneration, attempt]() {
                pollRecordingSearchForGeneration(searchId, cameraId, requestId, authGeneration, attempt + 1);
            });
        } else {
            recordingSearchRequestsByCamera_.remove(cameraId);
            const QString failureKind = object.value(QStringLiteral("failureKind")).toString();
            emit recordingSearchFailed(
                requestId,
                cameraId,
                friendlyFailureMessage(failureKind, QStringLiteral("录像检索")));
        }
        reply->deleteLater();
    });
}

void ApiClient::controlPlayback(
    const QUuid &sessionId,
    const QString &action,
    const QDateTime &position,
    double speed) {
    const quint64 authGeneration = authGeneration_;
    const QHash<QString, int> actions{
        {QStringLiteral("Pause"), 0},
        {QStringLiteral("Resume"), 1},
        {QStringLiteral("Seek"), 2},
        {QStringLiteral("SetSpeed"), 3}};
    if (sessionId.isNull() || !actions.contains(action)) {
        emit playbackControlFailed(sessionId, QStringLiteral("回放控制参数无效。"));
        return;
    }
    QJsonObject payload{{QStringLiteral("action"), actions.value(action)},
                        {QStringLiteral("clientRequestId"), QUuid::createUuid().toString(QUuid::WithoutBraces)}};
    if (position.isValid()) {
        payload.insert(QStringLiteral("position"), position.toUTC().toString(Qt::ISODateWithMs));
    }
    if (speed > 0.0) {
        payload.insert(QStringLiteral("speed"), speed);
    }
    const QString path = QStringLiteral("api/v1/playback-sessions/%1/controls").arg(sessionId.toString(QUuid::WithoutBraces));
    auto *reply = network_.post(makeRequest(path), QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, sessionId, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            emit playbackControlFailed(sessionId, errorMessage(reply, body));
        } else {
            const QJsonObject object = QJsonDocument::fromJson(body).object();
            const PlaybackTransportInfo transport = parsePlaybackTransport(object);
            if (transport.commandId.isNull()) {
                emit playbackControlFailed(sessionId, QStringLiteral("中心 API 未返回回放控制命令编号。"));
            } else if (transport.status == QStringLiteral("Pending")) {
                const QDateTime deadline = QDateTime::currentDateTimeUtc().addSecs(30);
                QTimer::singleShot(350, this, [this, sessionId, commandId = transport.commandId, authGeneration, deadline]() {
                    pollPlaybackTransportForGeneration(sessionId, commandId, authGeneration, 0, deadline);
                });
            } else if (transport.status == QStringLiteral("Ready")) {
                emit playbackControlQueued(sessionId, transport);
            } else {
                emit playbackControlFailed(
                    sessionId,
                    friendlyFailureMessage(transport.detail, QStringLiteral("回放控制")));
            }
        }
        reply->deleteLater();
    });
}

void ApiClient::requestPlaybackTransport(const QUuid &sessionId) {
    if (sessionId.isNull() || !activeLeases_.contains(sessionId) || playbackTransportRefreshesInFlight_.contains(sessionId)) {
        return;
    }
    playbackTransportRefreshesInFlight_.insert(sessionId);
    const quint64 authGeneration = authGeneration_;
    const QString path = QStringLiteral("api/v1/playback-sessions/%1").arg(sessionId.toString(QUuid::WithoutBraces));
    auto *reply = network_.get(makeRequest(path));
    connect(reply, &QNetworkReply::finished, this, [this, reply, sessionId, authGeneration]() {
        playbackTransportRefreshesInFlight_.remove(sessionId);
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() == QNetworkReply::NoError && activeLeases_.contains(sessionId)) {
            const PlaybackTransportInfo transport = parsePlaybackTransport(QJsonDocument::fromJson(body).object());
            if (transport.status == QStringLiteral("Ready")) {
                emit playbackTransportRefreshed(sessionId, transport);
            }
        }
        reply->deleteLater();
    });
}

void ApiClient::pollPlaybackTransportForGeneration(
    const QUuid &sessionId,
    const QUuid &commandId,
    quint64 authGeneration,
    int attempt,
    const QDateTime &deadline) {
    if (authGeneration != authGeneration_) {
        return;
    }
    if (!activeLeases_.contains(sessionId)) {
        emit playbackControlFailed(sessionId, QStringLiteral("回放会话已结束，请重新打开录像回放。"));
        return;
    }
    if (attempt >= 40 || deadline <= QDateTime::currentDateTimeUtc()) {
        emit playbackControlFailed(sessionId, QStringLiteral("回放控制等待设备确认超时，请稍后重试。"));
        return;
    }
    const QString path = QStringLiteral("api/v1/playback-sessions/%1").arg(sessionId.toString(QUuid::WithoutBraces));
    auto *reply = network_.get(makeRequest(path));
    connect(reply, &QNetworkReply::finished, this, [this, reply, sessionId, commandId, authGeneration, attempt, deadline]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            emit playbackControlFailed(sessionId, errorMessage(reply, body));
            reply->deleteLater();
            return;
        }
        const QJsonObject object = QJsonDocument::fromJson(body).object();
        const QJsonObject transport = object.value(QStringLiteral("transport")).toObject();
        const QString status = transport.value(QStringLiteral("status")).toString();
        const QUuid returnedCommandId = parseId(transport.value(QStringLiteral("commandId")));
        if (returnedCommandId != commandId) {
            emit playbackControlFailed(sessionId, QStringLiteral("回放控制状态已被后续命令替代。"));
            reply->deleteLater();
            return;
        }
        if (status == QStringLiteral("Pending")) {
            const int delayMilliseconds = std::min(2000, 350 + attempt * 75);
            QTimer::singleShot(delayMilliseconds, this, [this, sessionId, commandId, authGeneration, attempt, deadline]() {
                pollPlaybackTransportForGeneration(sessionId, commandId, authGeneration, attempt + 1, deadline);
            });
        } else if (status == QStringLiteral("Failed")) {
            const QString detail = transport.value(QStringLiteral("detail")).toString();
            emit playbackControlFailed(
                sessionId,
                friendlyFailureMessage(detail, QStringLiteral("回放控制")));
        } else if (status == QStringLiteral("Ready")) {
            emit playbackControlQueued(sessionId, parsePlaybackTransport(object));
        } else {
            emit playbackControlFailed(sessionId, QStringLiteral("中心 API 返回了未知的回放控制状态。"));
        }
        reply->deleteLater();
    });
}

void ApiClient::handleStreamSessionReply(
    QNetworkReply *reply,
    const QUuid &requestId,
    bool allowReconnect,
    quint64 authGeneration) {
    if (authGeneration != authGeneration_) {
        reply->deleteLater();
        return;
    }
    const QByteArray body = reply->readAll();
    const QJsonObject object = QJsonDocument::fromJson(body).object();
    QUuid returnedSessionId = parseId(object.value(QStringLiteral("id")));
    if (returnedSessionId.isNull()) {
        returnedSessionId = parseId(object.value(QStringLiteral("sessionId")));
    }
    if (returnedSessionId.isNull()) {
        returnedSessionId = pendingSessionIdsByRequest_.value(requestId);
    }
    if (canceledSessionRequests_.remove(requestId)) {
        pendingPlaybackTransports_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        if (!returnedSessionId.isNull()) {
            revokeSession(returnedSessionId);
        }
        reply->deleteLater();
        return;
    }
    if (reply->error() != QNetworkReply::NoError) {
        pendingPlaybackTransports_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        const int statusCode = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        const QUuid existingSessionId = parseId(object.value(QStringLiteral("sessionId")));
        if (allowReconnect && statusCode == 409 && !existingSessionId.isNull()) {
            reply->deleteLater();
            requestReconnectTicketForGeneration(existingSessionId, requestId, authGeneration);
            return;
        }
        emit streamSessionFailed(requestId, errorMessage(reply, body));
        reply->deleteLater();
        return;
    }

    StreamSessionInfo session{
        parseId(object.value(QStringLiteral("id"))),
        QUrl(object.value(QStringLiteral("gatewayUri")).toString()),
        QDateTime::fromString(object.value(QStringLiteral("ticketExpiresAt")).toString(), Qt::ISODate),
        QDateTime::fromString(object.value(QStringLiteral("leaseExpiresAt")).toString(), Qt::ISODate),
        object.value(QStringLiteral("renewAfterSeconds")).toInt(),
        false,
        {}};
    if (pendingPlaybackTransports_.contains(requestId)) {
        session.hasPlaybackTransport = true;
        session.playbackTransport = pendingPlaybackTransports_.take(requestId);
    }
    if (session.id.isNull() || !session.gatewayUri.isValid() || session.gatewayUri.isEmpty() ||
        !session.ticketExpiresAt.isValid() || !session.leaseExpiresAt.isValid() || session.renewAfterSeconds < 15) {
        if (!session.id.isNull()) revokeSession(session.id);
        pendingPlaybackTransports_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        emit streamSessionFailed(requestId, QStringLiteral("中心 API 返回的流会话字段不完整。"));
    } else {
        pendingSessionIdsByRequest_.remove(requestId);
        const QDateTime now = QDateTime::currentDateTimeUtc();
        activeLeases_.insert(session.id, {session.leaseExpiresAt, now.addSecs(session.renewAfterSeconds)});
        if (!leaseTimer_->isActive()) leaseTimer_->start();
        emit streamSessionCreated(requestId, session);
    }
    reply->deleteLater();
}

void ApiClient::requestReconnectTicket(const QUuid &sessionId, const QUuid &requestId) {
    requestReconnectTicketForGeneration(sessionId, requestId, authGeneration_);
}

void ApiClient::requestReconnectTicketForGeneration(const QUuid &sessionId, const QUuid &requestId, quint64 authGeneration) {
    if (authGeneration != authGeneration_) {
        return;
    }
    if (canceledSessionRequests_.contains(requestId)) {
        canceledSessionRequests_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        revokeSession(sessionId);
        return;
    }
    pendingSessionIdsByRequest_.insert(requestId, sessionId);
    const QString path = QStringLiteral("api/v1/cameras/sessions/%1/tickets").arg(sessionId.toString(QUuid::WithoutBraces));
    auto *reply = network_.post(makeRequest(path), QByteArrayLiteral("{}"));
    connect(reply, &QNetworkReply::finished, this, [this, reply, requestId, authGeneration]() {
        handleStreamSessionReply(reply, requestId, false, authGeneration);
    });
}

void ApiClient::pollPlaybackSessionForGeneration(const QUuid &sessionId, const QUuid &requestId, quint64 authGeneration) {
    if (authGeneration != authGeneration_) {
        return;
    }
    if (canceledSessionRequests_.contains(requestId)) {
        canceledSessionRequests_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        revokeSession(sessionId);
        return;
    }
    const QString path = QStringLiteral("api/v1/playback-sessions/%1").arg(sessionId.toString(QUuid::WithoutBraces));
    auto *reply = network_.get(makeRequest(path));
    connect(reply, &QNetworkReply::finished, this, [this, reply, sessionId, requestId, authGeneration]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        if (canceledSessionRequests_.contains(requestId)) {
            canceledSessionRequests_.remove(requestId);
            pendingSessionIdsByRequest_.remove(requestId);
            revokeSession(sessionId);
            reply->deleteLater();
            return;
        }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            emit streamSessionFailed(requestId, errorMessage(reply, body));
            reply->deleteLater();
            return;
        }
        handlePlaybackSessionState(QJsonDocument::fromJson(body).object(), requestId, authGeneration);
        reply->deleteLater();
    });
}

void ApiClient::handlePlaybackSessionState(const QJsonObject &object, const QUuid &requestId, quint64 authGeneration) {
    if (authGeneration != authGeneration_) {
        return;
    }
    const QUuid sessionId = parseId(object.value(QStringLiteral("id")));
    const QString status = object.value(QStringLiteral("status")).toString();
    const QDateTime leaseExpiresAt = QDateTime::fromString(
        object.value(QStringLiteral("leaseExpiresAt")).toString(), Qt::ISODate);
    if (!sessionId.isNull()) {
        pendingSessionIdsByRequest_.insert(requestId, sessionId);
    }
    if (canceledSessionRequests_.remove(requestId)) {
        pendingPlaybackTransports_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        if (!sessionId.isNull()) revokeSession(sessionId);
        return;
    }
    if (sessionId.isNull() || !leaseExpiresAt.isValid()) {
        pendingPlaybackTransports_.remove(requestId);
        pendingSessionIdsByRequest_.remove(requestId);
        if (!sessionId.isNull()) revokeSession(sessionId);
        emit streamSessionFailed(requestId, QStringLiteral("中心 API 返回的回放会话字段不完整。"));
        return;
    }
    if (status == QStringLiteral("Ready")) {
        pendingPlaybackTransports_.insert(requestId, parsePlaybackTransport(object));
        requestReconnectTicketForGeneration(sessionId, requestId, authGeneration);
        return;
    }
    if (status == QStringLiteral("Pending")) {
        if (leaseExpiresAt <= QDateTime::currentDateTimeUtc()) {
            pendingPlaybackTransports_.remove(requestId);
            pendingSessionIdsByRequest_.remove(requestId);
            revokeSession(sessionId);
            emit streamSessionFailed(requestId, QStringLiteral("回放中继在租约内未就绪。"));
            return;
        }
        QTimer::singleShot(500, this, [this, sessionId, requestId, authGeneration]() {
            pollPlaybackSessionForGeneration(sessionId, requestId, authGeneration);
        });
        return;
    }
    const QString failureKind = object.value(QStringLiteral("failureKind")).toString();
    pendingPlaybackTransports_.remove(requestId);
    pendingSessionIdsByRequest_.remove(requestId);
    revokeSession(sessionId);
    emit streamSessionFailed(
        requestId,
        friendlyFailureMessage(failureKind, QStringLiteral("录像回放")));
}

void ApiClient::revokeSession(const QUuid &sessionId) {
    activeLeases_.remove(sessionId);
    renewalsInFlight_.remove(sessionId);
    revokeSessionForGeneration(sessionId, authGeneration_, 0);
}

void ApiClient::revokeSessionForGeneration(const QUuid &sessionId, quint64 authGeneration, int attempt) {
    if (sessionId.isNull() || authGeneration != authGeneration_) return;
    const QString path = QStringLiteral("api/v1/cameras/sessions/%1").arg(sessionId.toString(QUuid::WithoutBraces));
    auto *reply = network_.deleteResource(makeRequest(path));
    connect(reply, &QNetworkReply::finished, this, [this, reply, sessionId, authGeneration, attempt]() {
        if (authGeneration != authGeneration_) {
            reply->deleteLater();
            return;
        }
        const int statusCode = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        const bool succeeded = reply->error() == QNetworkReply::NoError || statusCode == 404;
        const bool shouldRetry = !succeeded && attempt < 2 &&
                                 (statusCode == 0 || statusCode == 409 || statusCode >= 500);
        const QString failureMessage = succeeded ? QString{} : errorMessage(reply, reply->readAll());
        reply->deleteLater();
        if (succeeded) {
            emit sessionRevoked(sessionId);
        } else if (shouldRetry) {
            QTimer::singleShot(200 * (attempt + 1), this, [this, sessionId, authGeneration, attempt]() {
                revokeSessionForGeneration(sessionId, authGeneration, attempt + 1);
            });
        } else {
            emit sessionRevocationFailed(
                sessionId,
                failureMessage.isEmpty() ? QStringLiteral("播放会话释放失败，将在租约到期后自动回收。") : failureMessage);
        }
    });
}

void ApiClient::requestPtz(const QUuid &cameraId, int action, int motion, int speed) {
    const quint64 authGeneration = authGeneration_;
    if (cameraId.isNull() || action < 0 || action > 13 || (motion != 0 && motion != 1) || speed < 1 || speed > 7) {
        emit ptzRequestFailed(QStringLiteral("云台控制命令参数无效。"));
        return;
    }
    auto &queue = ptzCommandQueues_[cameraId];
    if (motion == 0) {
        const auto inFlight = ptzCommandsInFlight_.constFind(cameraId);
        if (inFlight != ptzCommandsInFlight_.cend() && inFlight->action == action && inFlight->motion == 0 && inFlight->speed == speed) {
            return;
        }
        if (!queue.isEmpty() && queue.back().action == action && queue.back().motion == 0 && queue.back().speed == speed) {
            return;
        }
        queue.enqueue({action, motion, speed});
    } else {
        QQueue<PtzCommandRequest> retained;
        while (!queue.isEmpty()) {
            const PtzCommandRequest pending = queue.dequeue();
            if (pending.action != action || pending.motion != 0) {
                retained.enqueue(pending);
            }
        }
        queue = std::move(retained);
        const auto inFlight = ptzCommandsInFlight_.constFind(cameraId);
        const bool startInFlight = inFlight != ptzCommandsInFlight_.cend() &&
                                   inFlight->action == action && inFlight->motion == 0;
        const auto lease = ptzLeases_.constFind(cameraId);
        const bool hasValidLease = lease != ptzLeases_.cend() && lease->expiresAt > QDateTime::currentDateTimeUtc();
        const bool stopAlreadyQueued = !queue.isEmpty() && queue.back().action == action && queue.back().motion == 1;
        if ((startInFlight || hasValidLease) && !stopAlreadyQueued) {
            queue.enqueue({action, motion, 1});
        }
    }
    dispatchPtzQueue(cameraId, authGeneration);
}

void ApiClient::dispatchPtzQueue(const QUuid &cameraId, quint64 authGeneration) {
    if (authGeneration != authGeneration_ || ptzCommandsInFlight_.contains(cameraId)) {
        return;
    }
    auto queue = ptzCommandQueues_.find(cameraId);
    if (queue == ptzCommandQueues_.end() || queue->isEmpty()) {
        ptzCommandQueues_.remove(cameraId);
        return;
    }
    const auto lease = ptzLeases_.constFind(cameraId);
    if (lease == ptzLeases_.cend() || lease->expiresAt <= QDateTime::currentDateTimeUtc()) {
        ptzLeases_.remove(cameraId);
        if (queue->head().motion == 1) {
            queue->dequeue();
            if (queue->isEmpty()) ptzCommandQueues_.remove(cameraId);
            dispatchPtzQueue(cameraId, authGeneration);
            return;
        }
        acquirePtzLease(cameraId, authGeneration);
        return;
    }
    sendNextPtzCommand(cameraId, authGeneration);
}

void ApiClient::acquirePtzLease(const QUuid &cameraId, quint64 authGeneration) {
    if (ptzLeaseRequestsInFlight_.contains(cameraId)) {
        return;
    }
    ptzLeaseRequestsInFlight_.insert(cameraId);
    const QString path = QStringLiteral("api/v1/cameras/%1/ptz/leases").arg(cameraId.toString(QUuid::WithoutBraces));
    auto *reply = network_.post(makeRequest(path), QByteArrayLiteral("{}"));
    connect(reply, &QNetworkReply::finished, this, [this, reply, cameraId, authGeneration]() {
        ptzLeaseRequestsInFlight_.remove(cameraId);
        if (authGeneration != authGeneration_) { reply->deleteLater(); return; }
        const QByteArray body = reply->readAll();
        const QJsonObject object = QJsonDocument::fromJson(body).object();
        if (reply->error() != QNetworkReply::NoError) {
            clearPtzCameraState(cameraId);
            emit ptzRequestFailed(errorMessage(reply, body));
            reply->deleteLater();
            return;
        }
        PtzLease lease{parseId(object.value(QStringLiteral("leaseId"))), object.value(QStringLiteral("leaseToken")).toString(),
                       QDateTime::fromString(object.value(QStringLiteral("expiresAt")).toString(), Qt::ISODate),
                       object.value(QStringLiteral("lastSequence")).toInteger()};
        if (lease.id.isNull() || lease.token.isEmpty() || !lease.expiresAt.isValid()) {
            clearPtzCameraState(cameraId);
            emit ptzRequestFailed(QStringLiteral("中心 API 返回的云台控制租约字段不完整。"));
        } else {
            ptzLeases_.insert(cameraId, lease);
            dispatchPtzQueue(cameraId, authGeneration);
        }
        reply->deleteLater();
    });
}

void ApiClient::sendNextPtzCommand(const QUuid &cameraId, quint64 authGeneration) {
    auto iterator = ptzLeases_.find(cameraId);
    auto queue = ptzCommandQueues_.find(cameraId);
    if (iterator == ptzLeases_.end() || queue == ptzCommandQueues_.end() || queue->isEmpty()) {
        dispatchPtzQueue(cameraId, authGeneration);
        return;
    }
    const PtzCommandRequest command = queue->dequeue();
    if (queue->isEmpty()) {
        ptzCommandQueues_.erase(queue);
    }
    const qint64 nextSequence = iterator->sequence + 1;
    const QString path = QStringLiteral("api/v1/cameras/%1/ptz/commands").arg(cameraId.toString(QUuid::WithoutBraces));
    QJsonObject payload{{QStringLiteral("leaseId"), iterator->id.toString(QUuid::WithoutBraces)},
                        {QStringLiteral("leaseToken"), iterator->token}, {QStringLiteral("action"), command.action},
                        {QStringLiteral("motion"), command.motion}, {QStringLiteral("speed"), command.motion == 0 ? command.speed : 1},
                        {QStringLiteral("sequence"), nextSequence}};
    ptzCommandsInFlight_.insert(cameraId, command);
    auto *reply = network_.post(makeRequest(path), QJsonDocument(payload).toJson(QJsonDocument::Compact));
    connect(reply, &QNetworkReply::finished, this, [this, reply, cameraId, nextSequence, authGeneration]() {
        ptzCommandsInFlight_.remove(cameraId);
        if (authGeneration != authGeneration_) { reply->deleteLater(); return; }
        const QByteArray body = reply->readAll();
        if (reply->error() != QNetworkReply::NoError) {
            clearPtzCameraState(cameraId);
            emit ptzRequestFailed(errorMessage(reply, body));
        } else if (auto iterator = ptzLeases_.find(cameraId); iterator != ptzLeases_.end()) {
            const QJsonObject result = QJsonDocument::fromJson(body).object();
            const qint64 lastSequence = result.value(QStringLiteral("lastSequence")).toInteger(-1);
            const QDateTime expiresAt = ViewerLogic::parsePtzCommandLeaseExpiry(result);
            if (lastSequence < nextSequence || !expiresAt.isValid()) {
                clearPtzCameraState(cameraId);
                emit ptzRequestFailed(QStringLiteral("中心 API 返回的云台控制命令确认无效。"));
            } else {
                iterator->sequence = lastSequence;
                iterator->expiresAt = expiresAt;
                dispatchPtzQueue(cameraId, authGeneration);
            }
        }
        reply->deleteLater();
    });
}

void ApiClient::clearPtzCameraState(const QUuid &cameraId) {
    ptzLeases_.remove(cameraId);
    ptzCommandQueues_.remove(cameraId);
    ptzCommandsInFlight_.remove(cameraId);
    ptzLeaseRequestsInFlight_.remove(cameraId);
}

void ApiClient::clearPtzState() {
    ptzLeases_.clear();
    ptzCommandQueues_.clear();
    ptzCommandsInFlight_.clear();
    ptzLeaseRequestsInFlight_.clear();
}

void ApiClient::clearAuthenticationState() {
    ++authGeneration_;
    leaseTimer_->stop();
    activeLeases_.clear();
    renewalsInFlight_.clear();
    clearPtzState();
    recordingSearchRequestsByCamera_.clear();
    pendingPlaybackTransports_.clear();
    playbackTransportRefreshesInFlight_.clear();
    pendingSessionIdsByRequest_.clear();
    canceledSessionRequests_.clear();
    cameraStatusRefreshInFlight_ = false;
    token_.clear();
    username_.clear();
    passwordChangeRequired_ = false;
}

void ApiClient::renewActiveSessions() {
    const quint64 authGeneration = authGeneration_;
    const QDateTime now = QDateTime::currentDateTimeUtc();
    const auto sessionIds = activeLeases_.keys();
    for (const QUuid &sessionId : sessionIds) {
        const ActiveLease lease = activeLeases_.value(sessionId);
        if (renewalsInFlight_.contains(sessionId) || lease.renewAt > now) continue;

        renewalsInFlight_.insert(sessionId);
        const QString path = QStringLiteral("api/v1/cameras/sessions/%1/renew").arg(sessionId.toString(QUuid::WithoutBraces));
        auto *reply = network_.post(makeRequest(path), QByteArrayLiteral("{}"));
        connect(reply, &QNetworkReply::finished, this, [this, reply, sessionId, authGeneration]() {
            if (authGeneration != authGeneration_) {
                reply->deleteLater();
                return;
            }
            renewalsInFlight_.remove(sessionId);
            const QByteArray body = reply->readAll();
            if (!activeLeases_.contains(sessionId)) {
                reply->deleteLater();
                return;
            }
            if (reply->error() != QNetworkReply::NoError) {
                const ActiveLease lease = activeLeases_.value(sessionId);
                const QString message = errorMessage(reply, body);
                if (QDateTime::currentDateTimeUtc().addSecs(10) >= lease.expiresAt) {
                    activeLeases_.remove(sessionId);
                    emit streamSessionRenewalFailed(sessionId, message);
                } else {
                    activeLeases_[sessionId].renewAt = QDateTime::currentDateTimeUtc().addSecs(5);
                }
                reply->deleteLater();
                return;
            }

            const QJsonObject object = QJsonDocument::fromJson(body).object();
            const QDateTime expiresAt = QDateTime::fromString(object.value(QStringLiteral("leaseExpiresAt")).toString(), Qt::ISODate);
            const int renewAfterSeconds = object.value(QStringLiteral("renewAfterSeconds")).toInt();
            if (!expiresAt.isValid() || renewAfterSeconds < 15) {
                activeLeases_.remove(sessionId);
                emit streamSessionRenewalFailed(sessionId, QStringLiteral("中心 API 返回的续租结果无效。"));
            } else {
                activeLeases_[sessionId] = {expiresAt, QDateTime::currentDateTimeUtc().addSecs(renewAfterSeconds)};
            }
            reply->deleteLater();
        });
    }
    if (activeLeases_.isEmpty()) leaseTimer_->stop();
}

QNetworkRequest ApiClient::makeRequest(const QString &path) const {
    QNetworkRequest request(baseUrl_.resolved(QUrl(path)));
    request.setTransferTimeout(15000);
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    request.setRawHeader("Accept", "application/json");
    if (!token_.isEmpty()) {
        request.setRawHeader("Authorization", QByteArrayLiteral("Bearer ") + token_.toUtf8());
    }
    return request;
}

QString ApiClient::errorMessage(QNetworkReply *reply, const QByteArray &body) {
    const QJsonObject error = QJsonDocument::fromJson(body).object();
    const QString message = error.value(QStringLiteral("message")).toString();
    const QString detail = error.value(QStringLiteral("detail")).toString();
    const QString title = error.value(QStringLiteral("title")).toString();
    if (ViewerLogic::isPasswordChangeRequiredError(error)) {
        if (!passwordChangeRequired_) {
            passwordChangeRequired_ = true;
            emit forcedPasswordChangeRequired();
        }
        return containsChineseText(message)
            ? message
            : QStringLiteral("首次登录或密码重置后必须修改密码。");
    }
    if (containsChineseText(message)) return message;
    const QJsonObject errors = error.value(QStringLiteral("errors")).toObject();
    for (auto iterator = errors.constBegin(); iterator != errors.constEnd(); ++iterator) {
        const QJsonArray messages = iterator.value().toArray();
        if (!messages.isEmpty()) {
            const QString validationMessage = messages.first().toString();
            if (containsChineseText(validationMessage)) return validationMessage;
        }
    }
    if (containsChineseText(detail)) return detail;
    if (containsChineseText(title)) return title;

    const int statusCode = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
    if (reply->error() == QNetworkReply::TimeoutError) {
        return QStringLiteral("连接中心 API 超时，请检查网络后重试。");
    }
    if (reply->error() == QNetworkReply::ConnectionRefusedError ||
        reply->error() == QNetworkReply::HostNotFoundError ||
        reply->error() == QNetworkReply::NetworkSessionFailedError) {
        return QStringLiteral("无法连接中心 API，请确认服务已启动且地址可访问。");
    }
    if (statusCode == 401) {
        return QStringLiteral("登录状态已失效，请重新登录。");
    }
    if (statusCode == 403) {
        return QStringLiteral("当前账号没有执行此操作的权限，请联系管理员。");
    }
    if (statusCode == 404) {
        return QStringLiteral("请求的资源不存在或功能版本不匹配，请刷新后重试。");
    }
    if (statusCode == 429) {
        return QStringLiteral("操作过于频繁，请稍后再试。");
    }
    if (statusCode == 400 || statusCode == 422) {
        return QStringLiteral("请求内容无法处理，请检查时间范围和操作选项后重试。");
    }
    if (statusCode == 409) {
        return QStringLiteral("当前资源状态已变化，请刷新后重试。");
    }
    if (statusCode == 410) {
        return QStringLiteral("当前会话已结束，请重新发起操作。");
    }
    if (statusCode >= 500) {
        return QStringLiteral("中心 API 暂时不可用，请稍后重试。");
    }
    return statusCode > 0
               ? QStringLiteral("请求未能完成，请检查输入或刷新页面后重试。")
               : QStringLiteral("请求未能完成，请检查网络连接后重试。");
}
