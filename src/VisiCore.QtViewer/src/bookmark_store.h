#pragma once

#include <QDateTime>
#include <QList>
#include <QString>
#include <QUuid>

#include <optional>

struct PlaybackBookmark {
    QUuid id;
    QUuid cameraId;
    QString cameraLabel;
    QDateTime position;
    QDateTime createdAt;
    QString title;
    QString note;
};

class BookmarkStore final {
public:
    explicit BookmarkStore(QString username, QString localDataRoot = {});

    [[nodiscard]] QList<PlaybackBookmark> load(QString *errorMessage = nullptr) const;
    bool save(const QList<PlaybackBookmark> &bookmarks, QString *errorMessage = nullptr) const;
    [[nodiscard]] QString storagePath() const;

private:
    QString accountHash_;
    QString localDataRoot_;
};
