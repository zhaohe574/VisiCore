import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../p2p/p2p_bridge.dart';
import '../p2p/p2p_config.dart';
import 'api_client.dart';

/// 认证与配对状态管理服务
class AuthService extends ChangeNotifier {
  static final AuthService instance = AuthService._();
  AuthService._();

  static const _keyP2pConfig = 'visicore_p2p_config';
  static const _keyAccessToken = 'visicore_access_token';
  static const _keyUsername = 'visicore_username';

  P2PConfig? _config;
  String? _token;
  String? _username;
  bool _initialized = false;

  P2PConfig? get config => _config;
  P2PConfig? get currentConfig => _config;
  String? get token => _token;
  String? get username => _username;
  bool get isPaired => _config != null;
  bool get isAuthenticated => _token != null && _token!.isNotEmpty;
  bool get initialized => _initialized;

  Future<void> init() async {
    if (_initialized) return;
    final prefs = await SharedPreferences.getInstance();

    final rawConfig = prefs.getString(_keyP2pConfig);
    if (rawConfig != null) {
      _config = P2PConfig.tryParse(rawConfig);
      if (_config != null) {
        ApiClient.instance.updateConfig(_config!);
        // 自动拉起 P2P 隧道
        await P2PBridge.instance.start(_config!);
      }
    }

    _token = prefs.getString(_keyAccessToken);
    _username = prefs.getString(_keyUsername);
    if (_token != null) {
      ApiClient.instance.setAccessToken(_token);
    }

    _initialized = true;
    notifyListeners();
  }

  /// 保存扫码配对凭据并启动 P2P 隧道
  Future<bool> savePairing(P2PConfig newConfig) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_keyP2pConfig, newConfig.encode());
    _config = newConfig;
    ApiClient.instance.updateConfig(newConfig);

    // 启动 P2P 打洞
    final ok = await P2PBridge.instance.start(newConfig);
    notifyListeners();
    return ok;
  }

  String? _lastLoginError;
  String? get lastLoginError => _lastLoginError;

  /// 登录 VisiCore API
  Future<bool> login(String username, String password) async {
    _lastLoginError = null;
    try {
      final res = await ApiClient.instance.post('/auth/login', data: {
        'username': username,
        'password': password,
        'clientType': 'desktop', // 后端要求原生独立客户端传 desktop 以直接获取 Bearer 令牌并免除 CSRF Cookie
        'clientVersion': '2.0.0',
      });

      if (res.statusCode == 200 && res.data != null) {
        final data = res.data as Map<String, dynamic>;
        _token = data['accessToken'] as String?;
        _username = username;

        final prefs = await SharedPreferences.getInstance();
        if (_token != null) {
          await prefs.setString(_keyAccessToken, _token!);
        }
        await prefs.setString(_keyUsername, username);

        ApiClient.instance.setAccessToken(_token);
        notifyListeners();
        return true;
      }
    } on DioException catch (e) {
      if (e.type == DioExceptionType.connectionTimeout ||
          e.type == DioExceptionType.sendTimeout ||
          e.type == DioExceptionType.receiveTimeout) {
        final target = ApiClient.instance.config?.baseUrl ?? '';
        _lastLoginError = '连接超时：无法连通目标服务 ($target)\n• 若手机已连接公司 Wi-Fi，请点击【局域网直连 (10.37.200.74)】。\n• 若处于外部 4G/5G 网络，请确保 P2P 隧道已建立。';
      } else if (e.response?.data is Map<String, dynamic>) {
        final map = e.response!.data as Map<String, dynamic>;
        _lastLoginError = map['message']?.toString() ?? e.message;
      } else {
        _lastLoginError = e.message ?? '网络连接失败，请核对服务端 IP 是否可达';
      }
      debugPrint('[AuthService] 登录失败: $_lastLoginError');
    } catch (e) {
      _lastLoginError = e.toString();
      debugPrint('[AuthService] 登录异常: $e');
    }
    return false;
  }

  /// 退出登录
  Future<void> logout() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_keyAccessToken);
    _token = null;
    ApiClient.instance.setAccessToken(null);
    notifyListeners();
  }

  /// 清除所有配对和登录信息
  Future<void> resetAll() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_keyP2pConfig);
    await prefs.remove(_keyAccessToken);
    await prefs.remove(_keyUsername);
    _config = null;
    _token = null;
    _username = null;
    ApiClient.instance.setAccessToken(null);
    await P2PBridge.instance.stop();
    notifyListeners();
  }
}
