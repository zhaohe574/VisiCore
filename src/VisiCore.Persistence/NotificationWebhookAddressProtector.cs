using System.Security.Cryptography;
using System.Text;

namespace VisiCore.Persistence;

public sealed class NotificationWebhookAddressProtector
{
    public const string CurrentKeyVersion = "dpapi-local-machine-v1";
    public const NotificationChannelWebhookProtectionMode CurrentProtectionMode = NotificationChannelWebhookProtectionMode.WindowsDpapiLocalMachine;
    private const string EntropyPurpose = "VisiCore.NotificationWebhookAddress";

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
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("当前通知 Webhook 的数据库加密仅支持 Windows DPAPI。请在受控 Windows 主机上保存地址。 ");
        }

        var plaintext = Encoding.UTF8.GetBytes(webhookAddress);
        var entropy = CreateEntropy(channel.Id);
        try
        {
            channel.WebhookCiphertext = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.LocalMachine);
            channel.WebhookProtectionMode = CurrentProtectionMode;
            channel.WebhookKeyVersion = CurrentKeyVersion;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public string Unprotect(NotificationChannelEntity channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.Id == Guid.Empty)
        {
            throw new ArgumentException("通知渠道标识无效。", nameof(channel));
        }
        if (channel.WebhookCiphertext is null || channel.WebhookCiphertext.Length == 0 ||
            channel.WebhookProtectionMode is null || string.IsNullOrWhiteSpace(channel.WebhookKeyVersion))
        {
            throw new InvalidOperationException("企业微信群机器人尚未在数据库保存完整 Webhook 地址，请在后台编辑渠道并保存地址。 ");
        }
        if (channel.WebhookProtectionMode != CurrentProtectionMode ||
            !channel.WebhookKeyVersion.Equals(CurrentKeyVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("企业微信群机器人 Webhook 地址使用了当前服务不支持的保护方式或密钥版本。 ");
        }
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("当前通知 Webhook 的数据库解密仅支持保存地址的受控 Windows 主机。 ");
        }

        var entropy = CreateEntropy(channel.Id);
        byte[] plaintext;
        try
        {
            plaintext = ProtectedData.Unprotect(channel.WebhookCiphertext, entropy, DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("企业微信群机器人 Webhook 地址不能由当前 Windows 主机解密。迁移到新主机后请在后台重新保存地址。 ", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }

        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] CreateEntropy(Guid channelId)
    {
        var material = Encoding.UTF8.GetBytes($"{EntropyPurpose}:{CurrentKeyVersion}:{channelId:N}");
        try
        {
            return SHA256.HashData(material);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }
}
