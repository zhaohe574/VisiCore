using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class ReleaseTrustOptions
{
    public List<ReleaseTrustKeyOptions> Keys { get; init; } = [];

    public bool TryGetPublicKey(string keyId, out string publicKeyPem)
    {
        publicKeyPem = Keys.FirstOrDefault(item => string.Equals(item.KeyId, keyId, StringComparison.Ordinal))?.PublicKeyPem ?? string.Empty;
        return !string.IsNullOrWhiteSpace(publicKeyPem);
    }
}

public sealed class ReleaseTrustKeyOptions
{
    public string KeyId { get; init; } = string.Empty;
    public string PublicKeyPem { get; init; } = string.Empty;
}

/// <summary>
/// 发行目录只信任本机配置的发行公钥；上传者权限不能替代离线签名校验。
/// </summary>
public sealed class ReleaseCatalogService(
    PlatformDbContext dbContext,
    IOptions<ReleaseTrustOptions> trustOptions)
{
    public async Task<ReleaseCatalogEntity> RegisterAsync(
        string descriptorJson,
        string signatureBase64,
        string publicKeyId,
        CancellationToken cancellationToken)
    {
        if (!ReleaseDescriptor.TryParse(descriptorJson, out var descriptor, out var failureKind) ||
            !string.Equals(descriptor.SigningPublicKeyId, publicKeyId, StringComparison.Ordinal))
        {
            throw new ReleaseCatalogException(failureKind);
        }
        if (!ReleaseDescriptor.TryCanonicalizeJson(descriptorJson, out var canonicalDescriptorJson))
        {
            throw new ReleaseCatalogException("release_descriptor_invalid");
        }
        if (!trustOptions.Value.TryGetPublicKey(publicKeyId, out var publicKeyPem))
        {
            throw new ReleaseCatalogException("release_signing_key_unknown");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            throw new ReleaseCatalogException("release_signature_invalid");
        }

        try
        {
            using var publicKey = RSA.Create();
            publicKey.ImportFromPem(publicKeyPem);
            var payload = Encoding.UTF8.GetBytes(canonicalDescriptorJson);
            try
            {
                if (!publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                {
                    throw new ReleaseCatalogException("release_signature_invalid");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (CryptographicException)
        {
            throw new ReleaseCatalogException("release_signature_invalid");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }

        if (await dbContext.ReleaseCatalog.AnyAsync(
                item => item.ProductVersion == descriptor.ProductVersion && item.Channel == descriptor.Channel,
                cancellationToken))
        {
            throw new ReleaseCatalogException("release_already_registered");
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new ReleaseCatalogEntity
        {
            Id = Guid.NewGuid(),
            ProductVersion = descriptor.ProductVersion,
            Channel = descriptor.Channel,
            Status = "available",
            DescriptorJson = canonicalDescriptorJson,
            SignatureBase64 = signatureBase64,
            SigningPublicKeyId = publicKeyId,
            PublishedAt = descriptor.IssuedAt,
            ExpiresAt = descriptor.ExpiresAt,
            CreatedAt = now
        };
        dbContext.ReleaseCatalog.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public static bool TryReadDescriptor(ReleaseCatalogEntity entry, out ReleaseDescriptor descriptor) =>
        ReleaseDescriptor.TryParse(entry.DescriptorJson, out descriptor, out _);
}

public sealed class ReleaseCatalogException(string failureKind) : InvalidOperationException(failureKind)
{
    public string FailureKind { get; } = failureKind;
}
