#include "camera_tree_widget.h"

#include <QDrag>
#include <QMimeData>

CameraTreeWidget::CameraTreeWidget(QWidget *parent) : QTreeWidget(parent) {
    setDragEnabled(true);
    setDragDropMode(QAbstractItemView::DragOnly);
    setDefaultDropAction(Qt::CopyAction);
}

void CameraTreeWidget::startDrag(Qt::DropActions) {
    QTreeWidgetItem *item = currentItem();
    if (item == nullptr) {
        return;
    }
    QStringList cameraIds;
    appendCameraIds(item, &cameraIds);
    cameraIds.removeDuplicates();
    if (cameraIds.isEmpty()) {
        return;
    }
    auto *mimeData = new QMimeData;
    mimeData->setData(CatalogRoles::cameraIdsMimeType(), cameraIds.join(u'\n').toUtf8());
    auto *drag = new QDrag(this);
    drag->setMimeData(mimeData);
    drag->exec(Qt::CopyAction);
}

void CameraTreeWidget::appendCameraIds(QTreeWidgetItem *item, QStringList *cameraIds) {
    if (item == nullptr || cameraIds == nullptr) {
        return;
    }
    const QString kind = item->data(0, CatalogRoles::ResourceKind).toString();
    if (kind == QStringLiteral("camera") || kind == QStringLiteral("viewSlot")) {
        const QString cameraId = item->data(0, CatalogRoles::ResourceId).toString();
        if (!cameraId.isEmpty()) {
            cameraIds->append(cameraId);
        }
        return;
    }
    for (int index = 0; index < item->childCount(); ++index) {
        appendCameraIds(item->child(index), cameraIds);
    }
}
