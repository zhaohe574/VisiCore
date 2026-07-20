#include "apiclient.h"
#include "app_dialog.h"
#include "crash_reporter.h"
#include "connection_settings.h"
#include "login_dialog.h"
#include "main_window.h"
#include "mpv_player_widget.h"
#include "theme_manager.h"
#include "viewer_startup_state.h"
#include "window_title_bar.h"

#include <QApplication>
#include <QCommandLineOption>
#include <QCommandLineParser>
#include <QElapsedTimer>
#include <QFile>
#include <QFileInfo>
#include <QGridLayout>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonParseError>
#include <QPointer>
#include <QSet>
#include <QStringList>
#include <QTextStream>
#include <QTimer>
#include <QUrl>
#include <QVector>
#include <QWidget>

#include <cmath>
#include <functional>
#include <memory>

namespace {
bool isSafeVerificationStreamUrl(const QUrl &url) {
    if (!url.isValid() || url.host().isEmpty() || !url.userInfo().isEmpty()) {
        return false;
    }
    if (url.scheme().compare(QStringLiteral("https"), Qt::CaseInsensitive) == 0) {
        return true;
    }
    if (url.scheme().compare(QStringLiteral("http"), Qt::CaseInsensitive) != 0) {
        return false;
    }
    const QString host = url.host().toLower();
    return host == QStringLiteral("localhost") || host == QStringLiteral("127.0.0.1") ||
           host == QStringLiteral("::1") || host == QStringLiteral("[::1]");
}

bool loadGridManifest(const QString &path, QList<QUrl> *urls, QString *errorMessage) {
    const QFileInfo fileInfo(path);
    if (!fileInfo.isAbsolute() || !fileInfo.isFile()) {
        *errorMessage = QStringLiteral("--verify-mpv-grid-manifest 必须是存在的绝对 JSON 文件路径。");
        return false;
    }
    QFile file(fileInfo.absoluteFilePath());
    if (!file.open(QIODevice::ReadOnly)) {
        *errorMessage = QStringLiteral("无法读取分屏验收清单。");
        return false;
    }
    QJsonParseError parseError;
    const QJsonDocument document = QJsonDocument::fromJson(file.readAll(), &parseError);
    if (parseError.error != QJsonParseError::NoError || !document.isObject()) {
        *errorMessage = QStringLiteral("分屏验收清单必须是 JSON 对象。");
        return false;
    }
    const QJsonValue streamValue = document.object().value(QStringLiteral("streams"));
    if (!streamValue.isArray()) {
        *errorMessage = QStringLiteral("分屏验收清单必须包含 streams 数组。");
        return false;
    }
    const QJsonArray streams = streamValue.toArray();
    if (streams.isEmpty() || streams.size() > 64) {
        *errorMessage = QStringLiteral("分屏验收清单的 streams 数量必须在 1 到 64 之间。");
        return false;
    }
    QSet<QString> seenUrls;
    for (const QJsonValue &value : streams) {
        if (!value.isString()) {
            *errorMessage = QStringLiteral("分屏验收清单中的 streams 元素必须是字符串。");
            return false;
        }
        const QUrl url(value.toString(), QUrl::StrictMode);
        if (!isSafeVerificationStreamUrl(url)) {
            *errorMessage = QStringLiteral("分屏验收清单仅允许 HTTPS，或本机回环 HTTP 且不含用户信息的地址。");
            return false;
        }
        const QString canonical = url.toString(QUrl::FullyEncoded);
        if (seenUrls.contains(canonical)) {
            *errorMessage = QStringLiteral("分屏验收清单不允许重复流地址。");
            return false;
        }
        seenUrls.insert(canonical);
        urls->append(url);
    }
    return true;
}
}

int main(int argc, char *argv[]) {
    QApplication application(argc, argv);
    QApplication::setQuitOnLastWindowClosed(false);
    QApplication::setApplicationName(QStringLiteral("视枢查看端"));
    QApplication::setOrganizationName(QStringLiteral("VisiCore"));
    CrashReporter::install();
    ThemeManager::instance().apply(application);

    QCommandLineParser parser;
    parser.setApplicationDescription(QStringLiteral("视枢 Windows 原生查看客户端"));
    parser.addHelpOption();
    QCommandLineOption apiOption(QStringList{QStringLiteral("a"), QStringLiteral("api-url")},
                                 QStringLiteral("仅本次启动使用的中心 API 基础地址"), QStringLiteral("url"));
    QCommandLineOption insecureOption(QStringLiteral("allow-insecure-http"), QStringLiteral("仅允许 localhost 开发环境使用 HTTP"));
    QCommandLineOption safeUiOption(
        QStringLiteral("safe-ui"),
        QStringLiteral("忽略上次窗口与面板布局，以安全界面模式启动查看端"));
    QCommandLineOption mpvLibraryOption(QStringLiteral("mpv-library"),
                                        QStringLiteral("显式指定已审核的 libmpv x64 运行时绝对路径"), QStringLiteral("path"));
    QCommandLineOption verifyMpvRuntimeOption(QStringLiteral("verify-mpv-runtime"),
                                               QStringLiteral("仅校验 libmpv 运行时，不登录也不访问视频服务"));
    QCommandLineOption verifyMpvPlaybackOption(QStringLiteral("verify-mpv-playback"),
                                                QStringLiteral("离线播放本地媒体，校验 libmpv 初始化和嵌入播放"), QStringLiteral("file"));
    QCommandLineOption verifyMpvStreamOption(QStringLiteral("verify-mpv-stream"),
                                              QStringLiteral("播放受控流地址，校验 libmpv 网络 HLS 播放"), QStringLiteral("url"));
    QCommandLineOption verifyMpvGridOption(QStringLiteral("verify-mpv-grid-manifest"),
                                            QStringLiteral("读取受控 HLS 清单，在同一原生窗口验收 1 至 64 路分屏"), QStringLiteral("file"));
    QCommandLineOption verifyMpvGridDurationOption(QStringLiteral("verify-mpv-grid-duration-seconds"),
                                                    QStringLiteral("分屏验收全部就绪后的持续秒数，范围 1 至 3600"), QStringLiteral("seconds"), QStringLiteral("60"));
    QCommandLineOption verifyUiShellOption(
        QStringLiteral("verify-ui-shell"),
        QStringLiteral("不登录且不连接外部服务，显示空数据工作台用于视觉验收"));
    QCommandLineOption verifyLoginShellOption(
        QStringLiteral("verify-login-shell"),
        QStringLiteral("不登录且不连接外部服务，短时显示原生登录窗口用于稳定性验收"));
    QCommandLineOption verifyUiSizeOption(
        QStringLiteral("verify-ui-size"),
        QStringLiteral("指定视觉验收窗口尺寸，例如 1366x768"),
        QStringLiteral("widthxheight"));
    parser.addOption(apiOption);
    parser.addOption(insecureOption);
    parser.addOption(safeUiOption);
    parser.addOption(mpvLibraryOption);
    parser.addOption(verifyMpvRuntimeOption);
    parser.addOption(verifyMpvPlaybackOption);
    parser.addOption(verifyMpvStreamOption);
    parser.addOption(verifyMpvGridOption);
    parser.addOption(verifyMpvGridDurationOption);
    parser.addOption(verifyUiShellOption);
    parser.addOption(verifyLoginShellOption);
    parser.addOption(verifyUiSizeOption);
    parser.process(application);

    if (parser.isSet(mpvLibraryOption)) {
        const QFileInfo mpvLibraryInfo(parser.value(mpvLibraryOption));
        if (!mpvLibraryInfo.isAbsolute() || !mpvLibraryInfo.isFile()) {
            QTextStream(stderr) << "--mpv-library 必须是存在的绝对文件路径。\n";
            return 2;
        }
        qputenv("VISICORE_MPV_LIBRARY", mpvLibraryInfo.absoluteFilePath().toUtf8());
    }
    const int verificationModeCount = static_cast<int>(parser.isSet(verifyMpvRuntimeOption)) +
        static_cast<int>(parser.isSet(verifyMpvPlaybackOption)) +
        static_cast<int>(parser.isSet(verifyMpvStreamOption)) +
        static_cast<int>(parser.isSet(verifyMpvGridOption)) +
        static_cast<int>(parser.isSet(verifyUiShellOption)) +
        static_cast<int>(parser.isSet(verifyLoginShellOption));
    if (verificationModeCount > 1) {
        QTextStream(stderr) << "一次只能指定一种验收模式。\n";
        return 2;
    }
    if (parser.isSet(verifyMpvGridDurationOption) && !parser.isSet(verifyMpvGridOption)) {
        QTextStream(stderr) << "--verify-mpv-grid-duration-seconds 只能与 --verify-mpv-grid-manifest 一起使用。\n";
        return 2;
    }
    if (parser.isSet(verifyUiSizeOption) && !parser.isSet(verifyUiShellOption)) {
        QTextStream(stderr) << "--verify-ui-size 只能与 --verify-ui-shell 一起使用。\n";
        return 2;
    }
    if (parser.isSet(verifyMpvRuntimeOption)) {
        QString runtimeError;
        if (!MpvPlayerWidget::verifyRuntime(&runtimeError)) {
            QTextStream(stderr) << "libmpv 运行时校验失败：" << runtimeError << '\n';
            return 3;
        }
        QTextStream(stdout) << "libmpv 运行时校验通过。\n";
        return 0;
    }
    const auto runPlaybackVerification = [&application](const QUrl &mediaUrl, const QString &title, const QString &label) {
        QString runtimeError;
        if (!MpvPlayerWidget::verifyRuntime(&runtimeError)) {
            QTextStream(stderr) << "libmpv 运行时校验失败：" << runtimeError << '\n';
            return 3;
        }

        MpvPlayerWidget verificationPlayer;
        verificationPlayer.setWindowTitle(title);
        verificationPlayer.resize(16, 16);
        verificationPlayer.show();
        QTimer timeout;
        timeout.setSingleShot(true);
        timeout.setInterval(15000);
        bool completionScheduled = false;
        const auto finish = [&application, &verificationPlayer, &timeout, &completionScheduled](int exitCode) {
            if (completionScheduled) {
                return;
            }
            completionScheduled = true;
            QTimer::singleShot(0, &application, [&application, &verificationPlayer, &timeout, exitCode]() {
                timeout.stop();
                verificationPlayer.stop();
                verificationPlayer.release();
                QCoreApplication::exit(exitCode);
            });
        };
        QObject::connect(&timeout, &QTimer::timeout, &application, [&verificationPlayer, label, finish]() {
            QTextStream(stderr) << "libmpv " << label << "校验超时。\n";
            finish(4);
        });
        QObject::connect(&verificationPlayer, &MpvPlayerWidget::playbackStarted, &application, [label, finish](quint64) {
            QTextStream(stdout) << "libmpv " << label << "校验通过。\n";
            QTextStream(stdout) << "VERIFICATION_PASSED\n";
            finish(0);
        });
        QObject::connect(&verificationPlayer, &MpvPlayerWidget::playbackError, &application, [label, finish](quint64, const QString &message) {
            QTextStream(stderr) << "libmpv " << label << "校验失败：" << message << '\n';
            finish(4);
        });
        timeout.start();
        if (!verificationPlayer.start(mediaUrl, 1)) {
            timeout.stop();
            verificationPlayer.release();
            return 4;
        }
        return application.exec();
    };

    const auto runGridVerification = [&application](const QList<QUrl> &mediaUrls, int durationSeconds) {
        QString runtimeError;
        if (!MpvPlayerWidget::verifyRuntime(&runtimeError)) {
            QTextStream(stderr) << "libmpv 运行时校验失败：" << runtimeError << '\n';
            return 3;
        }

        auto *host = new QWidget;
        host->setWindowTitle(QStringLiteral("视频平台 64 分屏验收"));
        auto *layout = new QGridLayout(host);
        layout->setContentsMargins(0, 0, 0, 0);
        layout->setSpacing(1);
        const int columnCount = static_cast<int>(std::ceil(std::sqrt(static_cast<double>(mediaUrls.size()))));
        QVector<MpvPlayerWidget *> players;
        players.reserve(mediaUrls.size());
        for (qsizetype index = 0; index < mediaUrls.size(); ++index) {
            auto *player = new MpvPlayerWidget(host);
            player->setMinimumSize(16, 16);
            layout->addWidget(player, static_cast<int>(index) / columnCount, static_cast<int>(index) % columnCount);
            players.append(player);
        }
        host->showMaximized();

        QElapsedTimer elapsed;
        elapsed.start();
        auto *startupTimeout = new QTimer(host);
        auto *sustainTimer = new QTimer(host);
        startupTimeout->setSingleShot(true);
        sustainTimer->setSingleShot(true);
        startupTimeout->setInterval(60000);
        sustainTimer->setInterval(durationSeconds * 1000);
        int startedCount = 0;
        qint64 firstReadyMilliseconds = -1;
        qint64 allReadyMilliseconds = -1;
        bool finished = false;

        const auto finish = [&](int exitCode, const QString &status) {
            if (finished) {
                return;
            }
            finished = true;
            startupTimeout->stop();
            sustainTimer->stop();
            for (MpvPlayerWidget *player : players) {
                player->stop();
                player->release();
            }
            QTextStream output(exitCode == 0 ? stdout : stderr);
            output << "{\"status\":\"" << status << "\",\"players\":" << players.size()
                   << ",\"startedPlayers\":" << startedCount
                   << ",\"firstReadyMilliseconds\":" << firstReadyMilliseconds
                   << ",\"allReadyMilliseconds\":" << allReadyMilliseconds
                   << ",\"elapsedMilliseconds\":" << elapsed.elapsed() << "}\n";
            host->close();
            QCoreApplication::exit(exitCode);
        };

        QObject::connect(startupTimeout, &QTimer::timeout, host, [&]() {
            finish(4, QStringLiteral("startup_timeout"));
        });
        QObject::connect(sustainTimer, &QTimer::timeout, host, [&]() {
            finish(0, QStringLiteral("passed"));
        });
        for (qsizetype index = 0; index < players.size(); ++index) {
            MpvPlayerWidget *player = players[index];
            QObject::connect(player, &MpvPlayerWidget::playbackStarted, host, [&](quint64) {
                if (finished) {
                    return;
                }
                ++startedCount;
                if (firstReadyMilliseconds < 0) {
                    firstReadyMilliseconds = elapsed.elapsed();
                }
                if (startedCount == static_cast<int>(players.size())) {
                    allReadyMilliseconds = elapsed.elapsed();
                    sustainTimer->start();
                }
            });
            QObject::connect(player, &MpvPlayerWidget::playbackError, host, [&](quint64, const QString &) {
                finish(4, QStringLiteral("playback_error"));
            });
        }
        startupTimeout->start();
        for (qsizetype index = 0; index < players.size() && !finished; ++index) {
            if (!players[index]->start(mediaUrls[index], static_cast<quint64>(index + 1))) {
                finish(4, QStringLiteral("start_failed"));
            }
        }
        const int exitCode = application.exec();
        for (MpvPlayerWidget *player : players) {
            player->release();
        }
        delete host;
        return exitCode;
    };

    if (parser.isSet(verifyMpvPlaybackOption)) {
        const QFileInfo mediaFile(parser.value(verifyMpvPlaybackOption));
        if (!mediaFile.isAbsolute() || !mediaFile.isFile()) {
            QTextStream(stderr) << "--verify-mpv-playback 必须是存在的本地媒体绝对路径。\n";
            return 2;
        }
        return runPlaybackVerification(
            QUrl::fromLocalFile(mediaFile.absoluteFilePath()),
            QStringLiteral("视频平台播放器离线校验"),
            QStringLiteral("离线播放"));
    }
    if (parser.isSet(verifyMpvStreamOption)) {
        const QUrl streamUrl(parser.value(verifyMpvStreamOption), QUrl::StrictMode);
        if (!isSafeVerificationStreamUrl(streamUrl)) {
            QTextStream(stderr) << "--verify-mpv-stream 仅允许 HTTPS，或本机回环 HTTP 且不含用户信息的地址。\n";
            return 2;
        }
        return runPlaybackVerification(
            streamUrl,
            QStringLiteral("视频平台播放器流校验"),
            QStringLiteral("网络流播放"));
    }
    if (parser.isSet(verifyMpvGridOption)) {
        bool validDuration = false;
        const int durationSeconds = parser.value(verifyMpvGridDurationOption).toInt(&validDuration);
        if (!validDuration || durationSeconds < 1 || durationSeconds > 3600) {
            QTextStream(stderr) << "--verify-mpv-grid-duration-seconds 必须在 1 到 3600 之间。\n";
            return 2;
        }
        QList<QUrl> urls;
        QString manifestError;
        if (!loadGridManifest(parser.value(verifyMpvGridOption), &urls, &manifestError)) {
            QTextStream(stderr) << manifestError << '\n';
            return 2;
        }
        return runGridVerification(urls, durationSeconds);
    }

    if (parser.isSet(verifyUiShellOption)) {
        QCoreApplication::setApplicationName(QStringLiteral("视枢查看端-视觉验收"));
        ApiClient verificationApiClient(QUrl(QStringLiteral("http://127.0.0.1:1/")), true);
        MainWindow verificationWindow(&verificationApiClient);
        verificationWindow.setWindowTitle(QStringLiteral("视枢查看端 - 视觉验收"));
        if (parser.isSet(verifyUiSizeOption)) {
            const QStringList dimensions = parser.value(verifyUiSizeOption).toLower().split(QLatin1Char('x'));
            bool widthValid = false;
            bool heightValid = false;
            const int width = dimensions.value(0).toInt(&widthValid);
            const int height = dimensions.value(1).toInt(&heightValid);
            if (dimensions.size() != 2 || !widthValid || !heightValid ||
                width < 1080 || height < 680 || width > 7680 || height > 4320) {
                QTextStream(stderr) << "--verify-ui-size 必须是 1080x680 至 7680x4320 范围内的尺寸。\n";
                return 2;
            }
            verificationWindow.resize(width, height);
        }
        if (auto *titleBar = verificationWindow.findChild<WindowTitleBar *>()) {
            titleBar->setConnectionState(
                ViewerConnectionState::Disconnected,
                QStringLiteral("视觉验收模式"));
        }
        verificationWindow.show();
        return application.exec();
    }

    if (parser.isSet(verifyLoginShellOption)) {
        QCoreApplication::setApplicationName(QStringLiteral("视枢查看端-登录验收"));
        ApiClient verificationApiClient(QUrl(QStringLiteral("http://127.0.0.1:1/")), true);
        LoginDialog verificationLoginDialog(&verificationApiClient);
        QTimer::singleShot(1500, &verificationLoginDialog, &QDialog::reject);
        return verificationLoginDialog.exec() == QDialog::Rejected ? 0 : 4;
    }

    ViewerStartupState startupState = ViewerStartupState::begin(parser.isSet(safeUiOption));
    const ViewerStartupMode startupMode = startupState.mode();
    QObject::connect(&application, &QCoreApplication::aboutToQuit, &application, [&startupState]() {
        startupState.markCleanShutdown();
    });

    const bool allowInsecureHttp = parser.isSet(insecureOption);
    const QUrl initialApiUrl = parser.isSet(apiOption)
        ? QUrl(parser.value(apiOption), QUrl::StrictMode)
        : ConnectionSettings(allowInsecureHttp).savedBaseUrl();
    ApiClient apiClient(initialApiUrl, allowInsecureHttp);
    if (!apiClient.isBaseUrlValid()) {
        AppDialog::critical(
            nullptr,
            QStringLiteral("中心 API 地址无效"),
            QStringLiteral("生产环境必须使用 HTTPS；HTTP 仅允许 localhost 并显式启用开发开关。"));
        startupState.markCleanShutdown();
        return 2;
    }

    QPointer<MainWindow> activeWindow;
    std::function<bool()> showLogin;
    std::function<void()> scheduleLogin;
    std::function<void(MainWindow *, bool)> returnToLogin;
    const auto recoveredStartupAccounts = std::make_shared<QSet<QString>>();

    scheduleLogin = [&application, &showLogin]() {
        QTimer::singleShot(0, &application, [&application, &showLogin]() {
            if (!showLogin()) {
                application.quit();
            }
        });
    };
    returnToLogin = [&apiClient, &activeWindow, &scheduleLogin](MainWindow *window, bool logoutCurrentSession) {
        if (activeWindow != window) {
            return;
        }
        activeWindow.clear();
        window->deleteLater();
        if (!logoutCurrentSession) {
            scheduleLogin();
            return;
        }

        auto *logoutWaiter = new QObject(&apiClient);
        const auto completed = std::make_shared<bool>(false);
        const auto finishLogout = [completed, logoutWaiter, &scheduleLogin]() {
            if (*completed) {
                return;
            }
            *completed = true;
            logoutWaiter->deleteLater();
            scheduleLogin();
        };
        QObject::connect(
            &apiClient,
            &ApiClient::shutdownFinished,
            logoutWaiter,
            finishLogout,
            Qt::SingleShotConnection);
        QTimer::singleShot(2000, logoutWaiter, finishLogout);
        apiClient.logout();
    };
    showLogin = [&apiClient, &application, &activeWindow, &returnToLogin,
                 startupMode, recoveredStartupAccounts]() {
        LoginDialog loginDialog(&apiClient);
        if (loginDialog.exec() != QDialog::Accepted) {
            return false;
        }

        ViewerStartupMode windowStartupMode = startupMode;
        const QString username = apiClient.username();
        if (windowStartupMode != ViewerStartupMode::Normal) {
            if (recoveredStartupAccounts->contains(username)) {
                windowStartupMode = ViewerStartupMode::Normal;
            } else {
                recoveredStartupAccounts->insert(username);
            }
        }
        auto *window = new MainWindow(&apiClient, windowStartupMode);
        activeWindow = window;
        QObject::connect(window, &MainWindow::logoutRequested, &application, [&returnToLogin, window]() {
            returnToLogin(window, true);
        });
        QObject::connect(window, &MainWindow::reauthenticationRequested, &application, [&returnToLogin, window]() {
            returnToLogin(window, false);
        });
        window->show();
        return true;
    };

    if (!showLogin()) {
        startupState.markCleanShutdown();
        return 0;
    }

    const int exitCode = application.exec();
    delete activeWindow.data();
    startupState.markCleanShutdown();
    return exitCode;
}
