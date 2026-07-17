using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.Api;
using VideoPlatform.Core;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class PublicOfflineDeviceServiceTests
{
    [Fact(DisplayName = "公开掉线设备列表按时长排序并只投影定位所需字段")]
    public async Task ListOrdersByOfflineDurationAndDoesNotExposeSensitiveFields()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.FromHours(8));
        var campus = new RegionEntity { Id = Guid.NewGuid(), Code = "CAMPUS", Name = "园区" };
        var north = new RegionEntity { Id = Guid.NewGuid(), ParentId = campus.Id, Code = "NORTH", Name = "北区" };
        var recorder = CreateRecorder("NVR-NORTH", "北区录像机", DeviceKinds.Recorder, now.AddHours(-3));
        var directCarrier = CreateRecorder("DIRECT-CARRIER", "不应公开的承载设备", DeviceKinds.Camera, now.AddHours(-4));
        var oldestCamera = CreateCamera(recorder.Id, north.Id, "最早掉线摄像头", now.AddHours(-5), "CAM-SECRET-01");
        var recentCamera = CreateCamera(recorder.Id, north.Id, "最近掉线摄像头", now.AddHours(-1), "CAM-SECRET-02");
        var directCamera = CreateCamera(directCarrier.Id, north.Id, "北门直连摄像头", now.AddHours(-2), "CAM-SECRET-03");
        dbContext.Regions.AddRange(campus, north);
        dbContext.Recorders.AddRange(recorder, directCarrier);
        dbContext.Cameras.AddRange(oldestCamera, recentCamera, directCamera);
        dbContext.DeviceWorkerAssignments.Add(new DeviceWorkerAssignmentEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = Guid.NewGuid(),
            RecorderId = recorder.Id,
            DefaultRegionId = campus.Id
        });
        await dbContext.SaveChangesAsync();
        var service = new PublicOfflineDeviceService(dbContext, new FixedTimeProvider(now));

        var result = await service.ListAsync(new PublicOfflineDeviceQuery(null, null, null, 1, 100), CancellationToken.None);

        Assert.Equal(4, result.Total);
        Assert.Equal(
            ["最早掉线摄像头", "北区录像机", "北门直连摄像头", "最近掉线摄像头"],
            result.Items.Select(item => item.Name).ToArray());
        Assert.Equal("园区 / 北区", result.Items[0].Region);
        Assert.Equal(DeviceKinds.Camera, result.Items[0].DeviceType);
        Assert.Equal(18_000, result.Items[0].OfflineDurationSeconds);
        Assert.DoesNotContain(result.Items, item => item.Name == "不应公开的承载设备");

        var serialized = JsonSerializer.Serialize(result);
        Assert.False(serialized.Contains("CAM-SECRET", StringComparison.Ordinal));
        Assert.False(serialized.Contains("10.0.0.1", StringComparison.Ordinal));
        Assert.False(serialized.Contains("credential", StringComparison.OrdinalIgnoreCase));
        Assert.False(serialized.Contains("streaming", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "公开掉线设备列表支持区域、名称、类型筛选和分页上限")]
    public async Task ListFiltersByRegionNameAndDeviceType()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var now = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.FromHours(8));
        var region = new RegionEntity { Id = Guid.NewGuid(), Code = "EAST", Name = "东区" };
        var recorder = CreateRecorder("NVR-EAST", "东区录像机", DeviceKinds.Recorder, now.AddHours(-4));
        dbContext.Regions.Add(region);
        dbContext.Recorders.Add(recorder);
        dbContext.Cameras.AddRange(
            CreateCamera(recorder.Id, region.Id, "东门摄像头", now.AddHours(-3), "CAM-001"),
            CreateCamera(recorder.Id, region.Id, "西门摄像头", now.AddHours(-2), "CAM-002"));
        await dbContext.SaveChangesAsync();
        var service = new PublicOfflineDeviceService(dbContext, new FixedTimeProvider(now));

        var result = await service.ListAsync(new PublicOfflineDeviceQuery("东区", "东门", DeviceKinds.Camera, 1, 1), CancellationToken.None);

        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("东门摄像头", item.Name);
        Assert.Equal(DeviceKinds.Camera, item.DeviceType);
        Assert.Contains("东区", result.Regions);
        Assert.Contains(DeviceKinds.Camera, result.DeviceTypes);
        Assert.True(PublicOfflineDeviceQuery.TryCreate(null, null, DeviceKinds.Camera, 1, 100, out _, out _));
        Assert.False(PublicOfflineDeviceQuery.TryCreate(null, null, "invalid", 1, 100, out _, out _));
        Assert.False(PublicOfflineDeviceQuery.TryCreate(null, null, DeviceKinds.Camera, 1, 101, out _, out _));
    }

    private static RecorderEntity CreateRecorder(string code, string name, string deviceKind, DateTimeOffset offlineSince) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        Vendor = "Generic",
        AdapterType = "test",
        DeviceKind = deviceKind,
        Connectivity = CameraConnectivity.Offline,
        LastStateChangedAt = offlineSince,
        CreatedAt = offlineSince.AddDays(-30)
    };

    private static CameraEntity CreateCamera(Guid recorderId, Guid regionId, string alias, DateTimeOffset offlineSince, string code) => new()
    {
        Id = Guid.NewGuid(),
        RecorderId = recorderId,
        RegionId = regionId,
        Code = code,
        Alias = alias,
        InputChannelNumber = 1,
        StreamingChannelMap = "{\"main\":\"rtsp://10.0.0.1/live\",\"sub\":\"rtsp://10.0.0.1/sub\"}",
        Connectivity = CameraConnectivity.Offline,
        LastStateChangedAt = offlineSince,
        CreatedAt = offlineSince.AddDays(-30)
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
