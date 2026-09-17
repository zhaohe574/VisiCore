import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:media_kit_video/media_kit_video.dart';
import '../../core/network/auth_service.dart';
import '../../core/p2p/p2p_bridge.dart';
import '../../core/player/video_controller.dart';
import '../../models/channel_model.dart';
import '../common/app_theme.dart';
import '../playback/playback_page.dart';
import 'channel_drawer.dart';
import 'ptz_controller_pad.dart';

/// 现代化单屏实时监控与值守主界面
class LiveViewPage extends StatefulWidget {
  const LiveViewPage({super.key});

  @override
  State<LiveViewPage> createState() => _LiveViewPageState();
}

class _LiveViewPageState extends State<LiveViewPage> with WidgetsBindingObserver {
  late final VisiVideoController _videoController;
  ChannelModel? _currentChannel;
  int _streamType = 1; // 1 = 主码流 (高清), 2 = 子码流 (流畅)
  bool _showPtz = true;
  bool _isFullscreen = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _videoController = VisiVideoController();
    _videoController.addListener(_onVideoStateChanged);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _videoController.removeListener(_onVideoStateChanged);
    _videoController.dispose();
    // 确保退出时恢复竖屏方向
    SystemChrome.setPreferredOrientations([
      DeviceOrientation.portraitUp,
    ]);
    super.dispose();
  }

  void _onVideoStateChanged() {
    if (mounted) setState(() {});
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused || state == AppLifecycleState.inactive) {
      _videoController.pauseForBackground();
    } else if (state == AppLifecycleState.resumed) {
      _videoController.resumeFromForeground();
    }
  }

  Future<void> _switchChannel(ChannelModel channel) async {
    setState(() => _currentChannel = channel);
    await _videoController.startLive(
      channelId: channel.id,
      streamType: _streamType,
    );
  }

  Future<void> _toggleStreamType(int newType) async {
    if (_streamType == newType || _currentChannel == null) return;
    setState(() => _streamType = newType);
    await _videoController.startLive(
      channelId: _currentChannel!.id,
      streamType: newType,
    );
  }

  void _toggleFullscreen() {
    setState(() {
      _isFullscreen = !_isFullscreen;
      if (_isFullscreen) {
        SystemChrome.setPreferredOrientations([
          DeviceOrientation.landscapeLeft,
          DeviceOrientation.landscapeRight,
        ]);
        SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky);
      } else {
        SystemChrome.setPreferredOrientations([
          DeviceOrientation.portraitUp,
        ]);
        SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
      }
    });
  }

  Widget _buildVideoPlayer() {
    final p2p = P2PBridge.instance;

    return AspectRatio(
      aspectRatio: _isFullscreen ? (MediaQuery.of(context).size.aspectRatio) : (16 / 9),
      child: Container(
        color: Colors.black,
        child: Stack(
          children: [
            // 1. 核心视频渲染层
            if (_videoController.videoController != null &&
                _videoController.currentSession != null)
              Center(
                child: Video(
                  controller: _videoController.videoController!,
                  fit: BoxFit.contain,
                ),
              )
            else
              Center(
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(
                      Icons.videocam_off_rounded,
                      color: Colors.white.withAlpha(80),
                      size: 56,
                    ),
                    const SizedBox(height: 10),
                    Text(
                      _currentChannel == null
                          ? '点击左上角打开组织列表，选择通道开始预览'
                          : '正在连接并拉取实时画面...',
                      style: TextStyle(
                        color: Colors.white.withAlpha(160),
                        fontSize: 13,
                      ),
                    ),
                  ],
                ),
              ),

            // 2. 加载中微光指示
            if (_videoController.isLoading)
              Center(
                child: Container(
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: Colors.black54,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: const CircularProgressIndicator(
                    color: AppTheme.accentCyan,
                    strokeWidth: 2.5,
                  ),
                ),
              ),

            // 3. 顶部信息胶囊条 (通道名称 + P2P 直连状态)
            Positioned(
              top: 10,
              left: 12,
              right: 12,
              child: Row(
                children: [
                  if (_currentChannel != null)
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                      decoration: BoxDecoration(
                        color: Colors.black.withAlpha(150),
                        borderRadius: BorderRadius.circular(20),
                        border: Border.all(color: Colors.white12),
                      ),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Container(
                            width: 7,
                            height: 7,
                            decoration: const BoxDecoration(
                              color: AppTheme.onlineGreen,
                              shape: BoxShape.circle,
                            ),
                          ),
                          const SizedBox(width: 6),
                          Text(
                            _currentChannel!.displayName,
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ],
                      ),
                    ),
                  const Spacer(),
                  // 穿透状态胶囊
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                    decoration: BoxDecoration(
                      color: Colors.black.withAlpha(150),
                      borderRadius: BorderRadius.circular(20),
                      border: Border.all(color: Colors.white12),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          p2p.isConnected ? Icons.bolt_rounded : Icons.cloud_off_rounded,
                          size: 14,
                          color: p2p.isConnected ? AppTheme.accentCyan : Colors.redAccent,
                        ),
                        const SizedBox(width: 4),
                        Text(
                          p2p.isConnected ? '极速直通' : '离线',
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w500,
                            color: p2p.isConnected ? AppTheme.accentCyan : Colors.redAccent,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),

            // 4. 底部快捷悬浮栏 (码流标签 + 全屏切换)
            Positioned(
              bottom: 8,
              left: 12,
              right: 12,
              child: Row(
                children: [
                  if (_videoController.currentSession != null)
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: AppTheme.primaryColor.withAlpha(180),
                        borderRadius: BorderRadius.circular(6),
                      ),
                      child: Text(
                        _streamType == 1 ? 'HD 高清' : 'SD 流畅',
                        style: const TextStyle(
                          color: Colors.white,
                          fontSize: 11,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ),
                  const Spacer(),
                  // 重新拉流按钮
                  if (_currentChannel != null)
                    IconButton(
                      icon: const Icon(Icons.refresh_rounded, color: Colors.white, size: 20),
                      tooltip: '重新拉流',
                      onPressed: () => _videoController.startLive(
                        channelId: _currentChannel!.id,
                        streamType: _streamType,
                      ),
                    ),
                  // 全屏切换按钮
                  IconButton(
                    icon: Icon(
                      _isFullscreen ? Icons.fullscreen_exit_rounded : Icons.fullscreen_rounded,
                      color: Colors.white,
                      size: 24,
                    ),
                    tooltip: _isFullscreen ? '退出全屏' : '全屏预览',
                    onPressed: _toggleFullscreen,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (_isFullscreen) {
      return Scaffold(
        backgroundColor: Colors.black,
        body: _buildVideoPlayer(),
      );
    }

    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Scaffold(
      appBar: AppBar(
        leading: Builder(
          builder: (ctx) => IconButton(
            icon: const Icon(Icons.account_tree_rounded),
            tooltip: '组织通道列表',
            onPressed: () => Scaffold.of(ctx).openDrawer(),
          ),
        ),
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              _currentChannel?.displayName ?? '请选择通道',
              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
            ),
            Text(
              _currentChannel != null
                  ? '${_currentChannel!.deviceName} · 通道 ${_currentChannel!.deviceChannel}'
                  : 'VisiCore 视枢移动端',
              style: const TextStyle(fontSize: 11, color: Colors.grey),
            ),
          ],
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.tune_rounded, size: 20),
            tooltip: '断开与重置',
            onPressed: () async {
              final ok = await showDialog<bool>(
                context: context,
                builder: (ctx) => AlertDialog(
                  title: const Text('退出登录'),
                  content: const Text('确定要断开连接并退出当前系统吗？'),
                  actions: [
                    TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
                    TextButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('确定')),
                  ],
                ),
              );
              if (ok == true) {
                await _videoController.stopCurrentSession();
                await AuthService.instance.resetAll();
              }
            },
          ),
        ],
      ),
      drawer: ChannelDrawer(
        selectedChannelId: _currentChannel?.id,
        onSelectChannel: _switchChannel,
      ),
      body: SafeArea(
        child: Column(
          children: [
            // 1. 视窗播放器
            _buildVideoPlayer(),

            // 2. 现代快捷操作工具条
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
              decoration: BoxDecoration(
                color: isDark ? AppTheme.darkSurface : Colors.white,
                border: Border(
                  bottom: BorderSide(
                    color: isDark ? AppTheme.darkBorder : const Color(0xFFE2E8F0),
                    width: 1,
                  ),
                ),
              ),
              child: Row(
                children: [
                  // 清晰度切换
                  SegmentedButton<int>(
                    segments: const [
                      ButtonSegment(value: 1, label: Text('主码流')),
                      ButtonSegment(value: 2, label: Text('子码流')),
                    ],
                    selected: {_streamType},
                    onSelectionChanged: (set) => _toggleStreamType(set.first),
                    style: SegmentedButton.styleFrom(
                      visualDensity: VisualDensity.compact,
                      textStyle: const TextStyle(fontSize: 12),
                    ),
                  ),
                  const Spacer(),
                  // 录像回放快捷入口
                  if (_currentChannel != null)
                    FilledButton.tonalIcon(
                      icon: const Icon(Icons.history_rounded, size: 18),
                      label: const Text('录像回放', style: TextStyle(fontSize: 12)),
                      onPressed: () {
                        Navigator.push(
                          context,
                          MaterialPageRoute(
                            builder: (ctx) => PlaybackPage(channel: _currentChannel!),
                          ),
                        );
                      },
                    ),
                ],
              ),
            ),

            // 3. 云台与设备控制面板区
            Expanded(
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  Row(
                    children: [
                      const Icon(Icons.gamepad_rounded, size: 20, color: AppTheme.primaryColor),
                      const SizedBox(width: 8),
                      const Text(
                        '云台操控',
                        style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold),
                      ),
                      const Spacer(),
                      Switch(
                        value: _showPtz,
                        activeThumbColor: AppTheme.primaryColor,
                        onChanged: (val) => setState(() => _showPtz = val),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  if (_showPtz && _currentChannel != null)
                    PtzControllerPad(
                      channelId: _currentChannel!.id,
                      enabled: _currentChannel!.ptzCapable,
                    )
                  else if (_currentChannel != null && !_currentChannel!.ptzCapable)
                    Container(
                      padding: const EdgeInsets.all(24),
                      alignment: Alignment.center,
                      child: Text(
                        '该通道 (${_currentChannel!.displayName}) 为固定枪机，不支持云台控制',
                        style: const TextStyle(color: Colors.grey, fontSize: 13),
                      ),
                    )
                  else
                    Container(
                      padding: const EdgeInsets.all(32),
                      alignment: Alignment.center,
                      child: Column(
                        children: [
                          Icon(Icons.touch_app_rounded, size: 40, color: Colors.grey.withAlpha(100)),
                          const SizedBox(height: 8),
                          const Text(
                            '请拉出左侧组织列表选择摄像头',
                            style: TextStyle(color: Colors.grey, fontSize: 13),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
