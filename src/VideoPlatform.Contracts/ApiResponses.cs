using System.Text.Json;

namespace VideoPlatform.Contracts;

// 以下契约对应当前实际响应；接口仍返回 JsonObject 时可直接用于 Produces 元数据。
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);
public sealed record CsrfTokenResponse(string Token);
public sealed record ResourceCreatedResponse(long Id);
public sealed record DeviceSyncResponse(bool Success, int Channels);
public sealed record RecordingDto(string FileName, DateTimeOffset Start, DateTimeOffset End, long FileSize, int FileType, int StreamType, uint FileIndex);
public sealed record RecordingSegmentDto(DateTimeOffset Start, DateTimeOffset End, string? FileName = null, long? FileSize = null, int? FileType = null, int? StreamType = null, uint? FileIndex = null);
public record LiveSessionDto(Guid Id, long ChannelId, int StreamType, string State, DateTimeOffset ExpiresAt, string RtspUrl, string HttpFlvUrl, string HlsUrl, string Codec, bool Transcoded)
{
    public string? HttpTsUrl { get; init; }
}
public sealed record PlaybackSessionDto(Guid Id, long ChannelId, int StreamType, string State, DateTimeOffset ExpiresAt, string RtspUrl, string HttpFlvUrl, string HlsUrl, string Codec, bool Transcoded,
    DateTimeOffset Start, DateTimeOffset End, DateTimeOffset CurrentTime, int Progress, double Speed, IReadOnlyList<RecordingSegmentDto> Segments, string? Error = null)
    : LiveSessionDto(Id, ChannelId, StreamType, State, ExpiresAt, RtspUrl, HttpFlvUrl, HlsUrl, Codec, Transcoded);
public record AlarmDto(long Id, long DeviceId, string DeviceName, long? ChannelId, string? ChannelName, string EventType, DateTimeOffset OccurredAt, string State, bool Recovered,
    long? OwnerId, string? OwnerName, string Note, bool ImageAvailable, JsonElement Payload, long Version);
public sealed record AlarmHistoryDto(long Id, string Action, string Note, string? Username, DateTimeOffset CreatedAt);
public sealed record AlarmDetailDto(long Id, long DeviceId, string DeviceName, long? ChannelId, string? ChannelName, string EventType, DateTimeOffset OccurredAt, string State, bool Recovered,
    long? OwnerId, string? OwnerName, string Note, bool ImageAvailable, JsonElement Payload, long Version, IReadOnlyList<AlarmHistoryDto> History)
    : AlarmDto(Id, DeviceId, DeviceName, ChannelId, ChannelName, EventType, OccurredAt, State, Recovered, OwnerId, OwnerName, Note, ImageAvailable, Payload, Version);
public sealed record AlarmActionResponse(long Id, string State);
public sealed record ExportDto(Guid Id, long ChannelId, string ChannelName, DateTimeOffset Start, DateTimeOffset End, string State, int Progress, string? Error,
    string? FileName, long FileSize, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record ExportCreatedResponse(Guid Id, string State, int Progress);
public sealed record ExportQueuedResponse(Guid Id, string State);
public record ReleaseDto(long Id, string Version, string FileName, string Sha256, long FileSize, string ReleaseNotes, string? MinimumVersion, bool ForceUpdate,
    string Status, DateTimeOffset? PublishedAt, long DownloadCount, string DownloadUrl, DateTimeOffset CreatedAt);
public sealed record LatestReleaseDto(long Id, string Version, string FileName, string Sha256, long FileSize, string ReleaseNotes, string? MinimumVersion, bool ForceUpdate,
    string Status, DateTimeOffset? PublishedAt, long DownloadCount, string DownloadUrl, DateTimeOffset CreatedAt, bool UpdateAvailable, IReadOnlyList<ReleaseDto> Packages)
    : ReleaseDto(Id, Version, FileName, Sha256, FileSize, ReleaseNotes, MinimumVersion, ForceUpdate, Status, PublishedAt, DownloadCount, DownloadUrl, CreatedAt);
