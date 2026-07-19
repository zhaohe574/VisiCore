using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VisiCore.StreamGateway;

public interface ILiveTranscodeProcessFactory
{
    ILiveTranscodeProcess Start(LiveTranscodeRoute route, LiveTranscodePublisherCredential credential);
}

public interface ILiveTranscodeProcess : IAsyncDisposable
{
    bool HasExited { get; }
    int? ExitCode { get; }
    int CapturedStderrCharacters { get; }
    bool TerminationConfirmed { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public sealed class FfmpegLiveTranscodeProcessFactory(LiveTranscodeOptions options) : ILiveTranscodeProcessFactory
{
    public ILiveTranscodeProcess Start(
        LiveTranscodeRoute route,
        LiveTranscodePublisherCredential credential)
    {
        ArgumentNullException.ThrowIfNull(route);
        Process? process = null;
        WindowsKillOnCloseJob? job = null;
        try
        {
            var startInfo = CreateStartInfo(route, credential);
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("FFmpeg 实时转码进程未能启动。");
            }
            job = WindowsKillOnCloseJob.CreateAndAssign(process);
            var relayProcess = new FfmpegLiveTranscodeProcess(process, job);
            process = null;
            job = null;
            return relayProcess;
        }
        catch (Exception exception) when (exception is not LiveTranscodeUnavailableException)
        {
            job?.Dispose();
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // 启动失败后的 Job 关闭与强制终止均已尝试，不记录包含短期凭据的进程参数。
                }
                process.Dispose();
            }
            throw new LiveTranscodeUnavailableException("无法启动 FFmpeg 实时主码流中继。", exception);
        }
    }

    public ProcessStartInfo CreateStartInfo(
        LiveTranscodeRoute route,
        LiveTranscodePublisherCredential credential)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(credential);
        options.Validate();
        if (!LiveTranscodePath.IsInternalSource(route.InputPath) ||
            !LiveTranscodePath.IsPublicMain(route.PublicPath) ||
            !IsExactRtspRoute(route.InputUri, route.InputPath) ||
            !IsExactRtspRoute(route.OutputUri, route.PublicPath) ||
            !string.IsNullOrEmpty(route.InputUri.UserInfo) ||
            !string.IsNullOrEmpty(route.OutputUri.UserInfo) ||
            string.IsNullOrWhiteSpace(credential.Username) ||
            credential.Username.Length > 64 ||
            !credential.Username.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
            string.IsNullOrWhiteSpace(credential.Password) ||
            credential.Password.Length is < 32 or > 256)
        {
            throw new InvalidOperationException("FFmpeg 实时转码只允许使用严格匹配的回环 RTSP 路径和短期发布凭据。");
        }

        var inputBuilder = new UriBuilder(route.InputUri)
        {
            UserName = credential.Username,
            Password = credential.Password
        };
        var publisherBuilder = new UriBuilder(route.OutputUri)
        {
            UserName = credential.Username,
            Password = credential.Password
        };
        var publisherUri = publisherBuilder.Uri;

        var startInfo = new ProcessStartInfo
        {
            FileName = options.FfmpegExecutablePath!,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true
        };
        AddArguments(startInfo.ArgumentList,
            "-nostdin",
            "-hide_banner",
            "-loglevel", "warning",
            "-fflags", "+genpts+discardcorrupt",
            "-err_detect", "ignore_err",
            "-rtsp_transport", "tcp",
            "-i", inputBuilder.Uri.AbsoluteUri,
            "-map", "0:v:0",
            "-an",
            "-c:v", "libopenh264",
            "-pix_fmt", "yuv420p",
            "-b:v", $"{options.VideoBitrateKbps}k",
            "-maxrate", $"{options.VideoMaxRateKbps}k",
            "-bufsize", $"{options.VideoBufferKbps}k",
            "-g", options.GopSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-bf", "0",
            "-f", "rtsp",
            "-rtsp_transport", "tcp",
            "-muxdelay", "0.1",
            publisherUri.AbsoluteUri);
        return startInfo;
    }

    private static bool IsExactRtspRoute(Uri uri, string pathName) =>
        uri.IsAbsoluteUri &&
        uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
        uri.IsLoopback &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.Equals(uri.AbsolutePath.Trim('/'), pathName, StringComparison.Ordinal);

    private static void AddArguments(ICollection<string> target, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            target.Add(argument);
        }
    }
}

internal sealed class FfmpegLiveTranscodeProcess : ILiveTranscodeProcess
{
    private readonly Process process;
    private readonly WindowsKillOnCloseJob job;
    private readonly Task stderrTask;
    private int capturedStderrCharacters;
    private int terminationConfirmed;
    private int? exitCode;
    private int disposed;

    public FfmpegLiveTranscodeProcess(Process process, WindowsKillOnCloseJob job)
    {
        this.process = process;
        this.job = job;
        stderrTask = DrainStderrAsync(process.StandardError);
    }

    public bool HasExited => Volatile.Read(ref disposed) != 0 ? TerminationConfirmed : process.HasExited;
    public int? ExitCode => Volatile.Read(ref disposed) != 0 ? exitCode : process.HasExited ? process.ExitCode : null;
    public int CapturedStderrCharacters => Volatile.Read(ref capturedStderrCharacters);
    public bool TerminationConfirmed => Volatile.Read(ref terminationConfirmed) != 0;

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        process.WaitForExitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        job.Dispose();
        try
        {
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch
        {
            // 进程可能已退出或拒绝终止；后续仍会关闭本地句柄并继续清理其他中继。
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2_000);
                }
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                    Interlocked.Exchange(ref terminationConfirmed, 1);
                }
            }
            catch
            {
                // 未确认退出时保持 fail-closed，管理器不会归还该中继的容量。
            }
            process.Dispose();
            try
            {
                await stderrTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 标准错误排空有独立上限，不能让异常子进程阻断服务停止。
                _ = stderrTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private async Task DrainStderrAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                return;
            }
            Interlocked.Add(ref capturedStderrCharacters, read);
        }
    }
}

internal sealed class WindowsKillOnCloseJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly SafeFileHandle? handle;

    private WindowsKillOnCloseJob(SafeFileHandle? handle)
    {
        this.handle = handle;
    }

    public static WindowsKillOnCloseJob CreateAndAssign(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            // Linux 容器由 Compose 负责主进程生命周期；DisposeAsync 仍会使用 Kill(entireProcessTree)
            // 收口 FFmpeg，避免调用 Windows Job Object。
            return new WindowsKillOnCloseJob(null);
        }

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new InvalidOperationException("无法创建 FFmpeg 进程 Job Object。");
        }

        var job = new WindowsKillOnCloseJob(handle);
        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };
            var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(information, buffer, false);
                if (!SetInformationJobObject(handle, 9, buffer, (uint)length))
                {
                    throw new InvalidOperationException("无法设置 FFmpeg Job Object 的退出清理策略。");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (!AssignProcessToJobObject(handle, process.Handle))
            {
                throw new InvalidOperationException("无法把 FFmpeg 加入受控 Job Object。");
            }
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public void Dispose() => handle?.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}

public sealed class LiveTranscodeUnavailableException : Exception
{
    public LiveTranscodeUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
