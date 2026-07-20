using System.Diagnostics;
using System.Text.RegularExpressions;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public interface IOnvifFfmpegPlaybackRelay
{
    Task EnsureRtspClockRangeSupportedAsync(CancellationToken cancellationToken);

    IOnvifPlaybackRelayProcess Start(
        Guid playbackSessionId,
        OnvifProfileGReplaySource source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt);

    IOnvifPlaybackRelayProcess Start(
        Guid playbackSessionId,
        OnvifProfileGReplaySource source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        double speed) => Start(playbackSessionId, source, startedAt, endedAt);
}

public interface IOnvifPlaybackRelayProcess : IAsyncDisposable
{
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public sealed class FfmpegOnvifPlaybackRelay(OnvifEdgeOptions edgeOptions) : IOnvifFfmpegPlaybackRelay
{
    private static readonly Regex RangeOptionPattern = new(
        @"(?im)^\s*-range\s+(?:<[^>]+>|\S+)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly OnvifPlaybackRelayOptions options = edgeOptions.PlaybackRelay;

    public async Task EnsureRtspClockRangeSupportedAsync(CancellationToken cancellationToken)
    {
        options.ValidateEnabled();
        var startInfo = new ProcessStartInfo
        {
            FileName = options.FfmpegExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-h");
        startInfo.ArgumentList.Add("demuxer=rtsp");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new NotSupportedException("无法启动 FFmpeg 以验证 RTSP 时段参数。 ");
        }
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var help = (await standardOutputTask) + Environment.NewLine + (await standardErrorTask);
            if (process.ExitCode != 0 || !RangeOptionPattern.IsMatch(help))
            {
                throw new NotSupportedException("当前 FFmpeg 不支持 RTSP range 参数，ONVIF 回放中继保持关闭。 ");
            }
        }
        catch (OperationCanceledException)
        {
            StopProcess(process);
            throw;
        }
    }

    public IOnvifPlaybackRelayProcess Start(
        Guid playbackSessionId,
        OnvifProfileGReplaySource source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        var process = new Process { StartInfo = CreateStartInfo(playbackSessionId, source, startedAt, endedAt) };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("无法启动 FFmpeg ONVIF 回放中继进程。 ");
            }
            return new FfmpegOnvifPlaybackRelayProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public IOnvifPlaybackRelayProcess Start(
        Guid playbackSessionId,
        OnvifProfileGReplaySource source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        double speed)
    {
        var process = new Process { StartInfo = CreateStartInfo(playbackSessionId, source, startedAt, endedAt, speed) };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("无法启动 FFmpeg ONVIF 回放中继进程。 ");
            }
            return new FfmpegOnvifPlaybackRelayProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public ProcessStartInfo CreateStartInfo(
        Guid playbackSessionId,
        OnvifProfileGReplaySource source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        double speed = 1.0)
    {
        options.ValidateEnabled();
        ValidatePlaybackRequest(playbackSessionId, source, startedAt, endedAt);
        if (speed is not (0.5 or 1.0 or 2.0 or 4.0))
        {
            throw new InvalidOperationException("ONVIF 回放速度仅支持 0.5、1、2、4 倍。 ");
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = options.FfmpegExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("warning");
        startInfo.ArgumentList.Add("-rtsp_transport");
        startInfo.ArgumentList.Add("tcp");
        if (speed != 1.0)
        {
            startInfo.ArgumentList.Add("-readrate");
            startInfo.ArgumentList.Add(speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        startInfo.ArgumentList.Add("-range");
        startInfo.ArgumentList.Add(BuildClockRange(startedAt, endedAt));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(BuildReplayInputUri(source).AbsoluteUri);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0?");
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a?");
        if (options.VideoMode == OnvifPlaybackRelayVideoMode.Copy)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");
        }
        else
        {
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("libopenh264");
            startInfo.ArgumentList.Add("-b:v");
            startInfo.ArgumentList.Add("2500k");
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add("50");
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("aac");
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add("96k");
        }
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rtsp");
        startInfo.ArgumentList.Add("-rtsp_transport");
        startInfo.ArgumentList.Add("tcp");
        startInfo.ArgumentList.Add(options.BuildPublisherUri(playbackSessionId).AbsoluteUri);
        return startInfo;
    }

    private static void ValidatePlaybackRequest(
        Guid playbackSessionId,
        OnvifProfileGReplaySource source,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        if (playbackSessionId == Guid.Empty || source.ReplayUri is null ||
            (!source.ReplayUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
             !source.ReplayUri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)) ||
            !RecorderEndpointHostPolicy.IsValidHost(source.ReplayUri.Host) ||
            source.ReplayUri.Port is < 1 or > 65535 ||
            !string.IsNullOrEmpty(source.ReplayUri.UserInfo) ||
            string.IsNullOrWhiteSpace(source.Username) || string.IsNullOrWhiteSpace(source.Password) ||
            startedAt >= endedAt || endedAt - startedAt > TimeSpan.FromDays(31))
        {
            throw new InvalidOperationException("ONVIF 回放中继参数无效。 ");
        }
    }

    private static string BuildClockRange(DateTimeOffset startedAt, DateTimeOffset endedAt) =>
        $"clock={startedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{endedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}";

    private static Uri BuildReplayInputUri(OnvifProfileGReplaySource source)
    {
        var builder = new UriBuilder(source.ReplayUri)
        {
            UserName = source.Username,
            Password = source.Password
        };
        return builder.Uri;
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
            // FFmpeg 可能已自然退出；调用方仍会收敛会话和并发资源。
        }
    }
}

public sealed class FfmpegOnvifPlaybackRelayProcess(Process process) : IOnvifPlaybackRelayProcess
{
    private int disposed;

    public bool HasExited => process.HasExited;

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("FFmpeg ONVIF 回放中继异常退出。 ");
            }
        }
        catch (OperationCanceledException)
        {
            StopProcess();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            StopProcess();
            process.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private void StopProcess()
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
            // 进程可能已在退出；资源释放仍应继续。
        }
    }
}
