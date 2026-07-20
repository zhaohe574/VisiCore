#pragma once

#include "models.h"

#include <QWidget>

struct RecordingTimelineTrack {
    QString label;
    QString status;
    QList<RecordingSegment> segments;
    bool selected = false;
};

class RecordingTimelineWidget final : public QWidget {
    Q_OBJECT

public:
    explicit RecordingTimelineWidget(QWidget *parent = nullptr);

    void setRange(const QDateTime &startedAt, const QDateTime &endedAt);
    void setTracks(const QList<RecordingTimelineTrack> &tracks);
    void setCursor(const QDateTime &position);
    void zoomIn();
    void zoomOut();
    void resetZoom();
    [[nodiscard]] double zoomFactor() const;

signals:
    void positionSelected(const QDateTime &position);
    void positionUnavailable(const QDateTime &position);
    void trackSelected(int trackIndex);
    void zoomChanged(double factor);

protected:
    void paintEvent(QPaintEvent *event) override;
    void mousePressEvent(QMouseEvent *event) override;
    void mouseMoveEvent(QMouseEvent *event) override;
    void wheelEvent(QWheelEvent *event) override;
    void keyPressEvent(QKeyEvent *event) override;

private:
    [[nodiscard]] QRect timelineRect() const;
    [[nodiscard]] QDateTime positionForX(int x) const;
    [[nodiscard]] int xForPosition(const QDateTime &position) const;
    [[nodiscard]] int trackIndexForY(int y) const;
    [[nodiscard]] QDateTime nearestSegmentPosition(int trackIndex, const QDateTime &position) const;
    void setZoomFactor(double factor, const QDateTime &anchor, double anchorRatio);
    void panByMilliseconds(qint64 milliseconds);

    QDateTime startedAt_;
    QDateTime endedAt_;
    QDateTime viewStartedAt_;
    QDateTime viewEndedAt_;
    QDateTime cursor_;
    QList<RecordingTimelineTrack> tracks_;
    double zoomFactor_ = 1.0;
};
