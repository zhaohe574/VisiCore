using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class ReleasePlanningTests
{
    [Fact(DisplayName = "统一发行描述必须由受信公钥签名，且边缘计划固定分为首台、10%与其余批次")]
    public async Task SignedReleaseCreatesFixedCanaryBatches()
    {
        await using var dbContext = CreateContext();
        using var rsa = RSA.Create(2048);
        const string keyId = "test-release-key";
        var descriptorJson = CreateDescriptorJson(keyId);
        Assert.True(ReleaseDescriptor.TryCanonicalizeJson(descriptorJson, out var canonicalDescriptorJson));
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(canonicalDescriptorJson), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        var trust = Options.Create(new ReleaseTrustOptions
        {
            Keys = [new ReleaseTrustKeyOptions { KeyId = keyId, PublicKeyPem = rsa.ExportRSAPublicKeyPem() }]
        });
        var catalog = new ReleaseCatalogService(dbContext, trust);
        var release = await catalog.RegisterAsync(descriptorJson, signature, keyId, CancellationToken.None);

        var agentIds = new List<Guid>();
        for (var index = 0; index < 12; index++)
        {
            var id = Guid.NewGuid();
            agentIds.Add(id);
            dbContext.EdgeAgents.Add(new EdgeAgentEntity
            {
                Id = id,
                DeviceWorkerId = Guid.NewGuid(),
                Name = $"edge-{index:D2}",
                Platform = "linux",
                AgentVersion = "0.1.0",
                PublicKeyId = $"key-{index}",
                SubjectPublicKeyInfoBase64 = "test",
                CapabilitiesJson = "{\"architecture\":\"amd64\",\"hostUpgradeReady\":true}",
                LastSeenAt = DateTimeOffset.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        var plans = new UpgradePlanService(dbContext);
        var plan = await plans.CreateAsync(release.Id, "edge", agentIds, "tester", CancellationToken.None);
        var targets = await dbContext.UpgradeTargets.Where(item => item.UpgradePlanId == plan.Id).ToListAsync();

        Assert.Equal(1, targets.Count(item => item.Batch == 1));
        Assert.Equal(2, targets.Count(item => item.Batch == 2));
        Assert.Equal(9, targets.Count(item => item.Batch == 3));
    }

    [Fact(DisplayName = "统一发行目录拒绝未经受信公钥验证的描述")]
    public async Task ReleaseCatalogRejectsInvalidSignature()
    {
        await using var dbContext = CreateContext();
        using var trustedKey = RSA.Create(2048);
        using var untrustedKey = RSA.Create(2048);
        const string keyId = "trusted-key";
        var descriptorJson = CreateDescriptorJson(keyId);
        Assert.True(ReleaseDescriptor.TryCanonicalizeJson(descriptorJson, out var canonicalDescriptorJson));
        var signature = Convert.ToBase64String(untrustedKey.SignData(Encoding.UTF8.GetBytes(canonicalDescriptorJson), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        var catalog = new ReleaseCatalogService(dbContext, Options.Create(new ReleaseTrustOptions
        {
            Keys = [new ReleaseTrustKeyOptions { KeyId = keyId, PublicKeyPem = trustedKey.ExportRSAPublicKeyPem() }]
        }));

        var exception = await Assert.ThrowsAsync<ReleaseCatalogException>(() => catalog.RegisterAsync(descriptorJson, signature, keyId, CancellationToken.None));
        Assert.Equal("release_signature_invalid", exception.FailureKind);
    }

    [Fact(DisplayName = "发行描述签名载荷必须使用按属性名排序的紧凑 JSON")]
    public void ReleaseDescriptorCanonicalizationIsStable()
    {
        const string left = "{\"z\":1,\"nested\":{\"b\":false,\"a\":true},\"a\":\"value\"}";
        const string right = "{ \"a\" : \"value\", \"nested\" : { \"a\" : true, \"b\" : false }, \"z\" : 1 }";

        Assert.True(ReleaseDescriptor.TryCanonicalizeJson(left, out var canonicalLeft));
        Assert.True(ReleaseDescriptor.TryCanonicalizeJson(right, out var canonicalRight));
        Assert.Equal("{\"a\":\"value\",\"nested\":{\"a\":true,\"b\":false},\"z\":1}", canonicalLeft);
        Assert.Equal(canonicalLeft, canonicalRight);
        Assert.False(ReleaseDescriptor.TryCanonicalizeJson("{\"a\":1,\"a\":2}", out _));
    }

    [Fact(DisplayName = "暂停态升级计划不能跳过失败批次再次启动")]
    public async Task PausedUpgradePlanCannotRestart()
    {
        await using var dbContext = CreateContext();
        var plan = new UpgradePlanEntity
        {
            Id = Guid.NewGuid(),
            ReleaseCatalogId = Guid.NewGuid(),
            TargetScope = "edge",
            Status = "paused",
            RequestedBy = "tester",
            RequestedAt = DateTimeOffset.UtcNow
        };
        dbContext.UpgradePlans.Add(plan);
        await dbContext.SaveChangesAsync();

        var service = new UpgradePlanService(dbContext);
        var exception = await Assert.ThrowsAsync<UpgradePlanException>(() => service.StartAsync(plan.Id, CancellationToken.None));
        Assert.Equal("upgrade_plan_not_startable", exception.FailureKind);
    }

    [Fact(DisplayName = "发行目录迁移必须与当前关系模型一致")]
    public async Task ReleaseCatalogMigrationMatchesCurrentModel()
    {
        await using var dbContext = CreateRelationalContext();
        var snapshotType = typeof(PlatformDbContext).Assembly.GetType(
            "VisiCore.Persistence.Migrations.PlatformDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var differ = dbContext.GetService<IMigrationsModelDiffer>();
        var initializer = dbContext.GetService<IModelRuntimeInitializer>();
        var snapshotModel = initializer.Initialize(snapshot.Model, designTime: true);
        var currentModel = dbContext.GetService<IDesignTimeModel>().Model;
        var changes = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());

        Assert.Empty(changes);
    }

    private static PlatformDbContext CreateContext() => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);

    private static PlatformDbContext CreateRelationalContext() => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=5432;Database=visicore;Username=postgres;Password=design-time-only")
        .Options);

    private static string CreateDescriptorJson(string keyId)
    {
        var digest = new string('a', 64);
        return JsonSerializer.Serialize(new
        {
            productVersion = "0.1.1",
            channel = "stable",
            minimumCoreVersion = "0.1.0",
            minimumEdgeVersion = "0.1.0",
            databaseMigrationMode = "automatic-backup",
            issuedAt = DateTimeOffset.UtcNow,
            expiresAt = DateTimeOffset.UtcNow.AddDays(30),
            signingPublicKeyId = keyId,
            artifacts = new[]
            {
                new { component = "core", platform = "linux", architecture = "amd64", artifactReference = $"visicore/visicore-core@sha256:{digest}", artifactSha256 = digest, sizeBytes = 1L, minimumHostAgentVersion = "0.1.0" },
                new { component = "edge-docker", platform = "linux", architecture = "amd64", artifactReference = $"visicore/visicore-edge-node@sha256:{digest}", artifactSha256 = digest, sizeBytes = 1L, minimumHostAgentVersion = "0.1.0" }
            }
        });
    }
}
