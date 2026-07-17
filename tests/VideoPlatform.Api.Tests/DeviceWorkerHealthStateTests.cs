using Microsoft.EntityFrameworkCore;
using VideoPlatform.Api;
using VideoPlatform.Core;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class DeviceWorkerHealthStateTests
{
    [Fact(DisplayName = "设备健康报告只更新实际包含的摄像机通道")]
    public async Task HealthReportUpdatesKnownChannelAndPreservesUnobservedChannel()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var recorder = new RecorderEntity
        {
            Id = Guid.NewGuid(),
            Code = "NVR-HEALTH",
            Name = "健康状态测试录像机",
            Vendor = "Generic",
            AdapterType = "onvif-standard",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var observedCamera = CreateCamera(recorder.Id, 1, "CAM-OBSERVED");
        var unobservedCamera = CreateCamera(recorder.Id, 2, "CAM-UNOBSERVED");
        var observedCameraId = observedCamera.Id;
        var unobservedCameraId = unobservedCamera.Id;
        var recorderId = recorder.Id;
        dbContext.Recorders.Add(recorder);
        dbContext.Cameras.AddRange(observedCamera, unobservedCamera);
        await dbContext.SaveChangesAsync();
        var observedAt = DateTimeOffset.UtcNow;

        var service = new DeviceWorkerSyncService(dbContext);
        await service.ApplyHealthAsync(
            new WorkerHealthReport(
                recorder.Id,
                true,
                new Dictionary<int, bool> { [1] = true },
                null,
                observedAt),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var persistedRecorder = await dbContext.Recorders.SingleAsync(item => item.Id == recorderId);
        var persistedObserved = await dbContext.Cameras.SingleAsync(item => item.Id == observedCameraId);
        var persistedUnobserved = await dbContext.Cameras.SingleAsync(item => item.Id == unobservedCameraId);
        Assert.Equal(CameraConnectivity.Online, persistedRecorder.Connectivity);
        Assert.Equal(observedAt, persistedRecorder.LastVerifiedAt);
        Assert.Equal(CameraConnectivity.Online, persistedObserved.Connectivity);
        Assert.Equal(observedAt, persistedObserved.LastVerifiedAt);
        Assert.Equal(CameraConnectivity.Unknown, persistedUnobserved.Connectivity);
        Assert.Null(persistedUnobserved.LastVerifiedAt);
        Assert.Equal(2, await dbContext.HealthStateEvents.CountAsync());
        Assert.Equal(2, await dbContext.OutboxEvents.CountAsync(item => item.EventType == "health.state.changed"));
        Assert.False(await dbContext.HealthStateEvents.AnyAsync(item => item.ResourceId == unobservedCameraId));
        Assert.False(await dbContext.OutboxEvents.AnyAsync(item => item.AggregateId == unobservedCameraId));
    }

    [Fact(DisplayName = "直连摄像头健康状态归属到摄像头且不产生承载设备重复告警")]
    public async Task DirectCameraHealthIsTrackedOnCameraWithoutTechnicalRecorderEvent()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var recorder = new RecorderEntity
        {
            Id = Guid.NewGuid(),
            Code = "DIRECT-CARRIER",
            Name = "直连摄像头承载设备",
            Vendor = "Generic",
            AdapterType = "direct-rtsp",
            DeviceKind = DeviceKinds.Camera,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var camera = CreateCamera(recorder.Id, 1, "DIRECT-CAMERA");
        var startedAt = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.FromHours(8));
        dbContext.Recorders.Add(recorder);
        dbContext.Cameras.Add(camera);
        await dbContext.SaveChangesAsync();
        var service = new DeviceWorkerSyncService(dbContext);

        foreach (var observedAt in new[]
                 {
                     startedAt,
                     startedAt.AddSeconds(30),
                     startedAt.AddMinutes(1),
                     startedAt.AddMinutes(3)
                 })
        {
            await service.ApplyHealthAsync(
                new WorkerHealthReport(recorder.Id, false, new Dictionary<int, bool>(), "SocketException", observedAt),
                CancellationToken.None);
        }

        dbContext.ChangeTracker.Clear();
        var persistedCamera = await dbContext.Cameras.SingleAsync(item => item.Id == camera.Id);
        var persistedRecorder = await dbContext.Recorders.SingleAsync(item => item.Id == recorder.Id);
        Assert.Equal(CameraConnectivity.Offline, persistedCamera.Connectivity);
        Assert.Equal(CameraConnectivity.Unknown, persistedRecorder.Connectivity);
        Assert.Equal(2, await dbContext.HealthStateEvents.CountAsync(item => item.ResourceId == camera.Id));
        Assert.Equal(0, await dbContext.HealthStateEvents.CountAsync(item => item.ResourceId == recorder.Id));
        Assert.Equal(2, await dbContext.OutboxEvents.CountAsync(item => item.AggregateId == camera.Id && item.EventType == "health.state.changed"));
        Assert.Equal(0, await dbContext.OutboxEvents.CountAsync(item => item.AggregateId == recorder.Id));
    }

    private static CameraEntity CreateCamera(Guid recorderId, int channelNumber, string code) => new()
    {
        Id = Guid.NewGuid(),
        RecorderId = recorderId,
        RegionId = Guid.NewGuid(),
        Code = code,
        Alias = code,
        InputChannelNumber = channelNumber,
        Connectivity = CameraConnectivity.Unknown
    };
}
