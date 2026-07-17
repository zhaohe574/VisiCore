using System.Text.Json;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public static class NotificationChannelRequestValidator
{
    public static bool TryValidateWebhookAddress(string? value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 2_048)
        {
            error = "企业微信群机器人必须填写完整 Webhook 地址。";
            return false;
        }
        if (!WeComWebhookAddress.TryParse(value, out _))
        {
            error = "企业微信群机器人 Webhook 地址不符合受控接口要求。";
            return false;
        }

        return true;
    }

    public static bool TryValidateSecretReference(string? value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 128)
        {
            error = "密钥引用不能为空且不能超过 128 个字符。";
            return false;
        }

        if (value.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            error = "密钥引用仅支持字母、数字、连字符、下划线和点。";
            return false;
        }

        return true;
    }

    public static bool TryValidateConfiguration(NotificationChannelType type, JsonElement configuration, out string error)
    {
        error = string.Empty;
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            error = "通知渠道非敏感配置必须是 JSON 对象。";
            return false;
        }
        if (type == NotificationChannelType.WeComWebhook)
        {
            return WeComWebhookConfiguration.TryParse(configuration, out _, out error);
        }
        if (type != NotificationChannelType.Email)
        {
            error = "通知渠道类型无效。";
            return false;
        }
        var valid = configuration.TryGetProperty("host", out var host) && host.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(host.GetString()) &&
                    configuration.TryGetProperty("port", out var port) && port.TryGetInt32(out var portValue) && portValue is >= 1 and <= 65535 &&
                    configuration.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(from.GetString()) &&
                    configuration.TryGetProperty("recipients", out var recipients) && recipients.ValueKind == JsonValueKind.Array && recipients.GetArrayLength() > 0;
        if (!valid)
        {
            error = "SMTP 邮件渠道的主机、端口、发件地址或收件地址配置无效。";
        }
        return valid;
    }
}
