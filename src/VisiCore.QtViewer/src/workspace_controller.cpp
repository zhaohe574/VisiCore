#include "workspace_controller.h"

#include <QList>

namespace {
bool isPreviewAction(ViewerActionId actionId) {
    switch (actionId) {
        case ViewerActionId::ToggleFavorite:
        case ViewerActionId::SaveView:
        case ViewerActionId::ChangePreviewLayout:
        case ViewerActionId::StopAllPreview:
        case ViewerActionId::ToggleTour:
        case ViewerActionId::TourPrevious:
        case ViewerActionId::TourNext:
            return true;
        default:
            return false;
    }
}

bool isPlaybackAction(ViewerActionId actionId) {
    switch (actionId) {
        case ViewerActionId::ChangePlaybackLayout:
        case ViewerActionId::StopAllPlayback:
        case ViewerActionId::PlaybackPause:
        case ViewerActionId::PlaybackResume:
        case ViewerActionId::PlaybackSync:
            return true;
        default:
            return false;
    }
}
}

WorkspaceController::WorkspaceController(QObject *parent)
    : QObject(parent) {
    publishActionStateChanges();
}

WorkspaceMode WorkspaceController::mode() const {
    return mode_;
}

bool WorkspaceController::interactionEnabled() const {
    return interactionEnabled_;
}

bool WorkspaceController::isActionEnabled(ViewerActionId actionId) const {
    if (!interactionEnabled_) {
        return false;
    }
    if (isPreviewAction(actionId)) {
        return mode_ == WorkspaceMode::Preview;
    }
    if (isPlaybackAction(actionId)) {
        return mode_ == WorkspaceMode::Playback;
    }
    return true;
}

bool WorkspaceController::isActionChecked(ViewerActionId actionId) const {
    switch (actionId) {
        case ViewerActionId::WorkspacePreview:
            return mode_ == WorkspaceMode::Preview;
        case ViewerActionId::WorkspacePlayback:
            return mode_ == WorkspaceMode::Playback;
        default:
            return false;
    }
}

bool WorkspaceController::setMode(WorkspaceMode mode) {
    if (!isKnownMode(mode)) {
        emit operationRejected(QStringLiteral("无法切换到未知的工作区。"));
        return false;
    }
    if (!interactionEnabled_) {
        emit operationRejected(QStringLiteral("当前正在结束会话，无法切换工作区。"));
        return false;
    }
    if (mode_ == mode) {
        return false;
    }

    const WorkspaceMode previousMode = mode_;
    emit workspaceAboutToChange(previousMode, mode);
    mode_ = mode;
    publishActionStateChanges();
    emit workspaceChanged(mode_);
    emit stateChanged();
    return true;
}

void WorkspaceController::setInteractionEnabled(bool enabled) {
    if (interactionEnabled_ == enabled) {
        return;
    }
    interactionEnabled_ = enabled;
    publishActionStateChanges();
    emit stateChanged();
}

bool WorkspaceController::isKnownMode(WorkspaceMode mode) {
    return mode == WorkspaceMode::Preview || mode == WorkspaceMode::Playback;
}

QList<ViewerActionId> WorkspaceController::allActionIds() {
    return {
        ViewerActionId::WorkspacePreview,
        ViewerActionId::WorkspacePlayback,
        ViewerActionId::RefreshCatalog,
        ViewerActionId::FocusSearch,
        ViewerActionId::ToggleFavorite,
        ViewerActionId::SaveView,
        ViewerActionId::ChangePreviewLayout,
        ViewerActionId::ChangePlaybackLayout,
        ViewerActionId::StopAllPreview,
        ViewerActionId::StopAllPlayback,
        ViewerActionId::ToggleFullScreen,
        ViewerActionId::RestoreWindow,
        ViewerActionId::ClearSelectedTile,
        ViewerActionId::ToggleTour,
        ViewerActionId::TourPrevious,
        ViewerActionId::TourNext,
        ViewerActionId::PlaybackPause,
        ViewerActionId::PlaybackResume,
        ViewerActionId::PlaybackSync,
        ViewerActionId::ShowResourceCatalog,
        ViewerActionId::ShowPtz,
        ViewerActionId::ShowPlaybackSearch,
        ViewerActionId::ShowRecordingTimeline,
        ViewerActionId::LockDockLayout,
        ViewerActionId::RestoreDefaultLayout,
        ViewerActionId::ChangePassword,
        ViewerActionId::Logout,
        ViewerActionId::ExitApplication,
    };
}

void WorkspaceController::publishActionStateChanges() {
    for (const ViewerActionId actionId : allActionIds()) {
        const int key = static_cast<int>(actionId);
        const bool enabled = isActionEnabled(actionId);
        const bool checked = isActionChecked(actionId);
        if (!publishedEnabledStates_.contains(key) ||
            publishedEnabledStates_.value(key) != enabled ||
            !publishedCheckedStates_.contains(key) ||
            publishedCheckedStates_.value(key) != checked) {
            publishedEnabledStates_.insert(key, enabled);
            publishedCheckedStates_.insert(key, checked);
            emit actionStateChanged(actionId, enabled, checked);
        }
    }
}
