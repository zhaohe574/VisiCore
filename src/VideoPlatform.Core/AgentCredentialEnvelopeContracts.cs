using System.Diagnostics;

namespace VideoPlatform.Core;

/// <summary>
/// 边缘节点发布给管理平台的 RSA 公钥。
/// <para>公钥采用 DER SubjectPublicKeyInfo 的标准 Base64 编码，可由浏览器 WebCrypto 通过 <c>spki</c> 格式导入。</para>
/// </summary>
public sealed record AgentPublicKeyContract(
    Guid AgentId,
    string KeyId,
    string KeyEncryptionAlgorithm,
    string SubjectPublicKeyInfoBase64);

/// <summary>
/// 设备凭据面向指定边缘节点的混合加密信封。
/// <para>浏览器 WebCrypto 的 AES-GCM 结果会将认证标签附在密文末尾；写入本契约前必须拆分最后 16 个字节到 <see cref="AuthenticationTagBase64"/>。</para>
/// </summary>
public sealed record AgentCredentialEnvelope(
    int SchemaVersion,
    Guid AgentId,
    Guid CredentialVersionId,
    string KeyId,
    string KeyEncryptionAlgorithm,
    string ContentEncryptionAlgorithm,
    string EncryptedKeyBase64,
    string InitializationVectorBase64,
    string CiphertextBase64,
    string AuthenticationTagBase64);

/// <summary>
/// 仅在边缘节点内存中短暂存在的设备登录凭据。
/// </summary>
[DebuggerDisplay("设备凭据：已隐藏")]
public sealed class AgentCredentialPayload
{
    public AgentCredentialPayload(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("设备凭据用户名不能为空。", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("设备凭据密码不能为空。", nameof(password));
        }

        Username = username;
        Password = password;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string Username { get; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public string Password { get; }

    public override string ToString() => "设备凭据：已隐藏";
}
