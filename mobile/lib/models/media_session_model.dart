/// VisiCore 媒体会话模型 (实时或回放)
class MediaSessionModel {
  final String id;
  final int channelId;
  final int streamType; // 1 = 主码流 (高清), 2 = 子码流 (流畅)
  final String? httpFlvUrl;
  final String? httpTsUrl;
  final String? rtspUrl;
  final String codec;
  final int? width;
  final int? height;
  final int? bitrateKbps;
  final DateTime? expiresAt;

  const MediaSessionModel({
    required this.id,
    required this.channelId,
    required this.streamType,
    this.httpFlvUrl,
    this.httpTsUrl,
    this.rtspUrl,
    this.codec = 'h264',
    this.width,
    this.height,
    this.bitrateKbps,
    this.expiresAt,
  });

  bool get isMainStream => streamType == 1;
  String get streamTypeLabel => streamType == 1 ? '高清 (主码流)' : '流畅 (子码流)';

  factory MediaSessionModel.fromJson(Map<String, dynamic> json) {
    DateTime? exp;
    if (json['expiresAt'] != null) {
      exp = DateTime.tryParse(json['expiresAt'].toString());
    }

    return MediaSessionModel(
      id: json['id']?.toString() ?? '',
      channelId: (json['channelId'] as num?)?.toInt() ?? 0,
      streamType: (json['streamType'] as num?)?.toInt() ?? 1,
      httpFlvUrl: json['httpFlvUrl'] as String?,
      httpTsUrl: json['httpTsUrl'] as String?,
      rtspUrl: json['rtspUrl'] as String?,
      codec: json['codec'] as String? ?? 'h264',
      width: (json['width'] as num?)?.toInt(),
      height: (json['height'] as num?)?.toInt(),
      bitrateKbps: (json['bitrateKbps'] as num?)?.toInt(),
      expiresAt: exp,
    );
  }
}
