using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class DevicePluginSignatureTests
{
    [Fact(DisplayName = "已签名的外部插件可以安装")]
    public async Task InstallAcceptsTrustedSignedPlugin()
    {
        using var rsa = RSA.Create(2048);
        var manifest = CreateManifest(rsa, "vendor-plugin-key");
        await using var dbContext = CreateDbContext();
        var service = new DevicePluginService(
            dbContext,
            Options.Create(new DevicePluginTrustOptions
            {
                PublicKeys = new Dictionary<string, string> { ["vendor-plugin-key"] = rsa.ExportRSAPublicKeyPem() }
            }));

        var plugin = await service.InstallAsync(manifest, CancellationToken.None);

        Assert.Equal("external-edge", plugin.RuntimeType);
        Assert.Equal(manifest.Package!.PackageSha256.ToUpperInvariant(), plugin.PackageHash);
    }

    [Fact(DisplayName = "签名不匹配的外部插件被拒绝")]
    public async Task InstallRejectsInvalidSignature()
    {
        using var trustedKey = RSA.Create(2048);
        using var signingKey = RSA.Create(2048);
        var manifest = CreateManifest(signingKey, "vendor-plugin-key");
        await using var dbContext = CreateDbContext();
        var service = new DevicePluginService(
            dbContext,
            Options.Create(new DevicePluginTrustOptions
            {
                PublicKeys = new Dictionary<string, string> { ["vendor-plugin-key"] = trustedKey.ExportRSAPublicKeyPem() }
            }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.InstallAsync(manifest, CancellationToken.None));
    }

    private static PlatformDbContext CreateDbContext() => new(new DbContextOptionsBuilder<PlatformDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .Options);

    private static DevicePluginManifest CreateManifest(RSA rsa, string signingKeyId)
    {
        var package = new DevicePluginPackage(
            "ghcr.io/example/visicore-plugin",
            "sha256:" + new string('a', 64),
            new string('b', 64),
            signingKeyId,
            string.Empty);
        var manifest = new DevicePluginManifest(
            "example-plugin",
            "示例外部插件",
            "1.0.0",
            "example",
            DevicePluginRuntimeTypes.ExternalEdge,
            "example-plugin",
            [DeviceKinds.Recorder],
            [new DevicePluginEndpointDefinition("Rtsp", "RTSP", 554)],
            new DevicePluginCapabilities(true, false, false, false, false, false),
            Description: "用于签名校验测试。",
            MinimumPlatformVersion: "1.0.0",
            Package: package,
            ConfigurationSchema: "{\"type\":\"object\"}");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            manifest.Key,
            manifest.Name,
            manifest.Version,
            manifest.MinimumPlatformVersion,
            manifest.ProtocolType,
            manifest.RuntimeType,
            manifest.AdapterType,
            manifest.SupportedDeviceKinds,
            manifest.Endpoints,
            manifest.Capabilities,
            manifest.Vendor,
            manifest.Models,
            manifest.Description,
            manifest.ConfigurationSchema,
            package.ImageReference,
            package.ImageDigest,
            package.PackageSha256,
            package.SigningKeyId
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return manifest with
        {
            Package = package with
            {
                Signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            }
        };
    }
}
