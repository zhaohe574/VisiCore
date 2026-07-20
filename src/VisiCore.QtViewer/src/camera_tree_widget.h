#pragma once

#include <QTreeWidget>

namespace CatalogRoles {

inline constexpr int ResourceId = Qt::UserRole;
inline constexpr int ResourceKind = Qt::UserRole + 1;
inline constexpr int ViewIndex = Qt::UserRole + 2;
inline constexpr int BookmarkIndex = Qt::UserRole + 3;
inline QString cameraIdsMimeType() { return QStringLiteral("application/x-visicore-camera-ids"); }

}

class CameraTreeWidget final : public QTreeWidget {
    Q_OBJECT

public:
    explicit CameraTreeWidget(QWidget *parent = nullptr);

protected:
    void startDrag(Qt::DropActions supportedActions) override;

private:
    static void appendCameraIds(QTreeWidgetItem *item, QStringList *cameraIds);
};
