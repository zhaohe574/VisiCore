using System.Diagnostics;

namespace VisiCore.EdgeAgent;

public interface IHostOperationExecutor
{
    Task<HostOperationExecutionResult> ExecuteAsync(
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken);
}

public sealed class WindowsMsiHostOperationExecutor(HostAgentOptions options) : IHostOperationExecutor
{
    public async Task<HostOperationExecutionResult> ExecuteAsync(
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        var installerPath = artifact.ArtifactPath;
        if (string.IsNullOrWhiteSpace(installerPath) || !Path.IsPathFullyQualified(installerPath) || !File.Exists(installerPath) ||
            !installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(options.WindowsInstallerExecutablePath) || !Path.IsPathFullyQualified(options.WindowsInstallerExecutablePath) ||
            !File.Exists(options.WindowsInstallerExecutablePath))
        {
            return HostOperationExecutionResult.Failed("windows_installer_configuration_invalid");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.WindowsInstallerExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("/i");
        process.StartInfo.ArgumentList.Add(installerPath);
        process.StartInfo.ArgumentList.Add("/qn");
        process.StartInfo.ArgumentList.Add("/norestart");
        try
        {
            if (!process.Start())
            {
                return HostOperationExecutionResult.Failed("windows_installer_start_failed");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ExecutionTimeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode is 0 or 3010
                ? HostOperationExecutionResult.Success()
                : HostOperationExecutionResult.Failed("windows_installer_exit_nonzero");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return HostOperationExecutionResult.Failed("windows_installer_timeout");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return HostOperationExecutionResult.Failed("windows_installer_start_failed");
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }
}

public sealed class PlatformHostOperationExecutor(
    DockerComposeHostOperationExecutor dockerExecutor,
    WindowsMsiHostOperationExecutor windowsExecutor) : IHostOperationExecutor
{
    public Task<HostOperationExecutionResult> ExecuteAsync(
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? windowsExecutor.ExecuteAsync(operationKind, artifact, cancellationToken)
            : dockerExecutor.ExecuteAsync(operationKind, artifact, cancellationToken);
}
