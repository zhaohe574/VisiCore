import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../../core/network/auth_service.dart';
import '../../core/p2p/p2p_config.dart';
import '../common/app_theme.dart';
import 'qr_scan_page.dart';

/// 扫码与服务器配对登录页面
class PairingPage extends StatefulWidget {
  const PairingPage({super.key});

  @override
  State<PairingPage> createState() => _PairingPageState();
}

class _PairingPageState extends State<PairingPage> {
  final _jsonController = TextEditingController();
  final _serverIpController = TextEditingController(text: '10.37.200.74');
  final _apiPortController = TextEditingController(text: '443');
  final _usernameController = TextEditingController(text: 'admin');
  final _passwordController = TextEditingController();
  
  bool _isConnecting = false;
  bool _obscurePassword = true;
  bool _showRawJson = false;
  String? _error;
  P2PConfig? _parsedConfig;

  // 正式机默认生产直连凭据（局域网 IP 与 P2P 虚拟网双通道）
  static const Map<String, dynamic> _productionPreset = {
    "version": 1,
    "protocol": "easytier-userspace",
    "serverName": "VisiCore 生产服务",
    "networkName": "visicore-9df08ef7",
    "networkSecret": "82e9d9f4b5b3d22a89debd465ea336cc",
    "serverIp": "10.37.200.74",
    "lanIp": "10.37.200.74",
    "virtualIp": "10.144.1.1",
    "apiPort": 443,
    "apiPrefix": "/api/v2",
    "publicPeers": [
      "tcp://public.easytier.top:11010",
      "udp://public.easytier.top:11010"
    ]
  };

  @override
  void initState() {
    super.initState();
    final auth = AuthService.instance;
    if (auth.isPaired && auth.currentConfig != null) {
      _applyConfig(auth.currentConfig!);
    } else {
      _loadProductionPreset();
    }
  }

  @override
  void dispose() {
    _jsonController.dispose();
    _serverIpController.dispose();
    _apiPortController.dispose();
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  void _applyConfig(P2PConfig config) {
    setState(() {
      _parsedConfig = config;
      _serverIpController.text = config.serverIp;
      _apiPortController.text = config.apiPort.toString();
      _jsonController.text = jsonEncode(config.toJson());
      _error = null;
    });
  }

  void _loadProductionPreset() {
    final config = P2PConfig.tryParse(jsonEncode(_productionPreset));
    if (config != null) {
      _applyConfig(config);
    }
  }

  void _selectMode(String ip) {
    setState(() {
      _serverIpController.text = ip;
      if (_parsedConfig != null) {
        _parsedConfig = _parsedConfig!.copyWith(serverIp: ip);
        _jsonController.text = jsonEncode(_parsedConfig!.toJson());
      }
      _error = null;
    });
  }

  /// 启动相机原生扫一扫
  Future<void> _handleScanQrCode() async {
    final scanned = await QrScanPage.scan(context);
    if (scanned != null && scanned.trim().isNotEmpty) {
      _parseAndApplyScannedText(scanned.trim());
    }
  }

  /// 从剪贴板粘贴
  Future<void> _handlePasteClipboard() async {
    final data = await Clipboard.getData(Clipboard.kTextPlain);
    final text = data?.text?.trim();
    if (text != null && text.isNotEmpty) {
      _parseAndApplyScannedText(text);
    } else {
      _showToast('剪贴板中未找到文本内容');
    }
  }

  void _parseAndApplyScannedText(String text) {
    final config = P2PConfig.tryParse(text);
    if (config != null) {
      _applyConfig(config);
      _showToast('已成功识别并绑定服务：${config.serverName}');
    } else {
      setState(() {
        _jsonController.text = text;
        _error = '识别到的文本非有效配对凭据，请核对二维码或 JSON 格式';
      });
    }
  }

  void _showToast(String msg) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(msg),
        duration: const Duration(seconds: 2),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }

  Future<void> _handlePairAndLogin() async {
    final serverIp = _serverIpController.text.trim();
    if (serverIp.isEmpty) {
      setState(() => _error = '请输入服务器 IP 地址或域名');
      return;
    }

    final port = int.tryParse(_apiPortController.text.trim()) ?? 443;

    if (_passwordController.text.trim().isEmpty) {
      setState(() => _error = '请输入平台登录密码');
      return;
    }

    setState(() {
      _isConnecting = true;
      _error = null;
    });

    // 组合最终配置
    var config = _parsedConfig ?? P2PConfig.fromJson(_productionPreset);
    config = config.copyWith(serverIp: serverIp, apiPort: port);

    final auth = AuthService.instance;
    // 1. 保存配对配置并更新 API 客户端基地址
    await auth.savePairing(config);

    // 2. 发起登录请求
    final loggedIn = await auth.login(
      _usernameController.text.trim(),
      _passwordController.text.trim(),
    );

    if (!loggedIn) {
      setState(() {
        _isConnecting = false;
        _error = auth.lastLoginError ?? '登录失败，请检查服务器 IP 或账号密码';
      });
      return;
    }

    if (mounted) {
      setState(() => _isConnecting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final currentIp = _serverIpController.text.trim();
    final isLanSelected = currentIp == '10.37.200.74';
    final isP2pSelected = currentIp == '10.144.1.1';

    return Scaffold(
      backgroundColor: AppTheme.scaffoldBg,
      appBar: AppBar(
        title: const Text('视枢 VisiCore 登录'),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.qr_code_scanner_rounded),
            tooltip: '扫一扫',
            onPressed: _handleScanQrCode,
          ),
        ],
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
          children: [
            // 顶部 Logo 与系统标识
            Center(
              child: Container(
                width: 60,
                height: 60,
                decoration: BoxDecoration(
                  color: AppTheme.accentColor.withValues(alpha: 0.15),
                  shape: BoxShape.circle,
                  border: Border.all(color: AppTheme.accentColor.withValues(alpha: 0.4), width: 2),
                ),
                child: const Icon(
                  Icons.videocam_rounded,
                  size: 32,
                  color: AppTheme.accentColor,
                ),
              ),
            ),
            const SizedBox(height: 10),
            const Text(
              '视枢智能监控平台',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.bold,
                color: Colors.white,
                letterSpacing: 0.5,
              ),
            ),
            const SizedBox(height: 4),
            const Text(
              '局域网直连 · 用户态免公网 IP 穿透',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 12, color: Colors.grey),
            ),
            const SizedBox(height: 16),

            // 错误提示条
            if (_error != null)
              Container(
                margin: const EdgeInsets.only(bottom: 16),
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                decoration: BoxDecoration(
                  color: Colors.red.withValues(alpha: 0.15),
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: Colors.redAccent.withValues(alpha: 0.5)),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Icon(Icons.error_outline_rounded, color: Colors.redAccent, size: 20),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Text(
                            _error!,
                            style: const TextStyle(color: Colors.redAccent, fontSize: 13, height: 1.4),
                          ),
                        ),
                      ],
                    ),
                    if (currentIp == '10.144.1.1') ...[
                      const SizedBox(height: 10),
                      Align(
                        alignment: Alignment.centerRight,
                        child: TextButton.icon(
                          style: TextButton.styleFrom(
                            foregroundColor: Colors.amberAccent,
                            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                          ),
                          icon: const Icon(Icons.swap_horiz_rounded, size: 16),
                          label: const Text('一键切换为局域网 IP (10.37.200.74)', style: TextStyle(fontSize: 12)),
                          onPressed: () => _selectMode('10.37.200.74'),
                        ),
                      ),
                    ],
                  ],
                ),
              ),

            // 模块 1：扫一扫与直连卡片
            _buildScanSection(),
            const SizedBox(height: 16),

            // 模块 2：服务器地址与网络模式（双通道切换）
            _buildServerAddressSection(isLanSelected, isP2pSelected),
            const SizedBox(height: 16),

            // 模块 3：平台账号与密码登录表单
            _buildLoginForm(),
            const SizedBox(height: 24),

            // 登录提交按钮
            ElevatedButton(
              onPressed: _isConnecting ? null : _handlePairAndLogin,
              style: ElevatedButton.styleFrom(
                backgroundColor: AppTheme.accentColor,
                foregroundColor: Colors.white,
                padding: const EdgeInsets.symmetric(vertical: 15),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                elevation: 3,
              ),
              child: _isConnecting
                  ? const Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        SizedBox(
                          width: 20,
                          height: 20,
                          child: CircularProgressIndicator(strokeWidth: 2.2, color: Colors.white),
                        ),
                        SizedBox(width: 12),
                        Text('正在连接并验证身份...', style: TextStyle(fontSize: 15)),
                      ],
                    )
                  : const Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.login_rounded, size: 20),
                        SizedBox(width: 8),
                        Text('立即登录系统', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
                      ],
                    ),
            ),

            const SizedBox(height: 16),

            // 高级选项展开：手动编辑 JSON
            _buildAdvancedSection(),
          ],
        ),
      ),
    );
  }

  /// 扫一扫绑定卡片
  Widget _buildScanSection() {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.cardBg,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.panelBorder),
      ),
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.qr_code_scanner_rounded, size: 18, color: AppTheme.accentColor),
              const SizedBox(width: 8),
              const Text(
                '扫码一键配置',
                style: TextStyle(fontSize: 14, fontWeight: FontWeight.bold, color: Colors.white),
              ),
              const Spacer(),
              if (_parsedConfig != null)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                  decoration: BoxDecoration(
                    color: Colors.green.withValues(alpha: 0.15),
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: Colors.green.withValues(alpha: 0.4)),
                  ),
                  child: const Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.check_circle_rounded, size: 12, color: Colors.green),
                      SizedBox(width: 4),
                      Text('配置已就绪', style: TextStyle(color: Colors.green, fontSize: 11)),
                    ],
                  ),
                ),
            ],
          ),
          const SizedBox(height: 12),

          // 扫一扫主按钮
          InkWell(
            onTap: _handleScanQrCode,
            borderRadius: BorderRadius.circular(10),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  colors: [
                    AppTheme.accentColor.withValues(alpha: 0.18),
                    AppTheme.accentColor.withValues(alpha: 0.06),
                  ],
                ),
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: AppTheme.accentColor.withValues(alpha: 0.5)),
              ),
              child: const Row(
                children: [
                  Icon(Icons.camera_alt_rounded, size: 26, color: AppTheme.accentColor),
                  SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          '点击扫一扫 (扫描电脑端二维码)',
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                            color: Colors.white,
                          ),
                        ),
                        SizedBox(height: 2),
                        Text(
                          '自动识别正式机参数与安全密钥',
                          style: TextStyle(fontSize: 11, color: Colors.grey),
                        ),
                      ],
                    ),
                  ),
                  Icon(Icons.arrow_forward_ios_rounded, size: 14, color: Colors.grey),
                ],
              ),
            ),
          ),

          const SizedBox(height: 10),

          // 辅助导入操作行
          Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: _handlePasteClipboard,
                  icon: const Icon(Icons.paste_rounded, size: 15),
                  label: const Text('粘贴凭据', style: TextStyle(fontSize: 12)),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: Colors.white70,
                    side: const BorderSide(color: AppTheme.panelBorder),
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: _loadProductionPreset,
                  icon: const Icon(Icons.refresh_rounded, size: 15),
                  label: const Text('重置为正式机', style: TextStyle(fontSize: 12)),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: Colors.white70,
                    side: const BorderSide(color: AppTheme.panelBorder),
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  /// 服务器网络连接与双通道配置
  Widget _buildServerAddressSection(bool isLanSelected, bool isP2pSelected) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.cardBg,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.panelBorder),
      ),
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(Icons.router_rounded, size: 18, color: AppTheme.accentColor),
              SizedBox(width: 8),
              Text(
                '服务器网络直连地址',
                style: TextStyle(fontSize: 14, fontWeight: FontWeight.bold, color: Colors.white),
              ),
            ],
          ),
          const SizedBox(height: 10),

          // 双通道快捷切换 Chip
          Row(
            children: [
              Expanded(
                child: ChoiceChip(
                  label: const Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.wifi_rounded, size: 15),
                      SizedBox(width: 6),
                      Text('局域网直连 (推荐)'),
                    ],
                  ),
                  selected: isLanSelected,
                  selectedColor: AppTheme.accentColor.withValues(alpha: 0.25),
                  side: BorderSide(
                    color: isLanSelected ? AppTheme.accentColor : AppTheme.panelBorder,
                  ),
                  onSelected: (val) {
                    if (val) _selectMode('10.37.200.74');
                  },
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: ChoiceChip(
                  label: const Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.public_rounded, size: 15),
                      SizedBox(width: 6),
                      Text('P2P 虚拟网'),
                    ],
                  ),
                  selected: isP2pSelected,
                  selectedColor: AppTheme.accentColor.withValues(alpha: 0.25),
                  side: BorderSide(
                    color: isP2pSelected ? AppTheme.accentColor : AppTheme.panelBorder,
                  ),
                  onSelected: (val) {
                    if (val) _selectMode('10.144.1.1');
                  },
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),

          // IP 与端口输入行
          Row(
            children: [
              Expanded(
                flex: 3,
                child: TextField(
                  controller: _serverIpController,
                  style: const TextStyle(color: Colors.white, fontSize: 14),
                  decoration: InputDecoration(
                    labelText: '服务 IP 或域名',
                    labelStyle: const TextStyle(color: Colors.grey, fontSize: 13),
                    prefixIcon: const Icon(Icons.dns_rounded, color: Colors.grey, size: 18),
                    filled: true,
                    fillColor: AppTheme.surfaceBg,
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: AppTheme.panelBorder),
                    ),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: AppTheme.panelBorder),
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: AppTheme.accentColor),
                    ),
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                flex: 1,
                child: TextField(
                  controller: _apiPortController,
                  keyboardType: TextInputType.number,
                  style: const TextStyle(color: Colors.white, fontSize: 14),
                  decoration: InputDecoration(
                    labelText: '端口',
                    labelStyle: const TextStyle(color: Colors.grey, fontSize: 13),
                    filled: true,
                    fillColor: AppTheme.surfaceBg,
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: AppTheme.panelBorder),
                    ),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: AppTheme.panelBorder),
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(10),
                      borderSide: const BorderSide(color: AppTheme.accentColor),
                    ),
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            isLanSelected
                ? '提示：手机连接公司 Wi-Fi 时，请使用此模式直接访问 10.37.200.74。'
                : '提示：使用 10.144.1.1 需处于 EasyTier 虚拟网环境。',
            style: TextStyle(
              fontSize: 11,
              color: isLanSelected ? Colors.greenAccent : Colors.grey,
            ),
          ),
        ],
      ),
    );
  }

  /// 账号与密码登录表单
  Widget _buildLoginForm() {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.cardBg,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.panelBorder),
      ),
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(Icons.person_pin_rounded, size: 18, color: AppTheme.accentColor),
              SizedBox(width: 8),
              Text(
                '平台账号凭据',
                style: TextStyle(fontSize: 14, fontWeight: FontWeight.bold, color: Colors.white),
              ),
            ],
          ),
          const SizedBox(height: 14),

          // 用户名
          TextField(
            controller: _usernameController,
            style: const TextStyle(color: Colors.white, fontSize: 14),
            decoration: InputDecoration(
              labelText: '平台账号',
              labelStyle: const TextStyle(color: Colors.grey, fontSize: 13),
              prefixIcon: const Icon(Icons.account_circle_outlined, color: Colors.grey, size: 20),
              filled: true,
              fillColor: AppTheme.surfaceBg,
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(10),
                borderSide: const BorderSide(color: AppTheme.panelBorder),
              ),
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(10),
                borderSide: const BorderSide(color: AppTheme.panelBorder),
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(10),
                borderSide: const BorderSide(color: AppTheme.accentColor),
              ),
              contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            ),
          ),
          const SizedBox(height: 12),

          // 密码
          TextField(
            controller: _passwordController,
            obscureText: _obscurePassword,
            style: const TextStyle(color: Colors.white, fontSize: 14),
            decoration: InputDecoration(
              labelText: '登录密码',
              labelStyle: const TextStyle(color: Colors.grey, fontSize: 13),
              prefixIcon: const Icon(Icons.lock_outline_rounded, color: Colors.grey, size: 20),
              suffixIcon: IconButton(
                icon: Icon(
                  _obscurePassword ? Icons.visibility_off_outlined : Icons.visibility_outlined,
                  color: Colors.grey,
                  size: 20,
                ),
                onPressed: () => setState(() => _obscurePassword = !_obscurePassword),
              ),
              filled: true,
              fillColor: AppTheme.surfaceBg,
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(10),
                borderSide: const BorderSide(color: AppTheme.panelBorder),
              ),
              enabledBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(10),
                borderSide: const BorderSide(color: AppTheme.panelBorder),
              ),
              focusedBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(10),
                borderSide: const BorderSide(color: AppTheme.accentColor),
              ),
              contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            ),
          ),
        ],
      ),
    );
  }

  /// 高级选项：查看与手动编辑 JSON
  Widget _buildAdvancedSection() {
    return Theme(
      data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
      child: ExpansionTile(
        initiallyExpanded: _showRawJson,
        title: const Text(
          '高级配置 (查看/手动编辑配对 JSON)',
          style: TextStyle(fontSize: 12, color: Colors.grey),
        ),
        trailing: Icon(
          _showRawJson ? Icons.expand_less : Icons.expand_more,
          color: Colors.grey,
          size: 18,
        ),
        onExpansionChanged: (val) => setState(() => _showRawJson = val),
        children: [
          TextField(
            controller: _jsonController,
            maxLines: 5,
            style: const TextStyle(fontFamily: 'monospace', fontSize: 12, color: Colors.white70),
            decoration: InputDecoration(
              hintText: '{"networkName":"...","networkSecret":"..."}',
              hintStyle: const TextStyle(color: Colors.white24),
              filled: true,
              fillColor: Colors.black38,
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(8),
                borderSide: const BorderSide(color: AppTheme.panelBorder),
              ),
            ),
            onChanged: (val) {
              final config = P2PConfig.tryParse(val.trim());
              if (config != null) {
                setState(() {
                  _parsedConfig = config;
                  _serverIpController.text = config.serverIp;
                  _apiPortController.text = config.apiPort.toString();
                });
              }
            },
          ),
        ],
      ),
    );
  }
}
