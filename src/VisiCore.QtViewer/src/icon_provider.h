#pragma once

#include <QColor>
#include <QHash>
#include <QIcon>
#include <QSize>
#include <QStringList>

enum class ViewerIcon {
    None = 0,
    Camera,
    Folder,
    Video,
    Play,
    Pause,
    Stop,
    Refresh,
    Search,
    Star,
    Save,
    LayoutGrid,
    Maximize,
    Minimize,
    PanelLeft,
    PanelRight,
    PanelBottom,
    Lock,
    Unlock,
    Restore,
    More,
    User,
    Logout,
    Password,
    ChevronDown,
    SkipBack,
    SkipForward,
    ZoomIn,
    ZoomOut,
    CalendarSearch,
    Alert,
    Success,
    Help,
    MoveUpLeft,
    MoveUpRight,
    MoveDownLeft,
    MoveDownRight,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Plus,
    Minus,
    Close,
    Settings,
};

class IconProvider final {
public:
    static IconProvider &instance();

    [[nodiscard]] QIcon icon(ViewerIcon icon, const QSize &logicalSize = QSize(20, 20));
    [[nodiscard]] QIcon icon(ViewerIcon icon, const QColor &color, const QSize &logicalSize = QSize(20, 20));
    [[nodiscard]] QString resourcePath(ViewerIcon icon) const;
    [[nodiscard]] bool hasResource(ViewerIcon icon) const;
    [[nodiscard]] QStringList missingResources() const;
    void clearCache();

private:
    IconProvider() = default;

    [[nodiscard]] QIcon createIcon(ViewerIcon icon, const QColor &color, const QSize &logicalSize) const;
    [[nodiscard]] QPixmap renderPixmap(ViewerIcon icon, const QColor &color, const QSize &logicalSize, qreal scale) const;

    QHash<QString, QIcon> cache_;
};
