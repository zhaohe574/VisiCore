#pragma once

#include <QOpenGLWidget>

class QLibrary;
class QTimer;
class QUrl;

class MpvPlayerWidget final : public QOpenGLWidget {
    Q_OBJECT

public:
    explicit MpvPlayerWidget(QWidget *parent = nullptr);
    ~MpvPlayerWidget() override;

    bool start(const QUrl &url, quint64 playbackGeneration);
    void stop();
    void release();
    [[nodiscard]] bool isReady() const;
    static bool verifyRuntime(QString *errorMessage = nullptr);

signals:
    void playbackStarted(quint64 playbackGeneration);
    void playbackEnded(quint64 playbackGeneration);
    void playbackPositionChanged(quint64 playbackGeneration, double seconds);
    void playbackBufferingChanged(quint64 playbackGeneration, bool buffering);
    void playbackError(quint64 playbackGeneration, const QString &message);

protected:
    void initializeGL() override;
    void paintGL() override;

private:
    struct MpvRenderParam {
        int type;
        void *data;
    };

    struct MpvOpenGlInitParams {
        void *(*getProcAddress)(void *context, const char *name);
        void *getProcAddressContext;
    };

    struct MpvOpenGlFbo {
        int fbo;
        int width;
        int height;
        int internalFormat;
    };

    bool initializeMpv();
    bool loadRuntimeSymbols();
    void startPendingPlayback();
    void releaseMpv();
    void processEvents(quint64 instanceGeneration);
    QString mpvError(int result) const;
    static void *resolveOpenGlProcAddress(void *context, const char *name);
    static void requestRender(void *context);

    static constexpr int RenderParamInvalid = 0;
    static constexpr int RenderParamApiType = 1;
    static constexpr int RenderParamOpenGlInitParams = 2;
    static constexpr int RenderParamOpenGlFbo = 3;
    static constexpr int RenderParamFlipY = 4;

    struct MpvEvent {
        int eventId;
        int error;
        unsigned long long replyUserdata;
        void *data;
    };

    struct MpvEventEndFile {
        int reason;
        int error;
    };

    struct MpvEventProperty {
        const char *name;
        int format;
        void *data;
    };

    using MpvClientApiVersion = unsigned long (*)();
    using MpvCreate = void *(*)();
    using MpvSetOptionString = int (*)(void *, const char *, const char *);
    using MpvInitialize = int (*)(void *);
    using MpvCommand = int (*)(void *, const char *const *);
    using MpvObserveProperty = int (*)(void *, unsigned long long, const char *, int);
    using MpvWaitEvent = const MpvEvent *(*)(void *, double);
    using MpvSetWakeupCallback = void (*)(void *, void (*)(void *), void *);
    using MpvTerminateDestroy = void (*)(void *);
    using MpvErrorString = const char *(*)(int);
    using MpvRenderContextCreate = int (*)(void **, void *, MpvRenderParam *);
    using MpvRenderContextSetUpdateCallback = void (*)(void *, void (*)(void *), void *);
    using MpvRenderContextRender = int (*)(void *, MpvRenderParam *);
    using MpvRenderContextReportSwap = void (*)(void *);
    using MpvRenderContextFree = void (*)(void *);

    QLibrary *library_ = nullptr;
    void *mpv_ = nullptr;
    MpvClientApiVersion clientApiVersion_ = nullptr;
    MpvCreate create_ = nullptr;
    MpvSetOptionString setOptionString_ = nullptr;
    MpvInitialize initialize_ = nullptr;
    MpvCommand command_ = nullptr;
    MpvObserveProperty observeProperty_ = nullptr;
    MpvWaitEvent waitEvent_ = nullptr;
    MpvSetWakeupCallback setWakeupCallback_ = nullptr;
    MpvTerminateDestroy terminateDestroy_ = nullptr;
    MpvErrorString errorString_ = nullptr;
    MpvRenderContextCreate renderContextCreate_ = nullptr;
    MpvRenderContextSetUpdateCallback renderContextSetUpdateCallback_ = nullptr;
    MpvRenderContextRender renderContextRender_ = nullptr;
    MpvRenderContextReportSwap renderContextReportSwap_ = nullptr;
    MpvRenderContextFree renderContextFree_ = nullptr;
    QTimer *eventPollTimer_ = nullptr;
    void *renderContext_ = nullptr;
    quint64 instanceGeneration_ = 0;
    bool stopping_ = false;
    bool waitingForStartEvent_ = false;
    bool fileLoaded_ = false;
    bool videoReconfigured_ = false;
    bool playbackStartedEmitted_ = false;
    bool buffering_ = false;
    bool glInitialized_ = false;
    bool pendingStart_ = false;
    QByteArray pendingUrl_;
    quint64 playbackGeneration_ = 0;
};
