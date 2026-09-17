import 'package:flutter/material.dart';
import '../../core/network/api_client.dart';
import '../../core/network/auth_service.dart';
import '../../models/channel_model.dart';
import '../../models/organization_model.dart';
import '../common/app_theme.dart';

/// 现代化组织架构通道树切换抽屉
class ChannelDrawer extends StatefulWidget {
  final int? selectedChannelId;
  final ValueChanged<ChannelModel> onSelectChannel;

  const ChannelDrawer({
    super.key,
    required this.selectedChannelId,
    required this.onSelectChannel,
  });

  @override
  State<ChannelDrawer> createState() => _ChannelDrawerState();
}

class _ChannelDrawerState extends State<ChannelDrawer> {
  List<ChannelModel> _allChannels = [];
  OrganizationTreeDto? _orgDto;
  List<OrgTreeNode> _treeNodes = [];
  bool _loading = true;
  String? _error;

  // 视图模式：0 = 按组织架构，1 = 按录像机
  int _viewMode = 0;
  String _searchQuery = '';
  final _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _fetchData();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _fetchData() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      // 1. 获取通道列表
      final chRes = await ApiClient.instance.get('/channels', queryParameters: {'pageSize': 500});
      if (chRes.statusCode == 200 && chRes.data != null) {
        List<dynamic> rawList = [];
        if (chRes.data is List) {
          rawList = chRes.data as List<dynamic>;
        } else if (chRes.data is Map && (chRes.data as Map)['items'] is List) {
          rawList = (chRes.data as Map)['items'] as List<dynamic>;
        }
        _allChannels = rawList.map((e) => ChannelModel.fromJson(e as Map<String, dynamic>)).toList();
      }

      // 2. 尽力获取组织架构数据（非管理员账号可能无法读取组织，做静默兼容）
      try {
        final orgRes = await ApiClient.instance.get('/organization');
        if (orgRes.statusCode == 200 && orgRes.data != null && orgRes.data is Map) {
          _orgDto = OrganizationTreeDto.fromJson(orgRes.data as Map<String, dynamic>);
        }
      } catch (_) {}

      _rebuildTree();
    } catch (e) {
      setState(() {
        _error = '加载组织通道失败: $e';
        _loading = false;
      });
    }
  }

  void _rebuildTree() {
    List<ChannelModel> filtered = _allChannels;
    if (_searchQuery.isNotEmpty) {
      final q = _searchQuery.toLowerCase();
      filtered = _allChannels.where((c) {
        return c.name.toLowerCase().contains(q) ||
            (c.alias?.toLowerCase().contains(q) ?? false) ||
            c.deviceName.toLowerCase().contains(q);
      }).toList();
    }

    if (_viewMode == 0 && _orgDto != null && _orgDto!.workshops.isNotEmpty) {
      _treeNodes = OrgTreeNode.buildOrgTree(orgData: _orgDto!, channels: filtered);
    } else {
      _treeNodes = OrgTreeNode.buildDeviceTree(filtered);
    }

    setState(() => _loading = false);
  }

  Widget _buildStatusBadge(int online, int total) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: online > 0 ? AppTheme.onlineGreen.withAlpha(30) : Colors.grey.withAlpha(30),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: online > 0 ? AppTheme.onlineGreen.withAlpha(120) : Colors.grey.withAlpha(80),
          width: 0.8,
        ),
      ),
      child: Text(
        '$online/$total 在线',
        style: TextStyle(
          fontSize: 11,
          fontWeight: FontWeight.w600,
          color: online > 0 ? AppTheme.onlineGreen : Colors.grey,
        ),
      ),
    );
  }

  Widget _buildTreeNode(OrgTreeNode node, {int depth = 0}) {
    if (node.isChannel && node.channel != null) {
      final ch = node.channel!;
      final isSelected = ch.id == widget.selectedChannelId;

      return Container(
        margin: const EdgeInsets.symmetric(horizontal: 10, vertical: 2),
        decoration: BoxDecoration(
          color: isSelected ? AppTheme.primaryColor.withAlpha(40) : Colors.transparent,
          borderRadius: BorderRadius.circular(10),
          border: isSelected
              ? Border.all(color: AppTheme.accentCyan, width: 1.2)
              : Border.all(color: Colors.transparent),
        ),
        child: ListTile(
          dense: true,
          contentPadding: EdgeInsets.only(left: 16.0 + depth * 12, right: 12),
          leading: Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(
              color: ch.isOnline
                  ? AppTheme.onlineGreen.withAlpha(30)
                  : Colors.grey.withAlpha(30),
              shape: BoxShape.circle,
            ),
            child: Icon(
              Icons.videocam_rounded,
              size: 18,
              color: ch.isOnline ? AppTheme.onlineGreen : AppTheme.offlineGrey,
            ),
          ),
          title: Text(
            ch.displayName,
            style: TextStyle(
              fontSize: 13.5,
              fontWeight: isSelected ? FontWeight.bold : FontWeight.w500,
              color: isSelected ? AppTheme.accentCyan : null,
            ),
          ),
          subtitle: Text(
            '${ch.deviceName} · 通道 ${ch.deviceChannel}',
            style: const TextStyle(fontSize: 11, color: Colors.grey),
          ),
          trailing: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (ch.ptzCapable)
                Container(
                  margin: const EdgeInsets.only(right: 6),
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                  decoration: BoxDecoration(
                    color: Colors.blue.withAlpha(30),
                    borderRadius: BorderRadius.circular(4),
                    border: Border.all(color: Colors.blue.withAlpha(80), width: 0.5),
                  ),
                  child: const Text(
                    'PTZ',
                    style: TextStyle(fontSize: 10, fontWeight: FontWeight.bold, color: Colors.blue),
                  ),
                ),
              Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(
                  color: ch.isOnline ? AppTheme.onlineGreen : AppTheme.offlineGrey,
                  shape: BoxShape.circle,
                ),
              ),
            ],
          ),
          onTap: () {
            widget.onSelectChannel(ch);
            Navigator.pop(context);
          },
        ),
      );
    }

    // 目录/组织分支节点
    IconData folderIcon;
    Color iconColor = AppTheme.primaryColor;

    switch (node.type) {
      case 'workshop':
        folderIcon = Icons.factory_outlined;
        iconColor = AppTheme.primaryColor;
        break;
      case 'area':
        folderIcon = Icons.domain_rounded;
        iconColor = Colors.teal;
        break;
      case 'unit':
        folderIcon = Icons.developer_board_rounded;
        iconColor = Colors.indigoAccent;
        break;
      case 'device':
      default:
        folderIcon = Icons.storage_rounded;
        iconColor = Colors.blueGrey;
        break;
    }

    return Theme(
      data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
      child: ExpansionTile(
        initiallyExpanded: depth < 1,
        tilePadding: EdgeInsets.only(left: 12.0 + depth * 10, right: 12),
        leading: Icon(folderIcon, size: 20, color: iconColor),
        title: Text(
          node.title,
          style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            _buildStatusBadge(node.onlineChannels, node.totalChannels),
            const SizedBox(width: 4),
            const Icon(Icons.expand_more, size: 18, color: Colors.grey),
          ],
        ),
        children: node.children.map((child) => _buildTreeNode(child, depth: depth + 1)).toList(),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Drawer(
      backgroundColor: isDark ? AppTheme.darkBg : Colors.white,
      child: SafeArea(
        child: Column(
          children: [
            // 1. 抽屉顶部头部栏
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
              child: Row(
                children: [
                  const Icon(Icons.account_tree_rounded, color: AppTheme.primaryColor, size: 24),
                  const SizedBox(width: 10),
                  const Text(
                    '监控组织架构',
                    style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
                  ),
                  const Spacer(),
                  IconButton(
                    icon: const Icon(Icons.refresh_rounded, size: 22),
                    tooltip: '刷新通道',
                    onPressed: _fetchData,
                  ),
                ],
              ),
            ),

            // 2. 模式切换标签栏 (组织架构 vs 录像设备)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
              child: SegmentedButton<int>(
                segments: const [
                  ButtonSegment(
                    value: 0,
                    icon: Icon(Icons.corporate_fare_rounded, size: 16),
                    label: Text('组织架构'),
                  ),
                  ButtonSegment(
                    value: 1,
                    icon: Icon(Icons.dvr_rounded, size: 16),
                    label: Text('录像设备'),
                  ),
                ],
                selected: {_viewMode},
                onSelectionChanged: (set) {
                  setState(() {
                    _viewMode = set.first;
                    _rebuildTree();
                  });
                },
                style: SegmentedButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                  textStyle: const TextStyle(fontSize: 12),
                ),
              ),
            ),

            // 3. 搜索栏
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              child: TextField(
                controller: _searchController,
                decoration: InputDecoration(
                  hintText: '搜索通道名称、别名或设备...',
                  hintStyle: const TextStyle(fontSize: 13, color: Colors.grey),
                  prefixIcon: const Icon(Icons.search_rounded, size: 20),
                  suffixIcon: _searchQuery.isNotEmpty
                      ? IconButton(
                          icon: const Icon(Icons.clear, size: 18),
                          onPressed: () {
                            _searchController.clear();
                            setState(() {
                              _searchQuery = '';
                              _rebuildTree();
                            });
                          },
                        )
                      : null,
                  isDense: true,
                  filled: true,
                  fillColor: isDark ? AppTheme.darkSurface : Colors.grey.shade100,
                  contentPadding: const EdgeInsets.symmetric(vertical: 10, horizontal: 12),
                  border: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(10),
                    borderSide: BorderSide.none,
                  ),
                ),
                onChanged: (val) {
                  setState(() {
                    _searchQuery = val.trim();
                    _rebuildTree();
                  });
                },
              ),
            ),
            const Divider(height: 1),

            // 4. 多级树状列表
            Expanded(
              child: _loading
                  ? const Center(child: CircularProgressIndicator())
                  : _error != null
                      ? Center(
                          child: Padding(
                            padding: const EdgeInsets.all(20),
                            child: Column(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: [
                                const Icon(Icons.error_outline_rounded, color: Colors.redAccent, size: 40),
                                const SizedBox(height: 12),
                                Text(_error!, textAlign: TextAlign.center, style: const TextStyle(color: Colors.redAccent, fontSize: 13)),
                                const SizedBox(height: 12),
                                OutlinedButton(onPressed: _fetchData, child: const Text('重试')),
                              ],
                            ),
                          ),
                        )
                      : _treeNodes.isEmpty
                          ? Center(
                              child: Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  Icon(Icons.search_off_rounded, size: 48, color: Colors.grey.withAlpha(120)),
                                  const SizedBox(height: 8),
                                  Text(
                                    _searchQuery.isNotEmpty ? '未匹配到任何通道' : '暂无组织通道数据',
                                    style: const TextStyle(color: Colors.grey),
                                  ),
                                ],
                              ),
                            )
                          : ListView.builder(
                              itemCount: _treeNodes.length,
                              itemBuilder: (context, index) => _buildTreeNode(_treeNodes[index]),
                            ),
            ),

            // 5. 底部服务连接状态与切换服务入口
            const Divider(height: 1),
            Container(
              color: isDark ? AppTheme.darkSurface : Colors.grey.shade50,
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              child: Row(
                children: [
                  const Icon(Icons.cloud_done_rounded, size: 16, color: Colors.green),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      AuthService.instance.currentConfig?.serverName ?? 'VisiCore 直连服务',
                      style: const TextStyle(fontSize: 12, color: Colors.grey),
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  TextButton.icon(
                    style: TextButton.styleFrom(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                      visualDensity: VisualDensity.compact,
                    ),
                    icon: const Icon(Icons.swap_horiz_rounded, size: 16),
                    label: const Text('重选/扫码', style: TextStyle(fontSize: 12)),
                    onPressed: () async {
                      Navigator.of(context).pop();
                      await AuthService.instance.logout();
                    },
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
