using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VisiCore.Core;
using VisiCore.NotificationWorker;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.NotificationWorker.Tests;

public sealed class NotificationChannelTestEventProcessorTests
{
    private const string WebhookAddress = "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=test-key";

    [Fact(DisplayName = "通知渠道测试不创建真实告警或投递记录，并允许测试已停用渠道")]
    public async Task ProcessTestSendsMessageWithoutPersistingAlertOrDelivery()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new PlatformDbContext(options);
        var channel = new NotificationChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = "停用企业微信群",
            Type = NotificationChannelType.WeComWebhook,
            ConfigurationJson = "{}",
            SecretReference = string.Empty,
            Enabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        new NotificationWebhookAddressProtector().Protect(channel, WebhookAddress);
        dbContext.NotificationChannels.Add(channel);
        await dbContext.SaveChangesAsync();

        string? sentContent = null;
        var dispatcher = CreateDispatcher(new RecordingHttpMessageHandler(request =>
        {
            using var requestBody = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            sentContent = requestBody.RootElement.GetProperty("markdown").GetProperty("content").GetString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"errcode\":0}", Encoding.UTF8, "application/json")
            };
        }));
        var occurredAt = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.FromHours(8));
        var outboxEvent = new OutboxEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = NotificationChannelTestRequestedPayload.EventType,
            AggregateType = NotificationChannelTestRequestedPayload.AggregateType,
            AggregateId = channel.Id,
            PayloadJson = JsonSerializer.Serialize(new NotificationChannelTestRequestedPayload(channel.Id)),
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt
        };

        var processor = new NotificationChannelTestEventProcessor(dbContext, dispatcher);
        await processor.ProcessAsync(outboxEvent, CancellationToken.None);

        Assert.Contains("通知渠道测试", sentContent);
        Assert.Contains("渠道：停用企业微信群", sentContent);
        Assert.Empty(await dbContext.AlertIncidents.ToListAsync());
        Assert.Empty(await dbContext.NotificationDeliveries.ToListAsync());
    }

    private static NotificationDispatcher CreateDispatcher(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .Build();
        return new NotificationDispatcher(
            new HttpClient(handler),
            new NotificationSecretResolver(configuration),
            new NotificationWebhookAddressProtector(),
            Options.Create(new NotificationWorkerOptions()));
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
