using System.Security.Cryptography;
using System.Text;

namespace VisiCore.Persistence;

/// <summary>
/// 使用仅保存在核心配置卷中的运行期主密钥保护通知 Webhook 地址。
/// 数据库只保存 AES-GCM 密文，核心运行环境不依赖 Windows DPAPI。
/// </summary>
public sealed class NotificationWebhookAddressProtector
{
    public const string CurrentKeyVersion = "runtime-config-aes-gcm-v1";
    public const NotificationChannelWebhookProtectionMode CurrentProtectionMode = NotificationChannelWebhookProtectionMode.AesGcmRuntimeKey;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] TestOnlyKey = SHA256.HashData(Encoding.UTF8.GetBytes("VisiCore.NotificationWebhookAddress.Tests"));
    private readonly byte[] masterKey;

    // 仅保留给单元测试和脱离宿主的开发验证；生产运行必须注入 setup 写入的主密钥。
    public NotificationWebhookAddressProtector() : this(TestOnlyKey)
    {
    }

    public NotificationWebhookAddressProtector(string masterKeyBase64) : this(ParseMasterKey(masterKeyBase64))
    {
    }

    private NotificationWebhookAddressProtector(byte[] masterKey)
    {
        if (masterKey.Length != 32)
        {
            throw new ArgumentException("通知 Webhook 主密钥必须是 32 字节。", nameof(masterKey));
        }
        this.masterKey = masterKey.ToArray();
    }

    public void Protect(NotificationChannelEntity channel, string webhookAddress)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Id == Guid.Empty)
        {
            throw new ArgumentException("通知渠道必须先分配标识后才能保存 Webhook 地址。", nameof(channel));
        }
        if (string.IsNullOrWhiteSpace(webhookAddress))
        {
            throw new ArgumentException("Webhook 地址不能为空。", nameof(webhookAddress));
        }

        var plaintext = Encoding.UTF8.GetBytes(webhookAddress);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var aad = CreateAssociatedData(channel.Id);
        try
        {
            using var aes = new AesGcm(masterKey, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            channel.WebhookCiphertext = nonce.Concat(ciphertext).Concat(tag).ToArray();
            channel.WebhookProtectionMode = CurrentProtectionMode;
            channel.WebhookKeyVersion = CurrentKeyVersion;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    public string Unprotect(NotificationChannelEntity channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Id == Guid.Empty)
        {
            throw new ArgumentException("通知渠道标识无效。", nameof(channel));
        }
        if (channel.WebhookCiphertext is null || channel.WebhookCiphertext.Length <= NonceSize + TagSize ||
            channel.WebhookProtectionMode != CurrentProtectionMode ||
            !string.Equals(channel.WebhookKeyVersion, CurrentKeyVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("企业微信群机器人 Webhook 地址缺失，或使用了当前核心不支持的保护方式。请在后台重新保存地址。 ");
        }

        var payload = channel.WebhookCiphertext;
        var ciphertextLength = payload.Length - NonceSize - TagSize;
        var plaintext = new byte[ciphertextLength];
        var aad = CreateAssociatedData(channel.Id);
        try
        {
            using var aes = new AesGcm(masterKey, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize, ciphertextLength),
                payload.AsSpan(NonceSize + ciphertextLength, TagSize),
                plaintext,
                aad);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("企业微信群机器人 Webhook 地址无法由当前核心配置卷中的密钥解密。", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(aad);
        }
    }

    private static byte[] ParseMasterKey(string masterKeyBase64)
    {
        try
        {
            var key = Convert.FromBase64String(masterKeyBase64);
            if (key.Length == 32)
            {
                return key;
            }
            CryptographicOperations.ZeroMemory(key);
        }
        catch (FormatException)
        {
        }
        throw new ArgumentException("通知 Webhook 主密钥格式无效。", nameof(masterKeyBase64));
    }

    private static byte[] CreateAssociatedData(Guid channelId) =>
        Encoding.UTF8.GetBytes($"VisiCore.NotificationWebhookAddress:{CurrentKeyVersion}:{channelId:N}");
}
