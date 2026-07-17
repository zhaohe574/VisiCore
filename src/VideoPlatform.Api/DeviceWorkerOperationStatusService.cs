using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class DeviceWorkerOperationStatusService(PlatformDbContext dbContext)
{
    private static readonly Regex FailureKindPattern = new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant);

    public async Task ApplyAsync(
        Guid workerId,
        WorkerOperationStatusReport report,
        CancellationToken cancellationToken)
    {
        Validate(report);
        await EnsureRecorderAssignmentsAsync(workerId, report.Operations, cancellationToken);
        var reportedAt = DateTimeOffset.UtcNow;
        if (string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            await ApplyPostgreSqlAsync(workerId, report.Operations, reportedAt, cancellationToken);
            return;
        }

        var recorderIds = report.Operations.Select(item => item.RecorderId).Distinct().ToArray();
        var operationTypes = report.Operations.Select(item => item.OperationType).Distinct(StringComparer.Ordinal).ToArray();
        var existing = await dbContext.DeviceWorkerOperationStatuses
            .Where(item => item.WorkerId == workerId && recorderIds.Contains(item.RecorderId) && operationTypes.Contains(item.OperationType))
            .ToDictionaryAsync(item => new OperationStatusKey(item.RecorderId, item.OperationType), cancellationToken);
        foreach (var status in report.Operations)
        {
            var key = new OperationStatusKey(status.RecorderId, status.OperationType);
            if (existing.TryGetValue(key, out var entity))
            {
                entity.IsReady = status.IsReady;
                entity.FailureKind = status.FailureKind;
                entity.ReportedAt = reportedAt;
                continue;
            }

            dbContext.DeviceWorkerOperationStatuses.Add(new DeviceWorkerOperationStatusEntity
            {
                Id = Guid.NewGuid(),
                WorkerId = workerId,
                RecorderId = status.RecorderId,
                OperationType = status.OperationType,
                IsReady = status.IsReady,
                FailureKind = status.FailureKind,
                ReportedAt = reportedAt
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyPostgreSqlAsync(
        Guid workerId,
        IReadOnlyList<WorkerOperationStatus> statuses,
        DateTimeOffset reportedAt,
        CancellationToken cancellationToken)
    {
        foreach (var status in statuses)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO device_worker_operation_statuses
                    ("Id", "WorkerId", "RecorderId", "OperationType", "IsReady", "FailureKind", "ReportedAt")
                VALUES
                    ({{Guid.NewGuid()}}, {{workerId}}, {{status.RecorderId}}, {{status.OperationType}}, {{status.IsReady}}, {{status.FailureKind}}, {{reportedAt}})
                ON CONFLICT ("WorkerId", "RecorderId", "OperationType") DO UPDATE
                SET "IsReady" = EXCLUDED."IsReady",
                    "FailureKind" = EXCLUDED."FailureKind",
                    "ReportedAt" = GREATEST(device_worker_operation_statuses."ReportedAt", EXCLUDED."ReportedAt")
                """, cancellationToken);
        }
    }

    private async Task EnsureRecorderAssignmentsAsync(
        Guid workerId,
        IReadOnlyList<WorkerOperationStatus> statuses,
        CancellationToken cancellationToken)
    {
        var recorderIds = statuses.Select(item => item.RecorderId).Distinct().ToArray();
        var assignedRecorderIds = await dbContext.DeviceWorkerAssignments.AsNoTracking()
            .Where(item => item.WorkerId == workerId && recorderIds.Contains(item.RecorderId))
            .Select(item => item.RecorderId)
            .ToListAsync(cancellationToken);
        if (assignedRecorderIds.Count != recorderIds.Length)
        {
            throw new ArgumentException("边缘 Worker 不能上报未分配录像机的运行态。", nameof(statuses));
        }
    }

    private static void Validate(WorkerOperationStatusReport report)
    {
        if (report.Operations is null || report.Operations.Count is < 1 or > 512 ||
            report.Operations.Any(item => item is null || item.RecorderId == Guid.Empty || !EdgeOperationTypes.KnownTypes.Contains(item.OperationType)) ||
            report.Operations.Select(item => new OperationStatusKey(item.RecorderId, item.OperationType)).Distinct().Count() != report.Operations.Count ||
            report.Operations.Any(item => item.IsReady
                ? item.FailureKind is not null
                : string.IsNullOrWhiteSpace(item.FailureKind) || !FailureKindPattern.IsMatch(item.FailureKind)))
        {
            throw new ArgumentException("边缘运行态上报内容无效。", nameof(report));
        }
    }

    private readonly record struct OperationStatusKey(Guid RecorderId, string OperationType);
}
