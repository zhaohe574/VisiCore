/// 摄像头通道模型
class ChannelModel {
  final int id;
  final int deviceId;
  final String deviceName;
  final int? unitId;
  final String name;
  final String? alias;
  final String status;
  final bool ptzCapable;
  final int deviceChannel;
  final String? model;
  final String? codec;

  const ChannelModel({
    required this.id,
    required this.deviceId,
    this.deviceName = '默认录像机',
    this.unitId,
    required this.name,
    this.alias,
    required this.status,
    required this.ptzCapable,
    required this.deviceChannel,
    this.model,
    this.codec,
  });

  bool get isOnline => status.toLowerCase() == 'online';
  String get displayName => (alias != null && alias!.isNotEmpty) ? '$alias ($name)' : name;

  factory ChannelModel.fromJson(Map<String, dynamic> json) {
    return ChannelModel(
      id: (json['id'] as num?)?.toInt() ?? 0,
      deviceId: ((json['deviceId'] ?? json['device_id']) as num?)?.toInt() ?? 0,
      deviceName: (json['deviceName'] ?? json['device_name'] as String?) ?? '未指定设备',
      unitId: ((json['unitId'] ?? json['unit_id']) as num?)?.toInt(),
      name: (json['name'] as String?) ?? '未命名通道',
      alias: json['alias'] as String?,
      status: (json['status'] as String?) ?? 'offline',
      ptzCapable: (json['ptzCapable'] ?? json['ptz_capable'] as bool?) ?? false,
      deviceChannel: ((json['deviceChannel'] ?? json['device_channel']) as num?)?.toInt() ?? 1,
      model: json['model'] as String?,
      codec: json['codec'] as String?,
    );
  }
}
