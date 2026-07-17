using System.Text.Json;

namespace VideoPlatform.EdgeAgent;

/// <summary>
/// 仅保存已完成部署的非敏感验证回执，使回滚可以绑定到本机实际验证过的发行。
/// 不保存签名、清单、令牌、凭据或任何命令。
/// </summary>
public sealed class VerifiedDeploymentStore(HostAgentOptions options)
{
    private const string FileName = "verified-deployments.json";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<bool> ContainsAsync(Guid sourceOperationId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken)).Any(item => item.SourceOperationId == sourceOperationId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RememberAsync(
        Guid sourceOperationId,
        string releaseId,
        string publicKeyId,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var receipts = await ReadAsync(cancellationToken);
            receipts.RemoveAll(item => item.SourceOperationId == sourceOperationId);
            receipts.Add(new VerifiedDeploymentReceipt(sourceOperationId, releaseId, publicKeyId, DateTimeOffset.UtcNow));
            // 保持有限回执，避免宿主状态目录无限增长。
            receipts = receipts.OrderByDescending(item => item.VerifiedAt).Take(100).ToList();
            await WriteAsync(receipts, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<VerifiedDeploymentReceipt>> ReadAsync(CancellationToken cancellationToken)
    {
        var path = GetPath();
        if (!File.Exists(path))
        {
            return [];
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<List<VerifiedDeploymentReceipt>>(content) ?? [];
    }

    private async Task WriteAsync(IReadOnlyList<VerifiedDeploymentReceipt> receipts, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(options.OperationStateDirectory);
        Directory.CreateDirectory(directory);
        var path = GetPath();
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(receipts), cancellationToken);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
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

    private string GetPath() => Path.Combine(Path.GetFullPath(options.OperationStateDirectory), FileName);
}

public sealed record VerifiedDeploymentReceipt(
    Guid SourceOperationId,
    string ReleaseId,
    string PublicKeyId,
    DateTimeOffset VerifiedAt);
