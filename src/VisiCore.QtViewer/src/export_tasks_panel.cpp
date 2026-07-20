#include "export_tasks_panel.h"

#include "export_controller.h"
#include "icon_provider.h"

#include <QHeaderView>
#include <QHBoxLayout>
#include <QLabel>
#include <QPushButton>
#include <QTreeWidget>
#include <QVBoxLayout>

namespace {
QString displayStatus(const PlaybackExportInfo &exportInfo) {
    const QString status = exportInfo.status.toLower();
    if (status == QStringLiteral("queued")) return QStringLiteral("等待导出");
    if (status == QStringLiteral("running")) return QStringLiteral("正在导出");
    if (status == QStringLiteral("completed")) return QStringLiteral("可下载");
    if (status == QStringLiteral("cancelled")) return QStringLiteral("已取消");
    if (status == QStringLiteral("expired")) return QStringLiteral("已过期");
    if (status == QStringLiteral("failed")) return QStringLiteral("导出失败");
    return exportInfo.status.isEmpty() ? QStringLiteral("状态未知") : exportInfo.status;
}

QString displayFailure(const PlaybackExportInfo &exportInfo) {
    if (exportInfo.failureCode.isEmpty()) {
        return {};
    }
    return QStringLiteral("失败原因：%1").arg(exportInfo.failureCode);
}
}

ExportTasksPanel::ExportTasksPanel(ExportController *controller, QWidget *parent)
    : QFrame(parent), controller_(controller) {
    setObjectName(QStringLiteral("exportTasksPanel"));
    setMinimumHeight(180);
    auto *layout = new QVBoxLayout(this);
    layout->setContentsMargins(10, 10, 10, 10);
    layout->setSpacing(8);

    auto *summary = new QLabel(QStringLiteral("当前账号提交的录像导出任务"), this);
    summary->setObjectName(QStringLiteral("panelTitle"));
    layout->addWidget(summary);

    taskTree_ = new QTreeWidget(this);
    taskTree_->setObjectName(QStringLiteral("exportTaskTree"));
    taskTree_->setRootIsDecorated(false);
    taskTree_->setAlternatingRowColors(true);
    taskTree_->setHeaderLabels({QStringLiteral("摄像头"), QStringLiteral("时间范围"), QStringLiteral("状态")});
    taskTree_->header()->setSectionResizeMode(0, QHeaderView::ResizeToContents);
    taskTree_->header()->setSectionResizeMode(1, QHeaderView::Stretch);
    taskTree_->header()->setSectionResizeMode(2, QHeaderView::ResizeToContents);
    layout->addWidget(taskTree_, 1);

    emptyLabel_ = new QLabel(QStringLiteral("暂无导出任务"), this);
    emptyLabel_->setObjectName(QStringLiteral("emptyStateLabel"));
    emptyLabel_->setAlignment(Qt::AlignCenter);
    layout->addWidget(emptyLabel_);

    auto *refreshButton = new QPushButton(IconProvider::instance().icon(ViewerIcon::Refresh), QStringLiteral("刷新"), this);
    downloadButton_ = new QPushButton(IconProvider::instance().icon(ViewerIcon::Save), QStringLiteral("下载"), this);
    cancelButton_ = new QPushButton(IconProvider::instance().icon(ViewerIcon::Close), QStringLiteral("取消任务"), this);
    auto *buttons = new QHBoxLayout;
    buttons->setContentsMargins(0, 0, 0, 0);
    buttons->addWidget(refreshButton);
    buttons->addStretch(1);
    buttons->addWidget(cancelButton_);
    buttons->addWidget(downloadButton_);
    layout->addLayout(buttons);

    connect(controller_, &ExportController::exportsChanged, this, &ExportTasksPanel::rebuild);
    connect(taskTree_, &QTreeWidget::itemSelectionChanged, this, &ExportTasksPanel::updateSelection);
    connect(refreshButton, &QPushButton::clicked, this, &ExportTasksPanel::refreshRequested);
    connect(cancelButton_, &QPushButton::clicked, this, [this]() { emit cancelRequested(selectedExportId()); });
    connect(downloadButton_, &QPushButton::clicked, this, [this]() { emit downloadRequested(selectedExportId()); });
    rebuild();
}

void ExportTasksPanel::setCameraLabels(const QHash<QUuid, QString> &labels) {
    cameraLabels_ = labels;
    rebuild();
}

void ExportTasksPanel::rebuild() {
    const QUuid previous = selectedExportId();
    taskTree_->clear();
    const QList<PlaybackExportInfo> exports = controller_ != nullptr ? controller_->exports() : QList<PlaybackExportInfo>{};
    for (const PlaybackExportInfo &exportInfo : exports) {
        const QString cameraLabel = cameraLabels_.value(
            exportInfo.cameraId,
            QStringLiteral("摄像头 %1").arg(exportInfo.cameraId.toString(QUuid::WithoutBraces).left(8)));
        auto *item = new QTreeWidgetItem({
            cameraLabel,
            QStringLiteral("%1 至 %2").arg(
                exportInfo.startedAt.toLocalTime().toString(QStringLiteral("MM-dd HH:mm:ss")),
                exportInfo.endedAt.toLocalTime().toString(QStringLiteral("MM-dd HH:mm:ss"))),
            displayStatus(exportInfo)});
        item->setData(0, Qt::UserRole, exportInfo.id.toString(QUuid::WithoutBraces));
        item->setToolTip(2, displayFailure(exportInfo));
        if (!previous.isNull() && previous == exportInfo.id) {
            taskTree_->setCurrentItem(item);
        }
        taskTree_->addTopLevelItem(item);
    }
    emptyLabel_->setVisible(exports.isEmpty());
    taskTree_->setVisible(!exports.isEmpty());
    updateSelection();
}

void ExportTasksPanel::updateSelection() {
    const std::optional<PlaybackExportInfo> exportInfo = controller_ != nullptr
        ? controller_->find(selectedExportId())
        : std::nullopt;
    const bool canCancel = exportInfo.has_value() && ExportController::isCancellable(*exportInfo);
    const bool canDownload = exportInfo.has_value() && exportInfo->artifact.has_value() &&
        exportInfo->artifact->expiresAt > QDateTime::currentDateTimeUtc();
    cancelButton_->setEnabled(canCancel);
    downloadButton_->setEnabled(canDownload);
}

QUuid ExportTasksPanel::selectedExportId() const {
    const auto selected = taskTree_->selectedItems();
    return selected.isEmpty() ? QUuid{} : QUuid(selected.first()->data(0, Qt::UserRole).toString());
}
