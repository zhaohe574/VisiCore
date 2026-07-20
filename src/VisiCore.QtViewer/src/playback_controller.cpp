#include "playback_controller.h"

#include <algorithm>

namespace {
QList<ViewerActionId> playbackActionIds() {
    return {
        ViewerActionId::ChangePlaybackLayout,
        ViewerActionId::StopAllPlayback,
        ViewerActionId::PlaybackPause,
        ViewerActionId::PlaybackResume,
        ViewerActionId::PlaybackSync,
    };
}
}

PlaybackController::PlaybackController(QObject *parent)
    : QObject(parent) {
    publishDerivedState();
}

QList<int> PlaybackController::supportedLayouts() {
    return {1, 4};
}

bool PlaybackController::isSupportedLayout(int count) {
    return supportedLayouts().contains(count);
}

bool PlaybackController::validateTimeRange(
    const QDateTime &startedAt,
    const QDateTime &endedAt,
    QString *errorMessage) {
    const bool valid = startedAt.isValid() && endedAt.isValid() &&
                       startedAt < endedAt && startedAt.secsTo(endedAt) <= MaximumRangeSeconds;
    if (!valid && errorMessage != nullptr) {
        *errorMessage = QStringLiteral("回放时间范围必须大于 0 且不超过 31 天。");
    } else if (valid && errorMessage != nullptr) {
        errorMessage->clear();
    }
    return valid;
}

int PlaybackController::layoutCount() const {
    return layoutCount_;
}

int PlaybackController::selectedTileIndex() const {
    return selectedTileIndex_;
}

bool PlaybackController::syncEnabled() const {
    return syncEnabled_;
}

bool PlaybackController::batchPending() const {
    return batchPending_;
}

bool PlaybackController::isTileVisible(int tileIndex) const {
    return tileIndex >= 0 && tileIndex < layoutCount_;
}

PlaybackTileState PlaybackController::tileState(int tileIndex) const {
    return tileStates_.value(tileIndex);
}

QList<int> PlaybackController::activeAssignedTileIndices() const {
    QList<int> result;
    for (int tileIndex = 0; tileIndex < layoutCount_; ++tileIndex) {
        if (tileStates_.value(tileIndex).assigned()) {
            result.append(tileIndex);
        }
    }
    return result;
}

QList<int> PlaybackController::controlTargetIndices() const {
    const PlaybackTileState selected = tileStates_.value(selectedTileIndex_);
    if (!isTileVisible(selectedTileIndex_) || !selected.assigned()) {
        return {};
    }
    if (!syncEnabled_ || !selected.syncMember) {
        return {selectedTileIndex_};
    }

    QList<int> targets;
    for (int tileIndex = 0; tileIndex < layoutCount_; ++tileIndex) {
        const PlaybackTileState candidate = tileStates_.value(tileIndex);
        if (candidate.assigned() && candidate.syncMember) {
            targets.append(tileIndex);
        }
    }
    return targets;
}

PlaybackControlState PlaybackController::controlState() const {
    PlaybackControlState state;
    const QList<int> targets = controlTargetIndices();
    state.hasTargets = !targets.isEmpty();
    if (!state.hasTargets) {
        state.pending = batchPending_;
        return state;
    }

    state.ready = true;
    state.canPause = true;
    state.canSeek = true;
    state.canChangeSpeed = true;
    state.allPaused = true;
    state.pending = batchPending_;
    for (const int tileIndex : targets) {
        const PlaybackTileState target = tileStates_.value(tileIndex);
        state.ready = state.ready && target.sessionReady();
        state.pending = state.pending || target.commandPending;
        state.canPause = state.canPause && target.transport.canPause;
        state.canSeek = state.canSeek && target.transport.canSeek;
        state.canChangeSpeed = state.canChangeSpeed && target.transport.canChangeSpeed;
        state.anyPaused = state.anyPaused || target.transport.isPaused;
        state.allPaused = state.allPaused && target.transport.isPaused;
    }

    if (!state.ready) {
        state.canPause = false;
        state.canSeek = false;
        state.canChangeSpeed = false;
    }
    state.pauseEnabled = state.canPause && !state.allPaused && !state.pending;
    state.resumeEnabled = state.canPause && state.anyPaused && !state.pending;
    state.seekEnabled = state.canSeek && !state.pending;
    state.speedEnabled = state.canChangeSpeed && !state.pending;
    return state;
}

bool PlaybackController::isActionEnabled(ViewerActionId actionId) const {
    const PlaybackControlState controls = controlState();
    switch (actionId) {
        case ViewerActionId::ChangePlaybackLayout:
            return true;
        case ViewerActionId::StopAllPlayback:
            return std::any_of(tileStates_.cbegin(), tileStates_.cend(), [](const PlaybackTileState &state) {
                return state.assigned() || state.sessionReady();
            });
        case ViewerActionId::PlaybackPause:
            return controls.pauseEnabled;
        case ViewerActionId::PlaybackResume:
            return controls.resumeEnabled;
        case ViewerActionId::PlaybackSync:
            return !activeAssignedTileIndices().isEmpty();
        default:
            return true;
    }
}

bool PlaybackController::setLayoutCount(int count) {
    if (!isSupportedLayout(count)) {
        emit operationRejected(QStringLiteral("录像回放分屏仅支持 1 路或 4 路。"));
        return false;
    }
    if (layoutCount_ == count) {
        return false;
    }
    layoutCount_ = count;
    emit layoutChanged(layoutCount_, layoutCount_ == 1 ? 1 : 2);
    if (!isTileVisible(selectedTileIndex_)) {
        selectedTileIndex_ = 0;
        emit selectedTileChanged(selectedTileIndex_);
    }
    publishDerivedState();
    emit stateChanged();
    return true;
}

bool PlaybackController::selectTile(int tileIndex) {
    if (!isTileVisible(tileIndex)) {
        emit operationRejected(QStringLiteral("只能选择当前分屏中可见的回放窗格。"));
        return false;
    }
    if (selectedTileIndex_ == tileIndex) {
        return false;
    }
    selectedTileIndex_ = tileIndex;
    emit selectedTileChanged(selectedTileIndex_);
    publishDerivedState();
    emit stateChanged();
    return true;
}

bool PlaybackController::setTileState(int tileIndex, const PlaybackTileState &state) {
    if (tileIndex < 0 || tileIndex >= MaximumTileCount) {
        emit operationRejected(QStringLiteral("回放窗格编号必须位于 0 到 3 之间。"));
        return false;
    }
    tileStates_.insert(tileIndex, state);
    publishDerivedState();
    emit stateChanged();
    return true;
}

bool PlaybackController::clearTile(int tileIndex) {
    if (tileIndex < 0 || tileIndex >= MaximumTileCount) {
        emit operationRejected(QStringLiteral("回放窗格编号必须位于 0 到 3 之间。"));
        return false;
    }
    if (!tileStates_.contains(tileIndex)) {
        return false;
    }
    tileStates_.remove(tileIndex);
    publishDerivedState();
    emit stateChanged();
    return true;
}

void PlaybackController::clearAllTiles() {
    if (tileStates_.isEmpty()) {
        return;
    }
    tileStates_.clear();
    batchPending_ = false;
    publishDerivedState();
    emit stateChanged();
}

void PlaybackController::setSyncEnabled(bool enabled) {
    if (syncEnabled_ == enabled) {
        return;
    }
    syncEnabled_ = enabled;
    emit syncEnabledChanged(syncEnabled_);
    publishDerivedState();
    emit stateChanged();
}

void PlaybackController::setBatchPending(bool pending) {
    if (batchPending_ == pending) {
        return;
    }
    batchPending_ = pending;
    publishDerivedState();
    emit stateChanged();
}

void PlaybackController::publishDerivedState() {
    const QList<int> targets = controlTargetIndices();
    if (targets != publishedTargets_) {
        publishedTargets_ = targets;
        emit controlTargetsChanged(publishedTargets_);
    }

    const PlaybackControlState controls = controlState();
    if (!hasPublishedControlState_ || controls != publishedControlState_) {
        publishedControlState_ = controls;
        hasPublishedControlState_ = true;
        emit controlStateChanged(publishedControlState_);
    }

    for (const ViewerActionId actionId : playbackActionIds()) {
        const int key = static_cast<int>(actionId);
        const bool enabled = isActionEnabled(actionId);
        if (!publishedActionStates_.contains(key) || publishedActionStates_.value(key) != enabled) {
            publishedActionStates_.insert(key, enabled);
            emit actionAvailabilityChanged(actionId, enabled);
        }
    }
}
