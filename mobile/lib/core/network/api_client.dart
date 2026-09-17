import 'dart:io';
import 'package:dio/dio.dart';
import 'package:dio/io.dart';
import 'package:flutter/foundation.dart';
import '../p2p/p2p_bridge.dart';
import '../p2p/p2p_config.dart';

/// 自动经过本地用户态 P2P 代理的 Dio 客户端
class ApiClient {
  static final ApiClient instance = ApiClient._();
  ApiClient._();

  late final Dio _dio = _initDio();
  String? _accessToken;
  P2PConfig? _config;

  Dio get dio => _dio;
  P2PConfig? get config => _config;

  Dio _initDio() {
    final dio = Dio(BaseOptions(
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(seconds: 15),
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
      },
    ));

    // 配置用户态代理与自签名证书支持
    final adapter = IOHttpClientAdapter(
      createHttpClient: () {
        final client = HttpClient();
        
        // 当 P2P 代理真实处于监听状态时，才将请求转入本地代理；直连模式走 DIRECT
        client.findProxy = (uri) {
          final bridge = P2PBridge.instance;
          if (bridge.isConnected && bridge.isProxyActive && bridge.localProxyPort > 0) {
            return 'PROXY 127.0.0.1:${bridge.localProxyPort}';
          }
          return 'DIRECT';
        };

        // 内网 HTTPS 自签名证书放行
        client.badCertificateCallback = (cert, host, port) => true;
        return client;
      },
    );

    dio.httpClientAdapter = adapter;

    // 拦截器：自动注入 Bearer Token
    dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) {
        if (_accessToken != null && _accessToken!.isNotEmpty) {
          options.headers['Authorization'] = 'Bearer $_accessToken';
        }
        return handler.next(options);
      },
      onError: (DioException error, handler) {
        debugPrint('[ApiClient] 请求错误: [${error.requestOptions.method}] ${error.requestOptions.path} -> ${error.message}');
        return handler.next(error);
      },
    ));

    return dio;
  }

  void updateConfig(P2PConfig config) {
    _config = config;
    _dio.options.baseUrl = config.baseUrl;
  }

  void setAccessToken(String? token) {
    _accessToken = token;
  }

  Future<Response<T>> get<T>(String path, {Map<String, dynamic>? queryParameters}) {
    return _dio.get<T>(path, queryParameters: queryParameters);
  }

  Future<Response<T>> post<T>(String path, {dynamic data, Map<String, dynamic>? queryParameters}) {
    return _dio.post<T>(path, data: data, queryParameters: queryParameters);
  }

  Future<Response<T>> delete<T>(String path, {dynamic data, Map<String, dynamic>? queryParameters}) {
    return _dio.delete<T>(path, data: data, queryParameters: queryParameters);
  }
}
