#pragma once

#include "viewer_ui_types.h"

#include <QByteArray>
#include <QString>

class QSettings;

struct ViewerUiLayoutSnapshot {
    QByteArray windowGeometry;
    QByteArray previewDockState;
    QByteArray playbackDockState;

    [[nodiscard]] bool isEmpty() const;
};

class ViewerUiSettings final {
public:
    static constexpr int CurrentSchemaVersion = 3;

    explicit ViewerUiSettings(const QString &username);

    static QString accountSettingsPrefix(const QString &username);

    QByteArray windowGeometry() const;
    void setWindowGeometry(const QByteArray &geometry) const;

    QByteArray dockState(WorkspaceMode mode) const;
    void setDockState(WorkspaceMode mode, const QByteArray &state) const;

    // 仅隔离窗口位置与两套停靠布局；不会删除收藏、分屏或其他账号偏好。
    [[nodiscard]] ViewerUiLayoutSnapshot currentLayout() const;
    [[nodiscard]] ViewerUiLayoutSnapshot recoveryLayoutBackup() const;
    [[nodiscard]] bool hasRecoveryLayoutBackup() const;
    void backupAndClearLayoutForRecovery() const;
    void clearActiveLayout() const;
    bool restoreLayoutFromRecoveryBackup() const;
    void clearRecoveryLayoutBackup() const;

    bool dockLocked() const;
    void setDockLocked(bool locked) const;

    int schemaVersion() const;

private:
    QString key(const QString &suffix) const;
    QString dockStateKey(WorkspaceMode mode) const;
    QString recoveryLayoutKey(const QString &suffix) const;
    void clearActiveLayout(QSettings &settings) const;
    void migrateIfRequired() const;

    QString prefix_;
};
