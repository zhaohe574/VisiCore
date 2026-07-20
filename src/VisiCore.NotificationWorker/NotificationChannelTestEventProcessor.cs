using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.NotificationWorker;

public sealed class NotificationChannelTestEventProcessor(
    PlatformDbContext dbContext,
    NotificationDispatcher dispatcher)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessAsync(OutboxEventEntity outboxEvent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<NotificationChannelTestRequestedPayload>(outboxEvent.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("通知渠道测试事件载荷无效。 ");
        if (!outboxEvent.EventType.Equals(NotificationChannelTestRequestedPayload.EventType, StringComparison.Ordinal) ||
            payload.ChannelId != outboxEvent.AggregateId ||
            !outboxEvent.AggregateType.Equals(NotificationChannelTestRequestedPayload.AggregateType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("通知渠道测试事件目标不一致。 ");
        }

        var channel = await dbContext.NotificationChannels.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == payload.ChannelId, cancellationToken)
            ?? throw new InvalidOperationException("测试通知渠道不存在。 ");

        // 测试由具备管理权限的用户显式发起，允许验证已停用渠道的密钥和消息配置。
        await dispatcher.SendTestAsync(channel, outboxEvent.OccurredAt, cancellationToken);
    }
}
