using System.Diagnostics;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 默认关闭的 Compose 执行器。它只接受本机配置中的固定可执行文件和 Compose 文件，
/// 不读取或拼接操作清单中的任何命令、路径或参数。
/// </summary>
public sealed class DockerComposeHostOperationExecutor(HostAgentOptions options) : IHostOperationExecutor
{
    public async Task<HostOperationExecutionResult> RestartConfiguredEdgeAgentAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() ||
            string.IsNullOrWhiteSpace(options.DockerComposeExecutablePath) ||
            string.IsNullOrWhiteSpace(options.ComposeFilePath) ||
            !Path.IsPathFullyQualified(options.DockerComposeExecutablePath) ||
            !Path.IsPathFullyQualified(options.ComposeFilePath) ||
            !File.Exists(options.DockerComposeExecutablePath) ||
            !File.Exists(options.ComposeFilePath))
        {
            return HostOperationExecutionResult.Failed("configuration_restart_unavailable");
        }

        return await RunComposeAsync(options.ComposeFilePath, ["up", "--detach", "--no-deps", "edge-node"], cancellationToken);
    }

    public async Task<HostOperationExecutionResult> ExecuteAsync(
        Guid operationId,
        HostOperationKind operationKind,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (!options.AllowExecution)
        {
            return HostOperationExecutionResult.Failed("host_execution_not_enabled");
        }

        if (!string.IsNullOrWhiteSpace(artifact.OciImageReference))
        {
            return await ExecuteOciReleaseAsync(artifact, cancellationToken);
        }

        var composeFilePath = artifact.ComposeFilePath;
        if (string.IsNullOrWhiteSpace(options.DockerComposeExecutablePath) ||
            string.IsNullOrWhiteSpace(composeFilePath) ||
            !Path.IsPathFullyQualified(options.DockerComposeExecutablePath) ||
            !Path.IsPathFullyQualified(composeFilePath) ||
            !File.Exists(options.DockerComposeExecutablePath) ||
            !File.Exists(composeFilePath) ||
            !composeFilePath.EndsWith("compose.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return HostOperationExecutionResult.Failed("host_executor_configuration_invalid");
        }

        var pullResult = await RunComposeAsync(composeFilePath, ["pull"], cancellationToken);
        if (!pullResult.Succeeded)
        {
            return pullResult with { FailureKind = "docker_compose_pull_failed" };
        }

        var applyResult = await RunComposeAsync(composeFilePath, ["up", "--detach", "--remove-orphans"], cancellationToken);
        return applyResult.Succeeded
            ? HostOperationExecutionResult.Success()
            : applyResult with { FailureKind = "docker_compose_apply_failed" };
    }

    private async Task<HostOperationExecutionResult> ExecuteOciReleaseAsync(
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ComposeFilePath) ||
            string.IsNullOrWhiteSpace(options.ActiveReleaseComposeOverridePath) ||
            string.IsNullOrWhiteSpace(artifact.OciImageReference) ||
            !Path.IsPathFullyQualified(options.ComposeFilePath) ||
            !Path.IsPathFullyQualified(options.ActiveReleaseComposeOverridePath) ||
            !File.Exists(options.ComposeFilePath) ||
            !artifact.OciImageReference.StartsWith("visicore/visicore-edge@", StringComparison.OrdinalIgnoreCase) ||
            !artifact.OciImageReference.Contains($"@sha256:{artifact.ArtifactSha256}", StringComparison.OrdinalIgnoreCase))
        {
            return HostOperationExecutionResult.Failed("host_executor_configuration_invalid");
        }

        var overridePath = options.ActiveReleaseComposeOverridePath;
        var directory = Path.GetDirectoryName(overridePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(overridePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            // 镜像引用已在签名描述与 Host Agent 中重复校验，写入内容没有用户可控路径或命令。
            await File.WriteAllTextAsync(temporaryPath, $"services:\n  edge-node:\n    image: {artifact.OciImageReference}\n", cancellationToken);
            File.Move(temporaryPath, overridePath, overwrite: true);
        }
        catch (IOException)
        {
            return HostOperationExecutionResult.Failed("active_release_pointer_write_failed");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var pullResult = await RunComposeAsync(options.ComposeFilePath, ["pull", "edge-node"], cancellationToken, overridePath);
        if (!pullResult.Succeeded)
        {
            return pullResult with { FailureKind = "docker_compose_pull_failed" };
        }
        var applyResult = await RunComposeAsync(options.ComposeFilePath, ["up", "--detach", "--no-deps", "edge-node"], cancellationToken, overridePath);
        return applyResult.Succeeded
            ? HostOperationExecutionResult.Success()
            : applyResult with { FailureKind = "docker_compose_apply_failed" };
    }

    private async Task<HostOperationExecutionResult> RunComposeAsync(
        string composeFilePath,
        IReadOnlyList<string> actionArguments,
        CancellationToken cancellationToken,
        string? releaseOverridePath = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.DockerComposeExecutablePath!,
                WorkingDirectory = Path.GetDirectoryName(composeFilePath)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("compose");
        if (!string.IsNullOrWhiteSpace(options.ComposeEnvironmentFilePath) && File.Exists(options.ComposeEnvironmentFilePath))
        {
            process.StartInfo.ArgumentList.Add("--env-file");
            process.StartInfo.ArgumentList.Add(options.ComposeEnvironmentFilePath);
        }
        process.StartInfo.ArgumentList.Add("--file");
        process.StartInfo.ArgumentList.Add(composeFilePath);
        if (!string.IsNullOrWhiteSpace(options.ResourcePolicyComposeOverridePath) && File.Exists(options.ResourcePolicyComposeOverridePath))
        {
            process.StartInfo.ArgumentList.Add("--file");
            process.StartInfo.ArgumentList.Add(options.ResourcePolicyComposeOverridePath);
        }
        if (!string.IsNullOrWhiteSpace(releaseOverridePath))
        {
            process.StartInfo.ArgumentList.Add("--file");
            process.StartInfo.ArgumentList.Add(releaseOverridePath);
        }
        foreach (var argument in actionArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var started = false;
        try
        {
            if (!process.Start())
            {
                return HostOperationExecutionResult.Failed("docker_compose_start_failed");
            }
            started = true;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ExecutionTimeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            // 输出可能包含运行环境细节，任何情况下都不写日志、诊断或数据库。
            return process.ExitCode == 0
                ? HostOperationExecutionResult.Success()
                : HostOperationExecutionResult.Failed("docker_compose_exit_nonzero");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return HostOperationExecutionResult.Failed("docker_compose_timeout");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return HostOperationExecutionResult.Failed("docker_compose_start_failed");
        }
        catch (InvalidOperationException)
        {
            return HostOperationExecutionResult.Failed("docker_compose_start_failed");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                TryKill(process);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 停止阶段只做尽力清理，不能输出可能包含环境细节的子进程信息。
        }
    }
}

public enum HostOperationKind
{
    Deployment = 0,
    Rollback = 1
}

public sealed record HostOperationExecutionResult(bool Succeeded, string? FailureKind)
{
    public static HostOperationExecutionResult Success() => new(true, null);
    public static HostOperationExecutionResult Failed(string failureKind) => new(false, failureKind);
}
