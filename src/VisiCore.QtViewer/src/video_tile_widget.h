#pragma once

#include "models.h"

#include <QFrame>
#include <QImage>
#include <QList>
#include <QString>
#include <optional>

class QLabel;
class MpvPlayerWidget;
class QEnterEvent;
class QEvent;
class QStackedLayout;
class QWidget;

class VideoTileWidget final : public QFrame {
    Q_OBJECT

public:
    explicit VideoTileWidget(int index, QWidget *parent = nullptr);
    ~VideoTileWidget() override;

    void assignCamera(const CameraInfo &camera, const QString &profile);
    void markSessionRequested(const QUuid &requestId);
    bool startSession(const StreamSessionInfo &session);
    void failSession(const QString &message);
    void handleReconnectFailure(const QString &message);
    void clearTile();
    void suspendSession();
    void releaseForShutdown();
    void refreshPlaybackStream();
    void setSelected(bool selected);
    void setSyncMember(bool synchronized);
    void setCompact(bool compact);
    void setCommandInteractionEnabled(bool enabled, const QString &unavailableReason = {});
    void requestClear();
    void requestSyncMembershipChange(bool synchronized);
    void updateCameraConnectivity(int connectivity);

    [[nodiscard]] bool isEmpty() const;
    [[nodiscard]] bool isPlaying() const;
    [[nodiscard]] bool hasAllocatedPlayer() const;
    [[nodiscard]] bool isSyncMember() const;
    [[nodiscard]] bool commandInteractionEnabled() const;
    [[nodiscard]] int index() const;
    [[nodiscard]] QString profile() const;
    [[nodiscard]] QUuid requestId() const;
    [[nodiscard]] QUuid sessionId() const;
    [[nodiscard]] std::optional<CameraInfo> camera() const;
    [[nodiscard]] QImage captureFrame();

signals:
    void activated(VideoTileWidget *tile);
    void maximizeRequested(VideoTileWidget *tile);
    void restartRequested(VideoTileWidget *tile);
    void profileChangeRequested(VideoTileWidget *tile, const QString &profile);
    void instantPlaybackRequested(VideoTileWidget *tile, int seconds);
    void screenshotRequested(VideoTileWidget *tile);
    void bookmarkRequested(VideoTileWidget *tile);
    void syncMembershipChanged(VideoTileWidget *tile, bool synchronized);
    void syncMembershipChangeRequested(VideoTileWidget *tile, bool synchronized);
    void clearRequested(VideoTileWidget *tile);
    void cameraIdsDropped(VideoTileWidget *tile, const QList<QUuid> &cameraIds);
    void playbackStateChanged(VideoTileWidget *tile, bool playing);
    void playbackMediaPositionChanged(VideoTileWidget *tile, double seconds);
    void tileCleared(const QUuid &sessionId, const QUuid &cameraId);
    void sessionShouldBeRevoked(const QUuid &sessionId);
    void sessionRequestShouldBeCanceled(const QUuid &requestId);
    void sessionReconnectRequested(const QUuid &sessionId, const QUuid &requestId);

protected:
    void enterEvent(QEnterEvent *event) override;
    void leaveEvent(QEvent *event) override;
    void mousePressEvent(QMouseEvent *event) override;
    void mouseDoubleClickEvent(QMouseEvent *event) override;
    void contextMenuEvent(QContextMenuEvent *event) override;
    void dragEnterEvent(QDragEnterEvent *event) override;
    void dropEvent(QDropEvent *event) override;
    void resizeEvent(QResizeEvent *event) override;

private:
    void handlePlaybackStarted(quint64 playbackGeneration);
    void handlePlaybackEnded(quint64 playbackGeneration);
    void handlePlaybackPositionChanged(quint64 playbackGeneration, double seconds);
    void handlePlaybackBufferingChanged(quint64 playbackGeneration, bool buffering);
    void handlePlaybackError(quint64 playbackGeneration, const QString &message);
    void requestReconnect(const QString &message);
    void updateOverlay(const QString &state);
    void updateHeaderVisibility();
    void updateTitleText();
    void updateProfileLabel();
    void updateVisualState(const QString &tone);
    void setPlaying(bool playing);
    MpvPlayerWidget *ensurePlayer();
    void stopPlayer();
    void releasePlayer();

    int index_;
    QString profile_;
    std::optional<CameraInfo> camera_;
    QUuid requestId_;
    QUuid sessionId_;
    quint64 playbackGeneration_ = 0;
    int reconnectAttempts_ = 0;
    bool syncMember_ = true;
    bool compact_ = false;
    bool playing_ = false;
    bool buffering_ = false;
    bool headerHovered_ = false;
    bool commandInteractionEnabled_ = true;
    QString cameraTitle_;
    QString commandUnavailableReason_;
    MpvPlayerWidget *player_ = nullptr;
    QStackedLayout *stack_ = nullptr;
    QWidget *overlay_ = nullptr;
    QLabel *titleLabel_;
    QLabel *profileLabel_;
    QLabel *stateLabel_;
    QFrame *statusIndicator_ = nullptr;
};
