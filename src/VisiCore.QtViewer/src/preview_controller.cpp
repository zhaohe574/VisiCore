#include "preview_controller.h"

#include <algorithm>

namespace {
QList<ViewerActionId> previewActionIds() {
    return {
        ViewerActionId::ToggleFavorite,
        ViewerActionId::SaveView,
        ViewerActionId::ChangePreviewLayout,
        ViewerActionId::StopAllPreview,
        ViewerActionId::ClearSelectedTile,
        ViewerActionId::ToggleTour,
        ViewerActionId::TourPrevious,
        ViewerActionId::TourNext,
    };
}
}

PreviewController::PreviewController(QObject *parent)
    : QObject(parent) {
    publishActionAvailability();
}

QList<int> PreviewController::supportedLayouts() {
    return {1, 4, 9, 16, 25, 36, 64};
}

bool PreviewController::isSupportedLayout(int count) {
    return supportedLayouts().contains(count);
}

int PreviewController::gridDimension(int count) {
    switch (count) {
        case 1: return 1;
        case 4: return 2;
        case 9: return 3;
        case 16: return 4;
        case 25: return 5;
        case 36: return 6;
        case 64: return 8;
        default: return 0;
    }
}

int PreviewController::bestLayoutForCameraCount(int cameraCount) {
    const int normalizedCount = std::max(1, cameraCount);
    for (const int layout : supportedLayouts()) {
        if (normalizedCount <= layout) {
            return layout;
        }
    }
    return MaximumTileCount;
}

int PreviewController::layoutCount() const {
    return layoutCount_;
}

int PreviewController::selectedTileIndex() const {
    return selectedTileIndex_;
}

bool PreviewController::isTileVisible(int tileIndex) const {
    return tileIndex >= 0 && tileIndex < layoutCount_;
}

bool PreviewController::isTileAssigned(int tileIndex) const {
    return assignedTileIndices_.contains(tileIndex);
}

QSet<int> PreviewController::assignedTileIndices() const {
    return assignedTileIndices_;
}

int PreviewController::activeSessionCount() const {
    return activeSessionCount_;
}

QString PreviewController::streamMode() const {
    return streamMode_;
}

QString PreviewController::effectiveStreamProfile() const {
    if (streamMode_ == QStringLiteral("main") || streamMode_ == QStringLiteral("sub")) {
        return streamMode_;
    }
    return layoutCount_ >= 9 ? QStringLiteral("sub") : QStringLiteral("main");
}

bool PreviewController::compactTiles() const {
    return layoutCount_ >= 25;
}

bool PreviewController::tourActive() const {
    return tourActive_;
}

int PreviewController::tourCandidateCount() const {
    return tourCandidateCount_;
}

bool PreviewController::isActionEnabled(ViewerActionId actionId) const {
    const bool selectedAssigned = assignedTileIndices_.contains(selectedTileIndex_);
    switch (actionId) {
        case ViewerActionId::ToggleFavorite:
        case ViewerActionId::ClearSelectedTile:
            return selectedAssigned;
        case ViewerActionId::SaveView:
            return !assignedTileIndices_.isEmpty();
        case ViewerActionId::ChangePreviewLayout:
            return true;
        case ViewerActionId::StopAllPreview:
            return activeSessionCount_ > 0 || !assignedTileIndices_.isEmpty();
        case ViewerActionId::ToggleTour:
            return tourActive_ || tourCandidateCount_ > 0;
        case ViewerActionId::TourPrevious:
        case ViewerActionId::TourNext:
            return tourActive_ && tourCandidateCount_ > 0;
        default:
            return true;
    }
}

bool PreviewController::setLayoutCount(int count) {
    if (!isSupportedLayout(count)) {
        emit operationRejected(QStringLiteral("预览分屏仅支持 1、4、9、16、25、36 或 64 路。"));
        return false;
    }
    if (layoutCount_ == count) {
        return false;
    }

    const QString previousProfile = effectiveStreamProfile();
    layoutCount_ = count;
    emit layoutChanged(layoutCount_, gridDimension(layoutCount_));
    if (!isTileVisible(selectedTileIndex_)) {
        selectedTileIndex_ = 0;
        emit selectedTileChanged(selectedTileIndex_);
    }
    if (previousProfile != effectiveStreamProfile()) {
        emit streamProfileChanged(streamMode_, effectiveStreamProfile());
    }
    publishActionAvailability();
    emit stateChanged();
    return true;
}

bool PreviewController::selectTile(int tileIndex) {
    if (!isTileVisible(tileIndex)) {
        emit operationRejected(QStringLiteral("只能选择当前分屏中可见的预览窗格。"));
        return false;
    }
    if (selectedTileIndex_ == tileIndex) {
        return false;
    }
    selectedTileIndex_ = tileIndex;
    emit selectedTileChanged(selectedTileIndex_);
    publishActionAvailability();
    emit stateChanged();
    return true;
}

bool PreviewController::setTileAssigned(int tileIndex, bool assigned) {
    if (tileIndex < 0 || tileIndex >= MaximumTileCount) {
        emit operationRejected(QStringLiteral("预览窗格编号必须位于 0 到 63 之间。"));
        return false;
    }
    if (assigned == assignedTileIndices_.contains(tileIndex)) {
        return false;
    }
    if (assigned) {
        assignedTileIndices_.insert(tileIndex);
    } else {
        assignedTileIndices_.remove(tileIndex);
    }
    publishActionAvailability();
    emit stateChanged();
    return true;
}

void PreviewController::clearAssignments() {
    if (assignedTileIndices_.isEmpty()) {
        return;
    }
    assignedTileIndices_.clear();
    publishActionAvailability();
    emit stateChanged();
}

void PreviewController::setActiveSessionCount(int count) {
    const int normalizedCount = std::clamp(count, 0, MaximumTileCount);
    if (activeSessionCount_ == normalizedCount) {
        return;
    }
    activeSessionCount_ = normalizedCount;
    publishActionAvailability();
    emit stateChanged();
}

void PreviewController::setStreamMode(const QString &mode) {
    const QString normalizedMode = normalizeStreamMode(mode);
    if (streamMode_ == normalizedMode) {
        return;
    }
    streamMode_ = normalizedMode;
    emit streamProfileChanged(streamMode_, effectiveStreamProfile());
    emit stateChanged();
}

void PreviewController::setTourCandidateCount(int count) {
    const int normalizedCount = std::max(0, count);
    if (tourCandidateCount_ == normalizedCount) {
        return;
    }
    tourCandidateCount_ = normalizedCount;
    if (tourCandidateCount_ == 0 && tourActive_) {
        tourActive_ = false;
        emit tourStateChanged(false);
    }
    publishActionAvailability();
    emit stateChanged();
}

bool PreviewController::setTourActive(bool active) {
    if (active && tourCandidateCount_ == 0) {
        emit operationRejected(QStringLiteral("当前轮巡范围内没有可用的在线摄像头。"));
        return false;
    }
    if (tourActive_ == active) {
        return false;
    }
    tourActive_ = active;
    emit tourStateChanged(tourActive_);
    publishActionAvailability();
    emit stateChanged();
    return true;
}

QString PreviewController::normalizeStreamMode(const QString &mode) {
    const QString normalized = mode.trimmed().toLower();
    if (normalized == QStringLiteral("main") || normalized == QStringLiteral("sub")) {
        return normalized;
    }
    return QStringLiteral("auto");
}

void PreviewController::publishActionAvailability() {
    for (const ViewerActionId actionId : previewActionIds()) {
        const int key = static_cast<int>(actionId);
        const bool enabled = isActionEnabled(actionId);
        if (!publishedActionStates_.contains(key) || publishedActionStates_.value(key) != enabled) {
            publishedActionStates_.insert(key, enabled);
            emit actionAvailabilityChanged(actionId, enabled);
        }
    }
}
