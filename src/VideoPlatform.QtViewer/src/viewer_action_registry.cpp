#include "viewer_action_registry.h"

#include <QAbstractButton>
#include <QAction>
#include <QWidget>

namespace {
QString actionObjectName(ViewerActionId id) {
    return QStringLiteral("viewerAction_%1").arg(static_cast<int>(id));
}

QString textWithUnavailableReason(const QString &baseText, const QString &reason) {
    if (reason.isEmpty()) {
        return baseText;
    }
    if (baseText.isEmpty()) {
        return reason;
    }
    return QStringLiteral("%1\n%2").arg(baseText, reason);
}
}

ViewerActionRegistry::ViewerActionRegistry(QObject *parent)
    : QObject(parent) {
}

QAction *ViewerActionRegistry::registerAction(const ViewerActionDescriptor &descriptor) {
    QAction *registered = actions_.value(descriptor.id, nullptr);
    if (registered == nullptr) {
        registered = new QAction(this);
        registered->setObjectName(actionObjectName(descriptor.id));
        registered->setData(static_cast<int>(descriptor.id));
        actions_.insert(descriptor.id, registered);
        connect(registered, &QAction::triggered, this, [this, id = descriptor.id](bool checked) {
            emit actionTriggered(id, checked);
        });
    }

    registered->setText(descriptor.text);
    registered->setToolTip(descriptor.text);
    registered->setCheckable(descriptor.checkable);
    registered->setShortcut(descriptor.shortcut);
    registered->setShortcutContext(descriptor.shortcutContext);
    registered->setIcon(IconProvider::instance().icon(descriptor.icon));

    ActionPresentation presentation;
    presentation.toolTip = registered->toolTip();
    presentation.statusTip = registered->statusTip();
    presentation.whatsThis = registered->whatsThis();
    registeredActionPresentations_.insert(descriptor.id, std::move(presentation));

    if (!states_.contains(descriptor.id)) {
        states_.insert(
            descriptor.id,
            ViewerActionState{registered->isEnabled(), registered->isChecked(), QString{}});
    }
    applyState(descriptor.id, state(descriptor.id));
    return registered;
}

QAction *ViewerActionRegistry::action(ViewerActionId id) const {
    return actions_.value(id, nullptr);
}

QList<QAction *> ViewerActionRegistry::actions(const QList<ViewerActionId> &ids) const {
    QList<QAction *> result;
    result.reserve(ids.size());
    for (ViewerActionId id : ids) {
        if (QAction *registered = action(id)) {
            result.append(registered);
        }
    }
    return result;
}

bool ViewerActionRegistry::contains(ViewerActionId id) const {
    return actions_.contains(id);
}

ViewerActionState ViewerActionRegistry::state(ViewerActionId id) const {
    return states_.value(id);
}

void ViewerActionRegistry::applyState(ViewerActionId id, ViewerActionState actionState) {
    actionState.unavailableReason = actionState.unavailableReason.trimmed();
    if (actionState.enabled) {
        actionState.unavailableReason.clear();
    }
    states_.insert(id, actionState);

    if (QAction *registered = action(id)) {
        ActionPresentation presentation = registeredActionPresentations_.value(id);
        if (!registeredActionPresentations_.contains(id)) {
            presentation.toolTip = registered->toolTip();
            presentation.statusTip = registered->statusTip();
            presentation.whatsThis = registered->whatsThis();
            registeredActionPresentations_.insert(id, presentation);
        }
        applyStateToAction(registered, actionState, presentation);
    }

    applyStateToBoundActions(id, actionState);
    applyStateToBoundWidgets(id, actionState);
}

void ViewerActionRegistry::setEnabled(ViewerActionId id, bool enabled) {
    ViewerActionState actionState = state(id);
    actionState.enabled = enabled;
    if (enabled) {
        actionState.unavailableReason.clear();
    }
    applyState(id, std::move(actionState));
}

void ViewerActionRegistry::setChecked(ViewerActionId id, bool checked) {
    ViewerActionState actionState = state(id);
    actionState.checked = checked;
    applyState(id, std::move(actionState));
}

void ViewerActionRegistry::bindWidget(ViewerActionId id, QWidget *widget) {
    if (widget == nullptr) {
        return;
    }

    auto &bindings = widgetBindings_[id];
    for (auto binding = bindings.begin(); binding != bindings.end();) {
        QWidget *boundWidget = binding->widget.data();
        if (boundWidget == nullptr) {
            binding = bindings.erase(binding);
            continue;
        }
        if (boundWidget == widget) {
            applyStateToWidget(widget, state(id), binding->presentation);
            return;
        }
        ++binding;
    }

    WidgetPresentation presentation;
    presentation.toolTip = widget->toolTip();
    presentation.statusTip = widget->statusTip();
    presentation.whatsThis = widget->whatsThis();
    presentation.accessibleDescription = widget->accessibleDescription();
    bindings.append(WidgetBinding{widget, std::move(presentation)});
    applyStateToWidget(widget, state(id), bindings.constLast().presentation);
}

void ViewerActionRegistry::bindAction(ViewerActionId id, QAction *boundAction) {
    if (boundAction == nullptr || boundAction == action(id)) {
        return;
    }

    auto &bindings = actionBindings_[id];
    for (auto binding = bindings.begin(); binding != bindings.end();) {
        QAction *existingAction = binding->action.data();
        if (existingAction == nullptr) {
            binding = bindings.erase(binding);
            continue;
        }
        if (existingAction == boundAction) {
            applyStateToAction(boundAction, state(id), binding->presentation);
            return;
        }
        ++binding;
    }

    ActionPresentation presentation;
    presentation.toolTip = boundAction->toolTip();
    presentation.statusTip = boundAction->statusTip();
    presentation.whatsThis = boundAction->whatsThis();
    bindings.append(ActionBinding{boundAction, std::move(presentation)});
    applyStateToAction(boundAction, state(id), bindings.constLast().presentation);
}

void ViewerActionRegistry::removeAction(ViewerActionId id) {
    if (QAction *registered = actions_.take(id)) {
        registered->deleteLater();
    }
    states_.remove(id);
    registeredActionPresentations_.remove(id);
    actionBindings_.remove(id);
    widgetBindings_.remove(id);
}

void ViewerActionRegistry::applyStateToAction(
    QAction *target,
    const ViewerActionState &actionState,
    const ActionPresentation &presentation) const {
    if (target == nullptr) {
        return;
    }

    const QString reason = actionState.enabled ? QString{} : actionState.unavailableReason;
    target->setEnabled(actionState.enabled);
    if (target->isCheckable()) {
        target->setChecked(actionState.checked);
    }
    target->setToolTip(textWithUnavailableReason(presentation.toolTip, reason));
    target->setStatusTip(textWithUnavailableReason(presentation.statusTip, reason));
    target->setWhatsThis(textWithUnavailableReason(presentation.whatsThis, reason));
}

void ViewerActionRegistry::applyStateToWidget(
    QWidget *widget,
    const ViewerActionState &actionState,
    const WidgetPresentation &presentation) const {
    if (widget == nullptr) {
        return;
    }

    const QString reason = actionState.enabled ? QString{} : actionState.unavailableReason;
    widget->setEnabled(actionState.enabled);
    if (auto *button = qobject_cast<QAbstractButton *>(widget);
        button != nullptr && button->isCheckable()) {
        button->setChecked(actionState.checked);
    }
    widget->setToolTip(textWithUnavailableReason(presentation.toolTip, reason));
    widget->setStatusTip(textWithUnavailableReason(presentation.statusTip, reason));
    widget->setWhatsThis(textWithUnavailableReason(presentation.whatsThis, reason));
    widget->setAccessibleDescription(
        textWithUnavailableReason(presentation.accessibleDescription, reason));
}

void ViewerActionRegistry::applyStateToBoundActions(
    ViewerActionId id,
    const ViewerActionState &actionState) {
    auto bindings = actionBindings_.find(id);
    if (bindings == actionBindings_.end()) {
        return;
    }

    for (auto binding = bindings->begin(); binding != bindings->end();) {
        QAction *boundAction = binding->action.data();
        if (boundAction == nullptr) {
            binding = bindings->erase(binding);
            continue;
        }
        applyStateToAction(boundAction, actionState, binding->presentation);
        ++binding;
    }
    if (bindings->isEmpty()) {
        actionBindings_.erase(bindings);
    }
}

void ViewerActionRegistry::applyStateToBoundWidgets(
    ViewerActionId id,
    const ViewerActionState &actionState) {
    auto bindings = widgetBindings_.find(id);
    if (bindings == widgetBindings_.end()) {
        return;
    }

    for (auto binding = bindings->begin(); binding != bindings->end();) {
        QWidget *widget = binding->widget.data();
        if (widget == nullptr) {
            binding = bindings->erase(binding);
            continue;
        }
        applyStateToWidget(widget, actionState, binding->presentation);
        ++binding;
    }
    if (bindings->isEmpty()) {
        widgetBindings_.erase(bindings);
    }
}
