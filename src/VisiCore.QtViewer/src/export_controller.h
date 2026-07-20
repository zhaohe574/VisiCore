#pragma once

#include "models.h"

#include <QObject>

class ExportController final : public QObject {
    Q_OBJECT

public:
    explicit ExportController(QObject *parent = nullptr);

    void setExports(QList<PlaybackExportInfo> exports);
    void upsert(const PlaybackExportInfo &exportInfo);
    void markCancelled(const QUuid &exportId);
    [[nodiscard]] QList<PlaybackExportInfo> exports() const;
    [[nodiscard]] std::optional<PlaybackExportInfo> find(const QUuid &exportId) const;
    [[nodiscard]] bool hasActiveExports() const;
    [[nodiscard]] static bool isCancellable(const PlaybackExportInfo &exportInfo);

signals:
    void exportsChanged();

private:
    QList<PlaybackExportInfo> exports_;
};
