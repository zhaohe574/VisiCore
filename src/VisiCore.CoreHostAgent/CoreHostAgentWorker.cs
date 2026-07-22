using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.CoreHostAgent;

public sealed class CoreHostAgentOptions
{
    public bool Enabled { get; init; }
    public string ExchangeDirectory { get; init; } = "/opt/visicore/upgrade-exchange";
    public string SigningPublicKeyPath { get; init; } = "/etc/visicore/release-public-key.pem";
    public string SigningPublicKeyId { get; init; } = string.Empty;
    public string DockerExecutablePath { get; init; } = "/usr/bin/docker";
    public string ComposeFilePath { get; init; } = "/opt/visicore/compose.yaml";
    public string ComposeOverridePath { get; init; } = "/opt/visicore/compose.release.yaml";
    public string CoreServiceName { get; init; } = "visicore-core";
    public string HealthUrl { get; init; } = "http://127.0.0.1:8080/healthz";
    public int ExecutionTimeoutSeconds { get; init; } = 900;
    public int HealthTimeoutSeconds { get; init; } = 300;

    public bool TryValidate(out string failureKind)
    {
        failureKind = string.Empty;
        if (!Enabled)
        {
            failureKind = "core_host_agent_disabled";
            return false;
        }
        if (string.IsNullOrWhiteSpace(SigningPublicKeyId) ||
            !Path.IsPathFullyQualified(ExchangeDirectory) ||
            !Path.IsPathFullyQualified(SigningPublicKeyPath) || !File.Exists(SigningPublicKeyPath) ||
            !Path.IsPathFullyQualified(DockerExecutablePath) || !File.Exists(DockerExecutablePath) ||
            !Path.IsPathFullyQualified(ComposeFilePath) || !File.Exists(ComposeFilePath) ||
            !Path.IsPathFullyQualified(ComposeOverridePath) ||
            !Uri.TryCreate(HealthUrl, UriKind.Absolute, out var healthUri) || !healthUri.IsLoopback ||
            ExecutionTimeoutSeconds is < 60 or > 3600 || HealthTimeoutSeconds is < 30 or > 900 ||
            string.IsNullOrWhiteSpace(CoreServiceName) || CoreServiceName.Length > 64)
        {
            failureKind = "core_host_agent_configuration_invalid";
            return false;
        }
        return true;
    }
}

/// <summary>
/// 中心容器之外的受限升级器。它不向容器暴露 Docker Socket，并且只执行固定 Compose、固定服务名与签名制品。
/// </summary>
public sealed class CoreHostAgentWorker(
    CoreHostAgentOptions options,
    ILogger<CoreHostAgentWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.TryValidate(out var failureKind))
        {
            logger.LogError("中心 Host Agent 配置无效，失败类别 {FailureKind}。", failureKind);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessInboxAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "中心 Host Agent 读取升级交换区失败。 ");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessInboxAsync(CancellationToken cancellationToken)
    {
        var inbox = Path.Combine(options.ExchangeDirectory, "inbox");
        var receipts = Path.Combine(options.ExchangeDirectory, "receipts");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(receipts);
        foreach (var path in Directory.EnumerateFiles(inbox, "*.operation.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CoreUpgradeReceipt receipt;
            try
            {
                var operation = JsonSerializer.Deserialize<CoreUpgradeMessage>(await File.ReadAllTextAsync(path, cancellationToken), SerializerOptions);
                if (operation is null || operation.OperationId == Guid.Empty)
                {
                    receipt = new CoreUpgradeReceipt(Guid.Empty, false, "core_upgrade_message_invalid", DateTimeOffset.UtcNow);
                }
                else
                {
                    var result = await ExecuteAsync(operation, cancellationToken);
                    receipt = new CoreUpgradeReceipt(operation.OperationId, result.Succeeded, result.FailureKind, DateTimeOffset.UtcNow);
                }
            }
            catch (JsonException)
            {
                receipt = new CoreUpgradeReceipt(Guid.Empty, false, "core_upgrade_message_invalid", DateTimeOffset.UtcNow);
            }
            catch (IOException)
            {
                // API 正在原子写入，下一轮再处理。
                continue;
            }

            if (receipt.OperationId != Guid.Empty)
            {
                await WriteReceiptAsync(receipts, receipt, cancellationToken);
            }
            File.Delete(path);
        }
    }

    private async Task<CoreHostOperationResult> ExecuteAsync(CoreUpgradeMessage operation, CancellationToken cancellationToken)
    {
        if (!TryValidateOperation(operation, out var artifact, out var failureKind))
        {
            return CoreHostOperationResult.Failed(failureKind);
        }

        var previousOverride = await ReadExistingOverrideAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(previousOverride))
        {
            return CoreHostOperationResult.Failed("core_known_good_release_missing");
        }
        var continuityFailure = await VerifyCoreVolumeContinuityAsync(cancellationToken);
        if (continuityFailure is not null)
        {
            return CoreHostOperationResult.Failed(continuityFailure);
        }
        if (!await WriteOverrideAsync(artifact.ArtifactReference, cancellationToken))
        {
            return CoreHostOperationResult.Failed("core_release_pointer_write_failed");
        }
        var applyResult = await RunComposeAsync(["pull", options.CoreServiceName], cancellationToken);
        if (applyResult.Succeeded)
        {
            applyResult = await RunComposeAsync(["up", "--detach", "--force-recreate", options.CoreServiceName], cancellationToken);
        }
        continuityFailure = applyResult.Succeeded
            ? await VerifyCoreVolumeContinuityAsync(cancellationToken)
            : "core_upgrade_apply_failed";
        if (applyResult.Succeeded && continuityFailure is null && await WaitForHealthAsync(cancellationToken))
        {
            await WriteKnownGoodOverrideAsync(artifact.ArtifactReference, cancellationToken);
            return CoreHostOperationResult.Success();
        }

        var restored = await RestoreKnownGoodAsync(previousOverride, operation.ProtectionBackupId, cancellationToken);
        return restored
            ? CoreHostOperationResult.Failed("core_upgrade_failed_rolled_back")
            : CoreHostOperationResult.Failed("core_upgrade_rollback_failed");
    }

    private bool TryValidateOperation(CoreUpgradeMessage operation, out ReleaseArtifactDescriptor artifact, out string failureKind)
    {
        artifact = null!;
        failureKind = "core_upgrade_descriptor_invalid";
        if (!ReleaseDescriptor.TryParse(operation.DescriptorJson, out var descriptor, out _) ||
            !string.Equals(descriptor.ProductVersion, operation.ProductVersion, StringComparison.Ordinal) ||
            !string.Equals(descriptor.SigningPublicKeyId, operation.PublicKeyId, StringComparison.Ordinal) ||
            !string.Equals(operation.PublicKeyId, options.SigningPublicKeyId, StringComparison.Ordinal) ||
            !VerifySignature(operation.DescriptorJson, operation.SignatureBase64))
        {
            return false;
        }

        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "amd64";
        artifact = descriptor.FindArtifacts("core", "linux", architecture).SingleOrDefault()!;
        if (artifact is null ||
            !string.Equals(artifact.ArtifactReference, operation.ArtifactReference, StringComparison.Ordinal) ||
            !string.Equals(artifact.ArtifactSha256, operation.ArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
            !operation.ArtifactReference.Contains($"@sha256:{operation.ArtifactSha256}", StringComparison.OrdinalIgnoreCase))
        {
            failureKind = "core_upgrade_target_invalid";
            return false;
        }
        var currentVersion = typeof(CoreHostAgentWorker).Assembly.GetName().Version ?? new Version(0, 0);
        if (!Version.TryParse(artifact.MinimumHostAgentVersion, out var minimumHostVersion) || currentVersion < minimumHostVersion)
        {
            failureKind = "minimum_host_version_not_met";
            return false;
        }
        return true;
    }

    private bool VerifySignature(string descriptorJson, string signatureBase64)
    {
        try
        {
            if (!ReleaseDescriptor.TryCanonicalizeJson(descriptorJson, out var canonicalDescriptorJson))
            {
                return false;
            }
            using var key = RSA.Create();
            key.ImportFromPem(File.ReadAllText(options.SigningPublicKeyPath));
            var payload = Encoding.UTF8.GetBytes(canonicalDescriptorJson);
            var signature = Convert.FromBase64String(signatureBase64);
            try
            {
                return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
                CryptographicOperations.ZeroMemory(signature);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task<bool> RestoreKnownGoodAsync(string previousOverride, Guid protectionBackupId, CancellationToken cancellationToken)
    {
        if (!await WriteOverrideRawAsync(previousOverride, cancellationToken))
        {
            return false;
        }
        var applyResult = await RunComposeAsync(["up", "--detach", "--force-recreate", options.CoreServiceName], cancellationToken);
        if (!applyResult.Succeeded)
        {
            return false;
        }

        var requestPath = Path.Combine(Path.GetTempPath(), $"visicore-upgrade-restore-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new { archivePath = $"/var/lib/visicore/backups/items/{protectionBackupId:N}.vcbackup" }), cancellationToken);
            if (!(await RunComposeAsync(["cp", requestPath, $"{options.CoreServiceName}:/var/lib/visicore/maintenance/upgrade-restore-request.json"], cancellationToken)).Succeeded ||
                !(await RunComposeAsync(["kill", "--signal", "USR1", options.CoreServiceName], cancellationToken)).Succeeded)
            {
                return false;
            }
            return await WaitForHealthAsync(cancellationToken);
        }
        finally
        {
            if (File.Exists(requestPath)) File.Delete(requestPath);
        }
    }

    private async Task<string> ReadExistingOverrideAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(options.ComposeOverridePath))
        {
            return await File.ReadAllTextAsync(options.ComposeOverridePath, cancellationToken);
        }
        var knownGood = GetKnownGoodOverridePath();
        return File.Exists(knownGood)
            ? await File.ReadAllTextAsync(knownGood, cancellationToken)
            : string.Empty;
    }

    private async Task<bool> WriteOverrideAsync(string imageReference, CancellationToken cancellationToken) =>
        await WriteOverrideRawAsync($"services:\n  {options.CoreServiceName}:\n    image: {imageReference}\n", cancellationToken);

    private async Task<bool> WriteOverrideRawAsync(string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 4096)
        {
            return false;
        }
        var directory = Path.GetDirectoryName(options.ComposeOverridePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(options.ComposeOverridePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, options.ComposeOverridePath, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task WriteKnownGoodOverrideAsync(string imageReference, CancellationToken cancellationToken)
    {
        var path = GetKnownGoodOverridePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, $"services:\n  {options.CoreServiceName}:\n    image: {imageReference}\n", cancellationToken);
    }

    private string GetKnownGoodOverridePath() => Path.Combine(Path.GetDirectoryName(options.ComposeOverridePath)!, "compose.known-good.yaml");

    private async Task<string?> VerifyCoreVolumeContinuityAsync(CancellationToken cancellationToken)
    {
        var configuration = await RunComposeAsync(["config", "--format", "json"], cancellationToken);
        if (!configuration.Succeeded || !CoreVolumeContinuity.TryReadExpectedVolumes(configuration.StandardOutput, out var expectedVolumes))
        {
            return "core_volume_continuity_configuration_invalid";
        }

        var currentContainer = await RunComposeAsync(["ps", "--quiet", options.CoreServiceName], cancellationToken);
        var containerId = currentContainer.StandardOutput.Trim();
        if (!currentContainer.Succeeded || string.IsNullOrWhiteSpace(containerId) || containerId.Contains('\r') || containerId.Contains('\n'))
        {
            return "core_volume_continuity_current_container_missing";
        }

        var inspection = await RunDockerAsync(["inspect", containerId], cancellationToken);
        return !inspection.Succeeded || !CoreVolumeContinuity.MatchesRunningContainer(inspection.StandardOutput, expectedVolumes)
            ? "core_volume_continuity_mismatch"
            : null;
    }

    private async Task<bool> WaitForHealthAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.HealthTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await HealthClient.GetAsync(options.HealthUrl, cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK) return true;
            }
            catch (HttpRequestException)
            {
                // 容器重建期间暂不可达，等待下一轮。
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        return false;
    }

    private Task<CoreHostCommandResult> RunComposeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var command = new List<string> { "compose", "--file", options.ComposeFilePath };
        if (File.Exists(options.ComposeOverridePath))
        {
            command.Add("--file");
            command.Add(options.ComposeOverridePath);
        }
        command.AddRange(arguments);
        return RunDockerAsync(command, cancellationToken);
    }

    private async Task<CoreHostCommandResult> RunDockerAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.DockerExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(options.ComposeFilePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        var started = false;
        try
        {
            if (!process.Start()) return CoreHostCommandResult.Failed("docker_start_failed");
            started = true;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ExecutionTimeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0
                ? CoreHostCommandResult.Success(await stdout)
                : CoreHostCommandResult.Failed("docker_command_failed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryStop(process);
            return CoreHostCommandResult.Failed("docker_command_timeout");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return CoreHostCommandResult.Failed("docker_start_failed");
        }
        finally
        {
            if (started && !process.HasExited) TryStop(process);
        }
    }

    private static void TryStop(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    private async Task WriteReceiptAsync(string receiptsDirectory, CoreUpgradeReceipt receipt, CancellationToken cancellationToken)
    {
        var path = Path.Combine(receiptsDirectory, $"{receipt.OperationId:N}.receipt.json");
        var temporaryPath = Path.Combine(receiptsDirectory, $".{receipt.OperationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(receipt, SerializerOptions), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

public sealed record CoreUpgradeMessage(
    Guid OperationId,
    string ProductVersion,
    string DescriptorJson,
    string SignatureBase64,
    string PublicKeyId,
    string ArtifactReference,
    string ArtifactSha256,
    Guid ProtectionBackupId);

public sealed record CoreUpgradeReceipt(Guid OperationId, bool Succeeded, string? FailureKind, DateTimeOffset CompletedAt);
public sealed record CoreHostOperationResult(bool Succeeded, string? FailureKind)
{
    public static CoreHostOperationResult Success() => new(true, null);
    public static CoreHostOperationResult Failed(string failureKind) => new(false, failureKind);
}

internal sealed record CoreHostCommandResult(bool Succeeded, string? FailureKind, string StandardOutput)
{
    public static CoreHostCommandResult Success(string standardOutput) => new(true, null, standardOutput);
    public static CoreHostCommandResult Failed(string failureKind) => new(false, failureKind, string.Empty);
}
