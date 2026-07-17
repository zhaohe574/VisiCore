using Microsoft.EntityFrameworkCore;
using VideoPlatform.Api;
using VideoPlatform.Core;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.IntegrationTests;

public sealed class DeviceWorkerOperationStatusPostgreSqlTests(StreamSessionPostgreSqlFixture fixture)
    : IClassFixture<StreamSessionPostgreSqlFixture>
{
    [Fact(DisplayName = "并发边缘运行态上报会原子合并为单条状态")]
    public async Task ConcurrentReportsUpsertSingleStatus()
    {
        async Task ReportAsync()
        {
            await using var dbContext = fixture.CreateDbContext();
            await new DeviceWorkerOperationStatusService(dbContext).ApplyAsync(
                fixture.WorkerId,
                new WorkerOperationStatusReport(
                [
                    new WorkerOperationStatus(EdgeOperationTypes.OnvifPtz, false, "validation_required", fixture.RecorderId)
                ]),
                CancellationToken.None);
        }

        await Task.WhenAll(ReportAsync(), ReportAsync());

        await using var verification = fixture.CreateDbContext();
        var status = await verification.DeviceWorkerOperationStatuses
            .Where(item => item.WorkerId == fixture.WorkerId && item.RecorderId == fixture.RecorderId && item.OperationType == EdgeOperationTypes.OnvifPtz)
            .ToListAsync();
        var stored = Assert.Single(status);
        Assert.False(stored.IsReady);
        Assert.Equal("validation_required", stored.FailureKind);
        Assert.True(stored.ReportedAt <= DateTimeOffset.UtcNow);
    }
}
