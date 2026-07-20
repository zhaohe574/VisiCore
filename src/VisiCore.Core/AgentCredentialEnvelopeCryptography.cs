using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisiCore.Core;

/// <summary>
/// 跨平台设备凭据信封使用的固定算法标识。
/// </summary>
public static class AgentCredentialEnvelopeAlgorithms
{
    public const int CurrentSchemaVersion = 1;
    public const string RsaOaepSha256 = "RSA-OAEP-256";
    public const string Aes256Gcm = "A256GCM";
}

/// <summary>
/// 浏览器与边缘节点共用的设备凭据信封加密和解封工具。
/// </summary>
public static class AgentCredentialEnvelopeCryptography
{
    public const int AesKeySizeBytes = 32;
    public const int InitializationVectorSizeBytes = 12;
    public const int AuthenticationTagSizeBytes = 16;

    private const int MinimumRsaKeySizeBits = 2048;
    private const int MaximumPublicKeySizeBytes = 16 * 1024;
    private const int MaximumEncryptedKeySizeBytes = 16 * 1024;
    private const int MaximumCiphertextSizeBytes = 64 * 1024;

    private static readonly JsonSerializerOptions CredentialJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// 从边缘节点持有的 RSA 密钥生成可交给浏览器导入的公钥契约。
    /// </summary>
    public static AgentPublicKeyContract CreatePublicKey(Guid agentId, string keyId, RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        ValidateAgentId(agentId, nameof(agentId));
        ValidateKeyId(keyId, nameof(keyId));
        EnsureSupportedRsaKey(rsa);

        var subjectPublicKeyInfo = rsa.ExportSubjectPublicKeyInfo();
        try
        {
            return new AgentPublicKeyContract(
                agentId,
                keyId,
                AgentCredentialEnvelopeAlgorithms.RsaOaepSha256,
                Convert.ToBase64String(subjectPublicKeyInfo));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subjectPublicKeyInfo);
        }
    }

    /// <summary>
    /// 使用目标边缘节点的 RSA 公钥加密设备凭据。
    /// </summary>
    public static AgentCredentialEnvelope Encrypt(
        AgentPublicKeyContract publicKey,
        Guid credentialVersionId,
        AgentCredentialPayload credential)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentNullException.ThrowIfNull(credential);
        if (!TryValidatePublicKey(publicKey, out var validationError))
        {
            throw new ArgumentException(validationError, nameof(publicKey));
        }

        ValidateCredentialVersionId(credentialVersionId, nameof(credentialVersionId));

        byte[]? subjectPublicKeyInfo = null;
        byte[]? plaintext = null;
        byte[]? contentEncryptionKey = null;
        byte[]? initializationVector = null;
        byte[]? ciphertext = null;
        byte[]? authenticationTag = null;
        byte[]? additionalAuthenticatedData = null;
        byte[]? encryptedKey = null;

        try
        {
            subjectPublicKeyInfo = DecodeCanonicalBase64(publicKey.SubjectPublicKeyInfoBase64, MaximumPublicKeySizeBytes);
            plaintext = JsonSerializer.SerializeToUtf8Bytes(credential, CredentialJsonOptions);
            contentEncryptionKey = RandomNumberGenerator.GetBytes(AesKeySizeBytes);
            initializationVector = RandomNumberGenerator.GetBytes(InitializationVectorSizeBytes);
            ciphertext = new byte[plaintext.Length];
            authenticationTag = new byte[AuthenticationTagSizeBytes];
            additionalAuthenticatedData = Encoding.UTF8.GetBytes(BuildAdditionalAuthenticatedData(publicKey.AgentId, credentialVersionId));

            using (var aes = new AesGcm(contentEncryptionKey, AuthenticationTagSizeBytes))
            {
                aes.Encrypt(initializationVector, plaintext, ciphertext, authenticationTag, additionalAuthenticatedData);
            }

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length)
            {
                throw new CryptographicException("边缘节点公钥格式无效。");
            }

            EnsureSupportedRsaKey(rsa);
            encryptedKey = rsa.Encrypt(contentEncryptionKey, RSAEncryptionPadding.OaepSHA256);

            return new AgentCredentialEnvelope(
                AgentCredentialEnvelopeAlgorithms.CurrentSchemaVersion,
                publicKey.AgentId,
                credentialVersionId,
                publicKey.KeyId,
                AgentCredentialEnvelopeAlgorithms.RsaOaepSha256,
                AgentCredentialEnvelopeAlgorithms.Aes256Gcm,
                Convert.ToBase64String(encryptedKey),
                Convert.ToBase64String(initializationVector),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(authenticationTag));
        }
        finally
        {
            ZeroMemory(subjectPublicKeyInfo);
            ZeroMemory(plaintext);
            ZeroMemory(contentEncryptionKey);
            ZeroMemory(initializationVector);
            ZeroMemory(ciphertext);
            ZeroMemory(authenticationTag);
            ZeroMemory(additionalAuthenticatedData);
            ZeroMemory(encryptedKey);
        }
    }

    /// <summary>
    /// 仅允许目标边缘节点使用其私钥解封信封；节点与凭据版本都会作为 AAD 完整性绑定。
    /// </summary>
    public static AgentCredentialPayload Decrypt(
        AgentCredentialEnvelope envelope,
        Guid expectedAgentId,
        Guid expectedCredentialVersionId,
        RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(privateKey);
        ValidateAgentId(expectedAgentId, nameof(expectedAgentId));
        ValidateCredentialVersionId(expectedCredentialVersionId, nameof(expectedCredentialVersionId));
        EnsureSupportedRsaKey(privateKey);

        if (!TryValidate(envelope, out _))
        {
            throw new CryptographicException("设备凭据信封格式无效。");
        }

        if (envelope.AgentId != expectedAgentId)
        {
            throw new CryptographicException("设备凭据信封不属于当前边缘节点。");
        }

        if (envelope.CredentialVersionId != expectedCredentialVersionId)
        {
            throw new CryptographicException("设备凭据信封的凭据版本上下文不匹配。");
        }

        byte[]? encryptedKey = null;
        byte[]? initializationVector = null;
        byte[]? ciphertext = null;
        byte[]? authenticationTag = null;
        byte[]? contentEncryptionKey = null;
        byte[]? plaintext = null;
        byte[]? additionalAuthenticatedData = null;

        try
        {
            encryptedKey = DecodeCanonicalBase64(envelope.EncryptedKeyBase64, MaximumEncryptedKeySizeBytes);
            initializationVector = DecodeCanonicalBase64(envelope.InitializationVectorBase64, InitializationVectorSizeBytes);
            ciphertext = DecodeCanonicalBase64(envelope.CiphertextBase64, MaximumCiphertextSizeBytes);
            authenticationTag = DecodeCanonicalBase64(envelope.AuthenticationTagBase64, AuthenticationTagSizeBytes);
            contentEncryptionKey = privateKey.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
            if (contentEncryptionKey.Length != AesKeySizeBytes)
            {
                throw new CryptographicException("设备凭据信封的内容密钥无效。");
            }

            plaintext = new byte[ciphertext.Length];
            additionalAuthenticatedData = Encoding.UTF8.GetBytes(BuildAdditionalAuthenticatedData(expectedAgentId, expectedCredentialVersionId));
            using (var aes = new AesGcm(contentEncryptionKey, AuthenticationTagSizeBytes))
            {
                aes.Decrypt(initializationVector, ciphertext, authenticationTag, plaintext, additionalAuthenticatedData);
            }

            return JsonSerializer.Deserialize<AgentCredentialPayload>(plaintext, CredentialJsonOptions)
                ?? throw new CryptographicException("设备凭据信封的凭据内容无效。");
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("设备凭据信封无法通过完整性校验或由当前节点解封。", exception);
        }
        catch (JsonException exception)
        {
            throw new CryptographicException("设备凭据信封的凭据内容无效。", exception);
        }
        catch (ArgumentException exception)
        {
            throw new CryptographicException("设备凭据信封的凭据内容无效。", exception);
        }
        finally
        {
            ZeroMemory(encryptedKey);
            ZeroMemory(initializationVector);
            ZeroMemory(ciphertext);
            ZeroMemory(authenticationTag);
            ZeroMemory(contentEncryptionKey);
            ZeroMemory(plaintext);
            ZeroMemory(additionalAuthenticatedData);
        }
    }

    /// <summary>
    /// 校验可由浏览器生产并由边缘节点解封的信封结构。错误信息不包含任何密文或凭据内容。
    /// </summary>
    public static bool TryValidate(AgentCredentialEnvelope? envelope, out string validationError)
    {
        if (envelope is null)
        {
            validationError = "设备凭据信封不能为空。";
            return false;
        }

        if (envelope.SchemaVersion != AgentCredentialEnvelopeAlgorithms.CurrentSchemaVersion)
        {
            validationError = "设备凭据信封版本不受支持。";
            return false;
        }

        if (envelope.AgentId == Guid.Empty || envelope.CredentialVersionId == Guid.Empty)
        {
            validationError = "设备凭据信封关联标识无效。";
            return false;
        }

        if (!IsValidKeyId(envelope.KeyId))
        {
            validationError = "设备凭据信封密钥标识无效。";
            return false;
        }

        if (!string.Equals(envelope.KeyEncryptionAlgorithm, AgentCredentialEnvelopeAlgorithms.RsaOaepSha256, StringComparison.Ordinal) ||
            !string.Equals(envelope.ContentEncryptionAlgorithm, AgentCredentialEnvelopeAlgorithms.Aes256Gcm, StringComparison.Ordinal))
        {
            validationError = "设备凭据信封算法不受支持。";
            return false;
        }

        byte[]? encryptedKey = null;
        byte[]? initializationVector = null;
        byte[]? ciphertext = null;
        byte[]? authenticationTag = null;
        try
        {
            if (!TryDecodeCanonicalBase64(envelope.EncryptedKeyBase64, MaximumEncryptedKeySizeBytes, out encryptedKey) ||
                encryptedKey.Length == 0 ||
                !TryDecodeCanonicalBase64(envelope.InitializationVectorBase64, InitializationVectorSizeBytes, out initializationVector) ||
                initializationVector.Length != InitializationVectorSizeBytes ||
                !TryDecodeCanonicalBase64(envelope.CiphertextBase64, MaximumCiphertextSizeBytes, out ciphertext) ||
                ciphertext.Length == 0 ||
                !TryDecodeCanonicalBase64(envelope.AuthenticationTagBase64, AuthenticationTagSizeBytes, out authenticationTag) ||
                authenticationTag.Length != AuthenticationTagSizeBytes)
            {
                validationError = "设备凭据信封编码或长度无效。";
                return false;
            }
        }
        finally
        {
            ZeroMemory(encryptedKey);
            ZeroMemory(initializationVector);
            ZeroMemory(ciphertext);
            ZeroMemory(authenticationTag);
        }

        validationError = string.Empty;
        return true;
    }

    /// <summary>
    /// 校验边缘节点公钥格式和算法要求。错误信息不包含公钥原始字节。
    /// </summary>
    public static bool TryValidatePublicKey(AgentPublicKeyContract? publicKey, out string validationError)
    {
        if (publicKey is null)
        {
            validationError = "边缘节点公钥不能为空。";
            return false;
        }

        if (publicKey.AgentId == Guid.Empty || !IsValidKeyId(publicKey.KeyId))
        {
            validationError = "边缘节点公钥关联标识无效。";
            return false;
        }

        if (!string.Equals(publicKey.KeyEncryptionAlgorithm, AgentCredentialEnvelopeAlgorithms.RsaOaepSha256, StringComparison.Ordinal))
        {
            validationError = "边缘节点公钥算法不受支持。";
            return false;
        }

        byte[]? subjectPublicKeyInfo = null;
        try
        {
            if (!TryDecodeCanonicalBase64(publicKey.SubjectPublicKeyInfoBase64, MaximumPublicKeySizeBytes, out subjectPublicKeyInfo))
            {
                validationError = "边缘节点公钥编码无效。";
                return false;
            }

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length || rsa.KeySize < MinimumRsaKeySizeBits)
            {
                validationError = "边缘节点公钥长度或格式不受支持。";
                return false;
            }
        }
        catch (CryptographicException)
        {
            validationError = "边缘节点公钥长度或格式不受支持。";
            return false;
        }
        catch (ArgumentException)
        {
            validationError = "边缘节点公钥长度或格式不受支持。";
            return false;
        }
        finally
        {
            ZeroMemory(subjectPublicKeyInfo);
        }

        validationError = string.Empty;
        return true;
    }

    /// <summary>
    /// 生成 AES-GCM 的附加认证数据。浏览器必须对返回值使用 UTF-8 编码后作为 WebCrypto 的 <c>additionalData</c>。
    /// </summary>
    public static string BuildAdditionalAuthenticatedData(Guid agentId, Guid credentialVersionId)
    {
        ValidateAgentId(agentId, nameof(agentId));
        ValidateCredentialVersionId(credentialVersionId, nameof(credentialVersionId));
        return $"{{\"agentId\":\"{agentId:D}\",\"credentialVersionId\":\"{credentialVersionId:D}\"}}";
    }

    private static void ValidateAgentId(Guid agentId, string parameterName)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("边缘节点标识不能为空。", parameterName);
        }
    }

    private static void ValidateCredentialVersionId(Guid credentialVersionId, string parameterName)
    {
        if (credentialVersionId == Guid.Empty)
        {
            throw new ArgumentException("设备凭据版本标识不能为空。", parameterName);
        }
    }

    private static void ValidateKeyId(string keyId, string parameterName)
    {
        if (!IsValidKeyId(keyId))
        {
            throw new ArgumentException("边缘节点密钥标识无效。", parameterName);
        }
    }

    private static bool IsValidKeyId(string? keyId) =>
        !string.IsNullOrWhiteSpace(keyId) &&
        keyId.Length <= 128 &&
        keyId.All(character => !char.IsControl(character));

    private static void EnsureSupportedRsaKey(RSA rsa)
    {
        if (rsa.KeySize < MinimumRsaKeySizeBits)
        {
            throw new ArgumentException("RSA 密钥长度必须至少为 2048 位。", nameof(rsa));
        }
    }

    private static byte[] DecodeCanonicalBase64(string value, int maximumBytes)
    {
        if (!TryDecodeCanonicalBase64(value, maximumBytes, out var bytes))
        {
            throw new CryptographicException("加密信封编码无效。");
        }

        return bytes;
    }

    private static bool TryDecodeCanonicalBase64(string? value, int maximumBytes, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value) || value.Length > GetMaximumBase64Length(maximumBytes))
        {
            return false;
        }

        try
        {
            var decoded = Convert.FromBase64String(value);
            if (decoded.Length > maximumBytes || !string.Equals(Convert.ToBase64String(decoded), value, StringComparison.Ordinal))
            {
                ZeroMemory(decoded);
                return false;
            }

            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int GetMaximumBase64Length(int maximumBytes) => checked(((maximumBytes + 2) / 3) * 4);

    private static void ZeroMemory(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
