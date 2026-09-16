using System.Collections.Concurrent;

/// <summary>
/// 通道码流能力探测（平台 B1）：直接对设备 RTSP 地址做一次 ffprobe，判断该档位是否存在并取回编码与分辨率。
///
/// 动机：实测存在单个摄像机没有配置子码流的情况，客户端只能「先请求子码流 → 失败 → 回退主码流」，
/// 每格要多付一次失败重试（约 1.2 秒）并多占一次设备通道。由适配器给出权威结论后，
/// 客户端可以直接选对档位。
///
/// 约束：结果按「设备 + 通道 + 档位」缓存（默认 30 分钟，可用 HIK_STREAM_PROBE_TTL_SECONDS 调整），
/// 探测失败不写缓存以外的任何状态，也**不伪造**分辨率与码率——探测不到就返回 Available=false 与错误原因。
/// 探测是只读的：不经过 ZLMediaKit，不建立共享流，不会影响正在播放的会话。
/// </summary>
internal sealed class StreamCapabilityProbe(IDevice device)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(
        int.TryParse(Environment.GetEnvironmentVariable("HIK_STREAM_PROBE_TTL_SECONDS"), out var seconds) && seconds > 0 ? seconds : 1800);

    private readonly ConcurrentDictionary<(int Channel, int StreamType), (DateTimeOffset At, StreamCapability Value)> _cache = new();

    public async Task<StreamCapability> ProbeAsync(int channel, int streamType, CancellationToken token)
    {
        Validate.Channel(channel);
        if (streamType is not (1 or 2)) throw new ArgumentException("码流类型无效。");
        if (_cache.TryGetValue((channel, streamType), out var cached) && DateTimeOffset.UtcNow - cached.At < Ttl) return cached.Value;

        var result = await RunAsync(channel, streamType, token);
        _cache[(channel, streamType)] = (DateTimeOffset.UtcNow, result);
        return result;
    }

    private async Task<StreamCapability> RunAsync(int channel, int streamType, CancellationToken token)
    {
        if (device.Simulated)
        {
            // 模拟设备没有真实 RTSP 端点：按与共享流一致的约定返回能力，用于离线回归。
            return streamType == 1
                ? new StreamCapability(true, "H265", 2560, 1440, 4096)
                : new StreamCapability(true, "H264", 640, 360, 512);
        }

        var host = device.Options.DeviceIp.Contains(':') ? $"[{device.Options.DeviceIp}]" : device.Options.DeviceIp;
        var origin = $"rtsp://{Uri.EscapeDataString(device.Options.Username)}:{Uri.EscapeDataString(device.Options.Password)}@{host}:554/Streaming/Channels/{channel}{streamType:D2}";
        try
        {
            var codecs = await MediaTools.ProbeAsync(origin, token);
            // ffprobe 探测不到视频流时会回落到「按 NAL 猜测」的默认值；这里以宽度缺失作为「该档位不可用」的判据，
            // 避免把猜测出来的编码当成真实能力上报。
            if (codecs.Width is not > 0 || codecs.Height is not > 0)
                return new StreamCapability(false, Error: "设备未返回该档位的视频参数");
            return new StreamCapability(true, codecs.DisplayVideo, codecs.Width, codecs.Height, codecs.BitrateKbps);
        }
        catch (Exception ex) when (ex is AdapterException or OperationCanceledException or InvalidOperationException)
        {
            return new StreamCapability(false, Error: ex is OperationCanceledException ? "探测超时或已取消" : ex.Message);
        }
    }
}
