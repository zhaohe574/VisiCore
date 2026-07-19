using System.Text.Json;

namespace VisiCore.Core;

public static class WeComWebhookMessageTypes
{
    public const string Markdown = "markdown";
    public const string Text = "text";
}

public sealed record WeComWebhookConfiguration(
    string MessageType,
    IReadOnlyList<string> MentionedList,
    IReadOnlyList<string> MentionedMobileList,
    string? SecurityKeyword)
{
    public const int MaximumMentionRecipients = 1_000;
    public const int MaximumMentionValueLength = 128;
    public const int MaximumMarkdownMentionRecipients = 40;
    public const int MaximumSecurityKeywordLength = 128;

    public static bool TryParse(JsonElement configuration, out WeComWebhookConfiguration parsed, out string error)
    {
        parsed = new WeComWebhookConfiguration(WeComWebhookMessageTypes.Markdown, [], [], null);
        error = string.Empty;
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            error = "企业微信群机器人配置必须是 JSON 对象。";
            return false;
        }
        foreach (var property in configuration.EnumerateObject())
        {
            if (property.Name is not ("messageType" or "mentionedList" or "mentionedMobileList" or "securityKeyword"))
            {
                error = "企业微信群机器人配置包含不支持的字段。";
                return false;
            }
        }

        var messageType = WeComWebhookMessageTypes.Markdown;
        if (configuration.TryGetProperty("messageType", out var messageTypeElement))
        {
            if (messageTypeElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(messageTypeElement.GetString()))
            {
                error = "企业微信群机器人消息格式无效。";
                return false;
            }
            messageType = messageTypeElement.GetString()!.Trim().ToLowerInvariant();
        }
        if (messageType is not (WeComWebhookMessageTypes.Markdown or WeComWebhookMessageTypes.Text))
        {
            error = "企业微信群机器人只支持 Markdown 或文本消息。";
            return false;
        }

        if (!TryReadMentionList(configuration, "mentionedList", false, out var mentionedList, out error) ||
            !TryReadMentionList(configuration, "mentionedMobileList", true, out var mentionedMobileList, out error))
        {
            return false;
        }
        if (messageType == WeComWebhookMessageTypes.Markdown && mentionedMobileList.Count > 0)
        {
            error = "企业微信群机器人 Markdown 消息不支持按手机号 @提醒。";
            return false;
        }
        if (messageType == WeComWebhookMessageTypes.Markdown && mentionedList.Any(item => item.Equals("@all", StringComparison.OrdinalIgnoreCase)))
        {
            error = "企业微信群机器人 Markdown 消息只能 @指定成员 UserID。";
            return false;
        }
        if (messageType == WeComWebhookMessageTypes.Markdown && mentionedList.Count > MaximumMarkdownMentionRecipients)
        {
            error = $"企业微信群机器人 Markdown 消息最多配置 {MaximumMarkdownMentionRecipients} 个提醒成员。";
            return false;
        }

        if (!TryReadSecurityKeyword(configuration, out var securityKeyword, out error))
        {
            return false;
        }

        parsed = new WeComWebhookConfiguration(messageType, mentionedList, mentionedMobileList, securityKeyword);
        return true;
    }

    private static bool TryReadMentionList(
        JsonElement configuration,
        string propertyName,
        bool isMobileList,
        out IReadOnlyList<string> values,
        out string error)
    {
        values = [];
        error = string.Empty;
        if (!configuration.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > MaximumMentionRecipients)
        {
            error = $"企业微信群机器人 {propertyName} 配置无效。";
            return false;
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"企业微信群机器人 {propertyName} 必须是字符串数组。";
                return false;
            }
            var value = item.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumMentionValueLength ||
                value.Contains('\r') || value.Contains('\n') ||
                (isMobileList && !IsMobileMention(value)) ||
                (!isMobileList && !IsUserIdMention(value)))
            {
                error = $"企业微信群机器人 {propertyName} 包含无效值。";
                return false;
            }
            unique.Add(value);
        }
        values = unique.ToList();
        return true;
    }

    private static bool IsMobileMention(string value) => value.Equals("@all", StringComparison.OrdinalIgnoreCase) || value.Length is >= 6 and <= 32 &&
        value.All(character => char.IsDigit(character) || character is '+' or '-');

    private static bool IsUserIdMention(string value) => value.Equals("@all", StringComparison.OrdinalIgnoreCase) ||
        value.All(character => char.IsLetterOrDigit(character) || character is '@' or '_' or '-' or '.');

    private static bool TryReadSecurityKeyword(JsonElement configuration, out string? securityKeyword, out string error)
    {
        securityKeyword = null;
        error = string.Empty;
        if (!configuration.TryGetProperty("securityKeyword", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            error = "企业微信群机器人安全关键词无效。";
            return false;
        }
        var value = element.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (value.Length > MaximumSecurityKeywordLength || value.Contains('\r') || value.Contains('\n'))
        {
            error = "企业微信群机器人安全关键词无效。";
            return false;
        }
        securityKeyword = value;
        return true;
    }
}
