using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VisiCore.Core;
using VisiCore.NotificationWorker;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.NotificationWorker.Tests;

public sealed class NotificationDispatcherTests
{
    private const string DefaultWebhookAddress = "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=test-key";
    private static readonly NotificationWebhookAddressProtector WebhookAddressProtector = new();

    [Fact(DisplayName = "企业微信投递同时校验 HTTP 与业务成功状态")]
    public async Task WeComDeliveryAcceptsSuccessfulBusinessResponse()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            Assert.Equal("qyapi.weixin.qq.com", request.RequestUri?.Host);
            Assert.Equal("/cgi-bin/webhook/send", request.RequestUri?.AbsolutePath);
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var content = payload.RootElement.GetProperty("markdown").GetProperty("content").GetString();
            Assert.Contains("设备类型：摄像头", content);
            Assert.Contains("区域：园区 / 北区", content);
            return JsonResponse("{\"errcode\":0,\"errmsg\":\"ok\"}");
        });
        var dispatcher = CreateDispatcher(handler);

        await dispatcher.SendAsync(
            CreateChannel(),
            CreateIncident(),
            NotificationEventType.Opened,
            new NotificationDeliveryContext("园区 / 北区", DeviceKinds.Camera),
            CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact(DisplayName = "企业微信 Markdown 投递附带指定成员提醒和安全关键词")]
    public async Task WeComMarkdownDeliveryAddsUserMentionsAndSecurityKeyword()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("markdown", payload.RootElement.GetProperty("msgtype").GetString());
            var content = payload.RootElement.GetProperty("markdown").GetProperty("content").GetString();
            Assert.Contains("<@zhangsan>", content);
            Assert.Contains("视频告警", content);
            return JsonResponse("{\"errcode\":0}");
        });
        var dispatcher = CreateDispatcher(handler);
        var configuration = JsonSerializer.Serialize(new
        {
            messageType = "markdown",
            mentionedList = new[] { "zhangsan" },
            mentionedMobileList = Array.Empty<string>(),
            securityKeyword = "视频告警"
        });

        await dispatcher.SendAsync(CreateChannel(configuration), CreateIncident(), NotificationEventType.Opened, CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact(DisplayName = "企业微信测试消息保留渠道配置且不暴露设备告警信息")]
    public async Task WeComTestDeliveryUsesTestContentWithoutDeviceAlertDetails()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("markdown", payload.RootElement.GetProperty("msgtype").GetString());
            var content = payload.RootElement.GetProperty("markdown").GetProperty("content").GetString();
            Assert.Contains("通知渠道测试", content);
            Assert.Contains("渠道：企业微信测试通道", content);
            Assert.Contains("<@zhangsan>", content);
            Assert.Contains("视频告警", content);
            Assert.DoesNotContain("测试摄像头", content);
            Assert.DoesNotContain("设备类型", content);
            Assert.DoesNotContain("告警类型", content);
            return JsonResponse("{\"errcode\":0}");
        });
        var dispatcher = CreateDispatcher(handler);
        var configuration = JsonSerializer.Serialize(new
        {
            messageType = "markdown",
            mentionedList = new[] { "zhangsan" },
            mentionedMobileList = Array.Empty<string>(),
            securityKeyword = "视频告警"
        });

        await dispatcher.SendTestAsync(
            CreateChannel(configuration),
            new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.FromHours(8)),
            CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact(DisplayName = "企业微信文本投递映射成员、手机号和全体提醒字段")]
    public async Task WeComTextDeliveryMapsMentionLists()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("text", payload.RootElement.GetProperty("msgtype").GetString());
            var text = payload.RootElement.GetProperty("text");
            Assert.Contains("@all", text.GetProperty("mentioned_list").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("zhangsan", text.GetProperty("mentioned_list").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("13800000000", text.GetProperty("mentioned_mobile_list").EnumerateArray().Select(item => item.GetString()));
            Assert.Contains("视频告警", text.GetProperty("content").GetString());
            return JsonResponse("{\"errcode\":0}");
        });
        var dispatcher = CreateDispatcher(handler);
        var configuration = JsonSerializer.Serialize(new
        {
            messageType = "text",
            mentionedList = new[] { "@all", "zhangsan" },
            mentionedMobileList = new[] { "13800000000" },
            securityKeyword = "视频告警"
        });

        await dispatcher.SendAsync(CreateChannel(configuration), CreateIncident(), NotificationEventType.Opened, CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact(DisplayName = "企业微信 HTTP 成功但业务失败会判定为投递失败")]
    public async Task WeComDeliveryRejectsBusinessFailureResponse()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("{\"errcode\":93000,\"errmsg\":\"failed\"}"));
        var dispatcher = CreateDispatcher(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync(CreateChannel(), CreateIncident(), NotificationEventType.Opened, CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact(DisplayName = "企业微信投递拒绝只保存历史引用而未保存数据库地址的渠道")]
    public async Task WeComDeliveryRejectsLegacySecretReferenceWithoutDatabaseWebhook()
    {
        var webhookKey = Guid.NewGuid().ToString();
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("{\"errcode\":0}"));
        var dispatcher = CreateDispatcher(handler);
        var channel = CreateChannel();
        channel.SecretReference = webhookKey;
        channel.WebhookCiphertext = null;
        channel.WebhookProtectionMode = null;
        channel.WebhookKeyVersion = null;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync(channel, CreateIncident(), NotificationEventType.Opened, CancellationToken.None));

        Assert.Contains("尚未在数据库保存完整 Webhook 地址", exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory(DisplayName = "企业微信投递拒绝非受控 Webhook 地址")]
    [InlineData("http://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=test-key")]
    [InlineData("https://example.com/cgi-bin/webhook/send?key=test-key")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/other?key=test-key")]
    [InlineData("https://qyapi.weixin.qq.com/cgi-bin/webhook/send")]
    public async Task WeComDeliveryRejectsUncontrolledWebhook(string webhook)
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("{\"errcode\":0}"));
        var dispatcher = CreateDispatcher(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync(CreateChannel(webhookAddress: webhook), CreateIncident(), NotificationEventType.Opened, CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact(DisplayName = "企业微信数据库密文不能复制到其他渠道使用")]
    public void WeComWebhookCiphertextIsBoundToChannelIdentifier()
    {
        var source = CreateChannel();
        var copied = new NotificationChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = "复制渠道",
            Type = NotificationChannelType.WeComWebhook,
            ConfigurationJson = "{}",
            WebhookCiphertext = source.WebhookCiphertext,
            WebhookProtectionMode = source.WebhookProtectionMode,
            WebhookKeyVersion = source.WebhookKeyVersion,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var exception = Assert.Throws<InvalidOperationException>(() => WebhookAddressProtector.Unprotect(copied));

        Assert.Contains("不能由当前 Windows 主机解密", exception.Message);
    }

    [Fact(DisplayName = "通知锁定时长必须覆盖投递超时和释放余量")]
    public void NotificationWorkerOptionsRequireSufficientLease()
    {
        Assert.Throws<InvalidOperationException>(() => new NotificationWorkerOptions
        {
            LockSeconds = 30,
            DeliveryTimeoutSeconds = 20
        }.Validate());

        new NotificationWorkerOptions
        {
            LockSeconds = 35,
            DeliveryTimeoutSeconds = 20
        }.Validate();
    }

    [Fact(DisplayName = "通知投递达到最大次数后进入死信并停止自动重试")]
    public void NotificationDeliveryFailurePolicyDeadLettersExhaustedDelivery()
    {
        var delivery = new NotificationDeliveryEntity
        {
            Attempts = 2,
            LockedBy = "worker",
            LockedUntil = DateTimeOffset.UtcNow
        };
        var occurredAt = new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.FromHours(8));

        NotificationDeliveryFailurePolicy.Apply(delivery, "InvalidOperationException", new NotificationWorkerOptions { MaxAttempts = 2 }, occurredAt);

        Assert.Equal(NotificationDeliveryStatus.DeadLettered, delivery.Status);
        Assert.Equal(DateTimeOffset.MaxValue, delivery.NextAttemptAt);
        Assert.Null(delivery.LockedBy);
        Assert.Null(delivery.LockedUntil);
    }

    [Fact(DisplayName = "通知投递在剩余重试次数内保留失败状态和退避时间")]
    public void NotificationDeliveryFailurePolicySchedulesRetry()
    {
        var delivery = new NotificationDeliveryEntity { Attempts = 2 };
        var occurredAt = new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.FromHours(8));

        NotificationDeliveryFailurePolicy.Apply(delivery, "HttpRequestException", new NotificationWorkerOptions { MaxAttempts = 3 }, occurredAt);

        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(occurredAt.AddSeconds(20), delivery.NextAttemptAt);
    }

    private static NotificationDispatcher CreateDispatcher(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .Build();
        return new NotificationDispatcher(
            new HttpClient(handler),
            new NotificationSecretResolver(configuration),
            WebhookAddressProtector,
            Options.Create(new NotificationWorkerOptions()));
    }

    private static NotificationChannelEntity CreateChannel(string configurationJson = "{}", string webhookAddress = DefaultWebhookAddress)
    {
        var channel = new NotificationChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = "企业微信测试通道",
            Type = NotificationChannelType.WeComWebhook,
            ConfigurationJson = configurationJson,
            SecretReference = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        };
        WebhookAddressProtector.Protect(channel, webhookAddress);
        return channel;
    }

    private static AlertIncidentEntity CreateIncident() => new()
    {
        Id = Guid.NewGuid(),
        ResourceType = "camera",
        ResourceName = "测试摄像头",
        OpenedAt = DateTimeOffset.UtcNow,
        LastObservedAt = DateTimeOffset.UtcNow
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
