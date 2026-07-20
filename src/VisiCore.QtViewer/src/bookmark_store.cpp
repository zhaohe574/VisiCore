#include "bookmark_store.h"

#include <QCryptographicHash>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QSaveFile>
#include <QStandardPaths>

#include <algorithm>
#include <utility>

namespace {
QString bookmarkDirectory(const QString &localDataRoot) {
    if (!localDataRoot.isEmpty()) {
        return QDir(localDataRoot).filePath(QStringLiteral("bookmarks"));
    }
    return QDir(QStandardPaths::writableLocation(QStandardPaths::AppLocalDataLocation))
        .filePath(QStringLiteral("bookmarks"));
}

QJsonObject toJson(const PlaybackBookmark &bookmark) {
    return {
        {QStringLiteral("id"), bookmark.id.toString(QUuid::WithoutBraces)},
        {QStringLiteral("cameraId"), bookmark.cameraId.toString(QUuid::WithoutBraces)},
        {QStringLiteral("cameraLabel"), bookmark.cameraLabel},
        {QStringLiteral("position"), bookmark.position.toUTC().toString(Qt::ISODateWithMs)},
        {QStringLiteral("createdAt"), bookmark.createdAt.toUTC().toString(Qt::ISODateWithMs)},
        {QStringLiteral("title"), bookmark.title},
        {QStringLiteral("note"), bookmark.note}};
}

std::optional<PlaybackBookmark> fromJson(const QJsonObject &object) {
    PlaybackBookmark bookmark;
    bookmark.id = QUuid(object.value(QStringLiteral("id")).toString());
    bookmark.cameraId = QUuid(object.value(QStringLiteral("cameraId")).toString());
    bookmark.cameraLabel = object.value(QStringLiteral("cameraLabel")).toString();
    bookmark.position = QDateTime::fromString(object.value(QStringLiteral("position")).toString(), Qt::ISODate);
    bookmark.createdAt = QDateTime::fromString(object.value(QStringLiteral("createdAt")).toString(), Qt::ISODate);
    bookmark.title = object.value(QStringLiteral("title")).toString().left(80);
    bookmark.note = object.value(QStringLiteral("note")).toString().left(500);
    if (bookmark.id.isNull() || bookmark.cameraId.isNull() || !bookmark.position.isValid() || !bookmark.createdAt.isValid()) {
        return std::nullopt;
    }
    return bookmark;
}
}

BookmarkStore::BookmarkStore(QString username, QString localDataRoot)
    : accountHash_(QString::fromLatin1(QCryptographicHash::hash(
          username.trimmed().toUtf8(), QCryptographicHash::Sha256).toHex())),
      localDataRoot_(std::move(localDataRoot)) {
}

QList<PlaybackBookmark> BookmarkStore::load(QString *errorMessage) const {
    QFile file(storagePath());
    if (!file.exists()) {
        return {};
    }
    if (!file.open(QIODevice::ReadOnly)) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("无法读取本地书签。");
        }
        return {};
    }
    QJsonParseError error;
    const QJsonDocument document = QJsonDocument::fromJson(file.readAll(), &error);
    if (error.error != QJsonParseError::NoError || !document.isArray()) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("本地书签文件格式无效，已忽略。");
        }
        return {};
    }
    QList<PlaybackBookmark> bookmarks;
    for (const QJsonValue &value : document.array()) {
        const std::optional<PlaybackBookmark> bookmark = fromJson(value.toObject());
        if (bookmark.has_value()) {
            bookmarks.append(*bookmark);
        }
    }
    std::sort(bookmarks.begin(), bookmarks.end(), [](const PlaybackBookmark &left, const PlaybackBookmark &right) {
        return left.createdAt > right.createdAt;
    });
    return bookmarks;
}

bool BookmarkStore::save(const QList<PlaybackBookmark> &bookmarks, QString *errorMessage) const {
    const QString directory = QFileInfo(storagePath()).absolutePath();
    if (!QDir().mkpath(directory)) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("无法创建本地书签目录。");
        }
        return false;
    }
    QJsonArray values;
    for (const PlaybackBookmark &bookmark : bookmarks) {
        values.append(toJson(bookmark));
    }
    QSaveFile file(storagePath());
    if (!file.open(QIODevice::WriteOnly) || file.write(QJsonDocument(values).toJson(QJsonDocument::Compact)) < 0 ||
        !file.commit()) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("无法保存本地书签。");
        }
        return false;
    }
    return true;
}

QString BookmarkStore::storagePath() const {
    return QDir(bookmarkDirectory(localDataRoot_)).filePath(accountHash_ + QStringLiteral(".json"));
}
