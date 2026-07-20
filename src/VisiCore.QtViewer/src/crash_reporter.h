#pragma once

#include <QString>

// 仅记录本机崩溃诊断，不写入账号、密码或播放地址。
class CrashReporter final {
public:
    static void install();
    static void updateContext(const QString &presentationState, bool fullScreen);
};
