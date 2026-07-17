#pragma once

#include "icon_provider.h"
#include "viewer_ui_types.h"

#include <QHash>
#include <QKeySequence>
#include <QList>
#include <QObject>
#include <QPointer>
#include <QString>

#include <utility>

class QAction;
class QWidget;

struct ViewerActionState {
    bool enabled = true;
    bool checked = false;
    QString unavailableReason;
};

struct ViewerActionDescriptor {
    ViewerActionId id;
    QString text;
    ViewerIcon icon = ViewerIcon::None;
    QKeySequence shortcut;
    bool checkable = false;
    Qt::ShortcutContext shortcutContext = Qt::WindowShortcut;

    ViewerActionDescriptor(
        ViewerActionId actionId,
        QString actionText,
        ViewerIcon actionIcon = ViewerIcon::None,
        QKeySequence actionShortcut = {},
        bool actionCheckable = false,
        Qt::ShortcutContext actionShortcutContext = Qt::WindowShortcut)
        : id(actionId),
          text(std::move(actionText)),
          icon(actionIcon),
          shortcut(std::move(actionShortcut)),
          checkable(actionCheckable),
          shortcutContext(actionShortcutContext) {
    }
};

class ViewerActionRegistry final : public QObject {
    Q_OBJECT

public:
    explicit ViewerActionRegistry(QObject *parent = nullptr);

    QAction *registerAction(const ViewerActionDescriptor &descriptor);
    [[nodiscard]] QAction *action(ViewerActionId id) const;
    [[nodiscard]] QList<QAction *> actions(const QList<ViewerActionId> &ids) const;
    [[nodiscard]] bool contains(ViewerActionId id) const;
    [[nodiscard]] ViewerActionState state(ViewerActionId id) const;

    void applyState(ViewerActionId id, ViewerActionState state);
    void setEnabled(ViewerActionId id, bool enabled);
    void setChecked(ViewerActionId id, bool checked);
    void bindWidget(ViewerActionId id, QWidget *widget);
    void bindAction(ViewerActionId id, QAction *action);
    void removeAction(ViewerActionId id);

signals:
    void actionTriggered(ViewerActionId id, bool checked);

private:
    struct ActionPresentation {
        QString toolTip;
        QString statusTip;
        QString whatsThis;
    };

    struct ActionBinding {
        QPointer<QAction> action;
        ActionPresentation presentation;
    };

    struct WidgetPresentation {
        QString toolTip;
        QString statusTip;
        QString whatsThis;
        QString accessibleDescription;
    };

    struct WidgetBinding {
        QPointer<QWidget> widget;
        WidgetPresentation presentation;
    };

    void applyStateToAction(QAction *action, const ViewerActionState &state,
                            const ActionPresentation &presentation) const;
    void applyStateToWidget(QWidget *widget, const ViewerActionState &state,
                            const WidgetPresentation &presentation) const;
    void applyStateToBoundActions(ViewerActionId id, const ViewerActionState &state);
    void applyStateToBoundWidgets(ViewerActionId id, const ViewerActionState &state);

    QHash<ViewerActionId, QAction *> actions_;
    QHash<ViewerActionId, ViewerActionState> states_;
    QHash<ViewerActionId, ActionPresentation> registeredActionPresentations_;
    QHash<ViewerActionId, QList<ActionBinding>> actionBindings_;
    QHash<ViewerActionId, QList<WidgetBinding>> widgetBindings_;
};
