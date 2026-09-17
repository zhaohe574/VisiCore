import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

/// 手机端原生相机扫码页面
class QrScanPage extends StatefulWidget {
  const QrScanPage({super.key});

  /// 打开扫码页面并返回扫出的原始文本
  static Future<String?> scan(BuildContext context) {
    return Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const QrScanPage()),
    );
  }

  @override
  State<QrScanPage> createState() => _QrScanPageState();
}

class _QrScanPageState extends State<QrScanPage> {
  late final MobileScannerController _controller;
  bool _hasScanned = false;
  bool _isTorchOn = false;

  bool get _isMobilePlatform =>
      !kIsWeb &&
      (defaultTargetPlatform == TargetPlatform.android ||
          defaultTargetPlatform == TargetPlatform.iOS);

  @override
  void initState() {
    super.initState();
    if (_isMobilePlatform) {
      _controller = MobileScannerController(
        detectionSpeed: DetectionSpeed.normal,
        facing: CameraFacing.back,
        torchEnabled: false,
      );
    }
  }

  @override
  void dispose() {
    if (_isMobilePlatform) {
      _controller.dispose();
    }
    super.dispose();
  }

  void _onDetect(BarcodeCapture capture) {
    if (_hasScanned) return;
    final barcode = capture.barcodes.firstOrNull;
    final rawValue = barcode?.rawValue;
    if (rawValue != null && rawValue.trim().isNotEmpty) {
      _hasScanned = true;
      HapticFeedback.mediumImpact();
      Navigator.of(context).pop(rawValue.trim());
    }
  }

  Future<void> _pasteFromClipboard() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    final text = data?.text?.trim();
    if (text != null && text.isNotEmpty && mounted) {
      Navigator.of(context).pop(text);
    } else if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('剪贴板中未找到文本内容')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        foregroundColor: Colors.white,
        elevation: 0,
        title: const Text('扫一扫配对'),
        centerTitle: true,
        actions: [
          if (_isMobilePlatform) ...[
            IconButton(
              icon: Icon(
                _isTorchOn ? Icons.flash_on : Icons.flash_off,
                color: _isTorchOn ? Colors.amberAccent : Colors.white,
              ),
              tooltip: '手电筒',
              onPressed: () async {
                await _controller.toggleTorch();
                setState(() => _isTorchOn = !_isTorchOn);
              },
            ),
            IconButton(
              icon: const Icon(Icons.flip_camera_ios_outlined),
              tooltip: '翻转镜头',
              onPressed: () => _controller.switchCamera(),
            ),
          ],
        ],
      ),
      body: _isMobilePlatform ? _buildCameraScanner() : _buildDesktopFallback(),
    );
  }

  Widget _buildCameraScanner() {
    return Stack(
      children: [
        MobileScanner(
          controller: _controller,
          onDetect: _onDetect,
          errorBuilder: (context, error) {
            return Center(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.videocam_off_outlined, size: 64, color: Colors.white54),
                    const SizedBox(height: 16),
                    Text(
                      '相机启动失败: ${error.errorCode.name}',
                      style: const TextStyle(color: Colors.white),
                    ),
                    const SizedBox(height: 8),
                    const Text(
                      '请确保已授予本应用相机使用权限。',
                      style: TextStyle(color: Colors.white60, fontSize: 13),
                    ),
                    const SizedBox(height: 16),
                    ElevatedButton.icon(
                      icon: const Icon(Icons.paste),
                      label: const Text('从剪贴板粘贴凭据'),
                      onPressed: _pasteFromClipboard,
                    ),
                  ],
                ),
              ),
            );
          },
        ),

        // 居中半透明取景框与扫描边角
        Center(
          child: Container(
            width: 260,
            height: 260,
            decoration: BoxDecoration(
              border: Border.all(color: Colors.blueAccent.withValues(alpha: 0.8), width: 2),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Stack(
              children: [
                // 四角装饰
                _buildCorner(Alignment.topLeft, top: true, left: true),
                _buildCorner(Alignment.topRight, top: true, left: false),
                _buildCorner(Alignment.bottomLeft, top: false, left: true),
                _buildCorner(Alignment.bottomRight, top: false, left: false),
              ],
            ),
          ),
        ),

        // 底部提示文字与快捷粘贴
        Positioned(
          left: 0,
          right: 0,
          bottom: 48,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                decoration: BoxDecoration(
                  color: Colors.black54,
                  borderRadius: BorderRadius.circular(20),
                ),
                child: const Text(
                  '将服务器二维码放入框内，即可自动扫描配对',
                  style: TextStyle(color: Colors.white, fontSize: 13),
                ),
              ),
              const SizedBox(height: 16),
              OutlinedButton.icon(
                onPressed: _pasteFromClipboard,
                icon: const Icon(Icons.paste_rounded, color: Colors.white70),
                label: const Text('从剪贴板粘贴配对内容', style: TextStyle(color: Colors.white)),
                style: OutlinedButton.styleFrom(
                  side: const BorderSide(color: Colors.white38),
                  backgroundColor: Colors.black38,
                  padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildCorner(Alignment alignment, {required bool top, required bool left}) {
    const double size = 20;
    const double stroke = 4;
    return Align(
      alignment: alignment,
      child: SizedBox(
        width: size,
        height: size,
        child: CustomPaint(
          painter: _CornerPainter(top: top, left: left, stroke: stroke),
        ),
      ),
    );
  }

  Widget _buildDesktopFallback() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.desktop_windows_outlined, size: 72, color: Colors.blueAccent),
            const SizedBox(height: 20),
            const Text(
              '桌面端相机扫描说明',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold, color: Colors.white),
            ),
            const SizedBox(height: 12),
            const Text(
              '相机扫一扫主要供 Android / iOS 移动端扫描电脑屏幕二维码。\n在电脑或无摄像头设备上，您可以直接从剪贴板粘贴配对文本。',
              textAlign: TextAlign.center,
              style: TextStyle(color: Colors.white70, fontSize: 14, height: 1.5),
            ),
            const SizedBox(height: 28),
            ElevatedButton.icon(
              onPressed: _pasteFromClipboard,
              icon: const Icon(Icons.paste_rounded),
              label: const Text('从剪贴板导入配对凭据'),
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 14),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CornerPainter extends CustomPainter {
  final bool top;
  final bool left;
  final double stroke;

  _CornerPainter({required this.top, required this.left, required this.stroke});

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = Colors.blueAccent
      ..strokeWidth = stroke
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;

    final path = Path();
    if (top && left) {
      path.moveTo(0, size.height);
      path.lineTo(0, 0);
      path.lineTo(size.width, 0);
    } else if (top && !left) {
      path.moveTo(0, 0);
      path.lineTo(size.width, 0);
      path.lineTo(size.width, size.height);
    } else if (!top && left) {
      path.moveTo(0, 0);
      path.lineTo(0, size.height);
      path.lineTo(size.width, size.height);
    } else {
      path.moveTo(size.width, 0);
      path.lineTo(size.width, size.height);
      path.lineTo(0, size.height);
    }
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
