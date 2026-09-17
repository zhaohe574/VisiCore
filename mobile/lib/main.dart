import 'package:flutter/material.dart';
import 'package:media_kit/media_kit.dart';
import 'core/network/auth_service.dart';
import 'ui/common/app_theme.dart';
import 'ui/live/live_view_page.dart';
import 'ui/pairing/pairing_page.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  
  // 初始化媒体解码核心 (自动加载 Windows libmpv-2.dll / Android so)
  try {
    MediaKit.ensureInitialized();
  } catch (e) {
    debugPrint('[Main] MediaKit 原生库初始化警告 (界面仍可正常渲染): $e');
  }

  // 初始化本地配对与鉴权缓存
  await AuthService.instance.init();

  runApp(const VisiCoreMobileApp());
}

class VisiCoreMobileApp extends StatelessWidget {
  const VisiCoreMobileApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'VisiCore 视枢移动端',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.lightTheme,
      darkTheme: AppTheme.darkTheme,
      themeMode: ThemeMode.dark, // 监控控制台默认采用深色专业模式
      home: const RootGate(),
    );
  }
}

/// 根路由门禁：根据本地配对与登录态自动分流
class RootGate extends StatelessWidget {
  const RootGate({super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: AuthService.instance,
      builder: (context, _) {
        final auth = AuthService.instance;

        // 1. 等待初始化加载完成
        if (!auth.initialized) {
          return const Scaffold(
            body: Center(child: CircularProgressIndicator()),
          );
        }

        // 2. 如果已完成扫码配对且具备有效 Token，直接进入单屏监控主页
        if (auth.isPaired && auth.isAuthenticated) {
          return const LiveViewPage();
        }

        // 3. 首次启动或未登录，进入配对与登录页
        return const PairingPage();
      },
    );
  }
}
