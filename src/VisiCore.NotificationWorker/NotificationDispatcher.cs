using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.NotificationWorker;

public sealed class NotificationDispatcher(
    HttpClient httpClient,
    NotificationSecretResolver secretResolver,
    NotificationWebhookAddressProtector webhookAddressProtector,
    IOptions<NotificationWorkerOptions> workerOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string NotificationTestIncidentType = "notification_test";
    private readonly int _deliveryTimeoutMilliseconds = checked(workerOptions.Value.DeliveryTimeoutSeconds * 1000);

    public Task SendAsync(
        NotificationChannelEntity channel,
        AlertIncidentEntity incident,
        NotificationEventType eventType,
        CancellationToken cancellationToken) => SendAsync(channel, incident, eventType, null, cancellationToken);

    public Task SendAsync(
        NotificationChannelEntity channel,
        AlertIncidentEntity incident,
        NotificationEventType eventType,
        NotificationDeliveryContext? context,
        CancellationToken cancellationToken) => channel.Type switch
    {
        NotificationChannelType.Email => SendEmailAsync(channel, incident, eventType, context, cancellationToken),
        NotificationChannelType.WeComWebhook => SendWeComAsync(channel, incident, eventType, context, cancellationToken),
        _ => throw new NotSupportedException("通知渠道类型不受支持。")
    };

    public Task SendTestAsync(NotificationChannelEntity channel, DateTimeOffset requestedAt, CancellationToken cancellationToken) =>
        SendAsync(
            channel,
            new AlertIncidentEntity
            {
                Id = Guid.Empty,
                ResourceType = NotificationTestIncidentType,
                ResourceName = channel.Name,
                IncidentType = NotificationTestIncidentType,
                OpenedAt = requestedAt,
                LastObservedAt = requestedAt
            },
            NotificationEventType.Opened,
            new NotificationDeliveryContext("平台运维", NotificationTestIncidentType),
            cancellationToken);

    private async Task SendEmailAsync(
        NotificationChannelEntity channel,
        AlertIncidentEntity incident,
        NotificationEventType eventType,
        NotificationDeliveryContext? context,
        CancellationToken cancellationToken)
    {
        var configuration = JsonSerializer.Deserialize<EmailChannelConfiguration>(channel.ConfigurationJson, JsonOptions)
            ?? throw new InvalidOperationException("邮件通知配置无效。 ");
        configuration.Validate();
        var secretJson = secretResolver.Resolve(channel.SecretReference);
        var secret = JsonSerializer.Deserialize<EmailChannelSecret>(secretJson, JsonOptions)
            ?? throw new InvalidOperationException("邮件通知密钥格式无效。 ");
        if (string.IsNullOrWhiteSpace(secret.Username) || string.IsNullOrWhiteSpace(secret.Password))
        {
            throw new InvalidOperationException("邮件通知密钥缺少账号或密码。 ");
        }

        var stateText = IncidentStateText(incident, eventType);
        var resourceName = SanitizeHeader(incident.ResourceName);
        using var message = new MailMessage
        {
            From = new MailAddress(configuration.From),
            Subject = $"{SanitizeHeader(configuration.SubjectPrefix)}{stateText}：{resourceName}",
            Body = BuildPlainTextBody(incident, eventType, context),
            IsBodyHtml = false
        };
        foreach (var recipient in configuration.Recipients)
        {
            message.To.Add(new MailAddress(recipient));
        }

        using var smtpClient = new SmtpClient(configuration.Host, configuration.Port)
        {
            EnableSsl = configuration.UseSsl,
            Credentials = new NetworkCredential(secret.Username, secret.Password),
            Timeout = _deliveryTimeoutMilliseconds
        };
        cancellationToken.ThrowIfCancellationRequested();
        await smtpClient.SendMailAsync(message, cancellationToken);
    }

    private async Task SendWeComAsync(
        NotificationChannelEntity channel,
        AlertIncidentEntity incident,
        NotificationEventType eventType,
        NotificationDeliveryContext? context,
        CancellationToken cancellationToken)
    {
        var webhookValue = webhookAddressProtector.Unprotect(channel);
        if (!WeComWebhookAddress.TryParse(webhookValue, out var webhookUri) || webhookUri is null)
        {
            throw new InvalidOperationException("企业微信群机器人 Webhook 地址不符合受控接口要求。 ");
        }
        var configuration = ParseWeComConfiguration(channel.ConfigurationJson);

        var stateText = IncidentStateText(incident, eventType);
        var color = eventType == NotificationEventType.Opened ? "warning" : "info";
        var regionName = string.IsNullOrWhiteSpace(context?.RegionName) ? "未归属" : context.RegionName;
        var deviceType = DeviceTypeText(context?.DeviceType, incident.ResourceType);
        var markdownMentions = configuration.MessageType == WeComWebhookMessageTypes.Markdown && configuration.MentionedList.Count > 0
            ? $"\n>提醒：{string.Join(" ", configuration.MentionedList.Select(item => $"<@{item}>"))}"
            : string.Empty;
        var securityKeyword = string.IsNullOrWhiteSpace(configuration.SecurityKeyword)
            ? string.Empty
            : $"\n>安全关键词：{configuration.SecurityKeyword}";
        var occurredAt = eventType == NotificationEventType.Opened ? incident.OpenedAt : incident.ResolvedAt;
        var content = IsNotificationTest(incident)
            ? $"**<font color=\"info\">{stateText}</font>**\n>渠道：{SanitizeMarkdown(incident.ResourceName)}\n>时间：{occurredAt:yyyy-MM-dd HH:mm:ss zzz}\n>说明：这是一条测试消息，不包含设备或告警信息。{markdownMentions}{securityKeyword}"
            : $"**<font color=\"{color}\">{stateText}</font>**\n>设备：{SanitizeMarkdown(incident.ResourceName)}\n>设备类型：{SanitizeMarkdown(deviceType)}\n>区域：{SanitizeMarkdown(regionName)}\n>时间：{occurredAt:yyyy-MM-dd HH:mm:ss zzz}{markdownMentions}{securityKeyword}";
        using var response = configuration.MessageType == WeComWebhookMessageTypes.Text
            ? await httpClient.PostAsJsonAsync(webhookUri, new
            {
                msgtype = WeComWebhookMessageTypes.Text,
                text = new
                {
                    content = EnsureWeComContentLength(AppendSecurityKeyword(BuildPlainTextBody(incident, eventType, context), configuration.SecurityKeyword), 2_048, "文本"),
                    mentioned_list = configuration.MentionedList,
                    mentioned_mobile_list = configuration.MentionedMobileList
                }
            }, cancellationToken)
            : await httpClient.PostAsJsonAsync(webhookUri, new
            {
                msgtype = WeComWebhookMessageTypes.Markdown,
                markdown = new { content = EnsureWeComContentLength(content, 4_096, "Markdown") }
            }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WeComWebhookResponse>(JsonOptions, cancellationToken);
        if (result?.ErrCode != 0)
        {
            throw new InvalidOperationException("企业微信群 Webhook 返回业务失败状态。 ");
        }
    }

    private static WeComWebhookConfiguration ParseWeComConfiguration(string configurationJson)
    {
        try
        {
            using var document = JsonDocument.Parse(configurationJson);
            if (WeComWebhookConfiguration.TryParse(document.RootElement, out var configuration, out var error))
            {
                return configuration;
            }
            throw new InvalidOperationException(error);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("企业微信群机器人配置不是有效 JSON。", exception);
        }
    }

    private static string BuildPlainTextBody(AlertIncidentEntity incident, NotificationEventType eventType, NotificationDeliveryContext? context)
    {
        if (IsNotificationTest(incident))
        {
            return $"通知渠道测试\r\n渠道：{SanitizePlainText(incident.ResourceName)}\r\n时间：{incident.OpenedAt:yyyy-MM-dd HH:mm:ss zzz}\r\n说明：这是一条测试消息，不包含设备或告警信息。";
        }
        var stateText = IncidentStateText(incident, eventType);
        var occurredAt = eventType == NotificationEventType.Opened ? incident.OpenedAt : incident.ResolvedAt;
        var regionName = string.IsNullOrWhiteSpace(context?.RegionName) ? "未归属" : context.RegionName;
        var deviceType = DeviceTypeText(context?.DeviceType, incident.ResourceType);
        return $"设备：{SanitizePlainText(incident.ResourceName)}\r\n设备类型：{SanitizePlainText(deviceType)}\r\n区域：{SanitizePlainText(regionName)}\r\n告警类型：{IncidentTypeText(incident.IncidentType)}\r\n状态：{stateText}\r\n时间：{occurredAt:yyyy-MM-dd HH:mm:ss zzz}\r\n事件编号：{incident.Id}";
    }

    private static string IncidentStateText(AlertIncidentEntity incident, NotificationEventType eventType) =>
        IsNotificationTest(incident)
            ? "通知渠道测试"
            : eventType == NotificationEventType.Opened
            ? $"{IncidentTypeText(incident.IncidentType)}告警"
            : $"{IncidentTypeText(incident.IncidentType)}恢复通知";

    private static bool IsNotificationTest(AlertIncidentEntity incident) =>
        incident.IncidentType.Equals(NotificationTestIncidentType, StringComparison.Ordinal);

    private static string IncidentTypeText(string incidentType) => incidentType.Equals(NotificationTestIncidentType, StringComparison.Ordinal)
        ? "通知渠道测试"
        : incidentType.Equals("clock_skew", StringComparison.Ordinal)
        ? "时钟偏差"
        : incidentType.Equals("upgrade_failed", StringComparison.Ordinal)
        ? "升级失败"
        : "设备掉线";

    private static string DeviceTypeText(string? deviceType, string resourceType)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceType) ? resourceType : deviceType;
        return normalized.Trim().ToLowerInvariant() switch
        {
            NotificationTestIncidentType => "测试消息",
            "upgrade_plan" => "升级计划",
            DeviceKinds.Camera => "摄像头",
            DeviceKinds.Recorder => "录像机",
            DeviceKinds.Matrix => "视频矩阵",
            DeviceKinds.Encoder => "编码器",
            DeviceKinds.Decoder => "解码器",
            DeviceKinds.Gateway => "视频网关",
            _ => "接入设备"
        };
    }

    private static string EnsureWeComContentLength(string content, int maximumBytes, string contentType)
    {
        if (Encoding.UTF8.GetByteCount(content) > maximumBytes)
        {
            throw new InvalidOperationException($"企业微信群机器人 {contentType} 消息超过 {maximumBytes} 字节限制。 ");
        }
        return content;
    }

    private static string AppendSecurityKeyword(string content, string? securityKeyword) =>
        string.IsNullOrWhiteSpace(securityKeyword) ? content : $"{content}\r\n{securityKeyword}";

    private static string SanitizeHeader(string value) => value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string SanitizePlainText(string value) => value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string SanitizeMarkdown(string value) => value.Replace("`", "'", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private sealed record WeComWebhookResponse(int? ErrCode);
}

public sealed class NotificationSecretResolver(IConfiguration configuration)
{
    public string Resolve(string secretReference)
    {
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            throw new InvalidOperationException("通知渠道未配置密钥引用。 ");
        }
        return configuration[$"NotificationSecrets:{secretReference}"]
            ?? throw new InvalidOperationException("通知渠道引用的运行时配置不存在。 ");
    }
}

public sealed record EmailChannelConfiguration(
    string Host,
    int Port,
    bool UseSsl,
    string From,
    IReadOnlyList<string> Recipients,
    string SubjectPrefix)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(From) || Recipients.Count == 0)
        {
            throw new InvalidOperationException("邮件通知渠道配置不完整。 ");
        }
        _ = new MailAddress(From);
        foreach (var recipient in Recipients)
        {
            _ = new MailAddress(recipient);
        }
    }
}

public sealed record EmailChannelSecret(string Username, string Password);

public sealed record NotificationDeliveryContext(string? RegionName, string DeviceType);
