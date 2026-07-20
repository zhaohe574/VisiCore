#include "theme_manager.h"

#include <QApplication>
#include <QAbstractItemView>
#include <QFont>
#include <QMenu>

#include <oclero/qlementine/style/QlementineStyle.hpp>
#include <oclero/qlementine/style/Theme.hpp>

namespace {
class ViewerQlementineStyle final : public oclero::qlementine::QlementineStyle {
public:
    explicit ViewerQlementineStyle(QObject *parent)
        : QlementineStyle(parent) {
    }

    void polish(QWidget *widget) override {
        if (qobject_cast<QMenu *>(widget) != nullptr) {
            // Qlementine 1.4.2 会吞掉菜单的鼠标释放事件，再延迟伪造一次释放；
            // Qt 6.12 下该事件无法稳定触发 QAction，因此菜单保留 Qt 原生输入处理。
            QCommonStyle::polish(widget);
            return;
        }
        if (isComboBoxPopupView(widget)) {
            // Qlementine 1.4.2 在 Qt 6.12 的组合框视图构造期会递归调用 QComboBox::view()。
            QCommonStyle::polish(widget);
            return;
        }
        QlementineStyle::polish(widget);
    }

    void unpolish(QWidget *widget) override {
        if (qobject_cast<QMenu *>(widget) != nullptr) {
            QCommonStyle::unpolish(widget);
            return;
        }
        if (isComboBoxPopupView(widget)) {
            QCommonStyle::unpolish(widget);
            return;
        }
        QlementineStyle::unpolish(widget);
    }

private:
    static bool isComboBoxPopupView(const QWidget *widget) {
        const auto *itemView = qobject_cast<const QAbstractItemView *>(widget);
        const QWidget *parent = itemView == nullptr ? nullptr : itemView->parentWidget();
        return parent != nullptr && parent->inherits("QComboBoxPrivateContainer");
    }
};
}

ThemeManager::ThemeManager(QObject *parent)
    : QObject(parent) {
}

ThemeManager &ThemeManager::instance() {
    static ThemeManager manager;
    return manager;
}

QColor ThemeManager::color(ThemeColor role) const {
    switch (role) {
        case ThemeColor::Window: return QColor(QStringLiteral("#0B0E10"));
        case ThemeColor::Surface: return QColor(QStringLiteral("#121619"));
        case ThemeColor::SurfaceElevated: return QColor(QStringLiteral("#181E21"));
        case ThemeColor::SurfaceStrong: return QColor(QStringLiteral("#20272B"));
        case ThemeColor::Border: return QColor(QStringLiteral("#30393D"));
        case ThemeColor::BorderStrong: return QColor(QStringLiteral("#465158"));
        case ThemeColor::Text: return QColor(QStringLiteral("#E9EEF0"));
        case ThemeColor::TextMuted: return QColor(QStringLiteral("#94A0A5"));
        case ThemeColor::TextDisabled: return QColor(QStringLiteral("#5F696E"));
        case ThemeColor::Primary: return QColor(QStringLiteral("#2BAA9A"));
        case ThemeColor::PrimaryHover: return QColor(QStringLiteral("#35BFAE"));
        case ThemeColor::Focus: return QColor(QStringLiteral("#D4B26A"));
        case ThemeColor::Danger: return QColor(QStringLiteral("#D95F5F"));
        case ThemeColor::Warning: return QColor(QStringLiteral("#D99B45"));
        case ThemeColor::Success: return QColor(QStringLiteral("#3FAB7B"));
        case ThemeColor::Information: return QColor(QStringLiteral("#4A91CE"));
    }
    return QColor(QStringLiteral("#E9EEF0"));
}

void ThemeManager::apply(QApplication &application) {
    auto *style = new ViewerQlementineStyle(&application);
    style->setAnimationsEnabled(true);
    const QString themePath = QStringLiteral(":/themes/visicore-dark.json");
    const auto customTheme = oclero::qlementine::Theme::fromJsonPath(themePath);
    if (!customTheme.has_value()) {
        qFatal("%s", qUtf8Printable(QStringLiteral("无法加载正式深色主题资源：%1").arg(themePath)));
    }
    style->setTheme(customTheme.value());
    application.setStyle(style);

    QFont font(QStringLiteral("Microsoft YaHei UI"));
    font.setPointSizeF(9.0);
    application.setFont(font);
    application.setStyleSheet(styleSheet());
    application.setProperty("viewerTheme", QStringLiteral("modern-dark"));
    emit themeApplied();
}

QString ThemeManager::styleSheet() const {
    return QStringLiteral(R"(
        QFrame#sidebar { background: #121619; border-right: 1px solid #30393D; }
        QLabel#brandTitle { color: #F5F7F8; font-size: 17px; font-weight: 700; }
        QLabel#workspaceTitle, QLabel#loginTitle { color: #F5F7F8; font-size: 16px; font-weight: 700; }
        QLabel#toolbarTitle, QLabel#panelTitle { color: #E9EEF0; font-size: 13px; font-weight: 700; }
        QLabel#loginSubtitle, QLabel#mutedLabel { color: #94A0A5; }
        QLabel#connectionState { color: #3FAB7B; padding: 0 6px; }
        QLabel#errorLabel {
            color: #FFD7D4;
            background: #321C1E;
            border: 1px solid #713A40;
            border-radius: 4px;
            padding: 9px 10px;
        }
        QLabel#dialogMessage { color: #C9D1D4; font-size: 13px; }
        QLabel#dialogMessageIcon { background: transparent; }
        QFrame#toolbar, QFrame#controlStrip {
            background: #181E21;
            border: 1px solid #30393D;
            border-radius: 4px;
        }
        QFrame#ptzPanel { background: #12181B; border-left: 1px solid #30393D; }
        QFrame#toolbarSeparator { color: #30393D; background: #30393D; max-width: 1px; }
        QFrame#videoHost { background: #050708; border: 1px solid #273034; }
        QFrame#videoTile { background: #0B0F11; border: 1px solid #2C363A; }
        QFrame#videoTile[selected="true"] { border: 2px solid #2BAA9A; }
        QFrame#videoTile[tone="error"] { border-color: #D95F5F; }
        QFrame#videoTile[tone="error"][selected="true"] { border: 2px solid #D95F5F; }
        QFrame#videoTile[compact="true"] QLabel#tileTitle { font-size: 10px; padding: 1px 3px; }
        QFrame#videoTile[compact="true"] QLabel#tileProfile { font-size: 9px; padding: 1px 3px; }
        QWidget#tileOverlay { background: transparent; border: none; }
        QLabel#tileTitle { color: #EDF2F3; background: rgba(7, 10, 12, 210); padding: 3px 6px; border-radius: 3px; }
        QLabel#tileProfile { color: #B4DAD5; background: rgba(14, 42, 40, 220); padding: 3px 6px; border-radius: 3px; }
        QLabel#tileState { color: #AAB4B8; background: rgba(7, 10, 12, 185); padding: 6px; }
        QFrame#tileStatusDot { border: none; border-radius: 4px; background: #657176; }
        QFrame#tileStatusDot[tone="idle"] { background: #657176; }
        QFrame#tileStatusDot[tone="info"] { background: #4A91CE; }
        QFrame#tileStatusDot[tone="success"] { background: #3FAB7B; }
        QFrame#tileStatusDot[tone="warning"] { background: #D99B45; }
        QFrame#tileStatusDot[tone="error"] { background: #D95F5F; }
        QFrame#videoTile[tone="info"] QLabel#tileState { color: #9DC7EB; }
        QFrame#videoTile[tone="success"] QLabel#tileState { color: #9BD5B8; }
        QFrame#videoTile[tone="warning"] QLabel#tileState { color: #EBC589; }
        QFrame#videoTile[tone="error"] QLabel#tileState { color: #F2A5A5; }
        QTreeWidget#catalogTree { background: #0F1416; border: 1px solid #293337; outline: none; }
        QTreeWidget#catalogTree::item { min-height: 29px; padding: 0 4px; }
        QTreeWidget#catalogTree::item:hover { background: #1B2528; }
        QTreeWidget#catalogTree::item:selected { color: #FFFFFF; background: #263D3B; border-left: 3px solid #2BAA9A; }
        QTreeWidget#catalogTree:focus { border-color: #D4B26A; }
        QTabBar#resourceTabs::tab {
            min-height: 29px; padding: 0 9px; color: #94A0A5; background: #171D20;
            border: 1px solid #30393D; border-right: none;
        }
        QTabBar#resourceTabs::tab:last { border-right: 1px solid #30393D; }
        QTabBar#resourceTabs::tab:selected { color: #FFFFFF; background: #24302F; border-bottom: 2px solid #2BAA9A; }
        QPushButton[primary="true"] { color: #07110F; background: #2BAA9A; border-color: #2BAA9A; font-weight: 700; }
        QPushButton[primary="true"]:hover { background: #35BFAE; border-color: #35BFAE; }
        QPushButton[primary="true"]:pressed { background: #238D80; border-color: #238D80; }
        QToolButton:disabled, QPushButton:disabled, QComboBox:disabled,
        QDateTimeEdit:disabled, QSpinBox:disabled, QLineEdit:disabled {
            color: #7A858A; background: #14191C; border-color: #30393D;
        }
        QSlider:disabled::groove:horizontal { background: #242C30; }
        QSlider:disabled::handle:horizontal { background: #5F696E; }
        QToolButton#accountButton { color: #B8C1C4; text-align: left; background: transparent; border-color: transparent; }
        QToolButton#accountButton:hover { color: #FFFFFF; background: #1E2629; }
        QDialog#appDialog { background: #121619; border: 1px solid #465158; }
        QWidget#dialogTitleBar { background: #181E21; border-bottom: 1px solid #30393D; }
        QLabel#dialogTitle { color: #E9EEF0; font-weight: 600; background: transparent; }
        QToolButton#dialogCloseButton {
            min-width: 38px; max-width: 38px; min-height: 36px; max-height: 36px;
            padding: 0; background: transparent; border: none; border-radius: 0;
        }
        QToolButton#dialogCloseButton:hover { color: #FFFFFF; background: #B74747; }
        QWidget#dialogContent { background: #121619; }
        QWidget#loginBrandMark { background: #173A36; border: 1px solid #286B64; border-radius: 6px; }
        QLabel#loginEyebrow { color: #58C6B8; font-size: 11px; font-weight: 700; }

        QWidget#windowTitleBar { background: #101416; border-bottom: 1px solid #293337; }
        QWidget#windowBrandMark { background: #173A36; border: 1px solid #286B64; border-radius: 5px; }
        QLabel#windowBrandTitle { color: #F3F6F7; font-size: 13px; font-weight: 700; background: transparent; }
        QLabel#windowConnectionDot { min-width: 8px; max-width: 8px; min-height: 8px; max-height: 8px; border-radius: 4px; }
        QLabel#windowConnectionDot[state="connected"] { background: #3FAB7B; }
        QLabel#windowConnectionDot[state="connecting"] { background: #D99B45; }
        QLabel#windowConnectionDot[state="disconnected"] { background: #6D787D; }
        QLabel#windowConnectionDot[state="error"] { background: #D95F5F; }
        QLabel#windowConnectionText { color: #94A0A5; background: transparent; }
        QToolButton#workspaceSegment {
            min-width: 88px; max-width: 112px; min-height: 30px; max-height: 30px;
            padding: 0 12px; color: #C6D0D3; background: #20282C;
            border: 1px solid #465158; border-radius: 0;
        }
        QToolButton#workspaceSegment[segmentPosition="first"] { border-top-left-radius: 4px; border-bottom-left-radius: 4px; }
        QToolButton#workspaceSegment[segmentPosition="last"] { border-top-right-radius: 4px; border-bottom-right-radius: 4px; }
        QToolButton#workspaceSegment:hover { color: #F5F8F9; background: #293438; border-color: #5A676C; }
        QToolButton#workspaceSegment:pressed { color: #FFFFFF; background: #314044; border-color: #6B7A80; }
        QToolButton#workspaceSegment:checked { color: #FFFFFF; background: #1F6A62; border-color: #35BFAE; font-weight: 600; }
        QToolButton#workspaceSegment:checked:hover { background: #287A71; border-color: #4BC6B7; }
        QToolButton#workspaceSegment:focus { border-color: #D4B26A; }
        QToolButton#workspaceSegment:disabled { color: #748085; background: #171D20; border-color: #30393D; }
        QToolButton#titleBarMenuButton { min-height: 32px; max-height: 32px; background: transparent; border-color: transparent; }
        QToolButton#titleBarMenuButton:hover { background: #20282B; border-color: #30393D; }
        QToolButton#windowControlButton {
            min-width: 46px; max-width: 46px; min-height: 47px; max-height: 47px;
            padding: 0; background: transparent; border: none; border-radius: 0;
        }
        QToolButton#windowControlButton:hover { background: #252D31; }
        QToolButton#windowCloseButton {
            min-width: 46px; max-width: 46px; min-height: 47px; max-height: 47px;
            padding: 0; background: transparent; border: none; border-radius: 0;
        }
        QToolButton#windowCloseButton:hover { color: #FFFFFF; background: #B74747; }
    )");
}
