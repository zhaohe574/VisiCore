using System.Text.Json;

namespace VisiCore.EdgeAgent;

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

    public async Task<HostVerifiedReleaseArtifact?> GetRollbackArtifactAsync(
        Guid sourceOperationId,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var receipt = (await ReadAsync(cancellationToken))
                .FirstOrDefault(item => item.SourceOperationId == sourceOperationId);
            return receipt is null ||
                   string.IsNullOrWhiteSpace(receipt.PreviousArtifactSha256)
                ? null
                : new HostVerifiedReleaseArtifact(
                    receipt.PreviousArtifactPath,
                    receipt.PreviousComposeFilePath,
                    receipt.PreviousArtifactSha256,
                    receipt.PreviousOciImageReference,
                    receipt.PreviousProductVersion);
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
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var receipts = await ReadAsync(cancellationToken);
            var previous = receipts.OrderByDescending(item => item.VerifiedAt).FirstOrDefault(item => item.IsActive);
            receipts.RemoveAll(item => item.SourceOperationId == sourceOperationId);
            foreach (var item in receipts)
            {
                item.IsActive = false;
            }
            receipts.Add(new VerifiedDeploymentReceipt(
                sourceOperationId,
                releaseId,
                publicKeyId,
                artifact.ArtifactPath,
                artifact.ComposeFilePath,
                artifact.ArtifactSha256,
                artifact.OciImageReference,
                artifact.ProductVersion,
                previous?.ReleaseId,
                previous?.ArtifactPath,
                previous?.ComposeFilePath,
                previous?.ArtifactSha256,
                previous?.OciImageReference,
                previous?.ProductVersion,
                true,
                DateTimeOffset.UtcNow));
            // 保持有限回执，避免宿主状态目录无限增长。
            receipts = receipts.OrderByDescending(item => item.VerifiedAt).Take(100).ToList();
            await WriteAsync(receipts, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> HasActiveArtifactAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(cancellationToken)).Any(item => item.IsActive && !string.IsNullOrWhiteSpace(item.ArtifactSha256));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task EnsureBaselineAsync(
        string releaseId,
        string publicKeyId,
        HostVerifiedReleaseArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (await HasActiveArtifactAsync(cancellationToken))
        {
            return;
        }
        await RememberAsync(Guid.Empty, releaseId, publicKeyId, artifact, cancellationToken);
    }

    public async Task ActivateRollbackAsync(Guid sourceOperationId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var receipts = await ReadAsync(cancellationToken);
            var source = receipts.FirstOrDefault(item => item.SourceOperationId == sourceOperationId);
            if (source is null || string.IsNullOrWhiteSpace(source.PreviousArtifactSha256))
            {
                return;
            }
            foreach (var item in receipts)
            {
                item.IsActive = string.Equals(item.ArtifactSha256, source.PreviousArtifactSha256, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(item.ArtifactPath, source.PreviousArtifactPath, StringComparison.Ordinal) &&
                                string.Equals(item.OciImageReference, source.PreviousOciImageReference, StringComparison.Ordinal);
            }
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

public sealed class VerifiedDeploymentReceipt
{
    public VerifiedDeploymentReceipt(
        Guid sourceOperationId,
        string releaseId,
        string publicKeyId,
        string? artifactPath,
        string? composeFilePath,
        string? artifactSha256,
        string? ociImageReference,
        string? productVersion,
        string? previousReleaseId,
        string? previousArtifactPath,
        string? previousComposeFilePath,
        string? previousArtifactSha256,
        string? previousOciImageReference,
        string? previousProductVersion,
        bool isActive,
        DateTimeOffset verifiedAt)
    {
        SourceOperationId = sourceOperationId;
        ReleaseId = releaseId;
        PublicKeyId = publicKeyId;
        ArtifactPath = artifactPath;
        ComposeFilePath = composeFilePath;
        ArtifactSha256 = artifactSha256;
        OciImageReference = ociImageReference;
        ProductVersion = productVersion;
        PreviousReleaseId = previousReleaseId;
        PreviousArtifactPath = previousArtifactPath;
        PreviousComposeFilePath = previousComposeFilePath;
        PreviousArtifactSha256 = previousArtifactSha256;
        PreviousOciImageReference = previousOciImageReference;
        PreviousProductVersion = previousProductVersion;
        IsActive = isActive;
        VerifiedAt = verifiedAt;
    }

    public Guid SourceOperationId { get; set; }
    public string ReleaseId { get; set; } = string.Empty;
    public string PublicKeyId { get; set; } = string.Empty;
    public string? ArtifactPath { get; set; }
    public string? ComposeFilePath { get; set; }
    public string? ArtifactSha256 { get; set; }
    public string? OciImageReference { get; set; }
    public string? ProductVersion { get; set; }
    public string? PreviousReleaseId { get; set; }
    public string? PreviousArtifactPath { get; set; }
    public string? PreviousComposeFilePath { get; set; }
    public string? PreviousArtifactSha256 { get; set; }
    public string? PreviousOciImageReference { get; set; }
    public string? PreviousProductVersion { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
}
