using System.Diagnostics;
using Xunit;

namespace VideoPlatform.StreamGateway.Tests;

public sealed class FfmpegLiveTranscodeRelayTests
{
    [Fact(DisplayName = "FFmpeg 实时转码只使用无凭据回环路径并输出 H264")]
    public void CommandUsesLoopbackPathsWithoutDeviceOrPublisherSecrets()
    {
        var cameraId = Guid.NewGuid();
        var options = new LiveTranscodeOptions
        {
            Enabled = true,
            FfmpegExecutablePath = Environment.ProcessPath,
            MediaMtxRtspBaseUri = "rtsp://127.0.0.1:8554/",
            ValidatedRecorderIds = [Guid.NewGuid()]
        };
        var inputPath = LiveTranscodePath.BuildInternalSource(cameraId);
        var outputPath = LiveTranscodePath.BuildPublicMain(cameraId);
        var route = new LiveTranscodeRoute(
            outputPath,
            inputPath,
            options.BuildMediaMtxUri(inputPath),
            options.BuildMediaMtxUri(outputPath),
            "fingerprint");
        var factory = new FfmpegLiveTranscodeProcessFactory(options);
        var credential = new LiveTranscodePublisherCredential(
            "live-relay",
            "temporary-publisher-secret-at-least-32-bytes");

        var startInfo = factory.CreateStartInfo(route, credential);
        var arguments = startInfo.ArgumentList.ToList();
        var joined = string.Join(' ', arguments);

        Assert.Contains("libopenh264", arguments);
        Assert.Contains("+genpts+discardcorrupt", arguments);
        Assert.Equal(2, arguments.Count(item => item == "tcp"));
        var inputUri = new Uri(arguments[arguments.IndexOf("-i") + 1]);
        var outputUri = new Uri(arguments[^1]);
        Assert.Equal(route.InputUri.AbsolutePath, inputUri.AbsolutePath);
        Assert.Equal(route.OutputUri.AbsolutePath, outputUri.AbsolutePath);
        Assert.Contains(credential.Username, inputUri.UserInfo, StringComparison.Ordinal);
        Assert.Contains(credential.Username, outputUri.UserInfo, StringComparison.Ordinal);
        Assert.DoesNotContain("device-secret", joined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(credential.Username, joined, StringComparison.Ordinal);
        Assert.Contains(credential.Password, joined, StringComparison.Ordinal);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact(DisplayName = "FFmpeg 实时转码拒绝包含凭据或非回环的路径")]
    public void CommandRejectsCredentialedOrRemoteRoutes()
    {
        var cameraId = Guid.NewGuid();
        var options = new LiveTranscodeOptions
        {
            Enabled = true,
            FfmpegExecutablePath = Environment.ProcessPath,
            MediaMtxRtspBaseUri = "rtsp://127.0.0.1:8554/",
            ValidatedRecorderIds = [Guid.NewGuid()]
        };
        var inputPath = LiveTranscodePath.BuildInternalSource(cameraId);
        var outputPath = LiveTranscodePath.BuildPublicMain(cameraId);
        var route = new LiveTranscodeRoute(
            outputPath,
            inputPath,
            new Uri($"rtsp://viewer:device-secret@127.0.0.1:8554/{inputPath}"),
            new Uri($"rtsp://127.0.0.1:8554/{outputPath}"),
            "fingerprint");

        var credential = new LiveTranscodePublisherCredential(
            "live-relay",
            "temporary-publisher-secret-at-least-32-bytes");

        Assert.Throws<InvalidOperationException>(() =>
            new FfmpegLiveTranscodeProcessFactory(options).CreateStartInfo(route, credential));
    }

    [Fact(DisplayName = "关闭 Windows Job Object 会终止已分配的子进程")]
    public async Task KillOnCloseJobTerminatesAssignedProcess()
    {
        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = commandInterpreter,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("ping -n 30 127.0.0.1 >nul");
        Assert.True(process.Start());

        using (var job = WindowsKillOnCloseJob.CreateAndAssign(process))
        {
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.HasExited);
    }
}
