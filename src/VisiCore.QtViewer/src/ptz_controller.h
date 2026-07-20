#pragma once

#include "models.h"
#include "viewer_ui_types.h"

#include <QObject>
#include <QTimer>
#include <optional>

enum class PtzAvailabilityReason {
    Available = 0,
    PlaybackWorkspace,
    NoCameraSelected,
    Unsupported,
    PermissionDenied,
    CameraOffline,
};

struct PtzPulseState {
    bool active = false;
    QUuid cameraId;
    int action = -1;
    int speed = 4;

    bool operator==(const PtzPulseState &) const = default;
};

class PtzController final : public QObject {
    Q_OBJECT

public:
    static constexpr int MinimumAction = 0;
    static constexpr int MaximumAction = 13;
    static constexpr int MinimumSpeed = 1;
    static constexpr int MaximumSpeed = 7;
    static constexpr int DefaultPulseIntervalMilliseconds = 1200;

    explicit PtzController(QObject *parent = nullptr);

    [[nodiscard]] WorkspaceMode workspaceMode() const;
    [[nodiscard]] std::optional<CameraInfo> selectedCamera() const;
    [[nodiscard]] bool available() const;
    [[nodiscard]] PtzAvailabilityReason availabilityReason() const;
    [[nodiscard]] QString statusText() const;
    [[nodiscard]] PtzPulseState pulseState() const;
    [[nodiscard]] int pulseIntervalMilliseconds() const;

    void setWorkspaceMode(WorkspaceMode mode);
    void setSelectedCamera(const CameraInfo &camera);
    void clearSelectedCamera();
    void setPulseIntervalMilliseconds(int intervalMilliseconds);
    bool beginPulse(int action, int speed);
    bool endPulse(int action);
    bool stopPulse();
    bool renewPulse();

signals:
    void availabilityChanged(bool available, PtzAvailabilityReason reason, const QString &statusText);
    void pulseStateChanged(const PtzPulseState &state);
    void ptzCommandRequested(const QUuid &cameraId, int action, int motion, int speed);
    void operationRejected(const QString &message);
    void stateChanged();

private:
    static QString connectivityLabel(int connectivity);
    static PtzAvailabilityReason evaluateAvailability(
        WorkspaceMode mode,
        const std::optional<CameraInfo> &camera);
    void updateAvailability(bool forceSignal = false);

    WorkspaceMode workspaceMode_ = WorkspaceMode::Preview;
    std::optional<CameraInfo> selectedCamera_;
    PtzAvailabilityReason availabilityReason_ = PtzAvailabilityReason::NoCameraSelected;
    PtzPulseState pulseState_;
    QTimer pulseTimer_;
};

Q_DECLARE_METATYPE(PtzAvailabilityReason)
Q_DECLARE_METATYPE(PtzPulseState)
