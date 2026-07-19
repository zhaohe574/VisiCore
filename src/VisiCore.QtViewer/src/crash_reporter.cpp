#include "crash_reporter.h"

#include <QCoreApplication>
#include <QDir>
#include <QGuiApplication>
#include <QSaveFile>
#include <QScreen>
#include <QStandardPaths>
#include <QTextStream>

#ifdef Q_OS_WIN
#include <windows.h>
#include <dbghelp.h>

#include <atomic>
#include <cstdio>
#include <cwchar>
#include <iterator>
#include <string>
#endif

namespace {
QString &reportDirectory() {
    static QString directory;
    return directory;
}

#ifdef Q_OS_WIN
std::wstring &nativeReportDirectory() {
    static std::wstring directory;
    return directory;
}

std::atomic_flag &handlingCrash() {
    static std::atomic_flag handling = ATOMIC_FLAG_INIT;
    return handling;
}

LONG WINAPI writeUnhandledCrashReport(EXCEPTION_POINTERS *exceptionPointers) {
    if (handlingCrash().test_and_set() || exceptionPointers == nullptr ||
        exceptionPointers->ExceptionRecord == nullptr || nativeReportDirectory().empty()) {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    SYSTEMTIME localTime{};
    GetLocalTime(&localTime);
    wchar_t fileStem[96]{};
    std::swprintf(
        fileStem,
        std::size(fileStem),
        L"viewer-%04u%02u%02u-%02u%02u%02u-%lu",
        localTime.wYear,
        localTime.wMonth,
        localTime.wDay,
        localTime.wHour,
        localTime.wMinute,
        localTime.wSecond,
        GetCurrentProcessId());

    const std::wstring basePath = nativeReportDirectory() + L"\\" + fileStem;
    const std::wstring metadataPath = basePath + L".txt";
    const std::wstring dumpPath = basePath + L".dmp";

    HANDLE metadata = CreateFileW(
        metadataPath.c_str(),
        GENERIC_WRITE,
        FILE_SHARE_READ,
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (metadata != INVALID_HANDLE_VALUE) {
        char details[160]{};
        const int written = std::snprintf(
            details,
            std::size(details),
            "异常码：0x%08lX\r\n异常地址：0x%p\r\n进程 ID：%lu\r\n",
            exceptionPointers->ExceptionRecord->ExceptionCode,
            exceptionPointers->ExceptionRecord->ExceptionAddress,
            GetCurrentProcessId());
        if (written > 0) {
            DWORD bytesWritten = 0;
            WriteFile(metadata, details, static_cast<DWORD>(written), &bytesWritten, nullptr);
        }
        CloseHandle(metadata);
    }

    HMODULE dbgHelp = LoadLibraryW(L"Dbghelp.dll");
    if (dbgHelp != nullptr) {
        using MiniDumpWriteDumpFunction = BOOL(WINAPI *)(
            HANDLE,
            DWORD,
            HANDLE,
            MINIDUMP_TYPE,
            PMINIDUMP_EXCEPTION_INFORMATION,
            PMINIDUMP_USER_STREAM_INFORMATION,
            PMINIDUMP_CALLBACK_INFORMATION);
        const auto miniDumpWriteDump = reinterpret_cast<MiniDumpWriteDumpFunction>(
            GetProcAddress(dbgHelp, "MiniDumpWriteDump"));
        HANDLE dump = CreateFileW(
            dumpPath.c_str(),
            GENERIC_WRITE,
            FILE_SHARE_READ,
            nullptr,
            CREATE_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (miniDumpWriteDump != nullptr && dump != INVALID_HANDLE_VALUE) {
            MINIDUMP_EXCEPTION_INFORMATION exceptionInfo{};
            exceptionInfo.ThreadId = GetCurrentThreadId();
            exceptionInfo.ExceptionPointers = exceptionPointers;
            exceptionInfo.ClientPointers = FALSE;
            miniDumpWriteDump(
                GetCurrentProcess(),
                GetCurrentProcessId(),
                dump,
                static_cast<MINIDUMP_TYPE>(MiniDumpNormal | MiniDumpWithThreadInfo),
                &exceptionInfo,
                nullptr,
                nullptr);
        }
        if (dump != INVALID_HANDLE_VALUE) {
            CloseHandle(dump);
        }
        FreeLibrary(dbgHelp);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
#endif
}

void CrashReporter::install() {
    QString directory = QStandardPaths::writableLocation(QStandardPaths::AppLocalDataLocation);
    if (directory.isEmpty()) {
        return;
    }
    directory += QStringLiteral("/CrashReports");
    if (!QDir().mkpath(directory)) {
        return;
    }
    reportDirectory() = directory;

#ifdef Q_OS_WIN
    nativeReportDirectory() = QDir::toNativeSeparators(directory).toStdWString();
    SetUnhandledExceptionFilter(writeUnhandledCrashReport);
#endif
    updateContext(QStringLiteral("启动中"), false);
}

void CrashReporter::updateContext(const QString &presentationState, bool fullScreen) {
    if (reportDirectory().isEmpty()) {
        return;
    }

    QSaveFile file(reportDirectory() + QStringLiteral("/last-context.txt"));
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        return;
    }
    QTextStream stream(&file);
    stream << "应用：" << QCoreApplication::applicationName() << '\n';
    stream << "版本：" << QCoreApplication::applicationVersion() << '\n';
    stream << "画布状态：" << presentationState << '\n';
    stream << "原生全屏：" << (fullScreen ? QStringLiteral("是") : QStringLiteral("否")) << '\n';
    stream << "显示器：" << QGuiApplication::screens().size() << '\n';
    for (const QScreen *screen : QGuiApplication::screens()) {
        if (screen != nullptr) {
            const QRect geometry = screen->geometry();
            stream << "屏幕：" << geometry.x() << ',' << geometry.y() << ' '
                   << geometry.width() << 'x' << geometry.height()
                   << "，缩放 " << screen->devicePixelRatio() << '\n';
        }
    }
    file.commit();
}
