using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VisiCore.Api;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class DeviceWorkerAccessServiceTests
{
    [Fact(DisplayName = "无效录像机凭据引用不会阻塞同一 Worker 的有效分配")]
    public async Task InvalidRecorderCredentialDoesNotBlockValidAssignment()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var worker = new DeviceWorkerEntity
        {
            Id = Guid.NewGuid(),
            Name = "credential-isolation-worker",
            TokenHash = "credential-isolation-token",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var validRecorder = CreateRecorder("VALID-RECORDER");
        var invalidRecorder = CreateRecorder("INVALID-RECORDER");
        dbContext.DeviceWorkers.Add(worker);
        dbContext.Recorders.AddRange(validRecorder, invalidRecorder);
        dbContext.DeviceWorkerAssignments.AddRange(
            CreateAssignment(worker.Id, validRecorder.Id),
            CreateAssignment(worker.Id, invalidRecorder.Id));
        dbContext.RecorderEndpoints.AddRange(
            CreateEndpoint(validRecorder.Id, "registered-reference"),
            CreateEndpoint(invalidRecorder.Id, "missing-reference"));
        dbContext.DeviceCredentials.Add(new DeviceCredentialEntity
        {
            Id = Guid.NewGuid(),
            Name = "registered-reference",
            ProtectionMode = DeviceCredentialProtectionMode.WindowsDpapiLocalMachine,
            Ciphertext = [1, 2, 3, 4],
            KeyVersion = "test",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new DeviceWorkerAccessService(
            dbContext,
            NullLogger<DeviceWorkerAccessService>.Instance);

        var assignments = await service.GetAssignmentsAsync(worker.Id, CancellationToken.None);

        var assignment = Assert.Single(assignments);
        Assert.Equal(validRecorder.Id, assignment.RecorderId);
        Assert.Single(assignment.Credentials);
        Assert.Equal("registered-reference", assignment.Credentials[0].Name);
    }

    private static RecorderEntity CreateRecorder(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Vendor = "Generic",
        AdapterType = "onvif-standard",
        TimeZoneId = "Asia/Shanghai",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static DeviceWorkerAssignmentEntity CreateAssignment(Guid workerId, Guid recorderId) => new()
    {
        Id = Guid.NewGuid(),
        WorkerId = workerId,
        RecorderId = recorderId,
        DefaultRegionId = Guid.Empty
    };

    private static RecorderEndpointEntity CreateEndpoint(Guid recorderId, string credentialReference) => new()
    {
        Id = Guid.NewGuid(),
        RecorderId = recorderId,
        Protocol = RecorderEndpointProtocol.Onvif,
        Host = "nvr.example.internal",
        Port = 80,
        CredentialReference = credentialReference
    };
}
