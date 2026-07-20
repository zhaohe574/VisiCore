#include "screenshot_service.h"

#include <QCryptographicHash>
#include <QDateTime>
#include <QDir>
#include <QSaveFile>
#include <QStandardPaths>

QString ScreenshotService::directoryForUser(const QString &username, const QString &picturesRoot) {
    const QString accountHash = QString::fromLatin1(QCryptographicHash::hash(
        username.trimmed().toUtf8(), QCryptographicHash::Sha256).toHex());
    const QString pictures = picturesRoot.isEmpty()
        ? QStandardPaths::writableLocation(QStandardPaths::PicturesLocation)
        : picturesRoot;
    return QDir(pictures).filePath(QStringLiteral("VisiCore/%1/截图").arg(accountHash));
}

ScreenshotSaveResult ScreenshotService::savePng(
    const QString &username,
    const QUuid &cameraId,
    const QImage &image,
    const QString &picturesRoot) {
    if (image.isNull()) {
        return {{}, QStringLiteral("当前窗格尚未生成可保存的视频画面。")};
    }
    const QString directory = directoryForUser(username, picturesRoot);
    if (!QDir().mkpath(directory)) {
        return {{}, QStringLiteral("无法创建截图目录。")};
    }
    const QString fileName = QStringLiteral("截图-%1-%2.png")
        .arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd-HHmmss-zzz")),
             cameraId.toString(QUuid::WithoutBraces).left(8));
    const QString filePath = QDir(directory).filePath(fileName);
    QSaveFile file(filePath);
    if (!file.open(QIODevice::WriteOnly) || !image.save(&file, "PNG") || !file.commit()) {
        return {{}, QStringLiteral("无法写入截图文件。")};
    }
    return {filePath, {}};
}
