using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed record ExportArtifactUploadRequest(
    Guid CommandId,
    string DeliveryToken,
    long DeclaredSizeBytes,
    string DeclaredSha256,
    Stream Content);

public sealed record ExportArtifactUploadResult(
    Guid ArtifactId,
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTimeOffset ExpiresAt,
    bool AlreadyStored);

public sealed record ExportArtifactDownload(
    PlaybackExportEntity Export,
    ExportArtifactEntity Artifact);

public sealed class PlaybackExportArtifactService(
    PlatformDbContext dbContext,
    LocalExportArtifactStore artifactStore,
    ILogger<PlaybackExportArtifactService> logger)
{
    public async Task<ExportArtifactUploadResult> AcceptUploadAsync(
        Guid workerId,
        Guid playbackExportId,
        ExportArtifactUploadRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(playbackExportId, request);
        var existing = await FindExistingForCurrentDeliveryAsync(workerId, playbackExportId, request, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var stored = await artifactStore.StoreAsync(
            playbackExportId,
            request.Content,
            request.DeclaredSizeBytes,
            request.DeclaredSha256,
            cancellationToken);
        try
        {
            var result = await AttachStoredArtifactAsync(workerId, playbackExportId, request, stored, cancellationToken);
            if (result.AlreadyStored)
            {
                await TryCleanupStoredArtifactAsync(playbackExportId, stored.StorageKey);
            }
            return result;
        }
        catch
        {
            await TryCleanupStoredArtifactAsync(playbackExportId, stored.StorageKey);
            throw;
        }
    }

    public async Task<ExportArtifactDownload?> FindDownloadAsync(Guid playbackExportId, CancellationToken cancellationToken)
    {
        var export = await dbContext.PlaybackExports.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == playbackExportId, cancellationToken);
        if (export is null)
        {
            return null;
        }
        var artifact = await dbContext.ExportArtifacts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PlaybackExportId == playbackExportId && item.DeletedAt == null, cancellationToken);
        return artifact is null ? null : new ExportArtifactDownload(export, artifact);
    }

    public Task<Stream?> OpenReadAsync(ExportArtifactEntity artifact, CancellationToken cancellationToken) =>
        artifactStore.OpenReadAsync(artifact.StorageKey, cancellationToken);

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var expired = await dbContext.ExportArtifacts
            .Where(item => item.DeletedAt == null && item.ExpiresAt <= DateTimeOffset.UtcNow)
            .OrderBy(item => item.ExpiresAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        var deleted = 0;
        foreach (var artifact in expired)
        {
            if (await artifactStore.DeleteAsync(artifact.StorageKey, cancellationToken))
            {
                artifact.DeletedAt = DateTimeOffset.UtcNow;
                deleted++;
            }
        }
        if (deleted > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return deleted;
    }

    private async Task<ExportArtifactUploadResult?> FindExistingForCurrentDeliveryAsync(
        Guid workerId,
        Guid playbackExportId,
        ExportArtifactUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsCurrentDeliveryAsync(workerId, playbackExportId, request, cancellationToken))
        {
            throw new UnauthorizedAccessException("导出文件上传不属于当前 Worker 命令投递。");
        }
        var artifact = await dbContext.ExportArtifacts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PlaybackExportId == playbackExportId, cancellationToken);
        if (artifact is null)
        {
            return null;
        }
        if (artifact.SizeBytes != request.DeclaredSizeBytes || !string.Equals(artifact.Sha256, request.DeclaredSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("该导出任务已关联其他文件，拒绝覆盖。");
        }
        return new ExportArtifactUploadResult(
            artifact.Id,
            artifact.FileName,
            artifact.SizeBytes,
            artifact.Sha256,
            artifact.ExpiresAt,
            AlreadyStored: true);
    }

    private async Task<ExportArtifactUploadResult> AttachStoredArtifactAsync(
        Guid workerId,
        Guid playbackExportId,
        ExportArtifactUploadRequest request,
        StoredExportArtifact stored,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var command = await dbContext.EdgeCommands.FromSqlInterpolated($$"""
            SELECT * FROM edge_commands
            WHERE "Id" = {{request.CommandId}} AND "WorkerId" = {{workerId}}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (!IsCurrentExportCommand(command, playbackExportId, request.DeliveryToken, DateTimeOffset.UtcNow))
        {
            throw new UnauthorizedAccessException("导出文件上传命令已过期或已失效。");
        }

        var existing = await dbContext.ExportArtifacts.SingleOrDefaultAsync(
            item => item.PlaybackExportId == playbackExportId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.SizeBytes != stored.SizeBytes || !string.Equals(existing.Sha256, stored.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("该导出任务已关联其他文件，拒绝覆盖。");
            }
            await transaction.CommitAsync(cancellationToken);
            return new ExportArtifactUploadResult(existing.Id, existing.FileName, existing.SizeBytes, existing.Sha256, existing.ExpiresAt, AlreadyStored: true);
        }

        var export = await dbContext.PlaybackExports.SingleOrDefaultAsync(item => item.Id == playbackExportId, cancellationToken)
            ?? throw new InvalidOperationException("导出任务不存在。");
        var settings = artifactStore.GetSettings();
        var now = DateTimeOffset.UtcNow;
        var artifact = new ExportArtifactEntity
        {
            Id = Guid.NewGuid(),
            PlaybackExportId = playbackExportId,
            StorageKey = stored.StorageKey,
            FileName = $"录像导出-{playbackExportId:N}.mp4",
            ContentType = "video/mp4",
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            CreatedAt = now,
            ExpiresAt = now.AddDays(settings.RetentionDays)
        };
        dbContext.ExportArtifacts.Add(artifact);
        export.Status = PlaybackExportStatus.Completed;
        export.CompletedAt = now;
        export.FailureCode = null;
        export.FailureDetail = null;
        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(),
            Action = "playback_export.artifact.attach",
            ResourceType = "playback_export",
            ResourceId = playbackExportId.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { artifact.Id, artifact.SizeBytes, artifact.Sha256, artifact.ExpiresAt }),
            OccurredAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ExportArtifactUploadResult(artifact.Id, artifact.FileName, artifact.SizeBytes, artifact.Sha256, artifact.ExpiresAt, AlreadyStored: false);
    }

    private async Task TryCleanupStoredArtifactAsync(Guid playbackExportId, string storageKey)
    {
        try
        {
            if (!await artifactStore.DeleteAsync(storageKey, CancellationToken.None))
            {
                logger.LogWarning("录像导出临时工件清理未确认：任务 {PlaybackExportId}。", playbackExportId);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "录像导出临时工件清理失败：任务 {PlaybackExportId}。", playbackExportId);
        }
    }

    private async Task<bool> IsCurrentDeliveryAsync(
        Guid workerId,
        Guid playbackExportId,
        ExportArtifactUploadRequest request,
        CancellationToken cancellationToken)
    {
        var command = await dbContext.EdgeCommands.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == request.CommandId && item.WorkerId == workerId,
            cancellationToken);
        return IsCurrentExportCommand(command, playbackExportId, request.DeliveryToken, DateTimeOffset.UtcNow);
    }

    private static bool IsCurrentExportCommand(
        EdgeCommandEntity? command,
        Guid playbackExportId,
        string deliveryToken,
        DateTimeOffset now) =>
        command is not null &&
        command.CompletedAt is null &&
        command.DeadLetteredAt is null &&
        command.CommandType == EdgeCommandTypes.PluginPlaybackExport &&
        command.AggregateType == PlaybackExportService.AggregateType &&
        command.AggregateId == playbackExportId &&
        command.LockedUntil > now &&
        !string.IsNullOrWhiteSpace(command.LockedBy) &&
        FixedTimeEquals(command.LockedBy, HashToken(deliveryToken));

    private static void ValidateRequest(Guid playbackExportId, ExportArtifactUploadRequest request)
    {
        if (playbackExportId == Guid.Empty || request.CommandId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.DeliveryToken) || request.DeliveryToken.Length > 256 ||
            request.DeclaredSizeBytes < 0 || request.Content is null)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "导出文件上传请求无效。");
        }
    }

    private static string HashToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
