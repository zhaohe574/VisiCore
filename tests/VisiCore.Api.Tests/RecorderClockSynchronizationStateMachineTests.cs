using VisiCore.Api;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class RecorderClockSynchronizationStateMachineTests
{
    [Fact(DisplayName = "首次和第二次时钟偏差不会立即进入告警状态")]
    public void DriftRequiresConfiguredConsecutiveObservations()
    {
        var firstObservedAt = new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.FromHours(8));
        var first = RecorderClockSynchronizationStateMachine.Observe(
            ClockSynchronization.Unknown, 0, 0, null, false, firstObservedAt, 3);
        var second = RecorderClockSynchronizationStateMachine.Observe(
            first.ClockSynchronization, first.ConsecutiveDrifts, first.ConsecutiveSynchronizations, first.DriftSinceAt,
            false, firstObservedAt.AddMinutes(15), 3);

        Assert.Equal(ClockSynchronization.Unknown, first.ClockSynchronization);
        Assert.Equal(ClockSynchronization.Unknown, second.ClockSynchronization);
        Assert.Equal(2, second.ConsecutiveDrifts);
        Assert.Equal(firstObservedAt, second.DriftSinceAt);
    }

    [Fact(DisplayName = "连续偏差后告警，连续恢复后才解除")]
    public void DriftAndRecoveryUseIndependentConsecutiveCounters()
    {
        var observedAt = new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.FromHours(8));
        var first = RecorderClockSynchronizationStateMachine.Observe(
            ClockSynchronization.Unknown, 0, 0, null, false, observedAt, 3);
        var second = RecorderClockSynchronizationStateMachine.Observe(
            first.ClockSynchronization, first.ConsecutiveDrifts, first.ConsecutiveSynchronizations, first.DriftSinceAt,
            false, observedAt.AddMinutes(15), 3);
        var drifted = RecorderClockSynchronizationStateMachine.Observe(
            second.ClockSynchronization, second.ConsecutiveDrifts, second.ConsecutiveSynchronizations, second.DriftSinceAt,
            false, observedAt.AddMinutes(30), 3);
        var firstRecovery = RecorderClockSynchronizationStateMachine.Observe(
            drifted.ClockSynchronization, drifted.ConsecutiveDrifts, drifted.ConsecutiveSynchronizations, drifted.DriftSinceAt,
            true, observedAt.AddMinutes(45), 3);
        var secondRecovery = RecorderClockSynchronizationStateMachine.Observe(
            firstRecovery.ClockSynchronization, firstRecovery.ConsecutiveDrifts, firstRecovery.ConsecutiveSynchronizations, firstRecovery.DriftSinceAt,
            true, observedAt.AddMinutes(60), 3);
        var synchronized = RecorderClockSynchronizationStateMachine.Observe(
            secondRecovery.ClockSynchronization, secondRecovery.ConsecutiveDrifts, secondRecovery.ConsecutiveSynchronizations, secondRecovery.DriftSinceAt,
            true, observedAt.AddMinutes(75), 3);

        Assert.Equal(ClockSynchronization.Drifted, drifted.ClockSynchronization);
        Assert.Equal(observedAt, drifted.DriftSinceAt);
        Assert.Equal(ClockSynchronization.Drifted, secondRecovery.ClockSynchronization);
        Assert.Equal(ClockSynchronization.Synchronized, synchronized.ClockSynchronization);
        Assert.Null(synchronized.DriftSinceAt);
        Assert.Equal(3, synchronized.ConsecutiveSynchronizations);
    }

    [Fact(DisplayName = "中心同步服务将连续时钟偏差和恢复各写入一次 Outbox")]
    public async Task ClockSyncServiceCreatesTransitionEventsAndHistory()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var recorder = new RecorderEntity
        {
            Id = Guid.NewGuid(),
            Code = "NVR-CLOCK-01",
            Name = "时钟验证录像机",
            Vendor = "Generic",
            AdapterType = "onvif-standard",
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Recorders.Add(recorder);
        await dbContext.SaveChangesAsync();
        var service = new DeviceWorkerSyncService(dbContext);
        var settings = new ClockMonitoringOptions
        {
            MaximumAbsoluteOffsetSeconds = 5,
            RequiredConsecutiveObservations = 3
        };
        var startedAt = new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.FromHours(8));

        for (var index = 0; index < 3; index++)
        {
            await service.ApplyClockAsync(CreateReport(recorder.Id, startedAt.AddMinutes(index * 15), TimeSpan.FromSeconds(6)), settings, CancellationToken.None);
        }

        var drifted = await dbContext.Recorders.SingleAsync(item => item.Id == recorder.Id);
        Assert.Equal(ClockSynchronization.Drifted, drifted.ClockSynchronization);
        Assert.Single(await dbContext.OutboxEvents.Where(item => item.EventType == "clock.synchronization.changed").ToListAsync());

        for (var index = 3; index < 6; index++)
        {
            await service.ApplyClockAsync(CreateReport(recorder.Id, startedAt.AddMinutes(index * 15), TimeSpan.Zero), settings, CancellationToken.None);
        }

        var synchronized = await dbContext.Recorders.SingleAsync(item => item.Id == recorder.Id);
        Assert.Equal(ClockSynchronization.Synchronized, synchronized.ClockSynchronization);
        Assert.Equal(6, await dbContext.RecorderClockObservations.CountAsync());
        Assert.Equal(2, await dbContext.OutboxEvents.CountAsync(item => item.EventType == "clock.synchronization.changed"));
    }

    private static WorkerClockReport CreateReport(Guid recorderId, DateTimeOffset startedAt, TimeSpan deviceOffset)
    {
        var respondedAt = startedAt.AddSeconds(1);
        var midpoint = startedAt.AddMilliseconds(500);
        return new WorkerClockReport(
            recorderId,
            new WorkerClockObservation(midpoint.Add(deviceOffset), startedAt, respondedAt),
            null,
            respondedAt);
    }
}
