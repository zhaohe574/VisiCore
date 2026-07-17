using System.Text.Json;
using VideoPlatform.Api;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.Tests;

public sealed class NotificationChannelRequestValidatorTests
{
    [Theory(DisplayName = "企业微信群机器人仅接受完整受控 Webhook 地址")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=00000000-0000-4000-8000-000000000001")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=test-key&debug=1")]
    public void WeComWebhookAddressAcceptsControlledWebhook(string value)
    {
        var valid = NotificationChannelRequestValidator.TryValidateWebhookAddress(value, out var error);

        Assert.True(valid);
        Assert.Empty(error);
    }

    [Theory(DisplayName = "企业微信群机器人拒绝不完整或非受控 Webhook 地址")]
    [InlineData("00000000-0000-4000-8000-000000000001")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send")]
    [InlineData("http://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=test-key")]
    [InlineData("https://example.test/cgi-bin/webhook/send?key=test-key")]
    public void WeComWebhookAddressRejectsIncompleteOrUncontrolledAddress(string value)
    {
        var valid = NotificationChannelRequestValidator.TryValidateWebhookAddress(value, out var error);

        Assert.False(valid);
        Assert.Contains("Webhook 地址", error);
    }

    [Theory(DisplayName = "SMTP 密钥引用保持环境变量引用格式")]
    [InlineData("smtp_password")]
    [InlineData("smtp.password-v2")]
    public void EmailSecretReferenceAcceptsEnvironmentReference(string value)
    {
        var valid = NotificationChannelRequestValidator.TryValidateSecretReference(value, out var error);

        Assert.True(valid);
        Assert.Empty(error);
    }

    [Fact(DisplayName = "SMTP 密钥引用拒绝完整地址")]
    public void EmailSecretReferenceRejectsAddress()
    {
        var valid = NotificationChannelRequestValidator.TryValidateSecretReference(
            "https://example.test/secret",
            out var error);

        Assert.False(valid);
        Assert.Contains("密钥引用", error);
    }

    [Fact(DisplayName = "企业微信群机器人响应不回显地址或旧密钥引用")]
    public void WeComChannelResponseDoesNotSerializeCredentialFields()
    {
        using var configuration = JsonDocument.Parse("{}");
        var response = new NotificationChannelResponse(
            Guid.NewGuid(),
            "监控预警",
            NotificationChannelType.WeComWebhook,
            configuration.RootElement.Clone(),
            null,
            true,
            DateTimeOffset.UtcNow,
            true);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"webhookConfigured\":true", json);
        Assert.DoesNotContain("secretReference", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhookUrl", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "企业微信群机器人更新配置沿用受控配置校验")]
    public void WeComConfigurationRejectsWebhookAddressField()
    {
        using var document = JsonDocument.Parse("{\"messageType\":\"markdown\",\"webhookUrl\":\"https://example.test\"}");

        var valid = NotificationChannelRequestValidator.TryValidateConfiguration(
            NotificationChannelType.WeComWebhook,
            document.RootElement,
            out var error);

        Assert.False(valid);
        Assert.Contains("不支持", error);
    }
}
