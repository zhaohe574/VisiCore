#include "ptz_controller.h"

#include <algorithm>

PtzController::PtzController(QObject *parent)
    : QObject(parent) {
    pulseTimer_.setInterval(DefaultPulseIntervalMilliseconds);
    pulseTimer_.setSingleShot(false);
    connect(&pulseTimer_, &QTimer::timeout, this, [this]() {
        renewPulse();
    });
}

WorkspaceMode PtzController::workspaceMode() const {
    return workspaceMode_;
}

std::optional<CameraInfo> PtzController::selectedCamera() const {
    return selectedCamera_;
}

bool PtzController::available() const {
    return availabilityReason_ == PtzAvailabilityReason::Available;
}

PtzAvailabilityReason PtzController::availabilityReason() const {
    return availabilityReason_;
}

QString PtzController::statusText() const {
    switch (availabilityReason_) {
        case PtzAvailabilityReason::Available:
            return QStringLiteral("正在控制：%1").arg(selectedCamera_->alias);
        case PtzAvailabilityReason::PlaybackWorkspace:
            return QStringLiteral("录像回放工作区不提供云台控制");
        case PtzAvailabilityReason::NoCameraSelected:
            return QStringLiteral("选择支持云台控制且在线的摄像头");
        case PtzAvailabilityReason::Unsupported:
            return QStringLiteral("%1 不支持云台控制").arg(selectedCamera_->alias);
        case PtzAvailabilityReason::PermissionDenied:
            return QStringLiteral("当前账号没有 %1 的云台控制权限").arg(selectedCamera_->alias);
        case PtzAvailabilityReason::CameraOffline:
            return QStringLiteral("%1 当前%2，云台控制已禁用")
                .arg(selectedCamera_->alias, connectivityLabel(selectedCamera_->connectivity));
    }
    return QStringLiteral("云台控制当前不可用");
}

PtzPulseState PtzController::pulseState() const {
    return pulseState_;
}

int PtzController::pulseIntervalMilliseconds() const {
    return pulseTimer_.interval();
}

void PtzController::setWorkspaceMode(WorkspaceMode mode) {
    if (workspaceMode_ == mode) {
        return;
    }
    if (pulseState_.active && mode != WorkspaceMode::Preview) {
        stopPulse();
    }
    workspaceMode_ = mode;
    updateAvailability();
    emit stateChanged();
}

void PtzController::setSelectedCamera(const CameraInfo &camera) {
    const std::optional<CameraInfo> nextCamera = camera;
    const PtzAvailabilityReason nextAvailability = evaluateAvailability(workspaceMode_, nextCamera);
    if (pulseState_.active &&
        (camera.id != pulseState_.cameraId || nextAvailability != PtzAvailabilityReason::Available)) {
        stopPulse();
    }
    selectedCamera_ = camera;
    updateAvailability(true);
    emit stateChanged();
}

void PtzController::clearSelectedCamera() {
    if (!selectedCamera_.has_value()) {
        return;
    }
    stopPulse();
    selectedCamera_.reset();
    updateAvailability(true);
    emit stateChanged();
}

void PtzController::setPulseIntervalMilliseconds(int intervalMilliseconds) {
    const int normalizedInterval = std::max(100, intervalMilliseconds);
    pulseTimer_.setInterval(normalizedInterval);
}

bool PtzController::beginPulse(int action, int speed) {
    if (!available() || !selectedCamera_.has_value()) {
        emit operationRejected(QStringLiteral("当前摄像头不可执行云台控制。"));
        return false;
    }
    if (action < MinimumAction || action > MaximumAction || speed < MinimumSpeed || speed > MaximumSpeed) {
        emit operationRejected(QStringLiteral("云台动作或速度参数无效。"));
        return false;
    }

    if (pulseState_.active) {
        stopPulse();
    }
    pulseState_ = PtzPulseState{true, selectedCamera_->id, action, speed};
    emit ptzCommandRequested(pulseState_.cameraId, pulseState_.action, 0, pulseState_.speed);
    pulseTimer_.start();
    emit pulseStateChanged(pulseState_);
    emit stateChanged();
    return true;
}

bool PtzController::endPulse(int action) {
    if (!pulseState_.active || pulseState_.action != action) {
        return false;
    }
    return stopPulse();
}

bool PtzController::stopPulse() {
    if (!pulseState_.active) {
        pulseTimer_.stop();
        return false;
    }
    pulseTimer_.stop();
    const PtzPulseState previousState = pulseState_;
    pulseState_ = PtzPulseState{};
    emit ptzCommandRequested(previousState.cameraId, previousState.action, 1, MinimumSpeed);
    emit pulseStateChanged(pulseState_);
    emit stateChanged();
    return true;
}

bool PtzController::renewPulse() {
    if (!pulseState_.active || !available() || !selectedCamera_.has_value() ||
        selectedCamera_->id != pulseState_.cameraId) {
        stopPulse();
        return false;
    }
    emit ptzCommandRequested(pulseState_.cameraId, pulseState_.action, 0, pulseState_.speed);
    return true;
}

QString PtzController::connectivityLabel(int connectivity) {
    switch (connectivity) {
        case 1: return QStringLiteral("在线");
        case 2: return QStringLiteral("疑似离线");
        case 3: return QStringLiteral("离线");
        case 4: return QStringLiteral("恢复中");
        default: return QStringLiteral("状态未知");
    }
}

PtzAvailabilityReason PtzController::evaluateAvailability(
    WorkspaceMode mode,
    const std::optional<CameraInfo> &camera) {
    if (mode != WorkspaceMode::Preview) {
        return PtzAvailabilityReason::PlaybackWorkspace;
    }
    if (!camera.has_value()) {
        return PtzAvailabilityReason::NoCameraSelected;
    }
    if (!camera->supportsPtz) {
        return PtzAvailabilityReason::Unsupported;
    }
    if (!camera->canControlPtz) {
        return PtzAvailabilityReason::PermissionDenied;
    }
    if (camera->connectivity != 1) {
        return PtzAvailabilityReason::CameraOffline;
    }
    return PtzAvailabilityReason::Available;
}

void PtzController::updateAvailability(bool forceSignal) {
    const PtzAvailabilityReason nextReason = evaluateAvailability(workspaceMode_, selectedCamera_);
    const bool changed = nextReason != availabilityReason_;
    availabilityReason_ = nextReason;
    if (changed || forceSignal) {
        emit availabilityChanged(available(), availabilityReason_, statusText());
    }
}
