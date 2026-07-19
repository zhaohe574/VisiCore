#pragma once

#include <QString>

enum class ViewerStartupMode {
    Normal = 0,
    RecoverAfterUnexpectedExit,
    SafeUi,
};

// 全局会话标记只用于判断查看端是否在上次启动后正常结束，不包含账号或播放信息。
class ViewerStartupState final {
public:
    static ViewerStartupState begin(bool safeUiRequested);

    [[nodiscard]] ViewerStartupMode mode() const;
    [[nodiscard]] bool previousRunExitedUnexpectedly() const;
    [[nodiscard]] bool shouldRecoverLayout() const;
    void markCleanShutdown() const;

private:
    ViewerStartupState(bool safeUiRequested, bool previousRunExitedUnexpectedly, QString sessionId);

    bool safeUiRequested_ = false;
    bool previousRunExitedUnexpectedly_ = false;
    QString sessionId_;
};
