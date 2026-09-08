using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class MediaTools
{
    public static string Ffmpeg => Environment.GetEnvironmentVariable("HIK_FFMPEG_PATH") ?? (OperatingSystem.IsWindows() ? "ffmpeg.exe" : "/usr/bin/ffmpeg");
    public static string Ffprobe => Environment.GetEnvironmentVariable("HIK_FFPROBE_PATH") ?? (OperatingSystem.IsWindows() ? "ffprobe.exe" : "/usr/bin/ffprobe");
    public static string RtspBase => Environment.GetEnvironmentVariable("HIK_ZLM_RTSP_URL") ?? "rtsp://127.0.0.1:554";
    public static string InternalRtsp(string app, string stream)
    {
        var key = Environment.GetEnvironmentVariable("HIK_ADAPTER_INTERNAL_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("未配置适配器内部媒体密钥。");
        return $"{RtspBase.TrimEnd('/')}/{app}/{stream}?token={Uri.EscapeDataString(key)}";
    }
    public static Process Start(string executable, IEnumerable<string> arguments, bool input = false)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = input, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        try { return Process.Start(info) ?? throw new InvalidOperationException("无法启动媒体工具。"); }
        catch (System.ComponentModel.Win32Exception) { throw new AdapterException(503, "MEDIA_TOOL_MISSING", "未找到 FFmpeg 或 FFprobe，请检查适配器配置。"); }
    }
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token, byte[]? input = null)
    {
        using var process = Start(executable, arguments, input is not null);
        var output = CaptureAsync(process.StandardOutput, 1024 * 1024, token);
        var error = CaptureAsync(process.StandardError, 16 * 1024, CancellationToken.None);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.BaseStream.WriteAsync(input, token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(token);
            await error;
            if (process.ExitCode != 0)
            {
                ReportFailure($"{Path.GetFileName(executable)} 退出码 {process.ExitCode}", await error);
                throw new AdapterException(502, "MEDIA_PROCESS_FAILED", $"媒体处理失败，进程退出码：{process.ExitCode}。");
            }
            return await output;
        }
        catch (IOException) when (!token.IsCancellationRequested)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            ReportFailure($"{Path.GetFileName(executable)} 管道中断", await error);
            throw;
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
        }
    }
    public static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer, token) > 0) { }
    }
    internal static async Task<string> CaptureAsync(StreamReader reader, int max, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, token)) > 0)
            if (result.Length < max) result.Append(buffer, 0, Math.Min(count, max - result.Length));
        return result.ToString();
    }
    internal static string RedactDiagnostic(string text)
    {
        // 错误输出仅供内部排障，媒体地址可能携带设备密码或播放令牌。
        text = Regex.Replace(text, @"(?i)\b(?:rtsp|rtsps|rtmp|rtmps|http|https)://[^\s\""'<>]+", "[媒体地址已隐藏]");
        text = Regex.Replace(text, @"(?i)\b(password|secret|token|key)\s*[=:]\s*[^\s&,;]+", "$1=[已隐藏]");
        return text;
    }
    internal static void ReportFailure(string context, string detail) =>
        Console.Error.WriteLine($"媒体诊断：{RedactDiagnostic(context)}；{RedactDiagnostic(detail)}");

    internal static async Task<string> NormalizeDownloadAsync(string path, CancellationToken token)
    {
        const int scanLimit = 1024 * 1024;
        var prefix = new byte[scanLimit];
        int read;
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true))
            read = await input.ReadAtLeastAsync(prefix, scanLimit, throwOnEndOfStream: false, token);

        // 只处理海康容器，避免其他格式正文偶然出现 PS 标记时被截断。
        if (!prefix.AsSpan(0, read).StartsWith("IMKH"u8)) return path;

        var offset = FindMpegPack(prefix.AsSpan(0, read));
        if (offset < 0) throw new AdapterException(502, "DOWNLOAD_FORMAT", "海康录像文件缺少可识别的 MPEG-PS 起始包。");

        var normalized = path + ".normalized";
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            await using var output = new FileStream(normalized, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
            input.Position = offset;
            await input.CopyToAsync(output, token);
            await output.FlushAsync(token);
            return normalized;
        }
        catch
        {
            try { File.Delete(normalized); } catch { }
            throw;
        }
    }

    private static int FindMpegPack(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 4 <= bytes.Length; i++)
            if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1 && bytes[i + 3] == 0xba)
                return i;
        return -1;
    }
    public static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
    }
    public static async Task<MediaCodecs> ProbeAsync(string input, CancellationToken token, byte[]? prefix = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var args = new List<string> { "-v", "error", "-analyzeduration", "2000000", "-probesize", "2097152" };
        if (input.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)) args.AddRange(["-rtsp_transport", "tcp"]);
        args.AddRange(["-i", input, "-show_entries", "stream=codec_type,codec_name", "-of", "json"]);
        var json = await RunAsync(Ffprobe, args, timeout.Token, prefix);
        using var document = JsonDocument.Parse(json);
        string? video = null, audio = null;
        foreach (var stream in document.RootElement.GetProperty("streams").EnumerateArray())
        {
            var codec = stream.GetProperty("codec_name").GetString();
            if (stream.GetProperty("codec_type").GetString() == "video") video = codec;
            else if (stream.GetProperty("codec_type").GetString() == "audio") audio = codec;
        }
        if (video is null) throw new AdapterException(502, "VIDEO_CODEC_UNKNOWN", "未能从真实码流识别视频编码。");
        return new(video, audio);
    }
}

internal sealed record MediaCodecs(string Video, string? Audio)
{
    public string DisplayVideo => Video is "hevc" or "h265" ? "H265" : Video == "h264" ? "H264" : Video;
    public bool RequiresBrowserTranscode => Video != "h264";
    public bool Mp4AudioCopy => Audio is null or "aac" or "mp3" or "alac" or "ac3" or "eac3";
}

internal sealed class TranscodeBudget
{
    private readonly SemaphoreSlim _slots = new(int.TryParse(Environment.GetEnvironmentVariable("HIK_TRANSCODE_GLOBAL"), out var count) ? Math.Max(1, count) : 4);
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public IDisposable Acquire()
    {
        if (!_slots.Wait(0)) throw new AdapterException(429, "TRANSCODE_LIMIT", "共享兼容转码数量已达上限。");
        Interlocked.Increment(ref _count);
        return new Release(() => { Interlocked.Decrement(ref _count); _slots.Release(); });
    }
    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
