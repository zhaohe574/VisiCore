using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisiCore.Core;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 受限宿主运维骨架：只验证离线签名清单，绝不直接执行 Docker、shell 或数据库命令。
/// 真正执行器必须由独立受控宿主服务实现，并重新验证同一份清单。
/// </summary>
public sealed class HostOperationWorker(
    HostAgentOptions options,
    HostOperationState state,
    IHostOperationExecutor executor,
    VerifiedDeploymentStore verifiedDeploymentStore,
    HostReleaseArtifactVerifier releaseArtifactVerifier,
    ILogger<HostOperationWorker> logger) : BackgroundService
{
    private static readonly HashSet<string> AllowedOperationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "validate-release",
        "compose-upgrade",
        "database-backup",
        "rollback"
    };
    private readonly ConcurrentDictionary<string, byte> observedManifestHashes = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            state.SetDisabled();
            return;
        }

        if (!options.TryValidate(out var validationError))
        {
            state.SetBlocked("invalid_configuration");
            logger.LogError("Host Agent 配置无效，失败类别 {FailureKind}。", "invalid_configuration");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VerifyOperationInboxAsync(stoppingToken);
                await ProcessOperationExchangeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                state.SetBlocked("operation_inbox_unavailable");
                logger.LogWarning("Host Agent 操作清单读取失败，失败类别 {FailureKind}。", exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken);
        }
    }

    public async Task<HostOperationExecutionResult> ValidateAndExecuteAsync(
        EdgeOperation operation,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return HostOperationExecutionResult.Failed("host_agent_disabled");
        }
        if (!options.TryValidate(out _))
        {
            state.SetBlocked("invalid_configuration");
            return HostOperationExecutionResult.Failed("host_executor_configuration_invalid");
        }
        if (!TryResolveControlPlaneOperationKind(operation.OperationType, out var operationKind))
        {
            return HostOperationExecutionResult.Failed("unsupported_host_operation");
        }
        if (!File.Exists(options.SigningPublicKeyPath))
        {
            state.SetBlocked("signing_key_unavailable");
            return HostOperationExecutionResult.Failed("signing_key_unavailable");
        }

        var sourceOperationId = Guid.Empty;
        var sourceFailureKind = string.Empty;
        var hasRollbackSource = operationKind == HostOperationKind.Rollback &&
            TryReadRollbackSourceOperationId(operation, out sourceOperationId, out sourceFailureKind);
        if (operationKind == HostOperationKind.Rollback && !hasRollbackSource)
        {
            return HostOperationExecutionResult.Failed(
                string.IsNullOrEmpty(sourceFailureKind) ? "rollback_source_missing" : sourceFailureKind);
        }
        if (hasRollbackSource)
        {
            var rollbackArtifact = await verifiedDeploymentStore.GetArtifactAsync(sourceOperationId, cancellationToken);
            if (rollbackArtifact is null ||
                !await releaseArtifactVerifier.VerifyPersistedAsync(rollbackArtifact, cancellationToken))
            {
                return HostOperationExecutionResult.Failed("rollback_source_unverified");
            }
            if (!options.AllowExecution)
            {
                return HostOperationExecutionResult.Failed("host_execution_not_enabled");
            }

            state.SetAccepted();
            return await executor.ExecuteAsync(HostOperationKind.Rollback, rollbackArtifact, cancellationToken);
        }
        if (operationKind == HostOperationKind.Rollback && !string.IsNullOrEmpty(sourceFailureKind))
        {
            return HostOperationExecutionResult.Failed(sourceFailureKind);
        }

        try
        {
            if (!TryReadControlPlaneManifest(operation, out var controlPlaneManifest, out var failureKind))
            {
                return HostOperationExecutionResult.Failed(failureKind);
            }

            using var publicKey = RSA.Create();
            publicKey.ImportFromPem(await File.ReadAllTextAsync(options.SigningPublicKeyPath!, cancellationToken));
            var manifestBytes = Encoding.UTF8.GetBytes(controlPlaneManifest.ManifestJson);
            var signature = Convert.FromBase64String(controlPlaneManifest.SignatureBase64);
            try
            {
                if (!publicKey.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                {
                    return HostOperationExecutionResult.Failed("signature_invalid");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(manifestBytes);
                CryptographicOperations.ZeroMemory(signature);
            }

            if (!TryValidateControlPlaneManifest(controlPlaneManifest, operationKind, out failureKind))
            {
                return HostOperationExecutionResult.Failed(failureKind);
            }

            if (!options.AllowExecution)
            {
                return HostOperationExecutionResult.Failed("host_execution_not_enabled");
            }

            if (!EdgeReleaseManifest.TryParse(controlPlaneManifest.ManifestJson, out var releaseManifest, out _))
            {
                return HostOperationExecutionResult.Failed("release_manifest_invalid");
            }

            var artifactVerification = await releaseArtifactVerifier.DownloadAndVerifyAsync(releaseManifest, cancellationToken);
            if (!artifactVerification.Succeeded || artifactVerification.Artifact is null)
            {
                return HostOperationExecutionResult.Failed(artifactVerification.FailureKind ?? "artifact_verification_failed");
            }

            state.SetAccepted();
            var executionResult = await executor.ExecuteAsync(operationKind, artifactVerification.Artifact, cancellationToken);
            if (!executionResult.Succeeded || operationKind != HostOperationKind.Deployment)
            {
                return executionResult;
            }

            try
            {
                await verifiedDeploymentStore.RememberAsync(
                    operation.Id,
                    controlPlaneManifest.ReleaseId,
                    options.SigningPublicKeyId!,
                    artifactVerification.Artifact,
                    cancellationToken);
                return executionResult;
            }
            catch (IOException)
            {
                return HostOperationExecutionResult.Failed("deployment_receipt_persist_failed");
            }
            catch (UnauthorizedAccessException)
            {
                return HostOperationExecutionResult.Failed("deployment_receipt_persist_failed");
            }
        }
        catch (CryptographicException)
        {
            return HostOperationExecutionResult.Failed("signature_invalid");
        }
        catch (FormatException)
        {
            return HostOperationExecutionResult.Failed("encoding_invalid");
        }
        catch (JsonException)
        {
            return HostOperationExecutionResult.Failed("json_invalid");
        }
        catch (IOException)
        {
            return HostOperationExecutionResult.Failed("signing_key_unavailable");
        }
        catch (UnauthorizedAccessException)
        {
            return HostOperationExecutionResult.Failed("signing_key_unavailable");
        }
    }

    private async Task VerifyOperationInboxAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.SigningPublicKeyPath))
        {
            state.SetBlocked("signing_key_unavailable");
            return;
        }

        Directory.CreateDirectory(options.OperationInboxDirectory);
        using var publicKey = RSA.Create();
        publicKey.ImportFromPem(await File.ReadAllTextAsync(options.SigningPublicKeyPath!, cancellationToken));
        foreach (var path in Directory.EnumerateFiles(options.OperationInboxDirectory, "*.manifest.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
            if (observedManifestHashes.ContainsKey(manifestHash))
            {
                continue;
            }

            if (!TryValidateManifest(manifestBytes, publicKey, out var failureKind))
            {
                observedManifestHashes.TryAdd(manifestHash, 0);
                state.SetBlocked(failureKind);
                logger.LogWarning("Host Agent 拒绝未通过验证的操作清单，失败类别 {FailureKind}。", failureKind);
                continue;
            }

            observedManifestHashes.TryAdd(manifestHash, 0);
            state.SetAccepted();
            logger.LogInformation("Host Agent 已验证受签名操作清单，等待独立执行器处理。 ");
        }
    }

    private async Task ProcessOperationExchangeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OperationInboxDirectory);
        Directory.CreateDirectory(options.OperationReceiptDirectory);
        foreach (var path in Directory.EnumerateFiles(options.OperationInboxDirectory, "*.operation.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            HostOperationReceipt receipt;
            try
            {
                var message = JsonSerializer.Deserialize<HostOperationMessage>(
                    await File.ReadAllTextAsync(path, cancellationToken));
                if (message is null || message.OperationId == Guid.Empty || !HostOperationExchange.IsHostOperation(message.OperationType))
                {
                    receipt = new HostOperationReceipt(Guid.Empty, false, "host_operation_message_invalid", DateTimeOffset.UtcNow);
                }
                else
                {
                    var result = await ValidateAndExecuteAsync(
                        new EdgeOperation(message.OperationId, message.OperationType, message.DetailsJson),
                        cancellationToken);
                    receipt = new HostOperationReceipt(message.OperationId, result.Succeeded, result.FailureKind, DateTimeOffset.UtcNow);
                }
            }
            catch (JsonException)
            {
                receipt = new HostOperationReceipt(Guid.Empty, false, "host_operation_message_invalid", DateTimeOffset.UtcNow);
            }
            catch (IOException)
            {
                // 正在由 Edge Agent 原子写入，留给下一轮处理。
                continue;
            }

            if (receipt.OperationId != Guid.Empty)
            {
                await WriteReceiptAsync(receipt, cancellationToken);
            }
            File.Delete(path);
        }
    }

    private async Task WriteReceiptAsync(HostOperationReceipt receipt, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.OperationReceiptDirectory, $"{receipt.OperationId:N}.receipt.json");
        var temporaryPath = Path.Combine(options.OperationReceiptDirectory, $".{receipt.OperationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(receipt), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryResolveControlPlaneOperationKind(string operationType, out HostOperationKind operationKind)
    {
        if (operationType.Equals("deployment", StringComparison.OrdinalIgnoreCase))
        {
            operationKind = HostOperationKind.Deployment;
            return true;
        }
        if (operationType.Equals("rollback", StringComparison.OrdinalIgnoreCase))
        {
            operationKind = HostOperationKind.Rollback;
            return true;
        }

        operationKind = default;
        return false;
    }

    private static bool TryReadRollbackSourceOperationId(
        EdgeOperation operation,
        out Guid sourceOperationId,
        out string failureKind)
    {
        sourceOperationId = Guid.Empty;
        failureKind = string.Empty;
        if (string.IsNullOrWhiteSpace(operation.DetailsJson))
        {
            failureKind = "rollback_source_invalid";
            return false;
        }

        try
        {
            using var details = JsonDocument.Parse(operation.DetailsJson);
            if (!details.RootElement.TryGetProperty("sourceOperationId", out var value))
            {
                return false;
            }
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out sourceOperationId))
            {
                failureKind = "rollback_source_invalid";
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            failureKind = "json_invalid";
            return false;
        }
    }

    private bool TryReadControlPlaneManifest(
        EdgeOperation operation,
        out ControlPlaneManifest manifest,
        out string failureKind)
    {
        manifest = null!;
        failureKind = "invalid_manifest";
        if (string.IsNullOrWhiteSpace(operation.DetailsJson) || operation.DetailsJson.Length > 131_072)
        {
            return false;
        }

        try
        {
            using var details = JsonDocument.Parse(operation.DetailsJson);
            var root = details.RootElement;
            if (!TryReadString(root, "releaseId", out var releaseId) || releaseId.Length > 128 ||
                !TryReadString(root, "manifestJson", out var manifestJson) || manifestJson.Length > 65_536 ||
                !TryReadString(root, "signatureBase64", out var signatureBase64) || signatureBase64.Length > 24_576 ||
                !TryReadString(root, "publicKeyId", out var publicKeyId) ||
                !string.Equals(publicKeyId, options.SigningPublicKeyId, StringComparison.Ordinal))
            {
                failureKind = "manifest_metadata_invalid";
                return false;
            }

            if (root.TryGetProperty("sourceOperationId", out var sourceOperationId) &&
                sourceOperationId.ValueKind != JsonValueKind.Null &&
                (sourceOperationId.ValueKind != JsonValueKind.String || !Guid.TryParse(sourceOperationId.GetString(), out _)))
            {
                failureKind = "manifest_metadata_invalid";
                return false;
            }

            manifest = new ControlPlaneManifest(releaseId, manifestJson, signatureBase64);
            return true;
        }
        catch (JsonException)
        {
            failureKind = "json_invalid";
            return false;
        }
    }

    private bool TryValidateControlPlaneManifest(
        ControlPlaneManifest manifest,
        HostOperationKind expectedOperationKind,
        out string failureKind)
    {
        failureKind = "payload_invalid";
        try
        {
            using var signedPayload = JsonDocument.Parse(manifest.ManifestJson);
            var content = signedPayload.RootElement;
            var expectedOperationType = expectedOperationKind == HostOperationKind.Deployment ? "deployment" : "rollback";
            if (!TryReadString(content, "releaseId", out var releaseId) ||
                !string.Equals(releaseId, manifest.ReleaseId, StringComparison.Ordinal) ||
                !TryReadString(content, "operationType", out var operationType) ||
                !string.Equals(operationType, expectedOperationType, StringComparison.OrdinalIgnoreCase) ||
                !TryReadDateTimeOffset(content, "issuedAt", out var issuedAt) ||
                !TryReadDateTimeOffset(content, "expiresAt", out var expiresAt) ||
                expiresAt <= DateTimeOffset.UtcNow || expiresAt > issuedAt.AddHours(24) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return false;
            }
            if (!EdgeReleaseManifest.TryParse(manifest.ManifestJson, out var releaseManifest, out _) ||
                !string.Equals(releaseManifest.ReleaseId, manifest.ReleaseId, StringComparison.Ordinal) ||
                !string.Equals(releaseManifest.OperationType, expectedOperationType, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(releaseManifest.SigningPublicKeyId, options.SigningPublicKeyId, StringComparison.Ordinal) ||
                !releaseManifest.TargetsCurrentHost())
            {
                failureKind = "release_target_invalid";
                return false;
            }
            var currentVersion = typeof(HostOperationWorker).Assembly.GetName().Version ?? new Version(0, 0);
            if (!Version.TryParse(releaseManifest.MinimumHostAgentVersion, out var minimumVersion) ||
                currentVersion < minimumVersion)
            {
                failureKind = "minimum_host_version_not_met";
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            failureKind = "json_invalid";
            return false;
        }
    }

    private static bool TryValidateManifest(byte[] rawManifest, RSA publicKey, out string failureKind)
    {
        failureKind = "invalid_manifest";
        try
        {
            using var manifest = JsonDocument.Parse(rawManifest);
            var root = manifest.RootElement;
            if (!TryReadString(root, "payloadBase64", out var payloadBase64) ||
                !TryReadString(root, "signatureBase64", out var signatureBase64))
            {
                return false;
            }

            var payload = Convert.FromBase64String(payloadBase64);
            var signature = Convert.FromBase64String(signatureBase64);
            if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            {
                failureKind = "signature_invalid";
                return false;
            }

            using var signedPayload = JsonDocument.Parse(payload);
            var content = signedPayload.RootElement;
            if (!TryReadString(content, "operationId", out var operationId) || !Guid.TryParse(operationId, out _) ||
                !TryReadString(content, "operationType", out var operationType) || !AllowedOperationTypes.Contains(operationType) ||
                !TryReadDateTimeOffset(content, "issuedAt", out var issuedAt) ||
                !TryReadDateTimeOffset(content, "expiresAt", out var expiresAt) ||
                expiresAt <= DateTimeOffset.UtcNow || expiresAt > issuedAt.AddHours(24) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                failureKind = "payload_invalid";
                return false;
            }

            return true;
        }
        catch (CryptographicException)
        {
            failureKind = "signature_invalid";
            return false;
        }
        catch (FormatException)
        {
            failureKind = "encoding_invalid";
            return false;
        }
        catch (JsonException)
        {
            failureKind = "json_invalid";
            return false;
        }
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private static bool TryReadDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(property.GetString(), out value);
    }

    private sealed record ControlPlaneManifest(string ReleaseId, string ManifestJson, string SignatureBase64);
}
