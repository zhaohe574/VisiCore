using System.Text.Json;

namespace VisiCore.Api;

/// <summary>
/// 中心容器与独立 Core Host Agent 的最小交换区。API 仅写入已登记的发行描述，不能传递 Docker 命令或宿主机路径。
/// </summary>
public sealed class CoreUpgradeOptions
{
    public bool Enabled { get; init; }
    public string ExchangeDirectory { get; init; } = "/var/lib/visicore/upgrade-exchange";

    public bool TryValidate(out string failureKind)
    {
        failureKind = string.Empty;
        if (!Enabled)
        {
            failureKind = "core_host_agent_not_initialized";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ExchangeDirectory) || !Path.IsPathFullyQualified(ExchangeDirectory))
        {
            failureKind = "core_upgrade_exchange_invalid";
            return false;
        }
        return true;
    }
}

public sealed class CoreUpgradeExchange(CoreUpgradeOptions options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task SubmitAsync(CoreUpgradeMessage message, CancellationToken cancellationToken)
    {
        if (!options.TryValidate(out var failureKind))
        {
            throw new CoreUpgradeExchangeException(failureKind);
        }
        var inbox = Path.Combine(options.ExchangeDirectory, "inbox");
        Directory.CreateDirectory(inbox);
        var finalPath = Path.Combine(inbox, $"{message.OperationId:N}.operation.json");
        if (File.Exists(finalPath))
        {
            return;
        }
        var temporaryPath = Path.Combine(inbox, $".{message.OperationId:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(message, SerializerOptions), cancellationToken);
            File.Move(temporaryPath, finalPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            // 重试时同一任务已成功写入。
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<CoreUpgradeReceipt?> TryReadReceiptAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!options.TryValidate(out _))
        {
            return null;
        }
        var path = Path.Combine(options.ExchangeDirectory, "receipts", $"{operationId:N}.receipt.json");
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var receipt = JsonSerializer.Deserialize<CoreUpgradeReceipt>(await File.ReadAllTextAsync(path, cancellationToken), SerializerOptions);
            return receipt is { OperationId: var id } && id == operationId ? receipt : null;
        }
        catch (JsonException)
        {
            return new CoreUpgradeReceipt(operationId, false, "core_host_receipt_invalid", DateTimeOffset.UtcNow);
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

public sealed class CoreUpgradeExchangeException(string failureKind) : InvalidOperationException(failureKind)
{
    public string FailureKind { get; } = failureKind;
}
