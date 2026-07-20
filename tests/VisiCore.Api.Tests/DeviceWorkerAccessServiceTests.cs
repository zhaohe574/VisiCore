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

    [Fact(DisplayName = "Linux 边缘节点分配不下发 DPAPI 凭据，缺失引用由节点上报可见状态")]
    public async Task EdgeAgentAssignmentDoesNotExposeProtectedCredentials()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var worker = new DeviceWorkerEntity
        {
            Id = Guid.NewGuid(),
            Name = "edge-agent-worker",
            TokenHash = "edge-agent-token",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var recorder = CreateRecorder("EDGE-RECORDER");
        dbContext.DeviceWorkers.Add(worker);
        dbContext.EdgeAgents.Add(new EdgeAgentEntity
        {
            Id = Guid.NewGuid(),
            DeviceWorkerId = worker.Id,
            Name = "linux-edge",
            Platform = "linux",
            AgentVersion = "0.1.0",
            PublicKeyId = "edge-key",
            SubjectPublicKeyInfoBase64 = "test-public-key",
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        });
        dbContext.Recorders.Add(recorder);
        dbContext.DeviceWorkerAssignments.Add(CreateAssignment(worker.Id, recorder.Id));
        dbContext.RecorderEndpoints.Add(CreateEndpoint(recorder.Id, "agent-envelope-reference"));
        dbContext.DeviceCredentials.Add(new DeviceCredentialEntity
        {
            Id = Guid.NewGuid(),
            Name = "agent-envelope-reference",
            ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope,
            Ciphertext = [],
            KeyVersion = "agent-envelope-v1",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = new DeviceWorkerAccessService(dbContext, NullLogger<DeviceWorkerAccessService>.Instance);
        var assignment = Assert.Single(await service.GetAssignmentsAsync(worker.Id, CancellationToken.None));

        Assert.Empty(assignment.Credentials);
        Assert.Equal("agent-envelope-reference", Assert.Single(assignment.Endpoints).CredentialReference);
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
