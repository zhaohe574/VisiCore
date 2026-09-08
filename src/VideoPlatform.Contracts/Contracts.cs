namespace VideoPlatform.Contracts;

public sealed record ErrorResponse(string Code, string Message, string TraceId);
public sealed record Page<T>(IReadOnlyList<T> Items, long Total, int PageNumber, int PageSize);
public sealed record LoginRequest(string Username, string Password, string ClientType = "web", string ClientVersion = "2.0.0");
public sealed record UserDto(long Id, string Username, string? DisplayName, string? Phone, string Status, string[] Permissions, long[] RoleIds);
public sealed record AuthResponse(UserDto User, string? AccessToken, DateTimeOffset ExpiresAt);
public sealed record ProfileRequest(string? DisplayName, string? Phone);
public sealed record PasswordRequest(string CurrentPassword, string NewPassword);
public sealed record DeviceRequest(string Name, string Host, int Port, string Username, string? Password, bool Enabled = true, string? PluginId = "hikvision", string? ExtraConfig = null);
public sealed record DeviceDto(long Id, string Name, string Host, int Port, string Username, bool Enabled, string Status, string? Model, string? SerialNumber, DateTimeOffset? LastSeenAt, long ChannelCount, long OnlineChannels, string? PluginId = "hikvision", string? PluginName = null);
public sealed record ChannelDto(long Id, long DeviceId, string DeviceName, int DeviceChannel, string Name, string? Alias, string? Model, string Status, long? UnitId, bool PtzCapable, string? Codec);
public sealed record ChannelUpdateRequest(string? Alias, long? UnitId = null);
public sealed record OrganizationRequest(string Name, string Code, string Status = "active", long? ParentId = null);
public sealed record AssignmentRequest(long[] ChannelIds, long? UnitId);
public sealed record UserRequest(string Username, string? Password, string? DisplayName, string? Phone, string Status, long[] RoleIds);
public sealed record RoleRequest(string Name, string Code, string Status = "active", string[]? PermissionCodes = null);
public sealed record PermissionRequest(string[] Codes);
public sealed record ScopeItem(string Type, long Id);
public sealed record ScopeRequest(bool AllChannels, ScopeItem[] Scopes);
public sealed record LiveRequest(long ChannelId, int StreamType = 2, string Profile = "browser");
public sealed record RecordingRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End);
public sealed record PlaybackRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End, string Profile = "browser");
public sealed record PlaybackControlRequest(string Action, DateTimeOffset? Position = null, double? Speed = null);
public sealed record PtzRequest(string Command, int Speed = 4);
public sealed record FavoritesRequest(long[] ChannelIds);
public sealed record LayoutRequest(string Name, string Kind, bool Shared, int Layout, int IntervalSeconds, long?[] ChannelIds);
public sealed record AlarmActionRequest(string Action, string? Note);
public sealed record PublishRequest(string? MinimumVersion = null, bool ForceUpdate = false);
public sealed record EventNotice(string Id, long Version, string Kind);
public sealed record PlatformSettings(
    int LivePerUser = 16, int PlaybackPerUser = 4, int PlaybackPerDevice = 20, int PlaybackGlobal = 32,
    int TranscodeGlobal = 4, int ExportGlobal = 2, int ExportPerDevice = 1, int ExportRetentionDays = 7,
    int ExportQuotaGb = 100, int AlarmRetentionDays = 180, int AuditRetentionDays = 180);
public sealed record DevicePluginDto(string Id, string Name, string Vendor, string Version, string? Description, string Status, string EndpointUrl, string[] Capabilities, string? ConfigSchema, long DeviceCount, string? HealthStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PluginStatusRequest(string Status);
