#include "video_tile_widget.h"

#include "camera_tree_widget.h"
#include "mpv_player_widget.h"

#include <algorithm>
#include <utility>
#include <QAction>
#include <QContextMenuEvent>
#include <QDragEnterEvent>
#include <QDropEvent>
#include <QEnterEvent>
#include <QHBoxLayout>
#include <QLabel>
#include <QMenu>
#include <QMimeData>
#include <QMouseEvent>
#include <QResizeEvent>
#include <QStackedLayout>
#include <QStyle>
#include <QTimer>
#include <QVBoxLayout>

namespace {
constexpr int MaxReconnectAttempts = 6;
}

VideoTileWidget::VideoTileWidget(int index, QWidget *parent) : QFrame(parent), index_(index) {
    setObjectName(QStringLiteral("videoTile"));
    setProperty("selected", false);
    setProperty("compact", false);
    setMinimumSize(64, 44);
    setAcceptDrops(true);
    setFocusPolicy(Qt::StrongFocus);
    setAccessibleName(QStringLiteral("视频窗格 %1").arg(index_ + 1));

    auto *overlay = new QWidget(this);
    overlay_ = overlay;
    overlay->setObjectName(QStringLiteral("tileOverlay"));
    overlay->setAttribute(Qt::WA_TransparentForMouseEvents);
    overlay->setAttribute(Qt::WA_TranslucentBackground);
    overlay->setAutoFillBackground(false);
    titleLabel_ = new QLabel(QStringLiteral("窗格 %1").arg(index_ + 1));
    titleLabel_->setObjectName(QStringLiteral("tileTitle"));
    titleLabel_->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    statusIndicator_ = new QFrame;
    statusIndicator_->setObjectName(QStringLiteral("tileStatusDot"));
    statusIndicator_->setProperty("tone", QStringLiteral("idle"));
    statusIndicator_->setFixedSize(8, 8);
    statusIndicator_->setToolTip(QStringLiteral("未分配摄像头"));
    profileLabel_ = new QLabel;
    profileLabel_->setObjectName(QStringLiteral("tileProfile"));
    profileLabel_->setAlignment(Qt::AlignRight | Qt::AlignVCenter);
    stateLabel_ = new QLabel(QStringLiteral("未分配摄像头"));
    stateLabel_->setObjectName(QStringLiteral("tileState"));
    stateLabel_->setAlignment(Qt::AlignCenter);
    stateLabel_->setWordWrap(true);

    auto *overlayLayout = new QVBoxLayout(overlay);
    overlayLayout->setContentsMargins(9, 7, 9, 9);
    auto *headerLayout = new QHBoxLayout;
    headerLayout->setContentsMargins(0, 0, 0, 0);
    headerLayout->setSpacing(5);
    headerLayout->addWidget(statusIndicator_, 0, Qt::AlignVCenter);
    headerLayout->addWidget(titleLabel_, 1);
    headerLayout->addWidget(profileLabel_);
    overlayLayout->addLayout(headerLayout);
    overlayLayout->addStretch();
    overlayLayout->addWidget(stateLabel_, 0, Qt::AlignCenter);
    overlayLayout->addStretch();

    auto *stack = new QStackedLayout(this);
    stack_ = stack;
    stack->setStackingMode(QStackedLayout::StackAll);
    stack->setContentsMargins(0, 0, 0, 0);
    stack->addWidget(overlay);
    stack->setCurrentWidget(overlay);
    updateHeaderVisibility();
}

VideoTileWidget::~VideoTileWidget() {
    if (!requestId_.isNull()) {
        emit sessionRequestShouldBeCanceled(requestId_);
    }
    if (!sessionId_.isNull()) {
        emit sessionShouldBeRevoked(sessionId_);
    }
}

MpvPlayerWidget *VideoTileWidget::ensurePlayer() {
    if (player_ != nullptr) {
        return player_;
    }
    player_ = new MpvPlayerWidget(this);
    stack_->insertWidget(0, player_);
    connect(player_, &MpvPlayerWidget::playbackStarted, this, &VideoTileWidget::handlePlaybackStarted, Qt::QueuedConnection);
    connect(player_, &MpvPlayerWidget::playbackEnded, this, &VideoTileWidget::handlePlaybackEnded, Qt::QueuedConnection);
    connect(player_, &MpvPlayerWidget::playbackPositionChanged, this, &VideoTileWidget::handlePlaybackPositionChanged, Qt::QueuedConnection);
    connect(player_, &MpvPlayerWidget::playbackBufferingChanged, this, &VideoTileWidget::handlePlaybackBufferingChanged, Qt::QueuedConnection);
    connect(player_, &MpvPlayerWidget::playbackError, this, &VideoTileWidget::handlePlaybackError, Qt::QueuedConnection);
    if (overlay_ != nullptr) {
        overlay_->raise();
    }
    return player_;
}

void VideoTileWidget::stopPlayer() {
    if (player_ != nullptr) {
        player_->stop();
    }
}

void VideoTileWidget::releasePlayer() {
    MpvPlayerWidget *player = std::exchange(player_, nullptr);
    if (player == nullptr) {
        return;
    }
    player->release();
    player->deleteLater();
}

void VideoTileWidget::assignCamera(const CameraInfo &camera, const QString &profile) {
    if (!requestId_.isNull()) {
        emit sessionRequestShouldBeCanceled(requestId_);
    }
    if (!sessionId_.isNull()) {
        emit sessionShouldBeRevoked(sessionId_);
    }
    // 切换码流时复用同一 OpenGL/libmpv 画布，避免在菜单事件中销毁并重建渲染窗口。
    stopPlayer();
    setPlaying(false);
    buffering_ = false;
    ++playbackGeneration_;
    reconnectAttempts_ = 0;
    camera_ = camera;
    profile_ = profile;
    requestId_ = QUuid();
    sessionId_ = QUuid();
    cameraTitle_ = QStringLiteral("%1 · %2").arg(camera.alias, camera.code);
    updateTitleText();
    updateProfileLabel();
    if (profile == QStringLiteral("playback")) {
        updateOverlay(QStringLiteral("正在准备录像回放…"));
    } else {
        updateOverlay(QStringLiteral("申请%1码流…").arg(profile == QStringLiteral("main") ? QStringLiteral("主") : QStringLiteral("子")));
    }
}

void VideoTileWidget::updateCameraConnectivity(int connectivity) {
    if (camera_.has_value()) {
        camera_->connectivity = connectivity;
    }
}

void VideoTileWidget::markSessionRequested(const QUuid &requestId) {
    if (!requestId_.isNull() && requestId_ != requestId) {
        emit sessionRequestShouldBeCanceled(requestId_);
    }
    requestId_ = requestId;
    reconnectAttempts_ = 0;
}

bool VideoTileWidget::startSession(const StreamSessionInfo &session) {
    if (!sessionId_.isNull() && sessionId_ != session.id) {
        emit sessionShouldBeRevoked(sessionId_);
    }
    sessionId_ = session.id;
    requestId_ = QUuid();
    const quint64 playbackGeneration = ++playbackGeneration_;
    stopPlayer();
    setPlaying(false);
    buffering_ = false;
    if (ensurePlayer()->start(session.gatewayUri, playbackGeneration)) {
        updateOverlay(QStringLiteral("正在连接视频网关…"));
        return true;
    } else {
        if (!sessionId_.isNull()) {
            emit sessionShouldBeRevoked(sessionId_);
            sessionId_ = QUuid();
        }
        return false;
    }
}

void VideoTileWidget::failSession(const QString &message) {
    ++playbackGeneration_;
    requestId_ = QUuid();
    stopPlayer();
    setPlaying(false);
    buffering_ = false;
    if (!sessionId_.isNull()) {
        emit sessionShouldBeRevoked(sessionId_);
        sessionId_ = QUuid();
    }
    reconnectAttempts_ = 0;
    updateOverlay(message);
}

void VideoTileWidget::handleReconnectFailure(const QString &message) {
    requestReconnect(message);
}

void VideoTileWidget::handlePlaybackStarted(quint64 playbackGeneration) {
    if (playbackGeneration != playbackGeneration_) {
        return;
    }
    reconnectAttempts_ = 0;
    setPlaying(!buffering_);
    updateVisualState(buffering_ ? QStringLiteral("warning") : QStringLiteral("success"));
    updateProfileLabel();
    if (buffering_) updateOverlay(QStringLiteral("回放正在缓冲，时间轴已暂停。"));
    else stateLabel_->hide();
}

void VideoTileWidget::handlePlaybackEnded(quint64 playbackGeneration) {
    if (playbackGeneration != playbackGeneration_) {
        return;
    }
    buffering_ = false;
    setPlaying(false);
    if (profile_ == QStringLiteral("playback")) {
        updateOverlay(QStringLiteral("回放已结束或已暂停，可继续播放或重新定位。"));
        return;
    }
    requestReconnect(QStringLiteral("视频流已结束。"));
}

void VideoTileWidget::handlePlaybackError(quint64 playbackGeneration, const QString &message) {
    if (playbackGeneration != playbackGeneration_) {
        return;
    }
    buffering_ = false;
    setPlaying(false);
    requestReconnect(message);
}

void VideoTileWidget::handlePlaybackPositionChanged(quint64 playbackGeneration, double seconds) {
    if (playbackGeneration != playbackGeneration_ || profile_ != QStringLiteral("playback") || sessionId_.isNull()) {
        return;
    }
    emit playbackMediaPositionChanged(this, seconds);
}

void VideoTileWidget::handlePlaybackBufferingChanged(quint64 playbackGeneration, bool buffering) {
    if (playbackGeneration != playbackGeneration_ || profile_ != QStringLiteral("playback") || buffering_ == buffering) {
        return;
    }
    if (buffering) {
        buffering_ = true;
        setPlaying(false);
        updateOverlay(QStringLiteral("回放正在缓冲，时间轴已暂停。"));
    } else if (buffering_) {
        buffering_ = false;
        setPlaying(true);
        stateLabel_->hide();
    }
}

void VideoTileWidget::requestReconnect(const QString &message) {
    ++playbackGeneration_;
    stopPlayer();
    setPlaying(false);
    buffering_ = false;
    requestId_ = QUuid();
    if (sessionId_.isNull()) {
        failSession(message);
        return;
    }
    if (reconnectAttempts_ >= MaxReconnectAttempts) {
        failSession(QStringLiteral("%1 已达到自动重连上限。").arg(message));
        return;
    }

    ++reconnectAttempts_;
    requestId_ = QUuid::createUuid();
    const QUuid reconnectSessionId = sessionId_;
    const QUuid reconnectRequestId = requestId_;
    const quint64 reconnectGeneration = playbackGeneration_;
    const int retryExponent = reconnectAttempts_ < 4 ? reconnectAttempts_ - 1 : 3;
    const int delayMilliseconds = 500 * (1 << retryExponent);
    updateOverlay(QStringLiteral("连接中断，%1 毫秒后重连（%2/%3）…")
                      .arg(delayMilliseconds)
                      .arg(reconnectAttempts_)
                      .arg(MaxReconnectAttempts));
    QTimer::singleShot(delayMilliseconds, this, [this, reconnectSessionId, reconnectRequestId, reconnectGeneration]() {
        if (playbackGeneration_ != reconnectGeneration || sessionId_ != reconnectSessionId || requestId_ != reconnectRequestId) {
            return;
        }
        emit sessionReconnectRequested(reconnectSessionId, reconnectRequestId);
    });
}

void VideoTileWidget::clearTile() {
    const QUuid previousSessionId = sessionId_;
    const QUuid previousCameraId = camera_.has_value() ? camera_->id : QUuid{};
    if (!requestId_.isNull()) {
        emit sessionRequestShouldBeCanceled(requestId_);
    }
    if (!sessionId_.isNull()) {
        emit sessionShouldBeRevoked(sessionId_);
    }
    releasePlayer();
    setPlaying(false);
    buffering_ = false;
    ++playbackGeneration_;
    reconnectAttempts_ = 0;
    camera_.reset();
    requestId_ = QUuid();
    sessionId_ = QUuid();
    cameraTitle_.clear();
    updateTitleText();
    updateProfileLabel();
    updateOverlay(QStringLiteral("未分配摄像头"));
    emit tileCleared(previousSessionId, previousCameraId);
}

void VideoTileWidget::suspendSession() {
    if (!requestId_.isNull()) {
        emit sessionRequestShouldBeCanceled(requestId_);
    }
    if (!sessionId_.isNull()) {
        emit sessionShouldBeRevoked(sessionId_);
    }
    releasePlayer();
    setPlaying(false);
    buffering_ = false;
    ++playbackGeneration_;
    reconnectAttempts_ = 0;
    requestId_ = QUuid();
    sessionId_ = QUuid();
    if (camera_.has_value()) {
        updateOverlay(QStringLiteral("工作区已切换，返回后自动恢复。"));
    }
}

void VideoTileWidget::refreshPlaybackStream() {
    if (profile_ != QStringLiteral("playback") || sessionId_.isNull() || !requestId_.isNull()) {
        return;
    }
    ++playbackGeneration_;
    stopPlayer();
    setPlaying(false);
    buffering_ = false;
    reconnectAttempts_ = 0;
    requestId_ = QUuid::createUuid();
    updateOverlay(QStringLiteral("正在恢复回放媒体…"));
    emit sessionReconnectRequested(sessionId_, requestId_);
}

void VideoTileWidget::releaseForShutdown() {
    if (!requestId_.isNull()) {
        emit sessionRequestShouldBeCanceled(requestId_);
    }
    releasePlayer();
    setPlaying(false);
    buffering_ = false;
    ++playbackGeneration_;
    reconnectAttempts_ = 0;
    camera_.reset();
    requestId_ = QUuid();
    sessionId_ = QUuid();
    cameraTitle_.clear();
    updateVisualState(QStringLiteral("idle"));
}

void VideoTileWidget::setSelected(bool selected) {
    setProperty("selected", selected);
    style()->unpolish(this);
    style()->polish(this);
}

void VideoTileWidget::setSyncMember(bool synchronized) {
    if (syncMember_ == synchronized) {
        return;
    }
    syncMember_ = synchronized;
    updateProfileLabel();
    emit syncMembershipChanged(this, syncMember_);
}

void VideoTileWidget::setCompact(bool compact) {
    if (compact_ == compact) {
        return;
    }
    compact_ = compact;
    setProperty("compact", compact);
    // 初始化 64 分屏时窗格尚未显示，避免对大量隐藏窗格重复触发 Qt 样式重建。
    if (isVisible()) {
        style()->unpolish(this);
        style()->polish(this);
    }
    updateTitleText();
}

void VideoTileWidget::setCommandInteractionEnabled(bool enabled, const QString &unavailableReason) {
    commandInteractionEnabled_ = enabled;
    commandUnavailableReason_ = enabled ? QString{} : unavailableReason.trimmed();
    setAccessibleDescription(commandUnavailableReason_);
}

void VideoTileWidget::requestClear() {
    emit clearRequested(this);
}

void VideoTileWidget::requestSyncMembershipChange(bool synchronized) {
    emit syncMembershipChangeRequested(this, synchronized);
}

bool VideoTileWidget::isEmpty() const { return !camera_.has_value(); }
bool VideoTileWidget::isPlaying() const { return playing_; }
bool VideoTileWidget::hasAllocatedPlayer() const { return player_ != nullptr; }
bool VideoTileWidget::isSyncMember() const { return syncMember_; }
bool VideoTileWidget::commandInteractionEnabled() const { return commandInteractionEnabled_; }
int VideoTileWidget::index() const { return index_; }
QString VideoTileWidget::profile() const { return profile_; }
QUuid VideoTileWidget::requestId() const { return requestId_; }
QUuid VideoTileWidget::sessionId() const { return sessionId_; }
std::optional<CameraInfo> VideoTileWidget::camera() const { return camera_; }
QImage VideoTileWidget::captureFrame() { return player_ != nullptr ? player_->captureFrame() : QImage{}; }

void VideoTileWidget::enterEvent(QEnterEvent *event) {
    QFrame::enterEvent(event);
    headerHovered_ = true;
    updateHeaderVisibility();
    updateTitleText();
}

void VideoTileWidget::leaveEvent(QEvent *event) {
    QFrame::leaveEvent(event);
    headerHovered_ = false;
    updateHeaderVisibility();
}

void VideoTileWidget::mousePressEvent(QMouseEvent *event) {
    if (!commandInteractionEnabled_) {
        event->accept();
        return;
    }
    emit activated(this);
    QFrame::mousePressEvent(event);
}

void VideoTileWidget::mouseDoubleClickEvent(QMouseEvent *event) {
    if (!commandInteractionEnabled_) {
        event->accept();
        return;
    }
    emit activated(this);
    emit maximizeRequested(this);
    event->accept();
}

void VideoTileWidget::contextMenuEvent(QContextMenuEvent *event) {
    QMenu menu(this);
    if (!commandInteractionEnabled_) {
        QAction *unavailableAction = menu.addAction(
            commandUnavailableReason_.isEmpty()
                ? QStringLiteral("当前窗格操作暂不可用。")
                : commandUnavailableReason_);
        unavailableAction->setEnabled(false);
        menu.exec(event->globalPos());
        return;
    }
    auto *maximizeAction = menu.addAction(QStringLiteral("切换单窗放大"));
    maximizeAction->setObjectName(QStringLiteral("videoTileAction.maximize"));
    maximizeAction->setEnabled(!isEmpty());
    auto *restartAction = menu.addAction(QStringLiteral("重新连接"));
    restartAction->setObjectName(QStringLiteral("videoTileAction.restart"));
    restartAction->setEnabled(!isEmpty());
    auto *screenshotAction = menu.addAction(QStringLiteral("保存当前画面截图"));
    screenshotAction->setObjectName(QStringLiteral("videoTileAction.screenshot"));
    screenshotAction->setEnabled(!isEmpty() && player_ != nullptr && player_->isReady());
    QAction *mainStreamAction = nullptr;
    QAction *subStreamAction = nullptr;
    QAction *syncAction = nullptr;
    QAction *bookmarkAction = nullptr;
    QList<QPair<QAction *, int>> instantActions;
    if (profile_ == QStringLiteral("playback")) {
        bookmarkAction = menu.addAction(QStringLiteral("添加本地书签"));
        bookmarkAction->setObjectName(QStringLiteral("videoTileAction.bookmark"));
        bookmarkAction->setEnabled(!isEmpty() && playing_);
        syncAction = menu.addAction(QStringLiteral("加入同步组"));
        syncAction->setObjectName(QStringLiteral("videoTileAction.sync"));
        syncAction->setCheckable(true);
        syncAction->setChecked(syncMember_);
    } else if (!isEmpty()) {
        auto *streamMenu = menu.addMenu(QStringLiteral("码流类型"));
        mainStreamAction = streamMenu->addAction(QStringLiteral("主码流"));
        subStreamAction = streamMenu->addAction(QStringLiteral("子码流"));
        mainStreamAction->setObjectName(QStringLiteral("videoTileAction.stream.main"));
        subStreamAction->setObjectName(QStringLiteral("videoTileAction.stream.sub"));
        mainStreamAction->setCheckable(true);
        subStreamAction->setCheckable(true);
        mainStreamAction->setChecked(profile_ == QStringLiteral("main"));
        subStreamAction->setChecked(profile_ == QStringLiteral("sub"));
        const bool streamSwitchReady = requestId_.isNull();
        mainStreamAction->setEnabled(streamSwitchReady && profile_ != QStringLiteral("main"));
        subStreamAction->setEnabled(streamSwitchReady && profile_ != QStringLiteral("sub"));
        if (!streamSwitchReady) {
            const QString reason = QStringLiteral("当前窗格正在切换码流，请等待会话建立完成。");
            mainStreamAction->setToolTip(reason);
            subStreamAction->setToolTip(reason);
        }
        auto *instantMenu = menu.addMenu(QStringLiteral("即时回放"));
        for (const auto &[label, seconds] : QList<QPair<QString, int>>{
                 {QStringLiteral("最近 30 秒"), 30},
                 {QStringLiteral("最近 1 分钟"), 60},
                 {QStringLiteral("最近 3 分钟"), 180},
                 {QStringLiteral("最近 5 分钟"), 300},
                 {QStringLiteral("最近 10 分钟"), 600}}) {
            QAction *instantAction = instantMenu->addAction(label);
            instantAction->setObjectName(QStringLiteral("videoTileAction.instant.%1").arg(seconds));
            instantActions.append({instantAction, seconds});
        }
    }
    menu.addSeparator();
    auto *clearAction = menu.addAction(profile_ == QStringLiteral("playback")
                                           ? QStringLiteral("停止并清除此回放")
                                           : QStringLiteral("停止并清除此预览"));
    clearAction->setObjectName(QStringLiteral("videoTileAction.clear"));
    clearAction->setEnabled(!isEmpty());
    QAction *selectedAction = menu.exec(event->globalPos());
    if (selectedAction == nullptr) {
        event->accept();
        return;
    }
    if (selectedAction == maximizeAction) {
        emit maximizeRequested(this);
    } else if (selectedAction == restartAction) {
        emit restartRequested(this);
    } else if (selectedAction == screenshotAction) {
        emit screenshotRequested(this);
    } else if (bookmarkAction != nullptr && selectedAction == bookmarkAction) {
        emit bookmarkRequested(this);
    } else if (mainStreamAction != nullptr && selectedAction == mainStreamAction) {
        emit profileChangeRequested(this, QStringLiteral("main"));
    } else if (subStreamAction != nullptr && selectedAction == subStreamAction) {
        emit profileChangeRequested(this, QStringLiteral("sub"));
    } else if (syncAction != nullptr && selectedAction == syncAction) {
        requestSyncMembershipChange(syncAction->isChecked());
    } else if (selectedAction == clearAction) {
        requestClear();
    } else {
        for (const auto &[action, seconds] : instantActions) {
            if (selectedAction == action) {
                emit instantPlaybackRequested(this, seconds);
                break;
            }
        }
    }
}

void VideoTileWidget::dragEnterEvent(QDragEnterEvent *event) {
    if (commandInteractionEnabled_ && event->mimeData()->hasFormat(CatalogRoles::cameraIdsMimeType())) {
        event->acceptProposedAction();
        return;
    }
    QFrame::dragEnterEvent(event);
}

void VideoTileWidget::dropEvent(QDropEvent *event) {
    if (!commandInteractionEnabled_ || !event->mimeData()->hasFormat(CatalogRoles::cameraIdsMimeType())) {
        QFrame::dropEvent(event);
        return;
    }
    QList<QUuid> cameraIds;
    const QStringList values = QString::fromUtf8(event->mimeData()->data(CatalogRoles::cameraIdsMimeType()))
                                   .split(u'\n', Qt::SkipEmptyParts);
    for (const QString &value : values) {
        const QUuid cameraId(value.trimmed());
        if (!cameraId.isNull() && !cameraIds.contains(cameraId)) {
            cameraIds.append(cameraId);
        }
    }
    if (!cameraIds.isEmpty()) {
        emit cameraIdsDropped(this, cameraIds);
        event->acceptProposedAction();
    }
}

void VideoTileWidget::resizeEvent(QResizeEvent *event) {
    QFrame::resizeEvent(event);
    updateTitleText();
}

void VideoTileWidget::updateOverlay(const QString &state) {
    stateLabel_->setText(state);
    stateLabel_->setVisible(true);
    setToolTip(state);
    if (state.contains(QStringLiteral("失败")) || state.contains(QStringLiteral("错误")) ||
        state.contains(QStringLiteral("无法")) || state.contains(QStringLiteral("上限"))) {
        updateVisualState(QStringLiteral("error"));
    } else if (state.contains(QStringLiteral("中断")) || state.contains(QStringLiteral("离线")) ||
               state.contains(QStringLiteral("暂停")) || state.contains(QStringLiteral("结束")) ||
               state.contains(QStringLiteral("缓冲"))) {
        updateVisualState(QStringLiteral("warning"));
    } else if (state == QStringLiteral("未分配摄像头")) {
        updateVisualState(QStringLiteral("idle"));
    } else {
        updateVisualState(QStringLiteral("info"));
    }
}

void VideoTileWidget::updateVisualState(const QString &tone) {
    setProperty("tone", tone);
    if (statusIndicator_ != nullptr) {
        statusIndicator_->setProperty("tone", tone);
        const QString description = tone == QStringLiteral("success")
            ? QStringLiteral("播放正常")
            : tone == QStringLiteral("warning")
                ? QStringLiteral("播放状态需要关注")
                : tone == QStringLiteral("error")
                    ? QStringLiteral("播放失败")
                    : tone == QStringLiteral("info")
                        ? QStringLiteral("正在建立播放")
                        : QStringLiteral("未分配摄像头");
        statusIndicator_->setToolTip(description);
        statusIndicator_->style()->unpolish(statusIndicator_);
        statusIndicator_->style()->polish(statusIndicator_);
    }
    style()->unpolish(this);
    style()->polish(this);
}

void VideoTileWidget::updateHeaderVisibility() {
    titleLabel_->setVisible(headerHovered_);
    if (statusIndicator_ != nullptr) {
        statusIndicator_->setVisible(headerHovered_);
    }
    profileLabel_->setVisible(headerHovered_ && camera_.has_value() && !profileLabel_->text().isEmpty());
}

void VideoTileWidget::updateTitleText() {
    const QString source = cameraTitle_.isEmpty() ? QStringLiteral("窗格 %1").arg(index_ + 1) : cameraTitle_;
    const int reservedWidth = profileLabel_ != nullptr && profileLabel_->isVisible() ? profileLabel_->sizeHint().width() + 24 : 18;
    const int availableWidth = std::max(24, width() - reservedWidth);
    titleLabel_->setText(titleLabel_->fontMetrics().elidedText(source, Qt::ElideRight, availableWidth));
    titleLabel_->setToolTip(source);
}

void VideoTileWidget::updateProfileLabel() {
    if (!camera_.has_value()) {
        profileLabel_->clear();
        updateHeaderVisibility();
        return;
    }
    QString text;
    if (profile_ == QStringLiteral("playback")) {
        text = syncMember_ ? QStringLiteral("回放 · 同步") : QStringLiteral("回放 · 独立");
    } else {
        text = profile_ == QStringLiteral("main") ? QStringLiteral("主码流") : QStringLiteral("子码流");
        if (playing_) {
            text.prepend(QStringLiteral("播放中 · "));
        }
    }
    profileLabel_->setText(text);
    updateHeaderVisibility();
    updateTitleText();
}

void VideoTileWidget::setPlaying(bool playing) {
    if (playing_ == playing) {
        return;
    }
    playing_ = playing;
    if (playing_) {
        updateVisualState(QStringLiteral("success"));
    }
    updateProfileLabel();
    emit playbackStateChanged(this, playing_);
}
