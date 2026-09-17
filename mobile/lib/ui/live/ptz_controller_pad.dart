import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import '../common/app_theme.dart';

/// 现代化云台与变焦控制盘 (严格对接 VisiCore 服务端指令集)
class PtzControllerPad extends StatefulWidget {
  final int channelId;
  final bool enabled;

  const PtzControllerPad({
    super.key,
    required this.channelId,
    this.enabled = true,
  });

  @override
  State<PtzControllerPad> createState() => _PtzControllerPadState();
}

class _PtzControllerPadState extends State<PtzControllerPad> {
  String? _activeCommand;
  int _speed = 4; // 1 ~ 7，默认 4

  Future<void> _sendPtz(String command) async {
    if (!widget.enabled) return;
    setState(() => _activeCommand = command);
    try {
      await ApiClient.instance.post('/channels/${widget.channelId}/ptz', data: {
        'command': command, // 服务端严格要求字段名为 command
        'speed': _speed,
      });
    } catch (_) {}
  }

  Future<void> _stopPtz() async {
    if (!widget.enabled) return;
    setState(() => _activeCommand = null);
    try {
      await ApiClient.instance.post('/channels/${widget.channelId}/ptz/stop');
    } catch (_) {}
  }

  Widget _buildDirectionBtn({
    required IconData icon,
    required String command,
    double width = 56,
    double height = 56,
  }) {
    final isActive = _activeCommand == command;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return GestureDetector(
      onTapDown: (_) => _sendPtz(command),
      onTapUp: (_) => _stopPtz(),
      onTapCancel: () => _stopPtz(),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 100),
        width: width,
        height: height,
        decoration: BoxDecoration(
          color: !widget.enabled
              ? (isDark ? Colors.white10 : Colors.black12)
              : isActive
                  ? AppTheme.primaryColor
                  : (isDark ? AppTheme.darkSurfaceElevated : Colors.white),
          shape: BoxShape.circle,
          boxShadow: isActive
              ? [
                  BoxShadow(
                    color: AppTheme.primaryColor.withAlpha(160),
                    blurRadius: 14,
                    spreadRadius: 2,
                  )
                ]
              : [
                  BoxShadow(
                    color: Colors.black.withAlpha(isDark ? 50 : 15),
                    blurRadius: 6,
                    offset: const Offset(0, 2),
                  )
                ],
          border: Border.all(
            color: isActive
                ? AppTheme.accentCyan
                : (isDark ? AppTheme.darkBorder : const Color(0xFFCBD5E1)),
            width: isActive ? 1.8 : 1,
          ),
        ),
        child: Center(
          child: Icon(
            icon,
            size: 26,
            color: !widget.enabled
                ? Colors.grey
                : isActive
                    ? Colors.white
                    : (isDark ? Colors.white70 : const Color(0xFF334155)),
          ),
        ),
      ),
    );
  }

  Widget _buildZoomBtn(IconData icon, String label, String command) {
    final isActive = _activeCommand == command;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return GestureDetector(
      onTapDown: (_) => _sendPtz(command),
      onTapUp: (_) => _stopPtz(),
      onTapCancel: () => _stopPtz(),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 100),
        padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 11),
        decoration: BoxDecoration(
          color: !widget.enabled
              ? (isDark ? Colors.white10 : Colors.black12)
              : isActive
                  ? AppTheme.primaryColor
                  : (isDark ? AppTheme.darkSurfaceElevated : Colors.white),
          borderRadius: BorderRadius.circular(24),
          border: Border.all(
            color: isActive ? AppTheme.accentCyan : (isDark ? AppTheme.darkBorder : const Color(0xFFCBD5E1)),
            width: isActive ? 1.5 : 1,
          ),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withAlpha(isDark ? 40 : 10),
              blurRadius: 4,
              offset: const Offset(0, 2),
            ),
          ],
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              icon,
              size: 20,
              color: !widget.enabled
                  ? Colors.grey
                  : isActive
                      ? Colors.white
                      : (isDark ? Colors.white70 : const Color(0xFF334155)),
            ),
            const SizedBox(width: 8),
            Text(
              label,
              style: TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
                color: !widget.enabled
                    ? Colors.grey
                    : isActive
                        ? Colors.white
                        : (isDark ? Colors.white70 : const Color(0xFF334155)),
              ),
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      padding: const EdgeInsets.symmetric(vertical: 18, horizontal: 20),
      decoration: BoxDecoration(
        color: isDark ? AppTheme.darkSurface : Colors.white,
        borderRadius: BorderRadius.circular(18),
        border: Border.all(
          color: isDark ? AppTheme.darkBorder : const Color(0xFFE2E8F0),
        ),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // 1. 转速挡位
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              const Text(
                '云台转速',
                style: TextStyle(fontSize: 13, color: Colors.grey, fontWeight: FontWeight.w500),
              ),
              SegmentedButton<int>(
                segments: const [
                  ButtonSegment(value: 1, label: Text('慢速 1x')),
                  ButtonSegment(value: 4, label: Text('标准 4x')),
                  ButtonSegment(value: 7, label: Text('快速 7x')),
                ],
                selected: {_speed},
                onSelectionChanged: (s) => setState(() => _speed = s.first),
                style: SegmentedButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                  textStyle: const TextStyle(fontSize: 11),
                ),
              ),
            ],
          ),
          const SizedBox(height: 20),

          // 2. 十字罗盘控制器 (上/下/左/右/自动)
          Column(
            children: [
              // 上
              _buildDirectionBtn(
                icon: Icons.keyboard_arrow_up_rounded,
                command: 'up',
              ),
              const SizedBox(height: 10),
              // 左 / 自动扫描 / 右
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  _buildDirectionBtn(
                    icon: Icons.keyboard_arrow_left_rounded,
                    command: 'left',
                  ),
                  const SizedBox(width: 14),
                  // 中心自动按钮
                  _buildDirectionBtn(
                    icon: Icons.sync_rounded,
                    command: 'auto',
                    width: 50,
                    height: 50,
                  ),
                  const SizedBox(width: 14),
                  _buildDirectionBtn(
                    icon: Icons.keyboard_arrow_right_rounded,
                    command: 'right',
                  ),
                ],
              ),
              const SizedBox(height: 10),
              // 下
              _buildDirectionBtn(
                icon: Icons.keyboard_arrow_down_rounded,
                command: 'down',
              ),
            ],
          ),
          const SizedBox(height: 22),

          // 3. 变倍调节 (zoomIn / zoomOut)
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              _buildZoomBtn(Icons.zoom_in_rounded, '拉近变焦', 'zoomIn'),
              const SizedBox(width: 16),
              _buildZoomBtn(Icons.zoom_out_rounded, '拉远变焦', 'zoomOut'),
            ],
          ),
        ],
      ),
    );
  }
}
