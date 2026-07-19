#include "recording_timeline_widget.h"

#include "viewer_logic.h"

#include <QKeyEvent>
#include <QMouseEvent>
#include <QPainter>
#include <QPaintEvent>
#include <QSizePolicy>
#include <QToolTip>
#include <QWheelEvent>

#include <algorithm>
#include <cstdlib>
#include <cmath>
#include <limits>

namespace {
constexpr int LabelWidth = 178;
constexpr int TrackHeight = 30;
constexpr int HeaderHeight = 34;
constexpr int BottomPadding = 21;
constexpr int HorizontalPadding = 10;
constexpr int TickCount = 6;

QColor segmentColor(const RecordingSegment &segment) {
    const QString type = segment.fileType.toLower();
    if (segment.locked) {
        return QColor(QStringLiteral("#D6A84A"));
    }
    if (type.contains(QStringLiteral("event")) || type.contains(QStringLiteral("alarm")) ||
        type.contains(QStringLiteral("motion"))) {
        return QColor(QStringLiteral("#DE6268"));
    }
    if (segment.approximate) {
        return QColor(QStringLiteral("#5A91C8"));
    }
    return QColor(QStringLiteral("#4CB782"));
}
}

RecordingTimelineWidget::RecordingTimelineWidget(QWidget *parent) : QWidget(parent) {
    setMinimumHeight(105);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    setMouseTracking(true);
    setFocusPolicy(Qt::StrongFocus);
    setAccessibleName(QStringLiteral("录像多轨时间轴"));
    QWidget::setCursor(Qt::PointingHandCursor);
}

void RecordingTimelineWidget::setRange(const QDateTime &startedAt, const QDateTime &endedAt) {
    const QDateTime localStartedAt = startedAt.toLocalTime();
    const QDateTime localEndedAt = endedAt.toLocalTime();
    const bool changed = localStartedAt != startedAt_ || localEndedAt != endedAt_;
    startedAt_ = localStartedAt;
    endedAt_ = localEndedAt;
    if (changed) {
        zoomFactor_ = 1.0;
        viewStartedAt_ = startedAt_;
        viewEndedAt_ = endedAt_;
        emit zoomChanged(zoomFactor_);
    }
    if (!cursor_.isValid() || cursor_ < startedAt_ || cursor_ > endedAt_) {
        cursor_ = startedAt_;
    }
    updateGeometry();
    update();
}

void RecordingTimelineWidget::setTracks(const QList<RecordingTimelineTrack> &tracks) {
    tracks_ = tracks;
    setMinimumHeight(std::max(105, HeaderHeight + BottomPadding + static_cast<int>(tracks_.size()) * TrackHeight));
    updateGeometry();
    update();
}

void RecordingTimelineWidget::setCursor(const QDateTime &position) {
    if (!position.isValid() || !startedAt_.isValid() || !endedAt_.isValid()) {
        return;
    }
    cursor_ = std::clamp(position.toLocalTime(), startedAt_, endedAt_);
    if (zoomFactor_ > 1.0 && (cursor_ < viewStartedAt_ || cursor_ > viewEndedAt_)) {
        const ViewerLogic::TimelineView view = ViewerLogic::zoomedTimelineView(
            startedAt_, endedAt_, cursor_, 0.5, zoomFactor_);
        viewStartedAt_ = view.startedAt;
        viewEndedAt_ = view.endedAt;
    }
    update();
}

void RecordingTimelineWidget::zoomIn() {
    setZoomFactor(zoomFactor_ * 1.5, cursor_, 0.5);
}

void RecordingTimelineWidget::zoomOut() {
    setZoomFactor(zoomFactor_ / 1.5, cursor_, 0.5);
}

void RecordingTimelineWidget::resetZoom() {
    setZoomFactor(1.0, startedAt_, 0.0);
}

double RecordingTimelineWidget::zoomFactor() const {
    return zoomFactor_;
}

QRect RecordingTimelineWidget::timelineRect() const {
    return {
        LabelWidth,
        HeaderHeight,
        std::max(1, width() - LabelWidth - HorizontalPadding),
        std::max(1, static_cast<int>(tracks_.size()) * TrackHeight)};
}

int RecordingTimelineWidget::xForPosition(const QDateTime &position) const {
    const QRect rect = timelineRect();
    const qint64 duration = viewStartedAt_.msecsTo(viewEndedAt_);
    if (duration <= 0) return rect.left();
    const qint64 offset = viewStartedAt_.msecsTo(position.toLocalTime());
    const double ratio = std::clamp(static_cast<double>(offset) / static_cast<double>(duration), 0.0, 1.0);
    return rect.left() + static_cast<int>(ratio * std::max(0, rect.width() - 1));
}

QDateTime RecordingTimelineWidget::positionForX(int x) const {
    const QRect rect = timelineRect();
    const qint64 duration = viewStartedAt_.msecsTo(viewEndedAt_);
    if (duration <= 0) return viewStartedAt_;
    const int pixelWidth = std::max(1, rect.width() - 1);
    const double ratio = std::clamp(static_cast<double>(x - rect.left()) / static_cast<double>(pixelWidth), 0.0, 1.0);
    return viewStartedAt_.addMSecs(static_cast<qint64>(duration * ratio));
}

int RecordingTimelineWidget::trackIndexForY(int y) const {
    const QRect rect = timelineRect();
    if (y < rect.top() || y > rect.bottom()) {
        return -1;
    }
    const int index = (y - rect.top()) / TrackHeight;
    return index >= 0 && index < tracks_.size() ? index : -1;
}

QDateTime RecordingTimelineWidget::nearestSegmentPosition(int trackIndex, const QDateTime &position) const {
    if (trackIndex < 0 || trackIndex >= tracks_.size() || tracks_.at(trackIndex).segments.isEmpty()) {
        return {};
    }
    QDateTime nearest;
    qint64 nearestDistance = std::numeric_limits<qint64>::max();
    for (const RecordingSegment &segment : tracks_.at(trackIndex).segments) {
        const QDateTime segmentStart = segment.startedAt.toLocalTime();
        const QDateTime segmentEnd = segment.endedAt.toLocalTime();
        if (position >= segmentStart && position <= segmentEnd) {
            return position;
        }
        const qint64 startDistance = std::llabs(position.msecsTo(segmentStart));
        if (startDistance < nearestDistance) {
            nearest = segmentStart;
            nearestDistance = startDistance;
        }
        const QDateTime safeEnd = segmentEnd > segmentStart ? segmentEnd.addMSecs(-1) : segmentEnd;
        const qint64 endDistance = std::llabs(position.msecsTo(safeEnd));
        if (endDistance < nearestDistance) {
            nearest = safeEnd;
            nearestDistance = endDistance;
        }
    }
    const qint64 visibleDuration = std::max<qint64>(1, viewStartedAt_.msecsTo(viewEndedAt_));
    const qint64 pixelTolerance = visibleDuration * 6 / std::max(1, timelineRect().width());
    const qint64 snapTolerance = std::clamp<qint64>(pixelTolerance, 1000, 5 * 60 * 1000);
    return nearestDistance <= snapTolerance ? nearest : QDateTime{};
}

void RecordingTimelineWidget::paintEvent(QPaintEvent *event) {
    QWidget::paintEvent(event);
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, false);
    painter.fillRect(rect(), QColor(QStringLiteral("#0B0E11")));

    const QRect plot = timelineRect();
    painter.setPen(QColor(QStringLiteral("#98A2A9")));
    painter.drawText(
        QRect(8, 4, LabelWidth - 16, HeaderHeight - 8),
        Qt::AlignLeft | Qt::AlignVCenter,
        QStringLiteral("录像轨道 · %1x").arg(zoomFactor_, 0, 'f', zoomFactor_ < 10.0 ? 1 : 0));

    for (int tick = 0; tick <= TickCount; ++tick) {
        const double ratio = static_cast<double>(tick) / TickCount;
        const int x = plot.left() + static_cast<int>(ratio * std::max(0, plot.width() - 1));
        painter.setPen(QColor(QStringLiteral("#293138")));
        painter.drawLine(x, plot.top(), x, plot.bottom());
        const QDateTime tickTime = viewStartedAt_.addMSecs(
            static_cast<qint64>(viewStartedAt_.msecsTo(viewEndedAt_) * ratio));
        painter.setPen(QColor(QStringLiteral("#89949B")));
        const QString label = viewStartedAt_.date() == viewEndedAt_.date()
            ? tickTime.toString(QStringLiteral("HH:mm:ss"))
            : tickTime.toString(QStringLiteral("MM-dd HH:mm"));
        const QRect labelRect(x - 44, 5, 88, HeaderHeight - 8);
        painter.drawText(labelRect, Qt::AlignCenter, label);
    }

    for (int index = 0; index < tracks_.size(); ++index) {
        const RecordingTimelineTrack &track = tracks_.at(index);
        const int y = plot.top() + index * TrackHeight;
        const QRect rowRect(0, y, width(), TrackHeight);
        painter.fillRect(rowRect, track.selected
                                      ? QColor(QStringLiteral("#183036"))
                                      : QColor(index % 2 == 0 ? QStringLiteral("#15191D") : QStringLiteral("#111519")));
        painter.setPen(QColor(QStringLiteral("#303840")));
        painter.drawLine(0, y + TrackHeight - 1, width(), y + TrackHeight - 1);
        const QString rowText = track.status.isEmpty()
            ? track.label
            : QStringLiteral("%1  ·  %2").arg(track.label, track.status);
        painter.setPen(track.selected ? QColor(QStringLiteral("#F2F5F7")) : QColor(QStringLiteral("#AAB4BA")));
        painter.drawText(
            QRect(8, y, LabelWidth - 16, TrackHeight),
            Qt::AlignLeft | Qt::AlignVCenter,
            painter.fontMetrics().elidedText(rowText, Qt::ElideRight, LabelWidth - 20));

        for (const RecordingSegment &segment : track.segments) {
            const QDateTime segmentStart = std::max(segment.startedAt.toLocalTime(), viewStartedAt_);
            const QDateTime segmentEnd = std::min(segment.endedAt.toLocalTime(), viewEndedAt_);
            if (segmentStart >= segmentEnd) {
                continue;
            }
            const int left = xForPosition(segmentStart);
            const int right = std::max(left + 2, xForPosition(segmentEnd));
            const QRect segmentRect(left, y + 7, std::max(2, right - left), TrackHeight - 14);
            painter.fillRect(segmentRect, segmentColor(segment));
        }
    }

    if (cursor_.isValid() && cursor_ >= viewStartedAt_ && cursor_ <= viewEndedAt_) {
        const int cursorX = xForPosition(cursor_);
        painter.setPen(QPen(QColor(QStringLiteral("#E6B85C")), 2));
        painter.drawLine(cursorX, plot.top(), cursorX, plot.bottom());
        const QString cursorText = cursor_.toString(QStringLiteral("yyyy-MM-dd HH:mm:ss"));
        const int labelWidth = 138;
        const int labelX = std::clamp(cursorX - labelWidth / 2, plot.left(), std::max(plot.left(), plot.right() - labelWidth));
        painter.fillRect(QRect(labelX, plot.bottom() + 2, labelWidth, 18), QColor(QStringLiteral("#3B321F")));
        painter.setPen(QColor(QStringLiteral("#F1D184")));
        painter.drawText(QRect(labelX, plot.bottom() + 2, labelWidth, 18), Qt::AlignCenter, cursorText);
    }

    if (tracks_.isEmpty()) {
        painter.setPen(QColor(QStringLiteral("#7F8A91")));
        painter.drawText(rect(), Qt::AlignCenter, QStringLiteral("分配回放摄像头后显示录像时间轴"));
    }
}

void RecordingTimelineWidget::mousePressEvent(QMouseEvent *event) {
    if (event->button() == Qt::LeftButton && startedAt_.isValid() && endedAt_.isValid() && startedAt_ < endedAt_) {
        const QPoint position = event->position().toPoint();
        const int trackIndex = trackIndexForY(position.y());
        if (trackIndex >= 0) {
            emit trackSelected(trackIndex);
            setFocus();
            if (!timelineRect().contains(position)) {
                event->accept();
                return;
            }
            const QDateTime requested = positionForX(position.x());
            const QDateTime selected = nearestSegmentPosition(trackIndex, requested);
            if (selected.isValid()) {
                emit positionSelected(selected);
            } else {
                emit positionUnavailable(requested);
            }
            event->accept();
            return;
        }
    }
    QWidget::mousePressEvent(event);
}

void RecordingTimelineWidget::mouseMoveEvent(QMouseEvent *event) {
    const QRect plot = timelineRect();
    if (plot.contains(event->position().toPoint()) && viewStartedAt_.isValid() && viewEndedAt_.isValid()) {
        const int trackIndex = trackIndexForY(event->position().toPoint().y());
        const QDateTime position = positionForX(event->position().toPoint().x());
        QString text = position.toString(QStringLiteral("yyyy-MM-dd HH:mm:ss"));
        if (trackIndex >= 0 && trackIndex < tracks_.size()) {
            text.prepend(QStringLiteral("%1\n").arg(tracks_.at(trackIndex).label));
        }
        QToolTip::showText(event->globalPosition().toPoint(), text, this);
    } else {
        QToolTip::hideText();
    }
    QWidget::mouseMoveEvent(event);
}

void RecordingTimelineWidget::wheelEvent(QWheelEvent *event) {
    if (!startedAt_.isValid() || !endedAt_.isValid() || startedAt_ >= endedAt_) {
        event->ignore();
        return;
    }
    if (event->modifiers().testFlag(Qt::ShiftModifier) && zoomFactor_ > 1.0) {
        const qint64 visibleDuration = viewStartedAt_.msecsTo(viewEndedAt_);
        panByMilliseconds(event->angleDelta().y() > 0 ? -visibleDuration / 5 : visibleDuration / 5);
    } else {
        const QRect plot = timelineRect();
        const double ratio = std::clamp(
            static_cast<double>(event->position().x() - plot.left()) / std::max(1, plot.width() - 1),
            0.0,
            1.0);
        const QDateTime anchor = positionForX(static_cast<int>(event->position().x()));
        setZoomFactor(event->angleDelta().y() > 0 ? zoomFactor_ * 1.5 : zoomFactor_ / 1.5, anchor, ratio);
    }
    event->accept();
}

void RecordingTimelineWidget::keyPressEvent(QKeyEvent *event) {
    if (event->key() == Qt::Key_Plus || event->key() == Qt::Key_Equal) {
        zoomIn();
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_Minus) {
        zoomOut();
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_Home) {
        resetZoom();
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_Left || event->key() == Qt::Key_Right) {
        const qint64 step = std::max<qint64>(1000, viewStartedAt_.msecsTo(viewEndedAt_) / 100);
        const QDateTime requested = cursor_.addMSecs(event->key() == Qt::Key_Left ? -step : step);
        const auto selectedIterator = std::find_if(
            tracks_.cbegin(),
            tracks_.cend(),
            [](const RecordingTimelineTrack &track) { return track.selected; });
        const int selectedTrack = selectedIterator == tracks_.cend()
            ? -1
            : static_cast<int>(std::distance(tracks_.cbegin(), selectedIterator));
        const QDateTime selected = nearestSegmentPosition(selectedTrack, std::clamp(requested, startedAt_, endedAt_));
        if (selected.isValid()) emit positionSelected(selected);
        else emit positionUnavailable(requested);
        event->accept();
        return;
    }
    QWidget::keyPressEvent(event);
}

void RecordingTimelineWidget::setZoomFactor(double factor, const QDateTime &anchor, double anchorRatio) {
    const double boundedFactor = std::clamp(factor, 1.0, 64.0);
    const ViewerLogic::TimelineView view = ViewerLogic::zoomedTimelineView(
        startedAt_, endedAt_, anchor, anchorRatio, boundedFactor);
    if (!view.startedAt.isValid() || !view.endedAt.isValid()) {
        return;
    }
    const bool factorChanged = !qFuzzyCompare(zoomFactor_, boundedFactor);
    zoomFactor_ = boundedFactor;
    viewStartedAt_ = view.startedAt;
    viewEndedAt_ = view.endedAt;
    if (factorChanged) emit zoomChanged(zoomFactor_);
    update();
}

void RecordingTimelineWidget::panByMilliseconds(qint64 milliseconds) {
    if (zoomFactor_ <= 1.0 || milliseconds == 0) {
        return;
    }
    const qint64 visibleDuration = viewStartedAt_.msecsTo(viewEndedAt_);
    QDateTime startedAt = viewStartedAt_.addMSecs(milliseconds);
    QDateTime endedAt = startedAt.addMSecs(visibleDuration);
    if (startedAt < startedAt_) {
        startedAt = startedAt_;
        endedAt = startedAt.addMSecs(visibleDuration);
    }
    if (endedAt > endedAt_) {
        endedAt = endedAt_;
        startedAt = endedAt.addMSecs(-visibleDuration);
    }
    viewStartedAt_ = startedAt;
    viewEndedAt_ = endedAt;
    update();
}
