#pragma once

#include "models.h"
#include "viewer_ui_types.h"

#include <QDateTime>
#include <QHash>
#include <QList>
#include <QObject>
#include <QUuid>

struct PlaybackTileState {
    QUuid cameraId;
    QUuid sessionId;
    bool syncMember = true;
    bool commandPending = false;
    PlaybackTransportInfo transport;

    [[nodiscard]] bool assigned() const { return !cameraId.isNull(); }
    [[nodiscard]] bool sessionReady() const { return !sessionId.isNull(); }
};

struct PlaybackControlState {
    bool hasTargets = false;
    bool ready = false;
    bool pending = false;
    bool canPause = false;
    bool canSeek = false;
    bool canChangeSpeed = false;
    bool anyPaused = false;
    bool allPaused = false;
    bool pauseEnabled = false;
    bool resumeEnabled = false;
    bool seekEnabled = false;
    bool speedEnabled = false;

    bool operator==(const PlaybackControlState &) const = default;
};

class PlaybackController final : public QObject {
    Q_OBJECT

public:
    static constexpr int MaximumTileCount = 4;
    static constexpr qint64 MaximumRangeSeconds = 31LL * 24LL * 60LL * 60LL;

    explicit PlaybackController(QObject *parent = nullptr);

    [[nodiscard]] static QList<int> supportedLayouts();
    [[nodiscard]] static bool isSupportedLayout(int count);
    [[nodiscard]] static bool validateTimeRange(
        const QDateTime &startedAt,
        const QDateTime &endedAt,
        QString *errorMessage = nullptr);

    [[nodiscard]] int layoutCount() const;
    [[nodiscard]] int selectedTileIndex() const;
    [[nodiscard]] bool syncEnabled() const;
    [[nodiscard]] bool batchPending() const;
    [[nodiscard]] bool isTileVisible(int tileIndex) const;
    [[nodiscard]] PlaybackTileState tileState(int tileIndex) const;
    [[nodiscard]] QList<int> activeAssignedTileIndices() const;
    [[nodiscard]] QList<int> controlTargetIndices() const;
    [[nodiscard]] PlaybackControlState controlState() const;
    [[nodiscard]] bool isActionEnabled(ViewerActionId actionId) const;

    bool setLayoutCount(int count);
    bool selectTile(int tileIndex);
    bool setTileState(int tileIndex, const PlaybackTileState &state);
    bool clearTile(int tileIndex);
    void clearAllTiles();
    void setSyncEnabled(bool enabled);
    void setBatchPending(bool pending);

signals:
    void layoutChanged(int count, int dimension);
    void selectedTileChanged(int tileIndex);
    void syncEnabledChanged(bool enabled);
    void controlTargetsChanged(const QList<int> &tileIndices);
    void controlStateChanged(const PlaybackControlState &state);
    void actionAvailabilityChanged(ViewerActionId actionId, bool enabled);
    void stateChanged();
    void operationRejected(const QString &message);

private:
    void publishDerivedState();

    int layoutCount_ = 1;
    int selectedTileIndex_ = 0;
    bool syncEnabled_ = true;
    bool batchPending_ = false;
    QHash<int, PlaybackTileState> tileStates_;
    QList<int> publishedTargets_;
    PlaybackControlState publishedControlState_;
    bool hasPublishedControlState_ = false;
    QHash<int, bool> publishedActionStates_;
};

Q_DECLARE_METATYPE(PlaybackTileState)
Q_DECLARE_METATYPE(PlaybackControlState)
