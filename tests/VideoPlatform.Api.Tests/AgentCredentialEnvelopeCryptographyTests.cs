using System.Security.Cryptography;
using VideoPlatform.Core;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class AgentCredentialEnvelopeCryptographyTests
{
    [Fact(DisplayName = "边缘节点可解封绑定自身与凭据版本的浏览器兼容信封")]
    public void EncryptAndDecryptRoundTrip()
    {
        var agentId = Guid.NewGuid();
        var credentialVersionId = Guid.NewGuid();
        using var rsa = RSA.Create(2048);
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, "edge-key-v1", rsa);

        var envelope = AgentCredentialEnvelopeCryptography.Encrypt(
            publicKey,
            credentialVersionId,
            new AgentCredentialPayload("camera-user", "not-a-real-password"));

        Assert.True(AgentCredentialEnvelopeCryptography.TryValidate(envelope, out var error), error);
        Assert.Equal(AgentCredentialEnvelopeAlgorithms.RsaOaepSha256, envelope.KeyEncryptionAlgorithm);
        Assert.Equal(AgentCredentialEnvelopeAlgorithms.Aes256Gcm, envelope.ContentEncryptionAlgorithm);
        Assert.Equal(AgentCredentialEnvelopeCryptography.InitializationVectorSizeBytes, Convert.FromBase64String(envelope.InitializationVectorBase64).Length);
        Assert.Equal(AgentCredentialEnvelopeCryptography.AuthenticationTagSizeBytes, Convert.FromBase64String(envelope.AuthenticationTagBase64).Length);

        var credential = AgentCredentialEnvelopeCryptography.Decrypt(envelope, agentId, credentialVersionId, rsa);

        Assert.Equal("camera-user", credential.Username);
        Assert.Equal("not-a-real-password", credential.Password);
        Assert.Equal("设备凭据：已隐藏", credential.ToString());
    }

    [Fact(DisplayName = "错误边缘节点或凭据版本不能解封信封")]
    public void WrongAgentOrCredentialVersionIsRejected()
    {
        var agentId = Guid.NewGuid();
        var credentialVersionId = Guid.NewGuid();
        using var rsa = RSA.Create(2048);
        var envelope = CreateEnvelope(agentId, credentialVersionId, rsa);

        Assert.Throws<CryptographicException>(() =>
            AgentCredentialEnvelopeCryptography.Decrypt(envelope, Guid.NewGuid(), credentialVersionId, rsa));
        Assert.Throws<CryptographicException>(() =>
            AgentCredentialEnvelopeCryptography.Decrypt(envelope, agentId, Guid.NewGuid(), rsa));
    }

    [Fact(DisplayName = "变更 AAD 关联标识会导致 AES-GCM 完整性校验失败")]
    public void ModifiedAdditionalAuthenticatedDataIsRejected()
    {
        var agentId = Guid.NewGuid();
        var credentialVersionId = Guid.NewGuid();
        using var rsa = RSA.Create(2048);
        var envelope = CreateEnvelope(agentId, credentialVersionId, rsa) with
        {
            CredentialVersionId = Guid.NewGuid()
        };

        Assert.Throws<CryptographicException>(() =>
            AgentCredentialEnvelopeCryptography.Decrypt(envelope, agentId, envelope.CredentialVersionId, rsa));
    }

    [Fact(DisplayName = "篡改密文或认证标签会被拒绝")]
    public void TamperedCiphertextOrTagIsRejected()
    {
        var agentId = Guid.NewGuid();
        var credentialVersionId = Guid.NewGuid();
        using var rsa = RSA.Create(2048);
        var envelope = CreateEnvelope(agentId, credentialVersionId, rsa);

        var tamperedCiphertext = envelope with
        {
            CiphertextBase64 = MutateBase64(envelope.CiphertextBase64)
        };
        var tamperedTag = envelope with
        {
            AuthenticationTagBase64 = MutateBase64(envelope.AuthenticationTagBase64)
        };

        Assert.Throws<CryptographicException>(() =>
            AgentCredentialEnvelopeCryptography.Decrypt(tamperedCiphertext, agentId, credentialVersionId, rsa));
        Assert.Throws<CryptographicException>(() =>
            AgentCredentialEnvelopeCryptography.Decrypt(tamperedTag, agentId, credentialVersionId, rsa));
    }

    [Fact(DisplayName = "信封格式错误不会在校验信息中回显加密字节")]
    public void ValidationErrorDoesNotExposeEnvelopeData()
    {
        var envelope = new AgentCredentialEnvelope(
            AgentCredentialEnvelopeAlgorithms.CurrentSchemaVersion,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "edge-key-v1",
            AgentCredentialEnvelopeAlgorithms.RsaOaepSha256,
            AgentCredentialEnvelopeAlgorithms.Aes256Gcm,
            "AQID",
            "AQID",
            "c2Vuc2l0aXZlLWVuY3J5cHRlZC1ieXRlcw==",
            "AQIDBAUGBwgJCgsMDQ4PEA==");

        var valid = AgentCredentialEnvelopeCryptography.TryValidate(envelope, out var error);

        Assert.False(valid);
        Assert.DoesNotContain(envelope.EncryptedKeyBase64, error, StringComparison.Ordinal);
        Assert.DoesNotContain(envelope.CiphertextBase64, error, StringComparison.Ordinal);
        Assert.DoesNotContain(envelope.AuthenticationTagBase64, error, StringComparison.Ordinal);
    }

    private static AgentCredentialEnvelope CreateEnvelope(Guid agentId, Guid credentialVersionId, RSA rsa)
    {
        var publicKey = AgentCredentialEnvelopeCryptography.CreatePublicKey(agentId, "edge-key-v1", rsa);
        return AgentCredentialEnvelopeCryptography.Encrypt(
            publicKey,
            credentialVersionId,
            new AgentCredentialPayload("camera-user", "not-a-real-password"));
    }

    private static string MutateBase64(string value)
    {
        var bytes = Convert.FromBase64String(value);
        bytes[0] ^= 0x01;
        return Convert.ToBase64String(bytes);
    }
}
