/// 录像搜索结果与片段模型
class RecordingModel {
  final DateTime startTime;
  final DateTime endTime;
  final int fileSize;
  final String recordType;

  const RecordingModel({
    required this.startTime,
    required this.endTime,
    required this.fileSize,
    required this.recordType,
  });

  Duration get duration => endTime.difference(startTime);

  factory RecordingModel.fromJson(Map<String, dynamic> json) {
    return RecordingModel(
      startTime: DateTime.tryParse(json['start']?.toString() ?? '') ?? DateTime.now(),
      endTime: DateTime.tryParse(json['end']?.toString() ?? '') ?? DateTime.now(),
      fileSize: (json['fileSize'] as num?)?.toInt() ?? 0,
      recordType: (json['recordType'] as String?) ?? 'schedule',
    );
  }
}
