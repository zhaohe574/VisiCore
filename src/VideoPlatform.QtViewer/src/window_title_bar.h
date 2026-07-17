#pragma once

#include "viewer_ui_types.h"

#include <QByteArray>
#include <QMetaObject>
#include <QPointer>
#include <QWidget>

class QAction;
class QLabel;
class QMenu;
class QShowEvent;
class QToolButton;

class WindowTitleBar final : public QWidget {
    Q_OBJECT

public:
    explicit WindowTitleBar(QWidget *parent = nullptr);

    static void applyFramelessWindow(QWidget *window);
    void setTargetWindow(QWidget *window);
    [[nodiscard]] QWidget *targetWindow() const;

    // 仅在主窗口完成工作区切换后调用，标题栏不会在请求发出时提前切换选中态。
    void confirmWorkspaceMode(WorkspaceMode mode);
    void setWorkspaceMode(WorkspaceMode mode);
    [[nodiscard]] WorkspaceMode workspaceMode() const;
    void setWorkspaceSwitchEnabled(bool enabled);
    // 工作区过渡期间仅冻结工具菜单，不影响窗口最小化、最大化和关闭。
    void setUtilityMenusEnabled(bool enabled);

    // 绑定后由动作负责实际的最大化/还原行为及可用状态，未绑定时保持标题栏原有的窗口操作。
    void setMaximizeRestoreAction(QAction *action);
    [[nodiscard]] QAction *maximizeRestoreAction() const;
    void setWindowMaximized(bool maximized);

    void setConnectionState(ViewerConnectionState state, const QString &detail = {});
    void setAccountName(const QString &accountName);
    void setPanelsMenu(QMenu *menu);
    void setAccountMenu(QMenu *menu);
    [[nodiscard]] QMenu *panelsMenu() const;
    [[nodiscard]] QMenu *accountMenu() const;

    // 主窗口从 nativeEvent 转发此调用，以保留边缘缩放和最大化按钮的 Windows Snap Layout。
    [[nodiscard]] bool handleNativeEvent(const QByteArray &eventType, void *message, qintptr *result);

signals:
    void workspaceModeRequested(WorkspaceMode mode);
    void minimizeRequested();
    void maximizeRestoreRequested();
    void closeRequested();

protected:
    bool eventFilter(QObject *watched, QEvent *event) override;
    void showEvent(QShowEvent *event) override;

private:
    void showMenuForButton(QToolButton *button);
    void requestWorkspaceMode(WorkspaceMode mode);
    void syncWorkspaceButtons();
    void syncWindowState();
    void syncMaximizeRestoreButton();
    void queueNativeMaximizeRestore();
    void cancelNativeMaximizePress();
    void queueNativeTargetWindowRefresh();

    QPointer<QWidget> targetWindow_;
    QPointer<QAction> maximizeRestoreAction_;
    QMetaObject::Connection maximizeRestoreActionChangedConnection_;
    QMetaObject::Connection maximizeRestoreActionDestroyedConnection_;
    WorkspaceMode workspaceMode_ = WorkspaceMode::Preview;
    bool windowMaximized_ = false;
    bool nativeMaximizePressPending_ = false;
    bool nativeMaximizeActivationQueued_ = false;
    bool nativeTargetWindowRefreshQueued_ = false;
    quintptr nativeTargetWindowId_ = 0;
    QLabel *connectionDot_;
    QLabel *connectionLabel_;
    QToolButton *previewButton_;
    QToolButton *playbackButton_;
    QToolButton *panelsButton_;
    QToolButton *accountButton_;
    QToolButton *minimizeButton_;
    QToolButton *maximizeButton_;
    QToolButton *closeButton_;
};
