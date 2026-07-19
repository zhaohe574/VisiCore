using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class LinuxEdgeAgentControlServiceTests
{
    [Fact]
    public async Task 配对后仅目标节点能取得其凭据信封并可完成预检状态回传()
    {
        await using var dbContext = CreateContext();
        var service = new LinuxEdgeAgentControlService(dbContext);
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
        var service = new LinuxEdgeAgentControlService(dbContext);
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

    private static PlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PlatformDbContext(options);
    }
}
