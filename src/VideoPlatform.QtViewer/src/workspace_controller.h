#pragma once

#include "viewer_ui_types.h"

#include <QHash>
#include <QList>
#include <QObject>
#include <QString>

class WorkspaceController final : public QObject {
    Q_OBJECT

public:
    explicit WorkspaceController(QObject *parent = nullptr);

    [[nodiscard]] WorkspaceMode mode() const;
    [[nodiscard]] bool interactionEnabled() const;
    [[nodiscard]] bool isActionEnabled(ViewerActionId actionId) const;
    [[nodiscard]] bool isActionChecked(ViewerActionId actionId) const;
    [[nodiscard]] static QList<ViewerActionId> allActionIds();

    bool setMode(WorkspaceMode mode);
    void setInteractionEnabled(bool enabled);

signals:
    void workspaceAboutToChange(WorkspaceMode previousMode, WorkspaceMode nextMode);
    void workspaceChanged(WorkspaceMode mode);
    void actionStateChanged(ViewerActionId actionId, bool enabled, bool checked);
    void stateChanged();
    void operationRejected(const QString &message);

private:
    static bool isKnownMode(WorkspaceMode mode);
    void publishActionStateChanges();

    WorkspaceMode mode_ = WorkspaceMode::Preview;
    bool interactionEnabled_ = true;
    QHash<int, bool> publishedEnabledStates_;
    QHash<int, bool> publishedCheckedStates_;
};
