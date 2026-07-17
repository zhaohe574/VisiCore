#include "icon_provider.h"

#include "theme_manager.h"

#include <QDebug>
#include <QFile>
#include <QPainter>
#include <QPixmap>

namespace {
QString iconFileName(ViewerIcon icon) {
    switch (icon) {
        case ViewerIcon::Camera: return QStringLiteral("camera");
        case ViewerIcon::Folder: return QStringLiteral("folder");
        case ViewerIcon::Video: return QStringLiteral("video");
        case ViewerIcon::Play: return QStringLiteral("play");
        case ViewerIcon::Pause: return QStringLiteral("pause");
        case ViewerIcon::Stop: return QStringLiteral("square");
        case ViewerIcon::Refresh: return QStringLiteral("refresh-cw");
        case ViewerIcon::Search: return QStringLiteral("search");
        case ViewerIcon::Star: return QStringLiteral("star");
        case ViewerIcon::Save: return QStringLiteral("save");
        case ViewerIcon::LayoutGrid: return QStringLiteral("layout-grid");
        case ViewerIcon::Maximize: return QStringLiteral("maximize-2");
        case ViewerIcon::Minimize: return QStringLiteral("minimize-2");
        case ViewerIcon::PanelLeft: return QStringLiteral("panel-left");
        case ViewerIcon::PanelRight: return QStringLiteral("panel-right");
        case ViewerIcon::PanelBottom: return QStringLiteral("panel-bottom");
        case ViewerIcon::Lock: return QStringLiteral("lock");
        case ViewerIcon::Unlock: return QStringLiteral("unlock");
        case ViewerIcon::Restore: return QStringLiteral("rotate-ccw");
        case ViewerIcon::More: return QStringLiteral("more-horizontal");
        case ViewerIcon::User: return QStringLiteral("user");
        case ViewerIcon::Logout: return QStringLiteral("log-out");
        case ViewerIcon::Password: return QStringLiteral("key-round");
        case ViewerIcon::ChevronDown: return QStringLiteral("chevron-down");
        case ViewerIcon::SkipBack: return QStringLiteral("skip-back");
        case ViewerIcon::SkipForward: return QStringLiteral("skip-forward");
        case ViewerIcon::ZoomIn: return QStringLiteral("zoom-in");
        case ViewerIcon::ZoomOut: return QStringLiteral("zoom-out");
        case ViewerIcon::CalendarSearch: return QStringLiteral("calendar-search");
        case ViewerIcon::Alert: return QStringLiteral("circle-alert");
        case ViewerIcon::Success: return QStringLiteral("circle-check");
        case ViewerIcon::Help: return QStringLiteral("circle-help");
        case ViewerIcon::MoveUpLeft: return QStringLiteral("move-up-left");
        case ViewerIcon::MoveUpRight: return QStringLiteral("move-up-right");
        case ViewerIcon::MoveDownLeft: return QStringLiteral("move-down-left");
        case ViewerIcon::MoveDownRight: return QStringLiteral("move-down-right");
        case ViewerIcon::ArrowUp: return QStringLiteral("arrow-up");
        case ViewerIcon::ArrowDown: return QStringLiteral("arrow-down");
        case ViewerIcon::ArrowLeft: return QStringLiteral("arrow-left");
        case ViewerIcon::ArrowRight: return QStringLiteral("arrow-right");
        case ViewerIcon::Plus: return QStringLiteral("plus");
        case ViewerIcon::Minus: return QStringLiteral("minus");
        case ViewerIcon::Close: return QStringLiteral("x");
        case ViewerIcon::Settings: return QStringLiteral("settings");
        case ViewerIcon::None: return {};
    }
    return {};
}

QColor disabledIconColor() {
    return ThemeManager::instance().color(ThemeColor::TextDisabled);
}
}

IconProvider &IconProvider::instance() {
    static IconProvider provider;
    return provider;
}

QIcon IconProvider::icon(ViewerIcon icon, const QSize &logicalSize) {
    return this->icon(icon, ThemeManager::instance().color(ThemeColor::Text), logicalSize);
}

QIcon IconProvider::icon(ViewerIcon icon, const QColor &color, const QSize &logicalSize) {
    if (icon == ViewerIcon::None || !logicalSize.isValid() || logicalSize.isEmpty()) {
        return {};
    }
    const QString key = QStringLiteral("%1:%2:%3x%4")
                            .arg(static_cast<int>(icon))
                            .arg(color.rgba(), 8, 16, QLatin1Char('0'))
                            .arg(logicalSize.width())
                            .arg(logicalSize.height());
    const auto cached = cache_.constFind(key);
    if (cached != cache_.constEnd()) {
        return cached.value();
    }
    if (!hasResource(icon)) {
        qWarning().noquote() << QStringLiteral("缺少 Lucide 图标资源：%1").arg(resourcePath(icon));
        cache_.insert(key, QIcon());
        return {};
    }
    const QIcon created = createIcon(icon, color, logicalSize);
    cache_.insert(key, created);
    return created;
}

QString IconProvider::resourcePath(ViewerIcon icon) const {
    const QString fileName = iconFileName(icon);
    if (fileName.isEmpty()) {
        return {};
    }
    return QStringLiteral(":/icons/lucide/%1.svg").arg(fileName);
}

bool IconProvider::hasResource(ViewerIcon icon) const {
    const QString path = resourcePath(icon);
    return !path.isEmpty() && QFile::exists(path);
}

QStringList IconProvider::missingResources() const {
    QStringList missing;
    for (int value = static_cast<int>(ViewerIcon::Camera);
         value <= static_cast<int>(ViewerIcon::Settings);
         ++value) {
        const auto icon = static_cast<ViewerIcon>(value);
        if (!hasResource(icon)) {
            missing.append(resourcePath(icon));
        }
    }
    return missing;
}

void IconProvider::clearCache() {
    cache_.clear();
}

QIcon IconProvider::createIcon(ViewerIcon icon, const QColor &color, const QSize &logicalSize) const {
    QIcon result;
    for (const qreal scale : {1.0, 2.0}) {
        result.addPixmap(renderPixmap(icon, color, logicalSize, scale), QIcon::Normal, QIcon::Off);
        result.addPixmap(renderPixmap(icon, color, logicalSize, scale), QIcon::Normal, QIcon::On);
        result.addPixmap(renderPixmap(icon, disabledIconColor(), logicalSize, scale), QIcon::Disabled, QIcon::Off);
        result.addPixmap(renderPixmap(icon, disabledIconColor(), logicalSize, scale), QIcon::Disabled, QIcon::On);
    }
    return result;
}

QPixmap IconProvider::renderPixmap(ViewerIcon icon, const QColor &color, const QSize &logicalSize, qreal scale) const {
    const QSize physicalSize = logicalSize * scale;
    const QIcon source(resourcePath(icon));
    QPixmap sourcePixmap = source.pixmap(physicalSize);
    if (sourcePixmap.isNull()) {
        qWarning().noquote() << QStringLiteral("无法渲染 Lucide 图标资源：%1").arg(resourcePath(icon));
        return {};
    }

    QPixmap tinted(physicalSize);
    tinted.fill(Qt::transparent);
    QPainter painter(&tinted);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.drawPixmap(0, 0, sourcePixmap);
    painter.setCompositionMode(QPainter::CompositionMode_SourceIn);
    painter.fillRect(tinted.rect(), color);
    painter.end();
    tinted.setDevicePixelRatio(scale);
    return tinted;
}
