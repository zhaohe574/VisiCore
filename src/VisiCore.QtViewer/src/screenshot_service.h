#pragma once

#include <QImage>
#include <QString>
#include <QUuid>

struct ScreenshotSaveResult {
    QString filePath;
    QString errorMessage;

    [[nodiscard]] bool succeeded() const { return !filePath.isEmpty(); }
};

class ScreenshotService final {
public:
    static ScreenshotSaveResult savePng(
        const QString &username,
        const QUuid &cameraId,
        const QImage &image,
        const QString &picturesRoot = {});
    [[nodiscard]] static QString directoryForUser(const QString &username, const QString &picturesRoot = {});
};
