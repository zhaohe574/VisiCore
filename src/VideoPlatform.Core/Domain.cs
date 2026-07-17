namespace VideoPlatform.Core;

[Flags]
public enum CameraPermission
{
    None = 0,
    LiveView = 1,
    Playback = 2,
    PtzControl = 4,
    Export = 8,
    Manage = 16
}

public enum CameraConnectivity
{
    Unknown,
    Online,
    SuspectedOffline,
    Offline,
    Recovering
}

public enum CapabilityState
{
    Unknown,
    Supported,
    Unsupported,
    RequiresExternalPlugin
}

[Flags]
public enum SystemPermission
{
    None = 0,
    ManageAssets = 1,
    ManageDeviceWorkers = 2,
    ManageNotifications = 4,
    ManageOperations = 8,
    ViewAudit = 16,
    ManageExports = 32,
    ManageDeviceCredentials = 64,
    All = ManageAssets | ManageDeviceWorkers | ManageNotifications | ManageOperations | ViewAudit | ManageExports | ManageDeviceCredentials
}

public sealed record Region(Guid Id, Guid? ParentId, string Name, string Code);

public sealed record Recorder(
    Guid Id,
    string Name,
    string Vendor,
    string Address,
    int Port,
    string AdapterType,
    string CredentialReference,
    bool? UseTls = null,
    string? CertificateThumbprint = null);

public sealed record RecorderCapabilities(
    CapabilityState LiveStream,
    CapabilityState Playback,
    CapabilityState Ptz,
    CapabilityState ChannelStatus,
    CapabilityState EventSubscription,
    CapabilityState Export,
    CapabilityState DeviceConfiguration,
    string Version);

public sealed record Camera(
    Guid Id,
    Guid RecorderId,
    Guid RegionId,
    string Code,
    string Alias,
    int ChannelNumber,
    bool SupportsPtz,
    CameraConnectivity Connectivity);

public sealed record StreamSession(
    Guid Id,
    Guid UserId,
    Guid CameraId,
    CameraPermission Operation,
    Uri GatewayUri,
    DateTimeOffset ExpiresAt);

public sealed record LiveUpstreamStreamSession(
    Guid CameraId,
    Uri SourceUri,
    DateTimeOffset ExpiresAt);

public sealed record PlaybackRelaySession(
    Guid CameraId,
    string RelaySessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    DateTimeOffset ExpiresAt);

public sealed record RecordingSegment(
    Guid CameraId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string VendorSegmentId,
    long? SizeBytes,
    bool IsLocked);

public enum PtzAction
{
    PanLeft,
    PanRight,
    TiltUp,
    TiltDown,
    PanTiltUpLeft,
    PanTiltUpRight,
    PanTiltDownLeft,
    PanTiltDownRight,
    ZoomIn,
    ZoomOut,
    FocusNear,
    FocusFar,
    IrisOpen,
    IrisClose
}

public enum PtzMotion
{
    Start,
    Stop
}

public sealed record PtzCommand(
    Guid CameraId,
    PtzAction Action,
    PtzMotion Motion,
    int Speed,
    long Sequence,
    DateTimeOffset RequestedAt);

public sealed record CameraStatusChanged(
    Guid CameraId,
    CameraConnectivity Previous,
    CameraConnectivity Current,
    DateTimeOffset OccurredAt);
