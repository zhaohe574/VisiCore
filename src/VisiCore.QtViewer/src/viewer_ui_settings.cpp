#include "viewer_ui_settings.h"

#include <QCryptographicHash>
#include <QSettings>

namespace {
const QString RecoveryLayoutPrefix = QStringLiteral("recovery/layoutBackup/");
const QString RecoveryLayoutAvailableKey = QStringLiteral("available");
const QString GeometryKey = QStringLiteral("geometry");
const QString PreviewDockStateKey = QStringLiteral("dock/previewState");
const QString PlaybackDockStateKey = QStringLiteral("dock/playbackState");
}

bool ViewerUiLayoutSnapshot::isEmpty() const {
    return windowGeometry.isEmpty() && previewDockState.isEmpty() && playbackDockState.isEmpty();
}

ViewerUiSettings::ViewerUiSettings(const QString &username)
    : prefix_(accountSettingsPrefix(username)) {
    migrateIfRequired();
}

QString ViewerUiSettings::accountSettingsPrefix(const QString &username) {
    const QByteArray accountHash = QCryptographicHash::hash(username.toUtf8(), QCryptographicHash::Sha256).toHex();
    return QStringLiteral("viewerWorkspace/%1/").arg(QString::fromLatin1(accountHash));
}

QByteArray ViewerUiSettings::windowGeometry() const {
    return QSettings().value(key(QStringLiteral("geometry"))).toByteArray();
}

void ViewerUiSettings::setWindowGeometry(const QByteArray &geometry) const {
    QSettings().setValue(key(QStringLiteral("geometry")), geometry);
}

QByteArray ViewerUiSettings::dockState(WorkspaceMode mode) const {
    return QSettings().value(dockStateKey(mode)).toByteArray();
}

void ViewerUiSettings::setDockState(WorkspaceMode mode, const QByteArray &state) const {
    QSettings().setValue(dockStateKey(mode), state);
}

ViewerUiLayoutSnapshot ViewerUiSettings::currentLayout() const {
    return {
        windowGeometry(),
        dockState(WorkspaceMode::Preview),
        dockState(WorkspaceMode::Playback)};
}

ViewerUiLayoutSnapshot ViewerUiSettings::recoveryLayoutBackup() const {
    QSettings settings;
    return {
        settings.value(recoveryLayoutKey(GeometryKey)).toByteArray(),
        settings.value(recoveryLayoutKey(PreviewDockStateKey)).toByteArray(),
        settings.value(recoveryLayoutKey(PlaybackDockStateKey)).toByteArray()};
}

bool ViewerUiSettings::hasRecoveryLayoutBackup() const {
    return QSettings().value(
        recoveryLayoutKey(RecoveryLayoutAvailableKey), false).toBool();
}

void ViewerUiSettings::backupAndClearLayoutForRecovery() const {
    QSettings settings;
    if (!hasRecoveryLayoutBackup()) {
        const ViewerUiLayoutSnapshot layout = currentLayout();
        settings.setValue(recoveryLayoutKey(GeometryKey), layout.windowGeometry);
        settings.setValue(recoveryLayoutKey(PreviewDockStateKey), layout.previewDockState);
        settings.setValue(recoveryLayoutKey(PlaybackDockStateKey), layout.playbackDockState);
        settings.setValue(recoveryLayoutKey(RecoveryLayoutAvailableKey), true);
    }
    clearActiveLayout(settings);
    settings.sync();
}

void ViewerUiSettings::clearActiveLayout() const {
    QSettings settings;
    clearActiveLayout(settings);
    settings.sync();
}

bool ViewerUiSettings::restoreLayoutFromRecoveryBackup() const {
    if (!hasRecoveryLayoutBackup()) {
        return false;
    }

    const ViewerUiLayoutSnapshot layout = recoveryLayoutBackup();
    QSettings settings;
    settings.setValue(key(GeometryKey), layout.windowGeometry);
    settings.setValue(dockStateKey(WorkspaceMode::Preview), layout.previewDockState);
    settings.setValue(dockStateKey(WorkspaceMode::Playback), layout.playbackDockState);
    settings.sync();
    return settings.status() == QSettings::NoError;
}

void ViewerUiSettings::clearRecoveryLayoutBackup() const {
    QSettings settings;
    settings.remove(recoveryLayoutKey(QString{}));
    settings.sync();
}

bool ViewerUiSettings::dockLocked() const {
    return QSettings().value(key(QStringLiteral("dock/locked")), false).toBool();
}

void ViewerUiSettings::setDockLocked(bool locked) const {
    QSettings().setValue(key(QStringLiteral("dock/locked")), locked);
}

int ViewerUiSettings::schemaVersion() const {
    return QSettings().value(key(QStringLiteral("uiSchemaVersion")), 0).toInt();
}

QString ViewerUiSettings::key(const QString &suffix) const {
    return prefix_ + suffix;
}

QString ViewerUiSettings::dockStateKey(WorkspaceMode mode) const {
    return key(mode == WorkspaceMode::Preview
                   ? PreviewDockStateKey
                   : PlaybackDockStateKey);
}

QString ViewerUiSettings::recoveryLayoutKey(const QString &suffix) const {
    return key(RecoveryLayoutPrefix + suffix);
}

void ViewerUiSettings::clearActiveLayout(QSettings &settings) const {
    settings.remove(key(GeometryKey));
    settings.remove(dockStateKey(WorkspaceMode::Preview));
    settings.remove(dockStateKey(WorkspaceMode::Playback));
}

void ViewerUiSettings::migrateIfRequired() const {
    QSettings settings;
    const QString schemaKey = key(QStringLiteral("uiSchemaVersion"));
    if (settings.value(schemaKey, 0).toInt() >= CurrentSchemaVersion) {
        return;
    }

    // 旧 geometry 与全部业务偏好继续沿用；旧 splitter 仅停止读取，便于旧版本回退。
    settings.setValue(schemaKey, CurrentSchemaVersion);
}
