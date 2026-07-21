using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class LinuxEdgeAgentControlServiceTests
{
    [Fact(DisplayName = "配对码台账保留使用关联且不会序列化配对码明文或哈希")]
    public async Task EnrollmentLedgerTracksUsageWithoutExposingSecrets()
    {
        await using var dbContext = CreateContext();
        var service = new EdgeAgentControlService(dbContext);
        var enrollment = await service.CreateEnrollmentAsync("台账节点", 15, CancellationToken.None);
        using var rsa = RSA.Create(2048);
        var agentId = Guid.NewGuid();
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, "ledger-key", rsa);

        await service.EnrollAsync(new EnrollEdgeAgentRequest(
            enrollment.EnrollmentCode,
            "1.0.0",
            "linux",
            "{}",
            new EdgeAgentPublicKeyRequest(publicKey.AgentId, publicKey.KeyId, publicKey.KeyEncryptionAlgorithm, publicKey.SubjectPublicKeyInfoBase64)),
            CancellationToken.None);

        var persisted = await dbContext.EdgeAgentEnrollments.SingleAsync();
        Assert.Equal(agentId, persisted.UsedByAgentId);
        Assert.NotNull(persisted.UsedAt);
        Assert.Equal("used", EdgeAgentControlService.GetEnrollmentStatus(persisted, DateTimeOffset.UtcNow));
        Assert.Equal("available", EdgeAgentControlService.GetEnrollmentStatus(new EdgeAgentEnrollmentEntity { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) }, DateTimeOffset.UtcNow));
        Assert.Equal("expired", EdgeAgentControlService.GetEnrollmentStatus(new EdgeAgentEnrollmentEntity { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, DateTimeOffset.UtcNow));

        var summary = new EdgeAgentEnrollmentSummary(
            persisted.Id,
            persisted.Name,
            persisted.CreatedAt,
            persisted.ExpiresAt,
            persisted.UsedAt,
            "used",
            persisted.UsedByAgentId,
            "台账节点");
        var serialized = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain(enrollment.EnrollmentCode, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("CodeHash", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("EnrollmentCode", serialized, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "边缘节点改名会同步更新受管 Worker 并拒绝重复名称")]
    public async Task RenameSynchronizesWorkerNameAndRejectsDuplicates()
    {
        await using var dbContext = CreateContext();
        var service = new EdgeAgentControlService(dbContext);
        var enrolled = await EnrollAsync(service, "旧节点", "rename-key");

        var updated = await service.RenameAsync(enrolled.AgentId, "新节点", CancellationToken.None);
        Assert.Equal("新节点", updated.Name);
        Assert.Equal("新节点", (await dbContext.EdgeAgents.SingleAsync(item => item.Id == enrolled.AgentId)).Name);
        Assert.Equal("edge-agent-新节点", (await dbContext.DeviceWorkers.SingleAsync(item => item.Id == enrolled.WorkerId)).Name);

        await service.SetStatusAsync(enrolled.AgentId, true, CancellationToken.None);
        Assert.NotNull((await dbContext.EdgeAgents.SingleAsync(item => item.Id == enrolled.AgentId)).DisabledAt);
        Assert.NotNull((await dbContext.DeviceWorkers.SingleAsync(item => item.Id == enrolled.WorkerId)).DisabledAt);
        await service.SetStatusAsync(enrolled.AgentId, false, CancellationToken.None);
        Assert.Null((await dbContext.EdgeAgents.SingleAsync(item => item.Id == enrolled.AgentId)).DisabledAt);
        Assert.Null((await dbContext.DeviceWorkers.SingleAsync(item => item.Id == enrolled.WorkerId)).DisabledAt);

        dbContext.DeviceWorkers.Add(new DeviceWorkerEntity
        {
            Id = Guid.NewGuid(),
            Name = "edge-agent-重复节点",
            TokenHash = "other-worker",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenameAsync(enrolled.AgentId, "重复节点", CancellationToken.None));
    }

    [Fact(DisplayName = "删除边缘节点会解除设备分配、保留历史引用并使旧令牌失效")]
    public async Task DeleteRemovesNodeScopedDataAndPreservesHistoricalReferences()
    {
        await using var dbContext = CreateContext();
        var service = new EdgeAgentControlService(dbContext);
        var enrolled = await EnrollAsync(service, "待删除节点", "delete-key");
        var agent = await dbContext.EdgeAgents.SingleAsync(item => item.Id == enrolled.AgentId);
        var enrollment = await dbContext.EdgeAgentEnrollments.SingleAsync(item => item.UsedByAgentId == enrolled.AgentId);
        var assignment = new DeviceWorkerAssignmentEntity { Id = Guid.NewGuid(), WorkerId = enrolled.WorkerId, RecorderId = Guid.NewGuid(), DefaultRegionId = Guid.NewGuid() };
        var status = new DeviceWorkerOperationStatusEntity { Id = Guid.NewGuid(), WorkerId = enrolled.WorkerId, RecorderId = assignment.RecorderId, OperationType = "recording.search", IsReady = true, ReportedAt = DateTimeOffset.UtcNow };
        var command = new EdgeCommandEntity { Id = Guid.NewGuid(), WorkerId = enrolled.WorkerId, RecorderId = assignment.RecorderId, CommandType = "diagnostic", AggregateType = "edge_agent", AggregateId = enrolled.AgentId, CreatedAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow };
        var envelope = new DeviceCredentialEnvelopeEntity { Id = Guid.NewGuid(), CredentialVersionId = Guid.NewGuid(), EdgeAgentId = enrolled.AgentId, KeyId = "delete-key", KeyEncryptionAlgorithm = "RSA-OAEP-256", ContentEncryptionAlgorithm = "AES-256-GCM", EncryptedKey = [1], InitializationVector = [1], Ciphertext = [1], AuthenticationTag = [1], CreatedAt = DateTimeOffset.UtcNow };
        var operation = new PlatformOperationEntity { Id = Guid.NewGuid(), EdgeAgentId = enrolled.AgentId, OperationType = "diagnostic", Status = "completed", Summary = "保留历史", DetailsJson = "{}", RequestedAt = DateTimeOffset.UtcNow };
        var target = new UpgradeTargetEntity { Id = Guid.NewGuid(), UpgradePlanId = Guid.NewGuid(), EdgeAgentId = enrolled.AgentId, TargetType = "edge", Component = "edge-agent", Batch = 1, Status = "completed", ExpectedVersion = "1.0.0", RequestedAt = DateTimeOffset.UtcNow };
        dbContext.DeviceWorkerAssignments.Add(assignment);
        dbContext.DeviceWorkerOperationStatuses.Add(status);
        dbContext.EdgeCommands.Add(command);
        dbContext.DeviceCredentialEnvelopes.Add(envelope);
        dbContext.PlatformOperations.Add(operation);
        dbContext.UpgradeTargets.Add(target);
        await dbContext.SaveChangesAsync();

        var deleted = await service.DeleteAsync(enrolled.AgentId, agent.Name, CancellationToken.None);

        Assert.Equal(1, deleted.DetachedAssignmentCount);
        Assert.Equal(1, deleted.DeletedCredentialEnvelopeCount);
        Assert.Null(await dbContext.EdgeAgents.FindAsync(enrolled.AgentId));
        Assert.Null(await dbContext.DeviceWorkers.FindAsync(enrolled.WorkerId));
        Assert.Empty(await dbContext.DeviceWorkerAssignments.ToListAsync());
        Assert.Empty(await dbContext.DeviceWorkerOperationStatuses.ToListAsync());
        Assert.Empty(await dbContext.EdgeCommands.ToListAsync());
        Assert.Empty(await dbContext.DeviceCredentialEnvelopes.ToListAsync());
        Assert.Empty(await dbContext.EdgeAgentConfigurations.ToListAsync());
        Assert.NotNull(enrollment.UsedAt);
        Assert.Null(enrollment.UsedByAgentId);
        Assert.Null((await dbContext.PlatformOperations.SingleAsync(item => item.Id == operation.Id)).EdgeAgentId);
        Assert.Null((await dbContext.UpgradeTargets.SingleAsync(item => item.Id == target.Id)).EdgeAgentId);

        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = $"Bearer {enrolled.WorkerToken}";
        Assert.Null(await service.AuthenticateAsync(request, enrolled.AgentId, CancellationToken.None));
    }

    [Fact(DisplayName = "Windows Edge Agent 使用同一配对协议并能回传配置拒绝状态")]
    public async Task WindowsAgentCanEnrollAndRejectInvalidConfiguration()
    {
        await using var dbContext = CreateContext();
        var service = new EdgeAgentControlService(dbContext);
        var enrollment = await service.CreateEnrollmentAsync("Windows 边缘节点", 15, CancellationToken.None);
        using var rsa = RSA.Create(3072);
        var agentId = Guid.NewGuid();
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, "windows-key", rsa);

        var enrolled = await service.EnrollAsync(new EnrollEdgeAgentRequest(
            enrollment.EnrollmentCode,
            "0.1.0",
            "windows-x64",
            "{\"declared\":[\"configuration-v1\"],\"architecture\":\"x64\"}",
            new EdgeAgentPublicKeyRequest(publicKey.AgentId, publicKey.KeyId, publicKey.KeyEncryptionAlgorithm, publicKey.SubjectPublicKeyInfoBase64)),
            CancellationToken.None);

        var agent = await dbContext.EdgeAgents.SingleAsync(item => item.Id == enrolled.AgentId);
        Assert.Equal("windows", agent.Platform);
        dbContext.EdgeAgentConfigurations.Add(new EdgeAgentConfigurationEntity
        {
            Id = Guid.NewGuid(),
            EdgeAgentId = agent.Id,
            Version = 2,
            ConfigurationJson = "{\"schemaVersion\":99}",
            Status = "published",
            PublishedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        await service.ReportConfigurationStatusAsync(agent, new EdgeAgentConfigurationStatusReport(
            2,
            false,
            "configuration_schema_invalid"), CancellationToken.None);

        var configuration = await dbContext.EdgeAgentConfigurations.SingleAsync(item => item.EdgeAgentId == agent.Id && item.Version == 2);
        Assert.Equal("rejected", configuration.Status);
        Assert.Equal("configuration_schema_invalid", configuration.FailureSummary);
        Assert.Null(configuration.AppliedAt);
    }

    [Fact]
    public async Task 配对后仅目标节点能取得其凭据信封并可完成预检状态回传()
    {
        await using var dbContext = CreateContext();
        var service = new EdgeAgentControlService(dbContext);
        var enrollment = await service.CreateEnrollmentAsync("园区 Linux 节点", 15, CancellationToken.None);
        using var rsa = RSA.Create(2048);
        var agentId = Guid.NewGuid();
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, "key-20260717", rsa);
        var enrolled = await service.EnrollAsync(new EnrollEdgeAgentRequest(
            enrollment.EnrollmentCode,
            "1.0.0",
            "linux",
            "{\"directRtsp\":true}",
            new EdgeAgentPublicKeyRequest(publicKey.AgentId, publicKey.KeyId, publicKey.KeyEncryptionAlgorithm, publicKey.SubjectPublicKeyInfoBase64)),
            CancellationToken.None);

        Assert.Equal(agentId, enrolled.AgentId);
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = $"Bearer {enrolled.WorkerToken}";
        var authenticated = await service.AuthenticateAsync(request, enrolled.AgentId, CancellationToken.None);
        Assert.NotNull(authenticated);

        var credentialId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var envelope = AgentCredentialEnvelopeCryptography.Encrypt(
            publicKey,
            versionId,
            new AgentCredentialPayload("admin", "not-written-to-the-database"));
        dbContext.DeviceCredentials.Add(new DeviceCredentialEntity
        {
            Id = credentialId,
            Name = "north-camera",
            ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope,
            Ciphertext = [],
            KeyVersion = "agent-envelope-v1",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.DeviceCredentialVersions.Add(new DeviceCredentialVersionEntity
        {
            Id = versionId,
            CredentialId = credentialId,
            Version = 1,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.DeviceCredentialEnvelopes.Add(new DeviceCredentialEnvelopeEntity
        {
            Id = Guid.NewGuid(),
            CredentialVersionId = versionId,
            EdgeAgentId = enrolled.AgentId,
            KeyId = envelope.KeyId,
            KeyEncryptionAlgorithm = envelope.KeyEncryptionAlgorithm,
            ContentEncryptionAlgorithm = envelope.ContentEncryptionAlgorithm,
            EncryptedKey = Convert.FromBase64String(envelope.EncryptedKeyBase64),
            InitializationVector = Convert.FromBase64String(envelope.InitializationVectorBase64),
            Ciphertext = Convert.FromBase64String(envelope.CiphertextBase64),
            AuthenticationTag = Convert.FromBase64String(envelope.AuthenticationTagBase64),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var bindings = await service.GetCredentialEnvelopesAsync(authenticated!, CancellationToken.None);
        var binding = Assert.Single(bindings);
        Assert.Equal(credentialId, binding.CredentialId);
        Assert.NotEqual("not-written-to-the-database", binding.CiphertextBase64);

        var operation = await service.ScheduleDirectRtspProbeAsync(
            enrolled.AgentId,
            credentialId,
            "rtsp://10.0.0.20:554/live/main",
            null,
            CancellationToken.None);
        await service.ReportDiagnosticAsync(authenticated!, new EdgeAgentDiagnosticReport(
            operation.Id,
            "direct-rtsp.probe",
            false,
            "password=not-written-to-the-database；rtsp://admin:not-written-to-the-database@10.0.0.20/live/main",
            "{\"password\":\"not-written-to-the-database\",\"endpoint\":\"rtsp://admin:not-written-to-the-database@10.0.0.20/live/main\",\"reachable\":false}"), CancellationToken.None);

        var persisted = await dbContext.DeviceCredentials.SingleAsync(item => item.Id == credentialId);
        Assert.Equal("password=[已脱敏]", persisted.LastVerificationError);
        var completed = await dbContext.PlatformOperations.SingleAsync(item => item.Id == operation.Id);
        Assert.Equal("failed", completed.Status);
        Assert.DoesNotContain("not-written-to-the-database", completed.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", completed.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 凭据轮换和停用后不会向节点下发旧版或已撤销信封()
    {
        await using var dbContext = CreateContext();
        var service = new EdgeAgentControlService(dbContext);
        var enrollment = await service.CreateEnrollmentAsync("轮换测试节点", 15, CancellationToken.None);
        using var rsa = RSA.Create(2048);
        var agentId = Guid.NewGuid();
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, "key-rotation", rsa);
        var enrolled = await service.EnrollAsync(new EnrollEdgeAgentRequest(
            enrollment.EnrollmentCode,
            "1.0.0",
            "linux-x64",
            "{}",
            new EdgeAgentPublicKeyRequest(publicKey.AgentId, publicKey.KeyId, publicKey.KeyEncryptionAlgorithm, publicKey.SubjectPublicKeyInfoBase64)),
            CancellationToken.None);
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = $"Bearer {enrolled.WorkerToken}";
        var authenticated = await service.AuthenticateAsync(request, enrolled.AgentId, CancellationToken.None);
        Assert.NotNull(authenticated);

        var credential = new DeviceCredentialEntity
        {
            Id = Guid.NewGuid(),
            Name = "rotated-camera",
            ProtectionMode = DeviceCredentialProtectionMode.AgentEnvelope,
            Ciphertext = [],
            KeyVersion = "agent-envelope-v2",
            CreatedAt = DateTimeOffset.UtcNow
        };
        var retiredVersion = Guid.NewGuid();
        var activeVersion = Guid.NewGuid();
        var retiredEnvelope = AgentCredentialEnvelopeCryptography.Encrypt(
            publicKey, retiredVersion, new AgentCredentialPayload("old-user", "old-password"));
        var activeEnvelope = AgentCredentialEnvelopeCryptography.Encrypt(
            publicKey, activeVersion, new AgentCredentialPayload("new-user", "new-password"));
        dbContext.DeviceCredentials.Add(credential);
        dbContext.DeviceCredentialVersions.AddRange(
            new DeviceCredentialVersionEntity
            {
                Id = retiredVersion, CredentialId = credential.Id, Version = 1, Status = "retired",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), RetiredAt = DateTimeOffset.UtcNow
            },
            new DeviceCredentialVersionEntity
            {
                Id = activeVersion, CredentialId = credential.Id, Version = 2, Status = "active", CreatedAt = DateTimeOffset.UtcNow
            });
        dbContext.DeviceCredentialEnvelopes.AddRange(
            ToEnvelopeEntity(retiredEnvelope, retiredVersion, enrolled.AgentId),
            ToEnvelopeEntity(activeEnvelope, activeVersion, enrolled.AgentId));
        await dbContext.SaveChangesAsync();

        var delivered = await service.GetCredentialEnvelopesAsync(authenticated!, CancellationToken.None);
        var onlyActiveEnvelope = Assert.Single(delivered);
        Assert.Equal(activeVersion, onlyActiveEnvelope.CredentialVersionId);
        Assert.NotNull(await service.ResolveCredentialBindingAsync(enrolled.AgentId, credential.Id, CancellationToken.None));

        credential.DisabledAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        Assert.Empty(await service.GetCredentialEnvelopesAsync(authenticated!, CancellationToken.None));
        Assert.Null(await service.ResolveCredentialBindingAsync(enrolled.AgentId, credential.Id, CancellationToken.None));
    }

    private static DeviceCredentialEnvelopeEntity ToEnvelopeEntity(AgentCredentialEnvelope envelope, Guid versionId, Guid agentId) => new()
    {
        Id = Guid.NewGuid(),
        CredentialVersionId = versionId,
        EdgeAgentId = agentId,
        KeyId = envelope.KeyId,
        KeyEncryptionAlgorithm = envelope.KeyEncryptionAlgorithm,
        ContentEncryptionAlgorithm = envelope.ContentEncryptionAlgorithm,
        EncryptedKey = Convert.FromBase64String(envelope.EncryptedKeyBase64),
        InitializationVector = Convert.FromBase64String(envelope.InitializationVectorBase64),
        Ciphertext = Convert.FromBase64String(envelope.CiphertextBase64),
        AuthenticationTag = Convert.FromBase64String(envelope.AuthenticationTagBase64),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static async Task<EnrolledEdgeAgent> EnrollAsync(EdgeAgentControlService service, string name, string keyId)
    {
        var enrollment = await service.CreateEnrollmentAsync(name, 15, CancellationToken.None);
        using var rsa = RSA.Create(2048);
        var agentId = Guid.NewGuid();
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, keyId, rsa);
        return await service.EnrollAsync(new EnrollEdgeAgentRequest(
            enrollment.EnrollmentCode,
            "1.0.0",
            "linux",
            "{}",
            new EdgeAgentPublicKeyRequest(publicKey.AgentId, publicKey.KeyId, publicKey.KeyEncryptionAlgorithm, publicKey.SubjectPublicKeyInfoBase64)),
            CancellationToken.None);
    }

    private static PlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PlatformDbContext(options);
    }
}
