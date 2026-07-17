using System.Text.Json;
using VideoPlatform.Core;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class WeComWebhookConfigurationTests
{
    [Fact(DisplayName = "旧企业微信群机器人空配置默认使用 Markdown")]
    public void EmptyConfigurationUsesMarkdownWithoutMentions()
    {
        using var document = JsonDocument.Parse("{}");

        var success = WeComWebhookConfiguration.TryParse(document.RootElement, out var configuration, out _);

        Assert.True(success);
        Assert.Equal(WeComWebhookMessageTypes.Markdown, configuration.MessageType);
        Assert.Empty(configuration.MentionedList);
        Assert.Empty(configuration.MentionedMobileList);
        Assert.Null(configuration.SecurityKeyword);
    }

    [Fact(DisplayName = "企业微信群机器人拒绝携带 Webhook 等未授权配置字段")]
    public void ConfigurationRejectsUnknownOrIncompatibleFields()
    {
        using var unknownField = JsonDocument.Parse("{\"messageType\":\"markdown\",\"webhookUrl\":\"https://example.test\"}");
        using var markdownMobiles = JsonDocument.Parse("{\"messageType\":\"markdown\",\"mentionedMobileList\":[\"13800000000\"]}");

        Assert.False(WeComWebhookConfiguration.TryParse(unknownField.RootElement, out _, out _));
        Assert.False(WeComWebhookConfiguration.TryParse(markdownMobiles.RootElement, out _, out _));
    }

    [Fact(DisplayName = "企业微信群机器人文本配置允许全体、手机号和安全关键词")]
    public void TextConfigurationSupportsMentionsAndSecurityKeyword()
    {
        using var document = JsonDocument.Parse("{\"messageType\":\"text\",\"mentionedList\":[\"@all\",\"zhangsan\"],\"mentionedMobileList\":[\"13800000000\",\"@all\"],\"securityKeyword\":\"视频告警\"}");

        var success = WeComWebhookConfiguration.TryParse(document.RootElement, out var configuration, out _);

        Assert.True(success);
        Assert.Equal(WeComWebhookMessageTypes.Text, configuration.MessageType);
        Assert.Equal(["@all", "zhangsan"], configuration.MentionedList);
        Assert.Equal(["13800000000", "@all"], configuration.MentionedMobileList);
        Assert.Equal("视频告警", configuration.SecurityKeyword);
    }

    [Fact(DisplayName = "企业微信群机器人限制 Markdown 提醒成员数量")]
    public void MarkdownConfigurationLimitsMentionRecipients()
    {
        var mentionedList = Enumerable.Range(0, WeComWebhookConfiguration.MaximumMarkdownMentionRecipients + 1)
            .Select(index => $"user{index}");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { messageType = "markdown", mentionedList }));

        Assert.False(WeComWebhookConfiguration.TryParse(document.RootElement, out _, out _));
    }
}
