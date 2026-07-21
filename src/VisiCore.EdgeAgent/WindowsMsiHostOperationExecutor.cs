using System.Diagnostics;

namespace VisiCore.EdgeAgent;

public interface IHostOperationExecutor
{
    Task<HostOperationExecutionResult> ExecuteAsync(
        Guid operationId,
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken);
}

public sealed class WindowsMsiHostOperationExecutor(HostAgentOptions options) : IHostOperationExecutor
{
    public async Task<HostOperationExecutionResult> ExecuteAsync(
        Guid operationId,
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        var installerPath = artifact.ArtifactPath;
        if (string.IsNullOrWhiteSpace(installerPath) || !Path.IsPathFullyQualified(installerPath) || !File.Exists(installerPath) ||
            !installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(options.WindowsInstallerExecutablePath) || !Path.IsPathFullyQualified(options.WindowsInstallerExecutablePath) ||
            !File.Exists(options.WindowsInstallerExecutablePath) ||
            string.IsNullOrWhiteSpace(options.WindowsUpdateRunnerExecutablePath) || !Path.IsPathFullyQualified(options.WindowsUpdateRunnerExecutablePath) ||
            !File.Exists(options.WindowsUpdateRunnerExecutablePath))
        {
            return HostOperationExecutionResult.Failed("windows_installer_configuration_invalid");
        }

        var jobsDirectory = Path.Combine(Path.GetFullPath(options.OperationStateDirectory), "update-jobs");
        var receiptsDirectory = Path.GetFullPath(options.OperationReceiptDirectory);
        Directory.CreateDirectory(jobsDirectory);
        Directory.CreateDirectory(receiptsDirectory);
        var jobPath = Path.Combine(jobsDirectory, $"{operationId:N}.json");
        var temporaryJobPath = Path.Combine(jobsDirectory, $".{operationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryJobPath, System.Text.Json.JsonSerializer.Serialize(new WindowsUpdateJob(
                operationId,
                installerPath,
                artifact.ArtifactSha256,
                options.WindowsInstallerExecutablePath,
                Path.Combine(receiptsDirectory, $"{operationId:N}.receipt.json"))), cancellationToken);
            File.Move(temporaryJobPath, jobPath, overwrite: true);
        }
        catch (IOException)
        {
            return HostOperationExecutionResult.Failed("windows_update_job_write_failed");
        }
        finally
        {
            if (File.Exists(temporaryJobPath)) File.Delete(temporaryJobPath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.WindowsUpdateRunnerExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--job");
        process.StartInfo.ArgumentList.Add(jobPath);
        try
        {
            if (!process.Start())
            {
                return HostOperationExecutionResult.Failed("windows_installer_start_failed");
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ExecutionTimeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0
                ? HostOperationExecutionResult.Success()
                : process.ExitCode == 3010
                    ? new HostOperationExecutionResult(true, "reboot_required")
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
        Guid operationId,
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? windowsExecutor.ExecuteAsync(operationId, operationKind, artifact, cancellationToken)
            : dockerExecutor.ExecuteAsync(operationId, operationKind, artifact, cancellationToken);
}

public sealed record WindowsUpdateJob(
    Guid OperationId,
    string InstallerPath,
    string InstallerSha256,
    string WindowsInstallerExecutablePath,
    string ReceiptPath);
