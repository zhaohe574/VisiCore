#pragma once

#include <QColor>
#include <QObject>

class QApplication;

enum class ThemeColor {
    Window,
    Surface,
    SurfaceElevated,
    SurfaceStrong,
    Border,
    BorderStrong,
    Text,
    TextMuted,
    TextDisabled,
    Primary,
    PrimaryHover,
    Focus,
    Danger,
    Warning,
    Success,
    Information,
};

class ThemeManager final : public QObject {
    Q_OBJECT

public:
    static ThemeManager &instance();

    void apply(QApplication &application);
    [[nodiscard]] QColor color(ThemeColor role) const;
    [[nodiscard]] QString styleSheet() const;

signals:
    void themeApplied();

private:
    explicit ThemeManager(QObject *parent = nullptr);
};
