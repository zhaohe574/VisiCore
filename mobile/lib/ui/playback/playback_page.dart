import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:media_kit_video/media_kit_video.dart';
import '../../core/network/api_client.dart';
import '../../core/p2p/p2p_bridge.dart';
import '../../models/channel_model.dart';
import '../../models/media_session_model.dart';
import '../../models/recording_model.dart';
import 'package:media_kit/media_kit.dart';

/// 历史录像检索与时间轴回放页面
class PlaybackPage extends StatefulWidget {
  final ChannelModel channel;

  const PlaybackPage({super.key, required this.channel});

  @override
  State<PlaybackPage> createState() => _PlaybackPageState();
}

class _PlaybackPageState extends State<PlaybackPage> {
  DateTime _selectedDate = DateTime.now();
  List<RecordingModel> _recordings = [];
  bool _searching = false;
  String? _error;

  late final Player _player;
  late final VideoController _videoController;
  MediaSessionModel? _playbackSession;
  bool _isPlaying = false;
  double _speed = 1.0;

  @override
  void initState() {
    super.initState();
    _player = Player(
      configuration: const PlayerConfiguration(
        logLevel: MPVLogLevel.warn,
      ),
    );
    _videoController = VideoController(_player);
    _player.stream.playing.listen((p) {
      if (mounted) setState(() => _isPlaying = p);
    });

    _searchRecordings();
  }

  @override
  void dispose() {
    _stopPlayback();
    _player.dispose();
    super.dispose();
  }

  Future<void> _searchRecordings() async {
    setState(() {
      _searching = true;
      _error = null;
    });

    final start = DateTime(_selectedDate.year, _selectedDate.month, _selectedDate.day, 0, 0, 0);
    final end = DateTime(_selectedDate.year, _selectedDate.month, _selectedDate.day, 23, 59, 59);

    try {
      final res = await ApiClient.instance.post('/recordings/search', data: {
        'channelId': widget.channel.id,
        'start': start.toUtc().toIso8601String(),
        'end': end.toUtc().toIso8601String(),
      });

      if (res.statusCode == 200 && res.data != null) {
        List<dynamic> list = [];
        if (res.data is List) {
          list = res.data as List<dynamic>;
        } else if (res.data is Map && (res.data as Map)['items'] is List) {
          list = (res.data as Map)['items'] as List<dynamic>;
        }
        setState(() {
          _recordings = list.map((e) => RecordingModel.fromJson(e as Map<String, dynamic>)).toList();
          _searching = false;
        });
        return;
      }
    } catch (e) {
      setState(() {
        _error = '录像检索失败: $e';
        _searching = false;
      });
    }
  }

  Future<void> _startPlayback(RecordingModel rec) async {
    await _stopPlayback();

    try {
      final res = await ApiClient.instance.post('/playback-sessions', data: {
        'channelId': widget.channel.id,
        'start': rec.startTime.toUtc().toIso8601String(),
        'end': rec.endTime.toUtc().toIso8601String(),
        'streamType': 1,
        'profile': 'native',
      });

      if (res.statusCode == 200 && res.data != null) {
        final session = MediaSessionModel.fromJson(res.data as Map<String, dynamic>);
        _playbackSession = session;

        final streamUrl = session.httpTsUrl ?? session.httpFlvUrl;
        if (streamUrl == null) throw Exception('未返回回放流');

        if (_player.platform is NativePlayer) {
          final native = _player.platform as NativePlayer;
          if (P2PBridge.instance.isProxyActive) {
            final proxyUrl = P2PBridge.instance.localProxyUrl;
            await native.setProperty('http-proxy', proxyUrl);
            await native.setProperty('network-proxy', proxyUrl);
          } else {
            await native.setProperty('http-proxy', '');
            await native.setProperty('network-proxy', '');
          }
          await native.setProperty('tls-verify', 'no');
          await native.setProperty('demuxer-lavf-o', 'tls_verify=0,fflags=+nobuffer');
          await native.setProperty('hwdec', 'auto-safe');
        }

        await _player.open(Media(streamUrl));
        setState(() {});
      }
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('启动回放失败: $e')),
      );
    }
  }

  Future<void> _stopPlayback() async {
    if (_playbackSession != null) {
      final id = _playbackSession!.id;
      _playbackSession = null;
      try {
        await _player.stop();
        await ApiClient.instance.delete('/playback-sessions/$id');
      } catch (_) {}
    }
  }

  Future<void> _changeSpeed(double newSpeed) async {
    if (_playbackSession == null) return;
    try {
      await ApiClient.instance.post(
        '/playback-sessions/${_playbackSession!.id}/control',
        data: {'action': 'speed', 'speed': newSpeed},
      );
      setState(() => _speed = newSpeed);
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    final dateFormat = DateFormat('yyyy-MM-dd');
    final timeFormat = DateFormat('HH:mm:ss');

    return Scaffold(
      appBar: AppBar(
        title: Text('${widget.channel.name} · 录像回放'),
      ),
      body: Column(
        children: [
          // 1. 回放视窗
          AspectRatio(
            aspectRatio: 16 / 9,
            child: Container(
              color: Colors.black,
              child: _playbackSession != null
                  ? Video(controller: _videoController)
                  : const Center(
                      child: Text(
                        '选择下方录像片段开始回放',
                        style: TextStyle(color: Colors.grey),
                      ),
                    ),
            ),
          ),

          // 2. 回放控制条
          if (_playbackSession != null)
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              color: Colors.grey.shade100,
              child: Row(
                children: [
                  IconButton(
                    icon: Icon(_isPlaying ? Icons.pause : Icons.play_arrow),
                    onPressed: () {
                      if (_isPlaying) {
                        _player.pause();
                      } else {
                        _player.play();
                      }
                    },
                  ),
                  const Spacer(),
                  // 倍速切换
                  DropdownButton<double>(
                    value: _speed,
                    items: const [
                      DropdownMenuItem(value: 0.5, child: Text('0.5x')),
                      DropdownMenuItem(value: 1.0, child: Text('1.0x')),
                      DropdownMenuItem(value: 2.0, child: Text('2.0x')),
                      DropdownMenuItem(value: 4.0, child: Text('4.0x')),
                    ],
                    onChanged: (s) {
                      if (s != null) _changeSpeed(s);
                    },
                  ),
                ],
              ),
            ),

          // 3. 日期筛选行
          Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                const Icon(Icons.calendar_today, size: 18),
                const SizedBox(width: 8),
                Text(
                  dateFormat.format(_selectedDate),
                  style: const TextStyle(fontWeight: FontWeight.bold),
                ),
                const Spacer(),
                TextButton(
                  onPressed: () async {
                    final picked = await showDatePicker(
                      context: context,
                      initialDate: _selectedDate,
                      firstDate: DateTime.now().subtract(const Duration(days: 7)),
                      lastDate: DateTime.now(),
                    );
                    if (picked != null) {
                      setState(() => _selectedDate = picked);
                      _searchRecordings();
                    }
                  },
                  child: const Text('选择日期'),
                ),
              ],
            ),
          ),
          const Divider(height: 1),

          // 4. 录像片段列表
          Expanded(
            child: _searching
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(child: Text(_error!, style: const TextStyle(color: Colors.red)))
                    : _recordings.isEmpty
                        ? const Center(child: Text('选定日期内未检索到录像片段'))
                        : ListView.builder(
                            itemCount: _recordings.length,
                            itemBuilder: (context, index) {
                              final rec = _recordings[index];
                              return ListTile(
                                leading: const Icon(Icons.movie_outlined, color: Colors.blueAccent),
                                title: Text(
                                  '${timeFormat.format(rec.startTime.toLocal())} ~ ${timeFormat.format(rec.endTime.toLocal())}',
                                  style: const TextStyle(fontSize: 14),
                                ),
                                subtitle: Text(
                                  '时长: ${rec.duration.inMinutes} 分钟',
                                  style: const TextStyle(fontSize: 12, color: Colors.grey),
                                ),
                                trailing: const Icon(Icons.play_circle_outline, color: Colors.blue),
                                onTap: () => _startPlayback(rec),
                              );
                            },
                          ),
          ),
        ],
      ),
    );
  }
}
