#include "mpv_player_widget.h"

#include <QDateTime>
#include <QFile>
#include <QLibrary>
#include <QMetaObject>
#include <QOpenGLContext>
#include <QStringList>
#include <QTextStream>
#include <QTimer>
#include <QUrl>

#include <cmath>

namespace {
constexpr int MpvEventIdNone = 0;
constexpr int MpvEventIdShutdown = 1;
constexpr int MpvEventIdStartFile = 6;
constexpr int MpvEventIdEndFile = 7;
constexpr int MpvEventIdFileLoaded = 8;
constexpr int MpvEventIdVideoReconfig = 17;
constexpr int MpvEventIdPropertyChange = 22;
constexpr int MpvFormatFlag = 3;
constexpr int MpvFormatDouble = 5;
constexpr unsigned long long TimePositionObservationId = 1;
constexpr unsigned long long BufferingObservationId = 2;
constexpr char MpvRenderApiOpenGl[] = "opengl";

void writeMpvDiagnostic(const QString &message) {
    const QString path = qEnvironmentVariable("VIDEO_PLATFORM_MPV_DIAGNOSTICS");
    if (path.isEmpty()) {
        return;
    }
    QFile file(path);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Append | QIODevice::Text)) {
        return;
    }
    QTextStream stream(&file);
    stream << QDateTime::currentDateTimeUtc().toString(Qt::ISODateWithMs) << " " << message << Qt::endl;
}

QStringList mpvRuntimeLibraryNames() {
    const QString configuredPath = qEnvironmentVariable("VIDEO_PLATFORM_MPV_LIBRARY");
    return configuredPath.isEmpty()
        ? QStringList{QStringLiteral("mpv-2"), QStringLiteral("libmpv-2")}
        : QStringList{configuredPath};
}

QString mpvRuntimeDisplayName() {
    return qEnvironmentVariableIsEmpty("VIDEO_PLATFORM_MPV_LIBRARY")
        ? QStringLiteral("mpv-2.dll 或 libmpv-2.dll")
        : QStringLiteral("指定的 libmpv 运行时");
}

bool loadMpvRuntimeLibrary(QLibrary &library) {
    for (const QString &name : mpvRuntimeLibraryNames()) {
        library.setFileName(name);
        if (library.load()) {
            return true;
        }
    }
    return false;
}
}

MpvPlayerWidget::MpvPlayerWidget(QWidget *parent) : QOpenGLWidget(parent) {
    setAutoFillBackground(false);
    setUpdateBehavior(QOpenGLWidget::PartialUpdate);
    writeMpvDiagnostic(QStringLiteral("创建 Render API 视频窗格。"));
    eventPollTimer_ = new QTimer(this);
    eventPollTimer_->setInterval(50);
    connect(eventPollTimer_, &QTimer::timeout, this, [this]() {
        processEvents(instanceGeneration_);
    });
}

MpvPlayerWidget::~MpvPlayerWidget() {
    release();
    delete library_;
    library_ = nullptr;
}

bool MpvPlayerWidget::start(const QUrl &url, quint64 playbackGeneration) {
    playbackGeneration_ = playbackGeneration;
    if (!url.isValid()) {
        emit playbackError(playbackGeneration_, QStringLiteral("视频地址无效。"));
        return false;
    }

    pendingUrl_ = url.toEncoded();
    pendingStart_ = true;
    stopping_ = false;
    waitingForStartEvent_ = true;
    fileLoaded_ = false;
    videoReconfigured_ = false;
    playbackStartedEmitted_ = false;
    buffering_ = false;
    writeMpvDiagnostic(QStringLiteral("收到媒体播放请求，OpenGL 已初始化：%1。").arg(glInitialized_));
    if (glInitialized_) {
        makeCurrent();
        const bool initialized = initializeMpv();
        doneCurrent();
        if (!initialized) {
            return false;
        }
        startPendingPlayback();
    } else {
        update();
    }
    return true;
}

void MpvPlayerWidget::stop() {
    pendingStart_ = false;
    pendingUrl_.clear();
    stopping_ = true;
    waitingForStartEvent_ = false;
    fileLoaded_ = false;
    videoReconfigured_ = false;
    playbackStartedEmitted_ = false;
    buffering_ = false;
    if (mpv_ == nullptr || command_ == nullptr) {
        return;
    }
    const char *command[] = {"stop", nullptr};
    command_(mpv_, command);
}

void MpvPlayerWidget::release() {
    ++instanceGeneration_;
    stop();
    if (eventPollTimer_ != nullptr) {
        eventPollTimer_->stop();
    }
    releaseMpv();
}

bool MpvPlayerWidget::isReady() const {
    return mpv_ != nullptr && renderContext_ != nullptr;
}

bool MpvPlayerWidget::verifyRuntime(QString *errorMessage) {
    QLibrary library;
    if (!loadMpvRuntimeLibrary(library)) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("%1 无法加载。").arg(mpvRuntimeDisplayName());
        }
        return false;
    }

    const auto clientApiVersion = reinterpret_cast<MpvClientApiVersion>(library.resolve("mpv_client_api_version"));
    const auto create = reinterpret_cast<MpvCreate>(library.resolve("mpv_create"));
    const auto setOptionString = reinterpret_cast<MpvSetOptionString>(library.resolve("mpv_set_option_string"));
    const auto initialize = reinterpret_cast<MpvInitialize>(library.resolve("mpv_initialize"));
    const auto command = reinterpret_cast<MpvCommand>(library.resolve("mpv_command"));
    const auto observeProperty = reinterpret_cast<MpvObserveProperty>(library.resolve("mpv_observe_property"));
    const auto waitEvent = reinterpret_cast<MpvWaitEvent>(library.resolve("mpv_wait_event"));
    const auto setWakeupCallback = reinterpret_cast<MpvSetWakeupCallback>(library.resolve("mpv_set_wakeup_callback"));
    const auto terminateDestroy = reinterpret_cast<MpvTerminateDestroy>(library.resolve("mpv_terminate_destroy"));
    const auto errorString = reinterpret_cast<MpvErrorString>(library.resolve("mpv_error_string"));
    const auto renderContextCreate = reinterpret_cast<MpvRenderContextCreate>(library.resolve("mpv_render_context_create"));
    const auto renderContextSetUpdateCallback = reinterpret_cast<MpvRenderContextSetUpdateCallback>(library.resolve("mpv_render_context_set_update_callback"));
    const auto renderContextRender = reinterpret_cast<MpvRenderContextRender>(library.resolve("mpv_render_context_render"));
    const auto renderContextReportSwap = reinterpret_cast<MpvRenderContextReportSwap>(library.resolve("mpv_render_context_report_swap"));
    const auto renderContextFree = reinterpret_cast<MpvRenderContextFree>(library.resolve("mpv_render_context_free"));
    if (clientApiVersion == nullptr || create == nullptr || setOptionString == nullptr || initialize == nullptr ||
        command == nullptr || observeProperty == nullptr || waitEvent == nullptr || setWakeupCallback == nullptr ||
        terminateDestroy == nullptr || errorString == nullptr || renderContextCreate == nullptr ||
        renderContextSetUpdateCallback == nullptr || renderContextRender == nullptr ||
        renderContextReportSwap == nullptr || renderContextFree == nullptr) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("%1 缺少所需的 libmpv Render API。").arg(mpvRuntimeDisplayName());
        }
        return false;
    }
    if ((clientApiVersion() >> 16U) != 2U) {
        if (errorMessage != nullptr) {
            *errorMessage = QStringLiteral("%1 的客户端 API 主版本不兼容。").arg(mpvRuntimeDisplayName());
        }
        return false;
    }
    return true;
}

void MpvPlayerWidget::initializeGL() {
    glInitialized_ = true;
    if (!pendingStart_) {
        writeMpvDiagnostic(QStringLiteral("OpenGL 上下文已创建，等待实际媒体播放请求。"));
        return;
    }
    writeMpvDiagnostic(QStringLiteral("OpenGL 上下文已创建，开始初始化 Render API。"));
    if (initializeMpv()) {
        writeMpvDiagnostic(QStringLiteral("Render API 初始化完成，开始处理待播放媒体。"));
        startPendingPlayback();
    }
}

void MpvPlayerWidget::paintGL() {
    if (renderContext_ == nullptr || renderContextRender_ == nullptr) {
        return;
    }

    const qreal scale = devicePixelRatioF();
    MpvOpenGlFbo fbo{
        static_cast<int>(defaultFramebufferObject()),
        static_cast<int>(std::lround(static_cast<qreal>(width()) * scale)),
        static_cast<int>(std::lround(static_cast<qreal>(height()) * scale)),
        0};
    if (fbo.width <= 0 || fbo.height <= 0) {
        return;
    }
    int flipY = 1;
    MpvRenderParam parameters[] = {
        {RenderParamOpenGlFbo, &fbo},
        {RenderParamFlipY, &flipY},
        {RenderParamInvalid, nullptr}};
    const int result = renderContextRender_(renderContext_, parameters);
    if (result < 0 && !stopping_) {
        emit playbackError(playbackGeneration_, QStringLiteral("视频渲染失败：%1").arg(mpvError(result)));
        return;
    }
    if (renderContextReportSwap_ != nullptr) {
        renderContextReportSwap_(renderContext_);
    }
}

bool MpvPlayerWidget::loadRuntimeSymbols() {
    if (library_ != nullptr) {
        return true;
    }

    library_ = new QLibrary(this);
    if (!loadMpvRuntimeLibrary(*library_)) {
        emit playbackError(playbackGeneration_, QStringLiteral("%1 无法加载。").arg(mpvRuntimeDisplayName()));
        delete library_;
        library_ = nullptr;
        return false;
    }
    clientApiVersion_ = reinterpret_cast<MpvClientApiVersion>(library_->resolve("mpv_client_api_version"));
    create_ = reinterpret_cast<MpvCreate>(library_->resolve("mpv_create"));
    setOptionString_ = reinterpret_cast<MpvSetOptionString>(library_->resolve("mpv_set_option_string"));
    initialize_ = reinterpret_cast<MpvInitialize>(library_->resolve("mpv_initialize"));
    command_ = reinterpret_cast<MpvCommand>(library_->resolve("mpv_command"));
    observeProperty_ = reinterpret_cast<MpvObserveProperty>(library_->resolve("mpv_observe_property"));
    waitEvent_ = reinterpret_cast<MpvWaitEvent>(library_->resolve("mpv_wait_event"));
    setWakeupCallback_ = reinterpret_cast<MpvSetWakeupCallback>(library_->resolve("mpv_set_wakeup_callback"));
    terminateDestroy_ = reinterpret_cast<MpvTerminateDestroy>(library_->resolve("mpv_terminate_destroy"));
    errorString_ = reinterpret_cast<MpvErrorString>(library_->resolve("mpv_error_string"));
    renderContextCreate_ = reinterpret_cast<MpvRenderContextCreate>(library_->resolve("mpv_render_context_create"));
    renderContextSetUpdateCallback_ = reinterpret_cast<MpvRenderContextSetUpdateCallback>(library_->resolve("mpv_render_context_set_update_callback"));
    renderContextRender_ = reinterpret_cast<MpvRenderContextRender>(library_->resolve("mpv_render_context_render"));
    renderContextReportSwap_ = reinterpret_cast<MpvRenderContextReportSwap>(library_->resolve("mpv_render_context_report_swap"));
    renderContextFree_ = reinterpret_cast<MpvRenderContextFree>(library_->resolve("mpv_render_context_free"));
    if (clientApiVersion_ == nullptr || create_ == nullptr || setOptionString_ == nullptr || initialize_ == nullptr ||
        command_ == nullptr || observeProperty_ == nullptr || waitEvent_ == nullptr || setWakeupCallback_ == nullptr ||
        terminateDestroy_ == nullptr || errorString_ == nullptr || renderContextCreate_ == nullptr ||
        renderContextSetUpdateCallback_ == nullptr || renderContextRender_ == nullptr ||
        renderContextReportSwap_ == nullptr || renderContextFree_ == nullptr) {
        emit playbackError(playbackGeneration_, QStringLiteral("%1 缺少所需的 libmpv Render API。").arg(mpvRuntimeDisplayName()));
        delete library_;
        library_ = nullptr;
        return false;
    }
    if ((clientApiVersion_() >> 16U) != 2U) {
        emit playbackError(playbackGeneration_, QStringLiteral("%1 的客户端 API 主版本不兼容。").arg(mpvRuntimeDisplayName()));
        delete library_;
        library_ = nullptr;
        return false;
    }
    writeMpvDiagnostic(QStringLiteral("libmpv Render API 符号加载完成。"));
    return true;
}

bool MpvPlayerWidget::initializeMpv() {
    if (mpv_ != nullptr && renderContext_ != nullptr) {
        return true;
    }
    if (context() == nullptr) {
        emit playbackError(playbackGeneration_, QStringLiteral("视频渲染上下文尚未创建。"));
        return false;
    }
    if (!loadRuntimeSymbols()) {
        return false;
    }

    writeMpvDiagnostic(QStringLiteral("创建 libmpv 播放内核。"));

    mpv_ = create_();
    if (mpv_ == nullptr) {
        emit playbackError(playbackGeneration_, QStringLiteral("无法创建 libmpv 实例。"));
        return false;
    }

    const auto setRequiredOption = [this](const char *name, const char *value) {
        const int optionResult = setOptionString_(mpv_, name, value);
        if (optionResult < 0) {
            emit playbackError(playbackGeneration_, QStringLiteral("libmpv 参数 %1 配置失败：%2")
                                                       .arg(QString::fromLatin1(name), mpvError(optionResult)));
            return false;
        }
        return true;
    };
    const auto setOptionalOption = [this](const char *name, const char *value) {
        const int optionResult = setOptionString_(mpv_, name, value);
        if (optionResult < 0) {
            writeMpvDiagnostic(QStringLiteral("libmpv 优化参数 %1 未生效：%2")
                                   .arg(QString::fromLatin1(name), mpvError(optionResult)));
        }
    };
    if (!setRequiredOption("audio", "no") ||
        !setRequiredOption("keep-open", "no") ||
        !setRequiredOption("network-timeout", "10") ||
        !setRequiredOption("vo", "libmpv")) {
        releaseMpv();
        return false;
    }
    setOptionalOption("cache", "yes");
    setOptionalOption("cache-secs", "4");
    setOptionalOption("demuxer-max-bytes", "12MiB");
    setOptionalOption("demuxer-max-back-bytes", "1MiB");
    setOptionalOption("demuxer-donate-buffer", "no");
    setOptionalOption("vd-lavc-threads", "2");
    const int initializeResult = initialize_(mpv_);
    if (initializeResult < 0) {
        emit playbackError(playbackGeneration_, QStringLiteral("libmpv 初始化失败：%1").arg(mpvError(initializeResult)));
        releaseMpv();
        return false;
    }
    const int observePositionResult = observeProperty_(mpv_, TimePositionObservationId, "time-pos", MpvFormatDouble);
    const int observeBufferingResult = observeProperty_(mpv_, BufferingObservationId, "paused-for-cache", MpvFormatFlag);
    if (observePositionResult < 0 || observeBufferingResult < 0) {
        emit playbackError(playbackGeneration_, QStringLiteral("播放器进度监听初始化失败。"));
        releaseMpv();
        return false;
    }

    writeMpvDiagnostic(QStringLiteral("libmpv 播放内核已初始化，创建 OpenGL 渲染上下文。"));

    MpvOpenGlInitParams openGlInitParams{&MpvPlayerWidget::resolveOpenGlProcAddress, this};
    char *apiType = const_cast<char *>(MpvRenderApiOpenGl);
    MpvRenderParam parameters[] = {
        {RenderParamApiType, apiType},
        {RenderParamOpenGlInitParams, &openGlInitParams},
        {RenderParamInvalid, nullptr}};
    const int createRenderResult = renderContextCreate_(&renderContext_, mpv_, parameters);
    if (createRenderResult < 0 || renderContext_ == nullptr) {
        emit playbackError(playbackGeneration_, QStringLiteral("视频渲染器初始化失败：%1").arg(mpvError(createRenderResult)));
        releaseMpv();
        return false;
    }
    renderContextSetUpdateCallback_(renderContext_, &MpvPlayerWidget::requestRender, this);
    writeMpvDiagnostic(QStringLiteral("OpenGL Render API 上下文已创建。"));
    ++instanceGeneration_;
    eventPollTimer_->start();
    return true;
}

void MpvPlayerWidget::startPendingPlayback() {
    if (!pendingStart_ || mpv_ == nullptr || command_ == nullptr) {
        return;
    }
    const char *command[] = {"loadfile", pendingUrl_.constData(), "replace", nullptr};
    const int result = command_(mpv_, command);
    if (result < 0) {
        waitingForStartEvent_ = false;
        pendingStart_ = false;
        emit playbackError(playbackGeneration_, QStringLiteral("媒体加载失败：%1").arg(mpvError(result)));
        return;
    }
    pendingStart_ = false;
    writeMpvDiagnostic(QStringLiteral("媒体加载命令已提交。"));
}

void MpvPlayerWidget::releaseMpv() {
    if (renderContext_ != nullptr) {
        if (context() != nullptr) {
            makeCurrent();
        }
        if (renderContextSetUpdateCallback_ != nullptr) {
            renderContextSetUpdateCallback_(renderContext_, nullptr, nullptr);
        }
        if (renderContextFree_ != nullptr) {
            renderContextFree_(renderContext_);
        }
        renderContext_ = nullptr;
        if (context() != nullptr) {
            doneCurrent();
        }
    }
    if (mpv_ != nullptr && terminateDestroy_ != nullptr) {
        if (setWakeupCallback_ != nullptr) {
            setWakeupCallback_(mpv_, nullptr, nullptr);
        }
        terminateDestroy_(mpv_);
    }
    mpv_ = nullptr;
}

void MpvPlayerWidget::processEvents(quint64 instanceGeneration) {
    if (mpv_ == nullptr || waitEvent_ == nullptr || instanceGeneration != instanceGeneration_) {
        return;
    }
    while (const MpvEvent *event = waitEvent_(mpv_, 0.0)) {
        if (instanceGeneration != instanceGeneration_) {
            return;
        }
        if (event->eventId == MpvEventIdNone) {
            return;
        }
        if (event->eventId == MpvEventIdStartFile) {
            waitingForStartEvent_ = false;
            writeMpvDiagnostic(QStringLiteral("媒体事件：开始加载。"));
        } else if (event->eventId == MpvEventIdFileLoaded) {
            fileLoaded_ = true;
            writeMpvDiagnostic(QStringLiteral("媒体事件：文件已加载。"));
        } else if (event->eventId == MpvEventIdVideoReconfig) {
            videoReconfigured_ = true;
            writeMpvDiagnostic(QStringLiteral("媒体事件：视频解码器已配置。"));
        } else if (event->eventId == MpvEventIdPropertyChange) {
            const auto *property = static_cast<const MpvEventProperty *>(event->data);
            if (property != nullptr && property->data != nullptr && event->replyUserdata == TimePositionObservationId &&
                property->format == MpvFormatDouble) {
                const double seconds = *static_cast<const double *>(property->data);
                if (std::isfinite(seconds) && seconds >= 0.0) {
                    emit playbackPositionChanged(playbackGeneration_, seconds);
                }
            } else if (property != nullptr && property->data != nullptr && event->replyUserdata == BufferingObservationId &&
                       property->format == MpvFormatFlag) {
                const bool buffering = *static_cast<const int *>(property->data) != 0;
                if (buffering_ != buffering) {
                    buffering_ = buffering;
                    emit playbackBufferingChanged(playbackGeneration_, buffering_);
                }
            }
        } else if (event->eventId == MpvEventIdEndFile && !stopping_ && !waitingForStartEvent_) {
            const auto *endFile = static_cast<const MpvEventEndFile *>(event->data);
            if (endFile != nullptr && endFile->error < 0) {
                emit playbackError(playbackGeneration_, QStringLiteral("媒体解码失败：%1").arg(mpvError(endFile->error)));
            } else {
                emit playbackEnded(playbackGeneration_);
            }
            return;
        } else if (event->eventId == MpvEventIdShutdown) {
            if (!stopping_) {
                emit playbackError(playbackGeneration_, QStringLiteral("视频播放内核已停止。"));
            }
            return;
        }
        if (!stopping_ && !waitingForStartEvent_ && fileLoaded_ && videoReconfigured_ && !playbackStartedEmitted_) {
            playbackStartedEmitted_ = true;
            writeMpvDiagnostic(QStringLiteral("媒体已进入可解码状态。"));
            emit playbackStarted(playbackGeneration_);
        }
    }
}

QString MpvPlayerWidget::mpvError(int result) const {
    return errorString_ == nullptr ? QString::number(result) : QString::fromUtf8(errorString_(result));
}

void *MpvPlayerWidget::resolveOpenGlProcAddress(void *context, const char *name) {
    auto *widget = static_cast<MpvPlayerWidget *>(context);
    if (widget == nullptr || widget->context() == nullptr || name == nullptr) {
        return nullptr;
    }
    return reinterpret_cast<void *>(widget->context()->getProcAddress(name));
}

void MpvPlayerWidget::requestRender(void *context) {
    auto *widget = static_cast<MpvPlayerWidget *>(context);
    if (widget == nullptr) {
        return;
    }
    QMetaObject::invokeMethod(widget, [widget]() {
        widget->update();
    }, Qt::QueuedConnection);
}
