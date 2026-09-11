using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

internal static class PlaybackPublisher
{
    public static IReadOnlyList<string> Arguments(string input, string target, MediaCodecs codecs, bool transcode, string profile, double speed, string format = "flv", bool paced = true)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-stats_period", "0.25", "-progress", "pipe:1", "-fflags", "+genpts+discardcorrupt", "-analyzeduration", "10000000", "-probesize", "10485760" };
        // readrate 必须与倍速一致，否则快进时 FFmpeg 读取管道速率远低于设备下发速率，导致音视频时间戳严重失步直至卡死。
        if (paced) args.AddRange(["-readrate", speed.ToString("R", CultureInfo.InvariantCulture)]);
        args.AddRange(["-itsscale", (1 / speed).ToString("R", CultureInfo.InvariantCulture), "-i", input, "-map", "0:v:0", "-map", "0:a:0?", "-c:v", transcode ? "libx264" : "copy"]);
        if (transcode) args.AddRange(["-preset", "veryfast", "-tune", "zerolatency", "-pix_fmt", "yuv420p"]);
        // 倍速播放时浏览器帧率 = 原始帧率（例如 25fps），-itsscale 只压缩 PTS 而不减少帧数，
        // 需要显式限制输出帧率避免推送 200fps 流导致浏览器解码队列耗尽。
        if (transcode && speed > 1) args.AddRange(["-r", "25"]);
        var audioCopy = speed == 1 && (profile != "browser" || codecs.Audio == "aac");
        args.AddRange(["-c:a", audioCopy ? "copy" : "aac"]);
        if (speed != 1 && codecs.Audio is not null)
            args.AddRange(["-af", speed == .25 ? "atempo=0.5,atempo=0.5,asetpts=N/SR/TB" : $"atempo={speed.ToString("R", CultureInfo.InvariantCulture)},asetpts=N/SR/TB"]);
        args.AddRange(["-f", format]);
        if (format == "flv") args.AddRange(["-flvflags", "no_duration_filesize"]);
        if (format == "rtsp") args.AddRange(["-rtsp_transport", "tcp"]);
        args.Add(target); return args;
    }
    public static async Task ReadProgressAsync(StreamReader reader, Action<TimeSpan> update, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line)
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) && micros >= 0)
                update(TimeSpan.FromTicks(checked(micros * 10)));
    }
    public static void Pause(Process process, bool paused)
    {
        if (process.HasExited) throw new AdapterException(409, "PUBLISHER_STOPPED", "媒体发布进程已停止。");
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("真实发布进程暂停仅支持 Linux。");
        if (Kill(process.Id, paused ? 19 : 18) != 0) throw new AdapterException(502, "PUBLISHER_PAUSE", "无法切换媒体发布进程暂停状态。");
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}
