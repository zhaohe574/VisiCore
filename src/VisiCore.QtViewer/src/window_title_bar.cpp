#include "window_title_bar.h"

#include "icon_provider.h"
#include "theme_manager.h"

#include <QAbstractButton>
#include <QAction>
#include <QApplication>
#include <QButtonGroup>
#include <QCursor>
#include <QEvent>
#include <QHBoxLayout>
#include <QLabel>
#include <QMenu>
#include <QShowEvent>
#include <QSizePolicy>
#include <QStyle>
#include <QToolButton>
#include <QTimer>
#include <QWindow>

#ifdef Q_OS_WIN
#include <qt_windows.h>
#endif

namespace {
QToolButton *createWindowButton(QWidget *parent, ViewerIcon icon, const QString &toolTip, const QString &objectName) {
    auto *button = new QToolButton(parent);
    button->setObjectName(objectName);
    button->setIcon(IconProvider::instance().icon(icon, QSize(18, 18)));
    button->setIconSize(QSize(18, 18));
    button->setToolTip(toolTip);
    button->setAccessibleName(toolTip);
    button->setFocusPolicy(Qt::NoFocus);
    return button;
}

bool containsGlobalPoint(const QWidget *widget, const QPoint &globalPosition) {
    if (widget == nullptr || !widget->isVisible()) {
        return false;
    }
    return QRect(widget->mapToGlobal(QPoint(0, 0)), widget->size()).contains(globalPosition);
}

void assignOwnedMenu(QToolButton *button, QMenu *menu) {
    if (button == nullptr || menu == nullptr || button->menu() == menu) {
        return;
    }
    QMenu *previousMenu = button->menu();
    const Qt::WindowFlags popupFlags = menu->windowFlags();
    menu->setParent(button, popupFlags);
    button->setMenu(menu);
    if (previousMenu != nullptr && previousMenu->parent() == button) {
        previousMenu->deleteLater();
    }
}
}

WindowTitleBar::WindowTitleBar(QWidget *parent)
    : QWidget(parent),
      connectionDot_(new QLabel(this)),
      connectionLabel_(new QLabel(this)),
      previewButton_(new QToolButton(this)),
      playbackButton_(new QToolButton(this)),
      panelsButton_(new QToolButton(this)),
      accountButton_(new QToolButton(this)),
      minimizeButton_(createWindowButton(this, ViewerIcon::Minus, QStringLiteral("最小化"), QStringLiteral("windowControlButton"))),
      maximizeButton_(createWindowButton(this, ViewerIcon::Maximize, QStringLiteral("最大化"), QStringLiteral("windowControlButton"))),
      closeButton_(createWindowButton(this, ViewerIcon::Close, QStringLiteral("关闭"), QStringLiteral("windowCloseButton"))) {
    setObjectName(QStringLiteral("windowTitleBar"));
    setAttribute(Qt::WA_StyledBackground, true);
    setFixedHeight(48);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    minimizeButton_->setProperty("nativeWindowControlId", QStringLiteral("minimize"));
    minimizeButton_->setAccessibleDescription(QStringLiteral("标题栏窗口控制：最小化"));
    maximizeButton_->setProperty("nativeWindowControlId", QStringLiteral("maximizeRestore"));
    maximizeButton_->setAccessibleDescription(QStringLiteral("标题栏窗口控制：最大化或还原"));
    closeButton_->setProperty("nativeWindowControlId", QStringLiteral("close"));
    closeButton_->setAccessibleDescription(QStringLiteral("标题栏窗口控制：关闭"));

    auto *brandMark = new QWidget(this);
    brandMark->setObjectName(QStringLiteral("windowBrandMark"));
    brandMark->setFixedSize(30, 30);
    auto *brandMarkLayout = new QHBoxLayout(brandMark);
    brandMarkLayout->setContentsMargins(5, 5, 5, 5);
    auto *brandIcon = new QLabel(brandMark);
    brandIcon->setPixmap(IconProvider::instance()
                             .icon(ViewerIcon::Camera, ThemeManager::instance().color(ThemeColor::PrimaryHover), QSize(18, 18))
                             .pixmap(QSize(18, 18)));
    brandMarkLayout->addWidget(brandIcon);

    auto *brandTitle = new QLabel(QStringLiteral("视频监控工作台"), this);
    brandTitle->setObjectName(QStringLiteral("windowBrandTitle"));

    auto configureWorkspaceButton = [](QToolButton *button, const QString &text) {
        button->setObjectName(QStringLiteral("workspaceSegment"));
        button->setText(text);
        button->setAccessibleName(text);
        button->setCheckable(true);
        button->setFocusPolicy(Qt::StrongFocus);
        button->setToolButtonStyle(Qt::ToolButtonTextOnly);
    };
    configureWorkspaceButton(previewButton_, QStringLiteral("实时预览"));
    configureWorkspaceButton(playbackButton_, QStringLiteral("录像回放"));
    previewButton_->setProperty("segmentPosition", QStringLiteral("first"));
    playbackButton_->setProperty("segmentPosition", QStringLiteral("last"));
    previewButton_->setChecked(true);
    auto *workspaceGroup = new QButtonGroup(this);
    workspaceGroup->setExclusive(true);
    workspaceGroup->addButton(previewButton_);
    workspaceGroup->addButton(playbackButton_);

    connectionDot_->setObjectName(QStringLiteral("windowConnectionDot"));
    connectionDot_->setProperty("state", QStringLiteral("disconnected"));
    connectionLabel_->setObjectName(QStringLiteral("windowConnectionText"));
    connectionLabel_->setText(QStringLiteral("连接已断开"));

    auto configureMenuButton = [](QToolButton *button, ViewerIcon icon, const QString &text, const QString &toolTip) {
        button->setObjectName(QStringLiteral("titleBarMenuButton"));
        button->setIcon(IconProvider::instance().icon(icon, QSize(17, 17)));
        button->setIconSize(QSize(17, 17));
        button->setText(text);
        button->setToolTip(toolTip);
        button->setAccessibleName(text);
        button->setToolButtonStyle(Qt::ToolButtonTextBesideIcon);
        button->setPopupMode(QToolButton::DelayedPopup);
        button->setFocusPolicy(Qt::StrongFocus);
    };
    configureMenuButton(panelsButton_, ViewerIcon::PanelLeft, QStringLiteral("面板"), QStringLiteral("显示或隐藏工具面板"));
    configureMenuButton(accountButton_, ViewerIcon::User, QStringLiteral("账号"), QStringLiteral("账号与安全"));
    panelsButton_->setMenu(new QMenu(panelsButton_));
    accountButton_->setMenu(new QMenu(accountButton_));

    auto *layout = new QHBoxLayout(this);
    layout->setContentsMargins(10, 0, 0, 0);
    layout->setSpacing(8);
    layout->addWidget(brandMark);
    layout->addWidget(brandTitle);
    layout->addSpacing(18);
    layout->addWidget(previewButton_);
    layout->addWidget(playbackButton_);
    layout->addStretch(1);
    layout->addWidget(connectionDot_);
    layout->addWidget(connectionLabel_);
    layout->addSpacing(8);
    layout->addWidget(panelsButton_);
    layout->addWidget(accountButton_);
    layout->addSpacing(2);
    layout->addWidget(minimizeButton_);
    layout->addWidget(maximizeButton_);
    layout->addWidget(closeButton_);

    connect(previewButton_, &QToolButton::clicked, this, [this]() {
        requestWorkspaceMode(WorkspaceMode::Preview);
    });
    connect(playbackButton_, &QToolButton::clicked, this, [this]() {
        requestWorkspaceMode(WorkspaceMode::Playback);
    });
    connect(panelsButton_, &QToolButton::clicked, this, [this]() {
        showMenuForButton(panelsButton_);
    });
    connect(accountButton_, &QToolButton::clicked, this, [this]() {
        showMenuForButton(accountButton_);
    });
    connect(minimizeButton_, &QToolButton::clicked, this, [this]() {
        emit minimizeRequested();
        if (targetWindow_) {
            targetWindow_->showMinimized();
        }
    });
    connect(maximizeButton_, &QToolButton::clicked, this, [this]() {
        if (maximizeRestoreAction_ != nullptr) {
            maximizeRestoreAction_->trigger();
            return;
        }
        emit maximizeRestoreRequested();
        if (!targetWindow_) {
            return;
        }
        if (targetWindow_->isMaximized()) {
            targetWindow_->showNormal();
        } else {
            targetWindow_->showMaximized();
        }
    });
    connect(closeButton_, &QToolButton::clicked, this, [this]() {
        emit closeRequested();
        if (targetWindow_) {
            targetWindow_->close();
        }
    });
}

void WindowTitleBar::applyFramelessWindow(QWidget *window) {
    if (window == nullptr) {
        return;
    }
    Qt::WindowFlags flags = window->windowFlags();
    flags |= Qt::FramelessWindowHint | Qt::WindowSystemMenuHint | Qt::WindowMinMaxButtonsHint;
    window->setWindowFlags(flags);
}

void WindowTitleBar::setTargetWindow(QWidget *window) {
    if (targetWindow_ == window) {
        return;
    }
    if (targetWindow_) {
        targetWindow_->removeEventFilter(this);
    }
    targetWindow_ = window;
    nativeTargetWindowId_ = 0;
    if (targetWindow_) {
        targetWindow_->installEventFilter(this);
        queueNativeTargetWindowRefresh();
    }
    syncWindowState();
}

QWidget *WindowTitleBar::targetWindow() const {
    return targetWindow_.data();
}

void WindowTitleBar::confirmWorkspaceMode(WorkspaceMode mode) {
    workspaceMode_ = mode;
    syncWorkspaceButtons();
}

void WindowTitleBar::setWorkspaceMode(WorkspaceMode mode) {
    confirmWorkspaceMode(mode);
}

WorkspaceMode WindowTitleBar::workspaceMode() const {
    return workspaceMode_;
}

void WindowTitleBar::setWorkspaceSwitchEnabled(bool enabled) {
    previewButton_->setEnabled(enabled);
    playbackButton_->setEnabled(enabled);
}

void WindowTitleBar::setUtilityMenusEnabled(bool enabled) {
    panelsButton_->setEnabled(enabled);
    accountButton_->setEnabled(enabled);
}

void WindowTitleBar::setMaximizeRestoreAction(QAction *action) {
    if (maximizeRestoreAction_ == action) {
        return;
    }

    QObject::disconnect(maximizeRestoreActionChangedConnection_);
    QObject::disconnect(maximizeRestoreActionDestroyedConnection_);
    maximizeRestoreAction_ = action;

    if (action != nullptr) {
        maximizeRestoreActionChangedConnection_ = connect(action, &QAction::changed, this, [this]() {
            syncMaximizeRestoreButton();
        });
        maximizeRestoreActionDestroyedConnection_ = connect(action, &QObject::destroyed, this, [this]() {
            maximizeRestoreAction_ = nullptr;
            syncMaximizeRestoreButton();
        });
    }
    syncMaximizeRestoreButton();
}

QAction *WindowTitleBar::maximizeRestoreAction() const {
    return maximizeRestoreAction_.data();
}

void WindowTitleBar::setWindowMaximized(bool maximized) {
    windowMaximized_ = maximized;
    syncMaximizeRestoreButton();
}

void WindowTitleBar::setConnectionState(ViewerConnectionState state, const QString &detail) {
    QString stateName;
    QString defaultText;
    switch (state) {
        case ViewerConnectionState::Connected:
            stateName = QStringLiteral("connected");
            defaultText = QStringLiteral("中心已连接");
            break;
        case ViewerConnectionState::Connecting:
            stateName = QStringLiteral("connecting");
            defaultText = QStringLiteral("正在连接");
            break;
        case ViewerConnectionState::Disconnected:
            stateName = QStringLiteral("disconnected");
            defaultText = QStringLiteral("连接已断开");
            break;
        case ViewerConnectionState::Error:
            stateName = QStringLiteral("error");
            defaultText = QStringLiteral("连接异常");
            break;
    }
    connectionDot_->setProperty("state", stateName);
    connectionDot_->style()->unpolish(connectionDot_);
    connectionDot_->style()->polish(connectionDot_);
    connectionDot_->update();
    connectionLabel_->setText(detail.isEmpty() ? defaultText : detail);
    connectionLabel_->setToolTip(connectionLabel_->text());
}

void WindowTitleBar::setAccountName(const QString &accountName) {
    const QString displayName = accountName.trimmed().isEmpty() ? QStringLiteral("账号") : accountName.trimmed();
    accountButton_->setText(displayName);
    accountButton_->setToolTip(QStringLiteral("当前账号：%1").arg(displayName));
}

void WindowTitleBar::setPanelsMenu(QMenu *menu) {
    assignOwnedMenu(panelsButton_, menu);
}

void WindowTitleBar::setAccountMenu(QMenu *menu) {
    assignOwnedMenu(accountButton_, menu);
}

QMenu *WindowTitleBar::panelsMenu() const {
    return panelsButton_->menu();
}

QMenu *WindowTitleBar::accountMenu() const {
    return accountButton_->menu();
}

void WindowTitleBar::showMenuForButton(QToolButton *button) {
    if (button == nullptr || !button->isEnabled()) {
        return;
    }
    QTimer::singleShot(0, this, [button]() {
        QMenu *menu = button->menu();
        if (menu == nullptr || menu->actions().isEmpty() || menu->isVisible()) {
            return;
        }
        menu->popup(button->mapToGlobal(QPoint(0, button->height())));
    });
}

void WindowTitleBar::requestWorkspaceMode(WorkspaceMode mode) {
    // QButtonGroup 会在 clicked 信号前自动更新选中态，必须先还原已确认状态。
    syncWorkspaceButtons();
    emit workspaceModeRequested(mode);
}

void WindowTitleBar::syncWorkspaceButtons() {
    if (workspaceMode_ == WorkspaceMode::Preview) {
        if (!previewButton_->isChecked()) {
            previewButton_->setChecked(true);
        }
    } else {
        if (!playbackButton_->isChecked()) {
            playbackButton_->setChecked(true);
        }
    }
}

void WindowTitleBar::queueNativeMaximizeRestore() {
    if (nativeMaximizeActivationQueued_) {
        return;
    }

    nativeMaximizeActivationQueued_ = true;
    QTimer::singleShot(0, this, [this]() {
        nativeMaximizeActivationQueued_ = false;
        if (maximizeButton_ == nullptr || !maximizeButton_->isEnabled() ||
            targetWindow_ == nullptr || targetWindow_->isFullScreen()) {
            return;
        }
        maximizeButton_->click();
    });
}

void WindowTitleBar::cancelNativeMaximizePress() {
    nativeMaximizePressPending_ = false;
#ifdef Q_OS_WIN
    const HWND targetHwnd = reinterpret_cast<HWND>(nativeTargetWindowId_);
    if (targetHwnd != nullptr && GetCapture() == targetHwnd) {
        ReleaseCapture();
    }
#endif
}

void WindowTitleBar::queueNativeTargetWindowRefresh() {
#ifdef Q_OS_WIN
    if (nativeTargetWindowRefreshQueued_) {
        return;
    }
    nativeTargetWindowRefreshQueued_ = true;
    QTimer::singleShot(0, this, [this]() {
        nativeTargetWindowRefreshQueued_ = false;
        if (targetWindow_ == nullptr) {
            nativeTargetWindowId_ = 0;
            return;
        }
        // nativeEvent 内调用 QWidget::winId() 会触发窗口创建并重新进入 nativeEvent，
        // 因此仅在 Qt 已经创建 QWindow 后异步缓存 HWND。
        QWindow *windowHandle = targetWindow_->windowHandle();
        nativeTargetWindowId_ = windowHandle == nullptr
            ? 0
            : reinterpret_cast<quintptr>(windowHandle->winId());
    });
#endif
}

bool WindowTitleBar::handleNativeEvent(const QByteArray &eventType, void *message, qintptr *result) {
    Q_UNUSED(eventType)
#ifdef Q_OS_WIN
    if (message == nullptr || result == nullptr || !targetWindow_) {
        return false;
    }
    const auto *nativeMessage = static_cast<const MSG *>(message);
    const HWND targetHwnd = reinterpret_cast<HWND>(nativeTargetWindowId_);
    // 只处理主窗口自己的原生消息，不能截获 QMenu、浮动面板或其他顶层窗口的输入。
    if (targetHwnd == nullptr) {
        queueNativeTargetWindowRefresh();
        return false;
    }
    if (nativeMessage->hwnd != targetHwnd) {
        return false;
    }
    if (QApplication::activePopupWidget() != nullptr || targetWindow_->isFullScreen()) {
        cancelNativeMaximizePress();
        return false;
    }

    if (nativeMessage->message == WM_NCLBUTTONDOWN || nativeMessage->message == WM_NCLBUTTONDBLCLK) {
        if (nativeMessage->wParam != static_cast<WPARAM>(HTMAXBUTTON) ||
            maximizeButton_ == nullptr || !maximizeButton_->isEnabled()) {
            return false;
        }
        // 保持 HTMAXBUTTON 供 Windows 11 展示 Snap Layout；按下后捕获鼠标，
        // 仅在同一原生点击抬起时桥接回 Qt，避免一次点击重复触发。
        nativeMaximizePressPending_ = true;
        if (GetCapture() != targetHwnd) {
            SetCapture(targetHwnd);
        }
        *result = 0;
        return true;
    }
    if (nativeMessage->message == WM_NCLBUTTONUP) {
        if (!nativeMaximizePressPending_) {
            return false;
        }
        const bool shouldActivate = nativeMessage->wParam == static_cast<WPARAM>(HTMAXBUTTON) &&
                                    maximizeButton_ != nullptr && maximizeButton_->isEnabled();
        cancelNativeMaximizePress();
        if (shouldActivate) {
            queueNativeMaximizeRestore();
        }
        *result = 0;
        return true;
    }
    if (nativeMessage->message == WM_CANCELMODE || nativeMessage->message == WM_CAPTURECHANGED) {
        cancelNativeMaximizePress();
        return false;
    }
    if (nativeMessage->message != WM_NCHITTEST) {
        return false;
    }

    const QPoint globalPosition = QCursor::pos();
    const QPoint localPosition = targetWindow_->mapFromGlobal(globalPosition);
    const int border = 7;
    if (!targetWindow_->isMaximized()) {
        const bool left = localPosition.x() >= 0 && localPosition.x() < border;
        const bool right = localPosition.x() < targetWindow_->width() && localPosition.x() >= targetWindow_->width() - border;
        const bool top = localPosition.y() >= 0 && localPosition.y() < border;
        const bool bottom = localPosition.y() < targetWindow_->height() && localPosition.y() >= targetWindow_->height() - border;
        if (top && left) *result = HTTOPLEFT;
        else if (top && right) *result = HTTOPRIGHT;
        else if (bottom && left) *result = HTBOTTOMLEFT;
        else if (bottom && right) *result = HTBOTTOMRIGHT;
        else if (left) *result = HTLEFT;
        else if (right) *result = HTRIGHT;
        else if (top) *result = HTTOP;
        else if (bottom) *result = HTBOTTOM;
        else *result = HTCLIENT;
        if (*result != HTCLIENT) {
            return true;
        }
    }

    if (!containsGlobalPoint(this, globalPosition)) {
        return false;
    }
    if (containsGlobalPoint(closeButton_, globalPosition)) {
        // 自绘关闭和最小化按钮保留为客户区，确保真实鼠标事件交由 Qt 按钮处理。
        *result = HTCLIENT;
        return true;
    }
    if (containsGlobalPoint(maximizeButton_, globalPosition)) {
        *result = maximizeButton_->isEnabled() ? HTMAXBUTTON : HTCLIENT;
        return true;
    }
    if (containsGlobalPoint(minimizeButton_, globalPosition)) {
        *result = HTCLIENT;
        return true;
    }
    if (containsGlobalPoint(previewButton_, globalPosition) ||
        containsGlobalPoint(playbackButton_, globalPosition) ||
        containsGlobalPoint(panelsButton_, globalPosition) ||
        containsGlobalPoint(accountButton_, globalPosition)) {
        *result = HTCLIENT;
        return true;
    }
    *result = HTCAPTION;
    return true;
#else
    Q_UNUSED(message)
    Q_UNUSED(result)
    return false;
#endif
}

bool WindowTitleBar::eventFilter(QObject *watched, QEvent *event) {
    if (watched == targetWindow_) {
        if (event->type() == QEvent::WinIdChange || event->type() == QEvent::Show) {
            nativeTargetWindowId_ = 0;
            queueNativeTargetWindowRefresh();
        }
        if (event->type() == QEvent::WindowStateChange || event->type() == QEvent::Show) {
            syncWindowState();
        }
    }
    return QWidget::eventFilter(watched, event);
}

void WindowTitleBar::showEvent(QShowEvent *event) {
    QWidget::showEvent(event);
    if (!targetWindow_) {
        setTargetWindow(window());
    }
    syncWindowState();
}

void WindowTitleBar::syncWindowState() {
    if (targetWindow_ != nullptr) {
        windowMaximized_ = targetWindow_->isMaximized();
    }
    syncMaximizeRestoreButton();
}

void WindowTitleBar::syncMaximizeRestoreButton() {
    const bool useRestoreIcon = windowMaximized_;
    QAction *action = maximizeRestoreAction_.data();
    if (action != nullptr) {
        maximizeButton_->setEnabled(action->isEnabled());
        const QString toolTip = action->toolTip().trimmed().isEmpty()
            ? action->text().remove(QLatin1Char('&'))
            : action->toolTip();
        maximizeButton_->setToolTip(toolTip);
        maximizeButton_->setAccessibleName(toolTip);
        maximizeButton_->setIcon(action->icon().isNull()
            ? IconProvider::instance().icon(useRestoreIcon ? ViewerIcon::Minimize : ViewerIcon::Maximize, QSize(18, 18))
            : action->icon());
        return;
    }

    const QString toolTip = useRestoreIcon ? QStringLiteral("还原") : QStringLiteral("最大化");
    maximizeButton_->setEnabled(true);
    maximizeButton_->setToolTip(toolTip);
    maximizeButton_->setAccessibleName(toolTip);
    maximizeButton_->setIcon(IconProvider::instance().icon(
        useRestoreIcon ? ViewerIcon::Minimize : ViewerIcon::Maximize,
        QSize(18, 18)));
}
