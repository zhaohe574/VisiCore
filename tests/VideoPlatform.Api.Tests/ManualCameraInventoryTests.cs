using Microsoft.EntityFrameworkCore;
using VideoPlatform.Api;
using VideoPlatform.Core;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class ManualCameraInventoryTests
{
    [Fact(DisplayName = "自动发现不会覆盖人工维护的码流和 PTZ 设置")]
    public async Task InventoryPreservesManualCameraConfiguration()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var recorderId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var camera = new CameraEntity
        {
            Id = Guid.NewGuid(), RecorderId = recorderId, RegionId = regionId, Code = "CAM-MANUAL", Alias = "人工通道",
            InputChannelNumber = 1, StreamingChannelMap = "{\"main\":\"/manual-main\",\"sub\":\"/manual-sub\"}",
            SourceType = CameraSourceTypes.RecorderChannel, ProvisioningMode = CameraProvisioningModes.Manual,
            SupportsPtz = false, CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.Regions.Add(new RegionEntity { Id = regionId, Code = "ROOT", Name = "根区域" });
        dbContext.Recorders.Add(new RecorderEntity
        {
            Id = recorderId, Code = "NVR-01", Name = "录像机", Vendor = "任意品牌", AdapterType = "custom",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.DeviceWorkers.Add(new DeviceWorkerEntity
        {
            Id = workerId, Name = "worker", TokenHash = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.DeviceWorkerAssignments.Add(new DeviceWorkerAssignmentEntity
        {
            Id = Guid.NewGuid(), WorkerId = workerId, RecorderId = recorderId, DefaultRegionId = regionId
        });
        dbContext.Cameras.Add(camera);
        await dbContext.SaveChangesAsync();

        var service = new DeviceWorkerSyncService(dbContext);
        await service.ApplyInventoryAsync(new WorkerInventoryReport(
            recorderId,
            new RecorderCapabilities(
                CapabilityState.Supported, CapabilityState.Unsupported, CapabilityState.Supported,
                CapabilityState.Supported, CapabilityState.Unsupported, CapabilityState.Unsupported,
                CapabilityState.Unsupported, "test-v1"),
            [new WorkerCameraInventory(1, "自动发现名称", true, "{\"main\":101,\"sub\":102}")],
            DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.False(camera.SupportsPtz);
        Assert.Equal("{\"main\":\"/manual-main\",\"sub\":\"/manual-sub\"}", camera.StreamingChannelMap);
    }
}
