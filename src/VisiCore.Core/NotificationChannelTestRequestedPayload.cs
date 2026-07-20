namespace VisiCore.Core;

public sealed record NotificationChannelTestRequestedPayload(Guid ChannelId)
{
    public const string EventType = "notification.channel.test.requested";
    public const string AggregateType = "notification_channel";
}
