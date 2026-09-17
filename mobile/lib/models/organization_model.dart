import 'channel_model.dart';

/// 组织架构节点 (车间 / 工段 / 单元)
class OrganizationNode {
  final int id;
  final String name;
  final String code;
  final String status;
  final int? parentId;

  const OrganizationNode({
    required this.id,
    required this.name,
    required this.code,
    required this.status,
    this.parentId,
  });

  factory OrganizationNode.fromJson(Map<String, dynamic> json) {
    return OrganizationNode(
      id: (json['id'] as num?)?.toInt() ?? 0,
      name: (json['name'] as String?) ?? '',
      code: (json['code'] as String?) ?? '',
      status: (json['status'] as String?) ?? 'active',
      parentId: ((json['parentId'] ?? json['parent_id']) as num?)?.toInt(),
    );
  }
}

/// 组织架构树 DTO
class OrganizationTreeDto {
  final List<OrganizationNode> workshops; // 车间 (顶层)
  final List<OrganizationNode> areas;     // 工段 / 区域
  final List<OrganizationNode> units;     // 单元 / 装置

  const OrganizationTreeDto({
    this.workshops = const [],
    this.areas = const [],
    this.units = const [],
  });

  factory OrganizationTreeDto.fromJson(Map<String, dynamic> json) {
    List<OrganizationNode> parseList(dynamic raw) {
      if (raw is List) {
        return raw.map((e) => OrganizationNode.fromJson(e as Map<String, dynamic>)).toList();
      }
      return [];
    }

    return OrganizationTreeDto(
      workshops: parseList(json['workshops']),
      areas: parseList(json['areas']),
      units: parseList(json['units']),
    );
  }
}

/// 用于 UI 渲染的多级组织树视图节点
class OrgTreeNode {
  final String id;
  final String title;
  final String type; // 'workshop', 'area', 'unit', 'device', 'channel'
  final ChannelModel? channel;
  final List<OrgTreeNode> children;
  int totalChannels;
  int onlineChannels;

  OrgTreeNode({
    required this.id,
    required this.title,
    required this.type,
    this.channel,
    List<OrgTreeNode>? children,
    this.totalChannels = 0,
    this.onlineChannels = 0,
  }) : children = children ?? [];

  bool get isChannel => type == 'channel';
  bool get isOnline => channel?.isOnline ?? (onlineChannels > 0);

  /// 递归构建组织架构树 (车间 -> 工段 -> 单元 -> 通道)
  static List<OrgTreeNode> buildOrgTree({
    required OrganizationTreeDto orgData,
    required List<ChannelModel> channels,
  }) {
    // 建立 unitId -> channels 映射
    final unitChannels = <int, List<ChannelModel>>{};
    final unassignedChannels = <ChannelModel>[];

    for (final ch in channels) {
      if (ch.unitId != null && ch.unitId! > 0) {
        unitChannels.putIfAbsent(ch.unitId!, () => []).add(ch);
      } else {
        unassignedChannels.add(ch);
      }
    }

    final tree = <OrgTreeNode>[];

    // 1. 遍历所有车间 (Workshops)
    for (final ws in orgData.workshops) {
      final wsNode = OrgTreeNode(
        id: 'ws_${ws.id}',
        title: ws.name,
        type: 'workshop',
      );

      // 寻找该车间下的所有工段 (Areas)
      final wsAreas = orgData.areas.where((a) => a.parentId == ws.id).toList();
      for (final area in wsAreas) {
        final areaNode = OrgTreeNode(
          id: 'area_${area.id}',
          title: area.name,
          type: 'area',
        );

        // 寻找工段下的所有单元 (Units)
        final areaUnits = orgData.units.where((u) => u.parentId == area.id).toList();
        for (final unit in areaUnits) {
          final chs = unitChannels[unit.id] ?? [];
          final unitNode = OrgTreeNode(
            id: 'unit_${unit.id}',
            title: unit.name,
            type: 'unit',
            totalChannels: chs.length,
            onlineChannels: chs.where((c) => c.isOnline).length,
            children: chs
                .map((c) => OrgTreeNode(
                      id: 'ch_${c.id}',
                      title: c.displayName,
                      type: 'channel',
                      channel: c,
                      totalChannels: 1,
                      onlineChannels: c.isOnline ? 1 : 0,
                    ))
                .toList(),
          );

          areaNode.children.add(unitNode);
          areaNode.totalChannels += unitNode.totalChannels;
          areaNode.onlineChannels += unitNode.onlineChannels;
        }

        if (areaNode.children.isNotEmpty) {
          wsNode.children.add(areaNode);
          wsNode.totalChannels += areaNode.totalChannels;
          wsNode.onlineChannels += areaNode.onlineChannels;
        }
      }

      if (wsNode.children.isNotEmpty) {
        tree.add(wsNode);
      }
    }

    // 2. 如果存在未分配组织的通道，归入“其他通道 (按设备)”
    if (unassignedChannels.isNotEmpty) {
      final deviceGroups = <String, List<ChannelModel>>{};
      for (final ch in unassignedChannels) {
        deviceGroups.putIfAbsent(ch.deviceName, () => []).add(ch);
      }

      final unassignedRoot = OrgTreeNode(
        id: 'unassigned_root',
        title: '未分配组织设备',
        type: 'workshop',
      );

      for (final entry in deviceGroups.entries) {
        final devNode = OrgTreeNode(
          id: 'dev_${entry.key}',
          title: entry.key,
          type: 'device',
          totalChannels: entry.value.length,
          onlineChannels: entry.value.where((c) => c.isOnline).length,
          children: entry.value
              .map((c) => OrgTreeNode(
                    id: 'ch_${c.id}',
                    title: c.displayName,
                    type: 'channel',
                    channel: c,
                    totalChannels: 1,
                    onlineChannels: c.isOnline ? 1 : 0,
                  ))
              .toList(),
        );

        unassignedRoot.children.add(devNode);
        unassignedRoot.totalChannels += devNode.totalChannels;
        unassignedRoot.onlineChannels += devNode.onlineChannels;
      }

      tree.add(unassignedRoot);
    }

    return tree;
  }

  /// 纯按录像设备分组构建树
  static List<OrgTreeNode> buildDeviceTree(List<ChannelModel> channels) {
    final groups = <String, List<ChannelModel>>{};
    for (final ch in channels) {
      groups.putIfAbsent(ch.deviceName, () => []).add(ch);
    }

    return groups.entries.map((entry) {
      final chs = entry.value;
      return OrgTreeNode(
        id: 'dev_${entry.key}',
        title: entry.key,
        type: 'device',
        totalChannels: chs.length,
        onlineChannels: chs.where((c) => c.isOnline).length,
        children: chs
            .map((c) => OrgTreeNode(
                  id: 'ch_${c.id}',
                  title: c.displayName,
                  type: 'channel',
                  channel: c,
                  totalChannels: 1,
                  onlineChannels: c.isOnline ? 1 : 0,
                ))
            .toList(),
      );
    }).toList();
  }
}
