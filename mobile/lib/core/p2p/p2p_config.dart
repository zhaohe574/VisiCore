import 'dart:convert';

/// VisiCore 移动端 P2P 配对凭据模型
class P2PConfig {
  final int version;
  final String protocol;
  final String serverName;
  final String networkName;
  final String networkSecret;
  final String serverIp;
  final String? lanIp;
  final String? virtualIp;
  final int apiPort;
  final String apiPrefix;
  final List<String> publicPeers;

  const P2PConfig({
    this.version = 1,
    this.protocol = 'easytier-userspace',
    required this.serverName,
    required this.networkName,
    required this.networkSecret,
    this.serverIp = '10.37.200.74',
    this.lanIp = '10.37.200.74',
    this.virtualIp = '10.144.1.1',
    this.apiPort = 443,
    this.apiPrefix = '/api/v2',
    this.publicPeers = const ['tcp://public.easytier.top:11010'],
  });

  /// 服务端完整基础 URL（通过用户态代理访问）
  String get baseUrl => 'https://$serverIp:$apiPort$apiPrefix';

  P2PConfig copyWith({
    String? serverName,
    String? networkName,
    String? networkSecret,
    String? serverIp,
    String? lanIp,
    String? virtualIp,
    int? apiPort,
    String? apiPrefix,
    List<String>? publicPeers,
  }) {
    return P2PConfig(
      version: version,
      protocol: protocol,
      serverName: serverName ?? this.serverName,
      networkName: networkName ?? this.networkName,
      networkSecret: networkSecret ?? this.networkSecret,
      serverIp: serverIp ?? this.serverIp,
      lanIp: lanIp ?? this.lanIp,
      virtualIp: virtualIp ?? this.virtualIp,
      apiPort: apiPort ?? this.apiPort,
      apiPrefix: apiPrefix ?? this.apiPrefix,
      publicPeers: publicPeers ?? this.publicPeers,
    );
  }

  factory P2PConfig.fromJson(Map<String, dynamic> json) {
    return P2PConfig(
      version: json['version'] as int? ?? 1,
      protocol: json['protocol'] as String? ?? 'easytier-userspace',
      serverName: json['serverName'] as String? ?? 'VisiCore 服务',
      networkName: json['networkName'] as String? ?? '',
      networkSecret: json['networkSecret'] as String? ?? '',
      serverIp: json['serverIp'] as String? ?? json['lanIp'] as String? ?? '10.37.200.74',
      lanIp: json['lanIp'] as String? ?? '10.37.200.74',
      virtualIp: json['virtualIp'] as String? ?? json['ipv4'] as String? ?? '10.144.1.1',
      apiPort: json['apiPort'] as int? ?? 443,
      apiPrefix: json['apiPrefix'] as String? ?? '/api/v2',
      publicPeers: (json['publicPeers'] as List<dynamic>?)
              ?.map((e) => e.toString())
              .toList() ??
          const ['tcp://public.easytier.top:11010'],
    );
  }

  Map<String, dynamic> toJson() => {
        'version': version,
        'protocol': protocol,
        'serverName': serverName,
        'networkName': networkName,
        'networkSecret': networkSecret,
        'serverIp': serverIp,
        'lanIp': lanIp,
        'virtualIp': virtualIp,
        'apiPort': apiPort,
        'apiPrefix': apiPrefix,
        'publicPeers': publicPeers,
      };

  String encode() => jsonEncode(toJson());

  static P2PConfig? tryParse(String raw) {
    try {
      final decoded = jsonDecode(raw.trim());
      if (decoded is Map<String, dynamic>) {
        return P2PConfig.fromJson(decoded);
      }
    } catch (_) {}
    return null;
  }
}
