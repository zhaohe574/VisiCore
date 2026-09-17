import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:media_kit/media_kit.dart';
import 'package:media_kit_video/media_kit_video.dart';
import '../network/api_client.dart';
import '../p2p/p2p_bridge.dart';
import '../../models/media_session_model.dart';

/// 移动端视频播放与会话续期控制器 (基于 media_kit 与用户态代理)
class VisiVideoController extends ChangeNotifier {
  Player? _player;
  VideoController? _videoController;
  MediaSessionModel? _currentSession;
  Timer? _renewTimer;
  bool _isPlaying = false;
  bool _isLoading = false;
  String? _error;

  Player? get player => _player;
  VideoController? get videoController => _videoController;
  MediaSessionModel? get currentSession => _currentSession;
  bool get isPlaying => _isPlaying;
  bool get isLoading => _isLoading;
  String? get error => _error;

  VisiVideoController() {
    _initPlayer();
  }

  void _initPlayer() {
    _player = Player(
      configuration: const PlayerConfiguration(
        logLevel: MPVLogLevel.warn,
        bufferSize: 1024 * 1024 * 2, // 2MB 低缓冲加速首帧
      ),
    );

    _videoController = VideoController(_player!);

    // 监听播放状态
    _player!.stream.playing.listen((playing) {
      _isPlaying = playing;
      notifyListeners();
    });

    _player!.stream.error.listen((err) {
      if (err.isNotEmpty) {
        debugPrint('[VisiVideoController] 播放器异常: $err');
        _error = err;
        notifyListeners();
      }
    });
  }

  /// 启动实时直播会话并开播
  Future<bool> startLive({
    required int channelId,
    required int streamType, // 1 = 主码流, 2 = 子码流
  }) async {
    _isLoading = true;
    _error = null;
    notifyListeners();

    try {
      // 1. 如果已有会话在播放，先安全释放
      await stopCurrentSession();

      // 2. 调用 VisiCore 接口申请媒体会话 (原生播放器使用 native profile)
      final res = await ApiClient.instance.post('/live-sessions', data: {
        'channelId': channelId,
        'streamType': streamType,
        'profile': 'native',
      });

      if (res.statusCode == 200 && res.data != null) {
        final session = MediaSessionModel.fromJson(res.data as Map<String, dynamic>);
        _currentSession = session;

        // 原生播放器优先使用 MPEG-TS 流 (httpTsUrl)，完美支持 H.264 与 H.265 硬解
        final streamUrl = session.httpTsUrl ?? session.httpFlvUrl;
        if (streamUrl == null || streamUrl.isEmpty) {
          throw Exception('服务端未下发有效的流媒体播放地址');
        }

        debugPrint('[VisiVideoController] 获得播放流地址: $streamUrl');

        // 3. 配置播放器极低延迟参数与用户态代理 / 直连参数
        if (_player?.platform is NativePlayer) {
          final native = _player!.platform as NativePlayer;
          if (P2PBridge.instance.isProxyActive) {
            final proxyUrl = P2PBridge.instance.localProxyUrl;
            debugPrint('[VisiVideoController] 视频流注入代理: $proxyUrl');
            await native.setProperty('http-proxy', proxyUrl);
            await native.setProperty('network-proxy', proxyUrl);
          } else {
            await native.setProperty('http-proxy', '');
            await native.setProperty('network-proxy', '');
          }
          // 放行内网自签名证书
          await native.setProperty('tls-verify', 'no');
          await native.setProperty('demuxer-lavf-o', 'tls_verify=0,fflags=+nobuffer');
          await native.setProperty('hwdec', 'auto-safe');
        }

        // 4. 打开媒体流
        await _player?.open(Media(streamUrl));

        // 5. 启动 90 秒租约自动续期定时器 (VisiCore 会话租约通常为 3 分钟)
        _startRenewTimer(session.id, 'live');

        _isLoading = false;
        notifyListeners();
        return true;
      }
    } catch (e) {
      debugPrint('[VisiVideoController] 播放失败: $e');
      _error = e.toString();
    }

    _isLoading = false;
    notifyListeners();
    return false;
  }

  /// 会话自动续期
  void _startRenewTimer(String sessionId, String kind) {
    _renewTimer?.cancel();
    _renewTimer = Timer.periodic(const Duration(seconds: 90), (_) async {
      try {
        await ApiClient.instance.post('/$kind-sessions/$sessionId/renew');
        debugPrint('[VisiVideoController] 媒体会话自动续期成功: $sessionId');
      } catch (e) {
        debugPrint('[VisiVideoController] 会话续期失败: $e');
      }
    });
  }

  /// 停止当前媒体会话
  Future<void> stopCurrentSession() async {
    _renewTimer?.cancel();
    _renewTimer = null;

    if (_currentSession != null) {
      final sid = _currentSession!.id;
      _currentSession = null;
      try {
        await _player?.stop();
        await ApiClient.instance.delete('/live-sessions/$sid');
        debugPrint('[VisiVideoController] 已停止会话: $sid');
      } catch (_) {}
    }
    _isPlaying = false;
    notifyListeners();
  }

  /// 手机切入后台时的休眠暂停（防止偷跑 4G/5G 流量）
  Future<void> pauseForBackground() async {
    debugPrint('[VisiVideoController] 切后台，挂起流媒体以节约流量');
    await _player?.pause();
    _renewTimer?.cancel();
  }

  /// 手机恢复前台时的快速恢复
  Future<void> resumeFromForeground() async {
    if (_currentSession != null) {
      debugPrint('[VisiVideoController] 恢复前台，重新唤醒流媒体');
      await _player?.play();
      _startRenewTimer(_currentSession!.id, 'live');
    }
  }

  @override
  void dispose() {
    _renewTimer?.cancel();
    _player?.dispose();
    super.dispose();
  }
}
