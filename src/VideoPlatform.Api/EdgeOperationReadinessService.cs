using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class EdgeOperationReadinessService(
    PlatformDbContext dbContext,
    IOptions<EdgeOperationReadinessOptions> options)
{
    public async Task<Guid?> GetReadyWorkerIdAsync(
        Guid recorderId,
        string operationType,
        CancellationToken cancellationToken)
    {
        if (!EdgeOperationTypes.KnownTypes.Contains(operationType))
        {
            throw new ArgumentOutOfRangeException(nameof(operationType), "未登记的边缘操作类型。");
        }

        var validAfter = DateTimeOffset.UtcNow.AddSeconds(-options.Value.MaximumStatusAgeSeconds);
        return await dbContext.DeviceWorkerAssignments.AsNoTracking()
            .Where(item => item.RecorderId == recorderId)
            .Join(
                dbContext.DeviceWorkers.AsNoTracking().Where(item => item.DisabledAt == null && item.LastSeenAt >= validAfter),
                assignment => assignment.WorkerId,
                worker => worker.Id,
                (assignment, _) => assignment)
            .Join(
                dbContext.DeviceWorkerOperationStatuses.AsNoTracking().Where(item =>
                    item.OperationType == operationType && item.IsReady && item.ReportedAt >= validAfter),
                assignment => new { assignment.WorkerId, assignment.RecorderId },
                status => new { status.WorkerId, status.RecorderId },
                (assignment, _) => (Guid?)assignment.WorkerId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetReadyRecorderIdsAsync(
        IEnumerable<Guid> recorderIds,
        string operationType,
        CancellationToken cancellationToken)
    {
        if (!EdgeOperationTypes.KnownTypes.Contains(operationType))
        {
            throw new ArgumentOutOfRangeException(nameof(operationType), "未登记的边缘操作类型。");
        }
        var candidates = recorderIds.Where(item => item != Guid.Empty).Distinct().ToArray();
        if (candidates.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var validAfter = DateTimeOffset.UtcNow.AddSeconds(-options.Value.MaximumStatusAgeSeconds);
        var readyRecorderIds = await dbContext.DeviceWorkerAssignments.AsNoTracking()
            .Where(item => candidates.Contains(item.RecorderId))
            .Join(
                dbContext.DeviceWorkers.AsNoTracking().Where(item => item.DisabledAt == null && item.LastSeenAt >= validAfter),
                assignment => assignment.WorkerId,
                worker => worker.Id,
                (assignment, _) => assignment)
            .Join(
                dbContext.DeviceWorkerOperationStatuses.AsNoTracking().Where(item =>
                    item.OperationType == operationType && item.IsReady && item.ReportedAt >= validAfter),
                assignment => new { assignment.WorkerId, assignment.RecorderId },
                status => new { status.WorkerId, status.RecorderId },
                (assignment, _) => assignment.RecorderId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return readyRecorderIds.ToHashSet();
    }
}
