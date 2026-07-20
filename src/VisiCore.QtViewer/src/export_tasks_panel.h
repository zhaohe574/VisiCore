#pragma once

#include <QFrame>
#include <QHash>
#include <QUuid>

class ExportController;
class QLabel;
class QPushButton;
class QTreeWidget;

class ExportTasksPanel final : public QFrame {
    Q_OBJECT

public:
    explicit ExportTasksPanel(ExportController *controller, QWidget *parent = nullptr);

    void setCameraLabels(const QHash<QUuid, QString> &labels);

signals:
    void refreshRequested();
    void cancelRequested(const QUuid &exportId);
    void downloadRequested(const QUuid &exportId);

private:
    void rebuild();
    void updateSelection();
    [[nodiscard]] QUuid selectedExportId() const;

    ExportController *controller_ = nullptr;
    QHash<QUuid, QString> cameraLabels_;
    QTreeWidget *taskTree_ = nullptr;
    QLabel *emptyLabel_ = nullptr;
    QPushButton *downloadButton_ = nullptr;
    QPushButton *cancelButton_ = nullptr;
};
