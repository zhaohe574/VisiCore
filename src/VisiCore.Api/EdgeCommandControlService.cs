using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class EdgeCommandControlService(PlatformDbContext dbContext)
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 32
    };

    public async Task<IReadOnlyList<WorkerEdgeCommand>> ClaimAsync(
        Guid workerId,
        int requestedLimit,
        IReadOnlyCollection<string>? allowedCommandTypes,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(requestedLimit, 1, 50);
        var now = DateTimeOffset.UtcNow;
        var commandTypes = allowedCommandTypes is null
            ? Array.Empty<string>()
            : allowedCommandTypes.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var workerParameter = new NpgsqlParameter("workerId", NpgsqlDbType.Uuid) { Value = workerId };
        var nowParameter = new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now };
        var commandTypesParameter = new NpgsqlParameter("commandTypes", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = commandTypes };
        var limitParameter = new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit };
        var commands = await dbContext.EdgeCommands.FromSqlRaw("""
            SELECT * FROM edge_commands
            WHERE "WorkerId" = @workerId
              AND "CompletedAt" IS NULL
              AND "DeadLetteredAt" IS NULL
              AND "NextAttemptAt" <= @now
              AND ("LockedUntil" IS NULL OR "LockedUntil" <= @now)
              AND (cardinality(@commandTypes) = 0 OR "CommandType" = ANY(@commandTypes))
            ORDER BY "NextAttemptAt", "CreatedAt"
            FOR UPDATE SKIP LOCKED
            LIMIT @limit
            """, workerParameter, nowParameter, commandTypesParameter, limitParameter).ToListAsync(cancellationToken);

        var result = new List<WorkerEdgeCommand>(commands.Count);
        foreach (var command in commands)
        {
            var deliveryToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            command.Attempts++;
            command.LockedBy = HashToken(deliveryToken);
            command.LockedUntil = now.Add(GetDeliveryLifetime(command.CommandType));
            await MarkPlaybackExportClaimedAsync(command, now, cancellationToken);
            result.Add(new WorkerEdgeCommand(
                command.Id,
                command.RecorderId,
                command.CommandType,
                command.AggregateType,
                command.AggregateId,
                command.PayloadJson,
                command.Attempts,
                command.CreatedAt,
                command.LockedUntil.Value,
                deliveryToken));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<EdgeCommandCompletionResult> CompleteAsync(
        Guid workerId,
        Guid commandId,
        WorkerEdgeCommandCompletion completion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(completion.DeliveryToken) || completion.DeliveryToken.Length > 256 ||
            !IsValidResultJson(completion.ResultJson) ||
            completion.FailureKind?.Length > 64)
        {
            return EdgeCommandCompletionResult.Invalid;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var command = await dbContext.EdgeCommands.FromSqlInterpolated($$"""
            SELECT * FROM edge_commands
            WHERE "Id" = {{commandId}} AND "WorkerId" = {{workerId}}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (command is null)
        {
            return EdgeCommandCompletionResult.NotFound;
        }
        if (command.CompletedAt is not null || command.DeadLetteredAt is not null)
        {
            return EdgeCommandCompletionResult.AlreadyCompleted;
        }

        var now = DateTimeOffset.UtcNow;
        if (command.LockedUntil <= now || string.IsNullOrWhiteSpace(command.LockedBy) ||
            !FixedTimeEquals(command.LockedBy, HashToken(completion.DeliveryToken)))
        {
            return EdgeCommandCompletionResult.StaleDelivery;
        }

        command.LockedBy = null;
        command.LockedUntil = null;
        if (completion.Succeeded)
        {
            command.CompletedAt = now;
            command.ResultJson = completion.ResultJson ?? "{}";
            command.LastError = null;
            await ApplyRecordingSearchCompletionAsync(command, true, completion.ResultJson, null, now, cancellationToken);
            await ApplyPlaybackExportCompletionAsync(command, true, null, now, cancellationToken);
        }
        else if (command.Attempts >= 10)
        {
            command.DeadLetteredAt = now;
            command.LastError = NormalizeFailureKind(completion.FailureKind);
            await ApplyRecordingSearchCompletionAsync(command, false, null, command.LastError, now, cancellationToken);
            await ApplyPlaybackExportCompletionAsync(command, false, command.LastError, now, cancellationToken);
        }
        else
        {
            command.NextAttemptAt = now.AddSeconds(Math.Min(300, Math.Pow(2, command.Attempts)));
            command.LastError = NormalizeFailureKind(completion.FailureKind);
            await ApplyPlaybackExportCompletionAsync(command, false, command.LastError, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return completion.Succeeded
            ? EdgeCommandCompletionResult.Completed
            : command.DeadLetteredAt is null
                ? EdgeCommandCompletionResult.RetryScheduled
                : EdgeCommandCompletionResult.DeadLettered;
    }

    public async Task<bool> CanStartPlaybackRelayAsync(
        Guid workerId,
        Guid commandId,
        Guid playbackSessionId,
        string deliveryToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deliveryToken) || deliveryToken.Length > 256)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var command = await dbContext.EdgeCommands.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.Id == commandId &&
                item.WorkerId == workerId &&
                item.AggregateType == StreamSessionOrchestrator.PlaybackRelayAggregateType &&
                item.AggregateId == playbackSessionId &&
                (item.CommandType == EdgeCommandTypes.OnvifPlaybackRelayStart ||
                 item.CommandType == EdgeCommandTypes.PluginPlaybackRelayStart),
                cancellationToken);
        if (command is null || command.CompletedAt is not null || command.DeadLetteredAt is not null ||
            command.LockedUntil <= now || string.IsNullOrWhiteSpace(command.LockedBy) ||
            !FixedTimeEquals(command.LockedBy, HashToken(deliveryToken)))
        {
            return false;
        }

        return await dbContext.StreamSessions.AsNoTracking().AnyAsync(item =>
            item.Id == playbackSessionId &&
            item.Operation == CameraPermission.Playback &&
            item.RevokedAt == null &&
            item.ExpiresAt > now,
            cancellationToken);
    }

    public async Task<bool> CanContinuePlaybackExportAsync(
        Guid workerId,
        Guid commandId,
        Guid playbackExportId,
        string deliveryToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deliveryToken) || deliveryToken.Length > 256)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var command = await dbContext.EdgeCommands.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == commandId &&
            item.WorkerId == workerId &&
            item.CommandType == EdgeCommandTypes.PluginPlaybackExport &&
            item.AggregateType == PlaybackExportService.AggregateType &&
            item.AggregateId == playbackExportId,
            cancellationToken);
        if (command is null || command.CompletedAt is not null || command.DeadLetteredAt is not null ||
            command.LockedUntil <= now || string.IsNullOrWhiteSpace(command.LockedBy) ||
            !FixedTimeEquals(command.LockedBy, HashToken(deliveryToken)))
        {
            return false;
        }

        return await dbContext.PlaybackExports.AsNoTracking().AnyAsync(item =>
            item.Id == playbackExportId &&
            (item.Status == PlaybackExportStatus.Queued || item.Status == PlaybackExportStatus.Running) &&
            item.CancellationRequestedAt == null,
            cancellationToken);
    }

    public async Task<bool> CanContinuePlaybackRelayAsync(
        Guid workerId,
        Guid playbackSessionId,
        CancellationToken cancellationToken)
    {
        if (workerId == Guid.Empty || playbackSessionId == Guid.Empty)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var startedByWorker = await dbContext.EdgeCommands.AsNoTracking().AnyAsync(item =>
            item.WorkerId == workerId &&
            item.AggregateType == StreamSessionOrchestrator.PlaybackRelayAggregateType &&
            item.AggregateId == playbackSessionId &&
            (item.CommandType == EdgeCommandTypes.OnvifPlaybackRelayStart ||
             item.CommandType == EdgeCommandTypes.PluginPlaybackRelayStart) &&
            item.CompletedAt != null &&
            item.DeadLetteredAt == null &&
            item.LastError == null,
            cancellationToken);
        if (!startedByWorker)
        {
            return false;
        }

        return await dbContext.StreamSessions.AsNoTracking().AnyAsync(item =>
            item.Id == playbackSessionId &&
            item.Operation == CameraPermission.Playback &&
            item.RevokedAt == null &&
            item.ExpiresAt > now,
            cancellationToken);
    }

    public async Task<PlaybackExportCancellationResult> CancelPlaybackExportAsync(
        Guid playbackExportId,
        CancellationToken cancellationToken)
    {
        return await CancelPlaybackExportAsync(
            playbackExportId,
            "cancelled_by_operator",
            "管理端已取消录像导出任务。",
            cancellationToken);
    }

    public async Task<PlaybackExportCancellationResult> CancelPlaybackExportAsync(
        Guid playbackExportId,
        string cancellationCode,
        string cancellationDetail,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cancellationCode) || cancellationCode.Length > 64 ||
            string.IsNullOrWhiteSpace(cancellationDetail) || cancellationDetail.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(cancellationCode), "录像导出取消原因无效。");
        }
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var command = await dbContext.EdgeCommands.FromSqlInterpolated($$"""
            SELECT * FROM edge_commands
            WHERE "AggregateType" = {{PlaybackExportService.AggregateType}}
              AND "AggregateId" = {{playbackExportId}}
              AND "CommandType" = {{EdgeCommandTypes.PluginPlaybackExport}}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        var export = await dbContext.PlaybackExports.FromSqlInterpolated($$"""
            SELECT * FROM playback_exports
            WHERE "Id" = {{playbackExportId}}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (export is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return PlaybackExportCancellationResult.NotFound;
        }
        if (export.Status is PlaybackExportStatus.Completed or PlaybackExportStatus.Failed or PlaybackExportStatus.Cancelled or PlaybackExportStatus.Expired)
        {
            await transaction.CommitAsync(cancellationToken);
            return PlaybackExportCancellationResult.NotCancellable;
        }

        var now = DateTimeOffset.UtcNow;
        export.Status = PlaybackExportStatus.Cancelled;
        export.CancellationRequestedAt = now;
        export.CompletedAt = now;
        export.FailureCode = cancellationCode;
        export.FailureDetail = cancellationDetail;
        if (command is not null && command.CompletedAt is null && command.DeadLetteredAt is null)
        {
            command.DeadLetteredAt = now;
            command.LastError = "export_cancelled";
            command.LockedBy = null;
            command.LockedUntil = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PlaybackExportCancellationResult.Cancelled;
    }

    private static bool IsValidResultJson(string? value)
    {
        if (value is null)
        {
            return true;
        }
        if (Encoding.UTF8.GetByteCount(value) > 64 * 1024)
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(value, JsonOptions);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeFailureKind(string? failureKind) =>
        string.IsNullOrWhiteSpace(failureKind) ? "unspecified_failure" : failureKind.Trim();

    private static TimeSpan GetDeliveryLifetime(string commandType) =>
        string.Equals(commandType, EdgeCommandTypes.PluginPlaybackExport, StringComparison.Ordinal)
            ? TimeSpan.FromHours(12)
            : TimeSpan.FromSeconds(60);

    private static string HashToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private async Task ApplyRecordingSearchCompletionAsync(
        EdgeCommandEntity command,
        bool succeeded,
        string? resultJson,
        string? failureKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.AggregateType, RecordingSearchService.AggregateType, StringComparison.Ordinal))
        {
            return;
        }

        var search = await dbContext.RecordingSearches.SingleOrDefaultAsync(
            item => item.Id == command.AggregateId,
            cancellationToken);
        if (search is null || search.ExpiresAt <= now || search.Status == RecordingSearchStatus.Expired)
        {
            return;
        }

        search.CompletedAt = now;
        search.Status = succeeded ? RecordingSearchStatus.Completed : RecordingSearchStatus.Failed;
        search.ResultJson = succeeded ? resultJson : null;
        search.FailureKind = succeeded ? null : failureKind;
    }

    private async Task MarkPlaybackExportClaimedAsync(
        EdgeCommandEntity command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.AggregateType, PlaybackExportService.AggregateType, StringComparison.Ordinal) ||
            !string.Equals(command.CommandType, EdgeCommandTypes.PluginPlaybackExport, StringComparison.Ordinal))
        {
            return;
        }
        var export = await dbContext.PlaybackExports.SingleOrDefaultAsync(item => item.Id == command.AggregateId, cancellationToken);
        if (export is null || export.Status is PlaybackExportStatus.Completed or PlaybackExportStatus.Cancelled or PlaybackExportStatus.Expired)
        {
            return;
        }
        export.Status = PlaybackExportStatus.Running;
        export.ProcessingStartedAt ??= now;
        export.Attempts = command.Attempts;
        export.FailureCode = null;
        export.FailureDetail = null;
    }

    private async Task ApplyPlaybackExportCompletionAsync(
        EdgeCommandEntity command,
        bool succeeded,
        string? failureKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.AggregateType, PlaybackExportService.AggregateType, StringComparison.Ordinal) ||
            !string.Equals(command.CommandType, EdgeCommandTypes.PluginPlaybackExport, StringComparison.Ordinal))
        {
            return;
        }
        var export = await dbContext.PlaybackExports.SingleOrDefaultAsync(item => item.Id == command.AggregateId, cancellationToken);
        if (export is null || export.Status is PlaybackExportStatus.Cancelled or PlaybackExportStatus.Expired)
        {
            return;
        }
        export.Attempts = command.Attempts;
        if (succeeded)
        {
            var hasArtifact = await dbContext.ExportArtifacts.AnyAsync(item => item.PlaybackExportId == export.Id && item.DeletedAt == null, cancellationToken);
            if (hasArtifact)
            {
                export.Status = PlaybackExportStatus.Completed;
                export.CompletedAt ??= now;
                export.FailureCode = null;
                export.FailureDetail = null;
            }
            else
            {
                export.Status = PlaybackExportStatus.Failed;
                export.CompletedAt = now;
                export.FailureCode = "artifact_missing";
                export.FailureDetail = "边缘 Worker 未上传经过校验的导出文件。";
            }
            return;
        }

        export.FailureCode = failureKind;
        export.FailureDetail = command.DeadLetteredAt is null
            ? "边缘导出失败，任务将在退避后重试。"
            : "边缘导出在最大重试次数后失败。";
        if (command.DeadLetteredAt is null)
        {
            export.Status = PlaybackExportStatus.Queued;
            export.NextAttemptAt = command.NextAttemptAt;
        }
        else
        {
            export.Status = PlaybackExportStatus.Failed;
            export.CompletedAt = now;
        }
    }
}

public enum EdgeCommandCompletionResult
{
    Invalid,
    NotFound,
    StaleDelivery,
    AlreadyCompleted,
    Completed,
    RetryScheduled,
    DeadLettered
}

public enum PlaybackExportCancellationResult
{
    NotFound,
    NotCancellable,
    Cancelled
}
