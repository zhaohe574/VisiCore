using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoPlatform.Desktop.Models;

public sealed record Page<T>(T[] Items, long Total, [property: JsonPropertyName("page")] int PageNumber = 1, int PageSize = 50);
public sealed record ApiError(string Code, string Message, string TraceId);
public sealed record User(long Id, string Username, string DisplayName, string? Phone, string Status, string[] Permissions, long[] RoleIds)
{
    public bool Can(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
}
public sealed record LoginRequest(string Username, string Password, string ClientType = "desktop", string ClientVersion = "2.0.0");
public sealed record LoginResponse(User User, string? AccessToken, DateTimeOffset ExpiresAt);
public sealed record SavedSession(string Server, string AccessToken, DateTimeOffset ExpiresAt);
public sealed record ClientSettings(string Server = "https://10.37.200.74", string? RequiredVersion = null, bool PreferRtsp = false);
public sealed record Channel(long Id, long DeviceId, string DeviceName, int DeviceChannel, string Name, string? Alias, string? Model,
    string Status, long? UnitId, bool PtzCapable, string? Codec)
{
    public bool Online => Status == "online";
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Name : Alias;
    public string Label => $"{DisplayName} · {(Online ? "在线" : "离线")}";
    public string Detail => $"{DeviceName} / 通道 {DeviceChannel:00} / {Codec ?? "编码未知"}";
}
public sealed record OrganizationNode(long Id, string Name, string? Code, string Status, long? ParentId);
public sealed record Organization(OrganizationNode[] Workshops, OrganizationNode[] Areas, OrganizationNode[] Units);
public sealed record LiveRequest(long ChannelId, int StreamType = 2, string Profile = "native");
public sealed record PlaybackRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End, string Profile = "native");
public sealed record RecordingRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End);
public sealed record PlaybackControl(string Action, DateTimeOffset? Position = null, double? Speed = null);
public record MediaSession(string Id, long ChannelId, int StreamType, string State, DateTimeOffset ExpiresAt,
    string RtspUrl, string? HttpFlvUrl = null, string? HlsUrl = null, string? Codec = null, bool Transcoded = false,
    DateTimeOffset? Start = null, DateTimeOffset? End = null, DateTimeOffset? CurrentTime = null,
    double Progress = 0, double Speed = 1, RecordingSegment[]? Segments = null, string? HttpTsUrl = null);
public sealed record RecordingSegment(DateTimeOffset Start, DateTimeOffset End);
public sealed record Recording(string FileName, DateTimeOffset Start, DateTimeOffset End, long FileSize, int FileType, int StreamType, uint FileIndex)
{
    public string Label => $"{Start.LocalDateTime:MM-dd HH:mm:ss} - {End.LocalDateTime:HH:mm:ss}";
}
public sealed record FavoritesRequest(long[] ChannelIds);
public sealed record LayoutDto(long Id, string Name, string Kind, bool Shared, int Layout, int IntervalSeconds, long?[] ChannelIds)
{
    public string Label => $"{Name}{(Shared ? " · 共享" : "")}{(Kind == "patrol" ? " · 轮巡" : "")}";
}
public sealed record LayoutRequest(string Name, string Kind, bool Shared, int Layout, int IntervalSeconds, long?[] ChannelIds);
public sealed record PtzRequest(string Command, int Speed);
public sealed record AlarmHistory(long Id, string Action, string? Note, string Username, DateTimeOffset CreatedAt)
{
    public string ActionLabel => Action switch { "claim" => "认领", "note" => "备注", "close" => "关闭", "reopen" => "重新打开", _ => Action };
}
public sealed record Alarm(long Id, long DeviceId, string DeviceName, long? ChannelId, string? ChannelName,
    string EventType, DateTimeOffset OccurredAt, string State, bool Recovered, long? OwnerId, string? OwnerName,
    string? Note, bool ImageAvailable, JsonElement? Payload, AlarmHistory[]? History = null)
{
    public string StateLabel => State switch { "new" => "待处理", "processing" => "处理中", "closed" => "已关闭", _ => State };
    public string RecoveryLabel => Recovered ? "设备已恢复" : "未收到恢复事件";
}
public sealed record AlarmAction(string Action, string? Note);
public sealed record ExportRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End);
public sealed record ExportJob(string Id, long ChannelId, string ChannelName, DateTimeOffset Start, DateTimeOffset End,
    string State, double Progress, string? Error, string? FileName, long? FileSize, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt)
{
    public string StateLabel => State switch { "queued" => "排队中", "running" => "导出中", "completed" => "已完成", "failed" => "失败", "cancelled" => "已取消", _ => State };
}
public sealed record Release(long Id, string Version, string FileName, string Sha256, long FileSize, string ReleaseNotes,
    string? MinimumVersion, bool ForceUpdate, string Status, DateTimeOffset? PublishedAt, long DownloadCount, string DownloadUrl);
public sealed record ResourceEvent(string Id, long Version, string Kind);
public sealed record ProfileRequest(string DisplayName, string Phone);
public sealed record PasswordRequest(string CurrentPassword, string NewPassword);
public sealed record Choice(string Value, string Label);
