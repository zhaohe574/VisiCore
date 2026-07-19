#pragma once

#include "viewer_ui_types.h"

#include <QHash>
#include <QList>
#include <QObject>
#include <QSet>
#include <QString>

class PreviewController final : public QObject {
    Q_OBJECT

public:
    static constexpr int MaximumTileCount = 64;

    explicit PreviewController(QObject *parent = nullptr);

    [[nodiscard]] static QList<int> supportedLayouts();
    [[nodiscard]] static bool isSupportedLayout(int count);
    [[nodiscard]] static int gridDimension(int count);
    [[nodiscard]] static int bestLayoutForCameraCount(int cameraCount);

    [[nodiscard]] int layoutCount() const;
    [[nodiscard]] int selectedTileIndex() const;
    [[nodiscard]] bool isTileVisible(int tileIndex) const;
    [[nodiscard]] bool isTileAssigned(int tileIndex) const;
    [[nodiscard]] QSet<int> assignedTileIndices() const;
    [[nodiscard]] int activeSessionCount() const;
    [[nodiscard]] QString streamMode() const;
    [[nodiscard]] QString effectiveStreamProfile() const;
    [[nodiscard]] bool compactTiles() const;
    [[nodiscard]] bool tourActive() const;
    [[nodiscard]] int tourCandidateCount() const;
    [[nodiscard]] bool isActionEnabled(ViewerActionId actionId) const;

    bool setLayoutCount(int count);
    bool selectTile(int tileIndex);
    bool setTileAssigned(int tileIndex, bool assigned);
    void clearAssignments();
    void setActiveSessionCount(int count);
    void setStreamMode(const QString &mode);
    void setTourCandidateCount(int count);
    bool setTourActive(bool active);

signals:
    void layoutChanged(int count, int dimension);
    void selectedTileChanged(int tileIndex);
    void streamProfileChanged(const QString &mode, const QString &effectiveProfile);
    void tourStateChanged(bool active);
    void actionAvailabilityChanged(ViewerActionId actionId, bool enabled);
    void stateChanged();
    void operationRejected(const QString &message);

private:
    static QString normalizeStreamMode(const QString &mode);
    void publishActionAvailability();

    int layoutCount_ = 4;
    int selectedTileIndex_ = 0;
    int activeSessionCount_ = 0;
    int tourCandidateCount_ = 0;
    QString streamMode_ = QStringLiteral("auto");
    bool tourActive_ = false;
    QSet<int> assignedTileIndices_;
    QHash<int, bool> publishedActionStates_;
};
