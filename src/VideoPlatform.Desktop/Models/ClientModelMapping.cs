using System.Text.Json;
using Generated = VideoPlatform.Client.Generated;

namespace VideoPlatform.Desktop.Models;

// 保留桌面模型的显示属性和数组约定，协议字段由生成类型在编译时校验。
internal static class ClientModelMapping
{
    public static User From(Generated.UserDto value) => new(value.Id, value.Username, value.DisplayName ?? "", value.Phone,
        value.Status, value.Permissions.ToArray(), value.RoleIds.ToArray());

    public static LoginResponse From(Generated.AuthResponse value) => new(From(value.User), value.AccessToken, value.ExpiresAt);

    public static Channel From(Generated.ChannelDto value) => new(value.Id, value.DeviceId, value.DeviceName, value.DeviceChannel,
        value.Name, value.Alias, value.Model, value.Status, value.UnitId, value.PtzCapable, value.Codec);

    public static Page<Channel> From(Generated.PagedChannelResponse value) => new(value.Items.Select(From).ToArray(), value.Total, value.Page, value.PageSize);

    public static LayoutDto From(Generated.AdministrationLayoutDto value) => new(value.Id, value.Name, value.Kind, value.Shared,
        value.Layout, value.IntervalSeconds, value.ChannelIds.ToArray());

    public static Generated.LayoutRequest To(LayoutRequest value) => new()
    {
        Name = value.Name, Kind = value.Kind, Shared = value.Shared, Layout = value.Layout,
        IntervalSeconds = value.IntervalSeconds, ChannelIds = value.ChannelIds
    };

    public static OrganizationNode From(Generated.OrganizationNodeDto value) => new(value.Id, value.Name, value.Code, value.Status, value.ParentId);
    public static Organization From(Generated.OrganizationTreeDto value) => new(value.Workshops.Select(From).ToArray(),
        value.Areas.Select(From).ToArray(), value.Units.Select(From).ToArray());

    public static MediaSession From(Generated.LiveSessionDto value) => new(value.Id.ToString(), value.ChannelId, value.StreamType,
        value.State, value.ExpiresAt, value.RtspUrl, value.HttpFlvUrl, value.HlsUrl, value.Codec, value.Transcoded, HttpTsUrl: value.HttpTsUrl);

    public static MediaSession From(Generated.PlaybackSessionDto value) => new(value.Id.ToString(), value.ChannelId, value.StreamType,
        value.State, value.ExpiresAt, value.RtspUrl, value.HttpFlvUrl, value.HlsUrl, value.Codec, value.Transcoded,
        value.Start, value.End, value.CurrentTime, value.Progress, value.Speed,
        value.Segments.Select(segment => new RecordingSegment(segment.Start, segment.End)).ToArray(), value.HttpTsUrl);

    public static Recording From(Generated.RecordingDto value) => new(value.FileName, value.Start, value.End, value.FileSize,
        value.FileType, value.StreamType, checked((uint)value.FileIndex));

    public static Alarm From(Generated.AlarmDto value) => new(value.Id, value.DeviceId, value.DeviceName, value.ChannelId,
        value.ChannelName, value.EventType, value.OccurredAt, value.State, value.Recovered, value.OwnerId, value.OwnerName,
        value.Note, value.ImageAvailable, Payload(value.Payload));

    public static Alarm From(Generated.AlarmDetailDto value) => new(value.Id, value.DeviceId, value.DeviceName, value.ChannelId,
        value.ChannelName, value.EventType, value.OccurredAt, value.State, value.Recovered, value.OwnerId, value.OwnerName,
        value.Note, value.ImageAvailable, Payload(value.Payload),
        value.History.Select(item => new AlarmHistory(item.Id, item.Action, item.Note, item.Username ?? "", item.CreatedAt)).ToArray());

    public static Page<Alarm> From(Generated.PagedAlarmResponse value) => new(value.Items.Select(From).ToArray(), value.Total, value.Page, value.PageSize);

    public static ExportJob From(Generated.ExportDto value) => new(value.Id.ToString(), value.ChannelId, value.ChannelName,
        value.Start, value.End, value.State, value.Progress, value.Error, value.FileName, value.FileSize, value.CreatedAt, value.ExpiresAt);

    public static Page<ExportJob> From(Generated.PagedExportResponse value) => new(value.Items.Select(From).ToArray(), value.Total, value.Page, value.PageSize);

    public static Release From(Generated.LatestReleaseDto value) => new(value.Id, value.Version, value.FileName, value.Sha256,
        value.FileSize, value.ReleaseNotes, value.MinimumVersion, value.ForceUpdate, value.Status, value.PublishedAt, value.DownloadCount, value.DownloadUrl);

    private static JsonElement? Payload(object? value) => value is null ? null : JsonSerializer.SerializeToElement(value);
}
