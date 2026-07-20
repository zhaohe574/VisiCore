using System.Text.Json;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 无特权 Edge Agent 与独立 Host Agent 的本地交换区。
/// 操作内容仍须由 Host Agent 独立验签；运行时仅能写入待处理消息并读取最小回执。
/// </summary>
public sealed class HostOperationExchange(HostAgentOptions options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task SubmitAsync(EdgeOperation operation, CancellationToken cancellationToken)
    {
        if (!IsHostOperation(operation.OperationType))
        {
            throw new ArgumentException("不支持的宿主操作类型。", nameof(operation));
        }

        Directory.CreateDirectory(options.OperationInboxDirectory);
        var path = GetOperationPath(operation.Id);
        if (File.Exists(path))
        {
            return;
        }

        var temporaryPath = Path.Combine(
            options.OperationInboxDirectory,
            $".{operation.Id:N}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(new HostOperationMessage(operation.Id, operation.OperationType, operation.DetailsJson), SerializerOptions),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // 另一轮心跳已完成原子投递。
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<HostOperationReceipt?> TryReadReceiptAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(options.OperationReceiptDirectory, $"{operationId:N}.receipt.json");
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var receipt = JsonSerializer.Deserialize<HostOperationReceipt>(
                await File.ReadAllTextAsync(path, cancellationToken),
                SerializerOptions);
            return receipt is { OperationId: var id } && id == operationId ? receipt : null;
        }
        catch (JsonException)
        {
            return new HostOperationReceipt(operationId, false, "host_receipt_invalid", DateTimeOffset.UtcNow);
        }
    }

    public static bool IsHostOperation(string operationType) =>
        operationType.Equals("deployment", StringComparison.OrdinalIgnoreCase) ||
        operationType.Equals("rollback", StringComparison.OrdinalIgnoreCase);

    internal string GetOperationPath(Guid operationId) =>
        Path.Combine(options.OperationInboxDirectory, $"{operationId:N}.operation.json");
}

public sealed record HostOperationMessage(Guid OperationId, string OperationType, string? DetailsJson);
public sealed record HostOperationReceipt(Guid OperationId, bool Succeeded, string? FailureKind, DateTimeOffset CompletedAt);
