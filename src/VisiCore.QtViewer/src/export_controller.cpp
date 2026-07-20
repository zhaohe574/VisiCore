#include "export_controller.h"

#include <algorithm>

ExportController::ExportController(QObject *parent)
    : QObject(parent) {
}

void ExportController::setExports(QList<PlaybackExportInfo> exports) {
    std::sort(exports.begin(), exports.end(), [](const PlaybackExportInfo &left, const PlaybackExportInfo &right) {
        return left.requestedAt > right.requestedAt;
    });
    exports_ = std::move(exports);
    emit exportsChanged();
}

void ExportController::upsert(const PlaybackExportInfo &exportInfo) {
    for (PlaybackExportInfo &existing : exports_) {
        if (existing.id == exportInfo.id) {
            existing = exportInfo;
            emit exportsChanged();
            return;
        }
    }
    exports_.prepend(exportInfo);
    emit exportsChanged();
}

void ExportController::markCancelled(const QUuid &exportId) {
    for (PlaybackExportInfo &existing : exports_) {
        if (existing.id == exportId) {
            existing.status = QStringLiteral("Cancelled");
            existing.failureCode.clear();
            emit exportsChanged();
            return;
        }
    }
}

QList<PlaybackExportInfo> ExportController::exports() const {
    return exports_;
}

std::optional<PlaybackExportInfo> ExportController::find(const QUuid &exportId) const {
    for (const PlaybackExportInfo &existing : exports_) {
        if (existing.id == exportId) {
            return existing;
        }
    }
    return std::nullopt;
}

bool ExportController::hasActiveExports() const {
    return std::any_of(exports_.cbegin(), exports_.cend(), [](const PlaybackExportInfo &exportInfo) {
        return isCancellable(exportInfo);
    });
}

bool ExportController::isCancellable(const PlaybackExportInfo &exportInfo) {
    return exportInfo.status.compare(QStringLiteral("Queued"), Qt::CaseInsensitive) == 0 ||
           exportInfo.status.compare(QStringLiteral("Running"), Qt::CaseInsensitive) == 0;
}
