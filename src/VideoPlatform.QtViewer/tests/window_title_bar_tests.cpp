#include "window_title_bar.h"

#include <QAction>
#include <QApplication>
#include <QCloseEvent>
#include <QMainWindow>
#include <QMenu>
#include <QSignalSpy>
#include <QTest>
#include <QToolButton>

#ifdef Q_OS_WIN
#include <qt_windows.h>
#endif

namespace {
class CloseIgnoringMainWindow final : public QMainWindow {
protected:
    void closeEvent(QCloseEvent *event) override {
        event->ignore();
    }
};

QToolButton *findWindowControl(WindowTitleBar *titleBar, const QString &controlId) {
    if (titleBar == nullptr) {
        return nullptr;
    }
    for (QToolButton *button : titleBar->findChildren<QToolButton *>()) {
        if (button->property("nativeWindowControlId").toString() == controlId) {
            return button;
        }
    }
    return nullptr;
}
}

class WindowTitleBarTests final : public QObject {
    Q_OBJECT

private slots:
    void panelsMenuOpensAndTriggersFromMenuWidget();
    void accountMenuOpensAndTriggersFromMenuWidget();
    void utilityMenusCanBeDisabledIndependently();
    void workspaceRequestWaitsForConfirmation();
    void maximizeRestoreActionControlsTitleBarButton();
    void clientWindowControlsUseQtClickPath();
    void nativeMaximizeHitBridgesToQtAction();
    void nativeEventsIgnoreForeignWindowsAndActivePopups();
    void fullScreenDoesNotInterceptNativeWindowControls();
};

void WindowTitleBarTests::panelsMenuOpensAndTriggersFromMenuWidget() {
    QMainWindow window;
    WindowTitleBar::applyFramelessWindow(&window);
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    QMenu *menu = titleBar->panelsMenu();
    QVERIFY(menu != nullptr);
    menu->clear();
    QAction *panelAction = menu->addAction(QStringLiteral("切换 PTZ 面板"));
    QSignalSpy triggerSpy(panelAction, &QAction::triggered);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    QToolButton *panelsButton = nullptr;
    for (QToolButton *button : titleBar->findChildren<QToolButton *>()) {
        if (button->accessibleName() == QStringLiteral("面板")) {
            panelsButton = button;
            break;
        }
    }
    QVERIFY(panelsButton != nullptr);

    QTest::mouseClick(panelsButton, Qt::LeftButton);
    QTRY_VERIFY(menu->isVisible());

    const QRect actionGeometry = menu->actionGeometry(panelAction);
    QVERIFY(actionGeometry.isValid());
    QTest::mouseClick(menu, Qt::LeftButton, Qt::NoModifier, actionGeometry.center());
    QTRY_COMPARE(triggerSpy.count(), 1);
    QTRY_VERIFY(!menu->isVisible());
}

void WindowTitleBarTests::accountMenuOpensAndTriggersFromMenuWidget() {
    QMainWindow window;
    WindowTitleBar::applyFramelessWindow(&window);
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    QMenu *menu = titleBar->accountMenu();
    QVERIFY(menu != nullptr);
    menu->clear();
    QAction *accountAction = menu->addAction(QStringLiteral("修改密码"));
    QSignalSpy triggerSpy(accountAction, &QAction::triggered);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    QToolButton *accountButton = nullptr;
    for (QToolButton *button : titleBar->findChildren<QToolButton *>()) {
        if (button->accessibleName() == QStringLiteral("账号")) {
            accountButton = button;
            break;
        }
    }
    QVERIFY(accountButton != nullptr);

    QTest::mouseClick(accountButton, Qt::LeftButton);
    QTRY_VERIFY(menu->isVisible());

    const QRect actionGeometry = menu->actionGeometry(accountAction);
    QVERIFY(actionGeometry.isValid());
    QTest::mouseClick(menu, Qt::LeftButton, Qt::NoModifier, actionGeometry.center());
    QTRY_COMPARE(triggerSpy.count(), 1);
    QTRY_VERIFY(!menu->isVisible());
}

void WindowTitleBarTests::utilityMenusCanBeDisabledIndependently() {
    QMainWindow window;
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    QToolButton *panelsButton = nullptr;
    QToolButton *accountButton = nullptr;
    QToolButton *minimizeButton = nullptr;
    QToolButton *maximizeButton = nullptr;
    QToolButton *closeButton = nullptr;
    for (QToolButton *button : titleBar->findChildren<QToolButton *>()) {
        const QString accessibleName = button->accessibleName();
        if (accessibleName == QStringLiteral("面板")) {
            panelsButton = button;
        } else if (accessibleName == QStringLiteral("账号")) {
            accountButton = button;
        } else if (accessibleName == QStringLiteral("最小化")) {
            minimizeButton = button;
        } else if (accessibleName == QStringLiteral("最大化")) {
            maximizeButton = button;
        } else if (accessibleName == QStringLiteral("关闭")) {
            closeButton = button;
        }
    }
    QVERIFY(panelsButton != nullptr);
    QVERIFY(accountButton != nullptr);
    QVERIFY(minimizeButton != nullptr);
    QVERIFY(maximizeButton != nullptr);
    QVERIFY(closeButton != nullptr);

    titleBar->setUtilityMenusEnabled(false);
    QVERIFY(!panelsButton->isEnabled());
    QVERIFY(!accountButton->isEnabled());
    QVERIFY(minimizeButton->isEnabled());
    QVERIFY(maximizeButton->isEnabled());
    QVERIFY(closeButton->isEnabled());

    titleBar->setUtilityMenusEnabled(true);
    QVERIFY(panelsButton->isEnabled());
    QVERIFY(accountButton->isEnabled());
}

void WindowTitleBarTests::workspaceRequestWaitsForConfirmation() {
    QMainWindow window;
    auto *titleBar = new WindowTitleBar(&window);
    window.setMenuWidget(titleBar);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    QToolButton *previewButton = nullptr;
    QToolButton *playbackButton = nullptr;
    for (QToolButton *button : titleBar->findChildren<QToolButton *>()) {
        if (button->accessibleName() == QStringLiteral("实时预览")) {
            previewButton = button;
        } else if (button->accessibleName() == QStringLiteral("录像回放")) {
            playbackButton = button;
        }
    }
    QVERIFY(previewButton != nullptr);
    QVERIFY(playbackButton != nullptr);

    QSignalSpy requestSpy(titleBar, &WindowTitleBar::workspaceModeRequested);
    QTest::mouseClick(playbackButton, Qt::LeftButton);
    QTRY_COMPARE(requestSpy.count(), 1);
    QCOMPARE(requestSpy.at(0).at(0).value<WorkspaceMode>(), WorkspaceMode::Playback);
    QCOMPARE(titleBar->workspaceMode(), WorkspaceMode::Preview);
    QVERIFY(previewButton->isChecked());
    QVERIFY(!playbackButton->isChecked());

    titleBar->confirmWorkspaceMode(WorkspaceMode::Playback);
    QCOMPARE(titleBar->workspaceMode(), WorkspaceMode::Playback);
    QVERIFY(!previewButton->isChecked());
    QVERIFY(playbackButton->isChecked());
}

void WindowTitleBarTests::maximizeRestoreActionControlsTitleBarButton() {
    QMainWindow window;
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    QAction maximizeRestoreAction(QStringLiteral("还原窗口"), &window);
    maximizeRestoreAction.setToolTip(QStringLiteral("由主窗口还原"));
    QSignalSpy triggerSpy(&maximizeRestoreAction, &QAction::triggered);
    titleBar->setMaximizeRestoreAction(&maximizeRestoreAction);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    QToolButton *maximizeButton = nullptr;
    for (QToolButton *button : titleBar->findChildren<QToolButton *>()) {
        if (button->toolTip() == QStringLiteral("由主窗口还原")) {
            maximizeButton = button;
            break;
        }
    }
    QVERIFY(maximizeButton != nullptr);
    QTest::mouseClick(maximizeButton, Qt::LeftButton);
    QTRY_COMPARE(triggerSpy.count(), 1);

    maximizeRestoreAction.setEnabled(false);
    QTRY_VERIFY(!maximizeButton->isEnabled());
    QCOMPARE(titleBar->maximizeRestoreAction(), &maximizeRestoreAction);
}

void WindowTitleBarTests::clientWindowControlsUseQtClickPath() {
    CloseIgnoringMainWindow window;
    WindowTitleBar::applyFramelessWindow(&window);
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    QToolButton *minimizeButton = findWindowControl(titleBar, QStringLiteral("minimize"));
    QToolButton *closeButton = findWindowControl(titleBar, QStringLiteral("close"));
    QVERIFY(minimizeButton != nullptr);
    QVERIFY(closeButton != nullptr);

    QSignalSpy minimizeSpy(titleBar, &WindowTitleBar::minimizeRequested);
    QSignalSpy closeSpy(titleBar, &WindowTitleBar::closeRequested);
    QTest::mouseClick(minimizeButton, Qt::LeftButton);
    QTRY_COMPARE(minimizeSpy.count(), 1);
    window.showNormal();
    QTRY_VERIFY(window.isVisible());

    QTest::mouseClick(closeButton, Qt::LeftButton);
    QTRY_COMPARE(closeSpy.count(), 1);
    QVERIFY(window.isVisible());
}

void WindowTitleBarTests::nativeMaximizeHitBridgesToQtAction() {
#ifdef Q_OS_WIN
    if (QGuiApplication::platformName() == QStringLiteral("offscreen")) {
        QSKIP("离屏平台没有真实 HWND，原生命中桥接由 Windows NativeSmoke 验收。");
    }
    QMainWindow window;
    WindowTitleBar::applyFramelessWindow(&window);
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    QAction maximizeRestoreAction(QStringLiteral("最大化窗口"), &window);
    QSignalSpy triggerSpy(&maximizeRestoreAction, &QAction::triggered);
    titleBar->setMaximizeRestoreAction(&maximizeRestoreAction);

    window.resize(900, 640);
    window.show();
    QTRY_VERIFY(titleBar->isVisible());

    MSG message{};
    message.hwnd = reinterpret_cast<HWND>(window.winId());
    message.message = WM_NCLBUTTONDOWN;
    message.wParam = HTMAXBUTTON;
    qintptr result = -1;
    QVERIFY(titleBar->handleNativeEvent({}, &message, &result));
    QCOMPARE(result, qintptr{0});
    QCOMPARE(triggerSpy.count(), 0);

    message.message = WM_NCLBUTTONUP;
    QVERIFY(titleBar->handleNativeEvent({}, &message, &result));
    QCOMPARE(result, qintptr{0});
    QTRY_COMPARE(triggerSpy.count(), 1);

    // 同一抬起消息不会第二次触发，避免原生路径与队列点击重复执行。
    QVERIFY(!titleBar->handleNativeEvent({}, &message, &result));
    QCOMPARE(triggerSpy.count(), 1);
#else
    QSKIP("仅 Windows 平台验证原生最大化命中桥接。");
#endif
}

void WindowTitleBarTests::nativeEventsIgnoreForeignWindowsAndActivePopups() {
#ifdef Q_OS_WIN
    if (QGuiApplication::platformName() == QStringLiteral("offscreen")) {
        QSKIP("离屏平台没有真实 HWND，原生窗口隔离由 Windows NativeSmoke 验收。");
    }
    QMainWindow window;
    WindowTitleBar::applyFramelessWindow(&window);
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    QAction maximizeRestoreAction(QStringLiteral("最大化窗口"), &window);
    QSignalSpy triggerSpy(&maximizeRestoreAction, &QAction::triggered);
    titleBar->setMaximizeRestoreAction(&maximizeRestoreAction);

    QMainWindow foreignWindow;
    window.resize(900, 640);
    foreignWindow.resize(480, 320);
    window.show();
    foreignWindow.show();
    QTRY_VERIFY(titleBar->isVisible());
    QTRY_VERIFY(foreignWindow.isVisible());

    MSG message{};
    message.message = WM_NCLBUTTONDOWN;
    message.wParam = HTMAXBUTTON;
    message.hwnd = reinterpret_cast<HWND>(foreignWindow.winId());
    qintptr result = -1;
    QVERIFY(!titleBar->handleNativeEvent({}, &message, &result));
    QCOMPARE(triggerSpy.count(), 0);

    QMenu popup;
    popup.addAction(QStringLiteral("测试菜单项"));
    popup.popup(window.mapToGlobal(QPoint(20, 20)));
    QTRY_VERIFY(popup.isVisible());
    QTRY_VERIFY(QApplication::activePopupWidget() == &popup);

    message.hwnd = reinterpret_cast<HWND>(window.winId());
    QVERIFY(!titleBar->handleNativeEvent({}, &message, &result));
    QCOMPARE(triggerSpy.count(), 0);
    popup.close();
#else
    QSKIP("仅 Windows 平台验证原生窗口和弹出菜单隔离。");
#endif
}

void WindowTitleBarTests::fullScreenDoesNotInterceptNativeWindowControls() {
#ifdef Q_OS_WIN
    if (QGuiApplication::platformName() == QStringLiteral("offscreen")) {
        QSKIP("离屏平台没有真实 HWND，原生全屏输入放行由 Windows NativeSmoke 验收。");
    }
    QMainWindow window;
    WindowTitleBar::applyFramelessWindow(&window);
    auto *titleBar = new WindowTitleBar(&window);
    titleBar->setTargetWindow(&window);
    window.setMenuWidget(titleBar);

    QAction maximizeRestoreAction(QStringLiteral("最大化窗口"), &window);
    QSignalSpy triggerSpy(&maximizeRestoreAction, &QAction::triggered);
    titleBar->setMaximizeRestoreAction(&maximizeRestoreAction);

    window.resize(900, 640);
    window.showFullScreen();
    QTRY_VERIFY(window.isFullScreen());

    MSG message{};
    message.hwnd = reinterpret_cast<HWND>(window.winId());
    message.message = WM_NCLBUTTONDOWN;
    message.wParam = HTMAXBUTTON;
    qintptr result = -1;
    QVERIFY(!titleBar->handleNativeEvent({}, &message, &result));
    QCOMPARE(triggerSpy.count(), 0);
    window.showNormal();
#else
    QSKIP("仅 Windows 平台验证全屏原生输入放行。");
#endif
}

QTEST_MAIN(WindowTitleBarTests)
#include "window_title_bar_tests.moc"
