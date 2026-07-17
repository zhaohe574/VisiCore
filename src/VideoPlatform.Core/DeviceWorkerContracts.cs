namespace VideoPlatform.Core;

public sealed record WorkerRecorderEndpoint(
    string Protocol,
    string Host,
    int Port,
    bool UseTls,
    string CredentialReference,
    string? CertificateThumbprint = null);

public sealed record WorkerProtectedCredential(
    string Name,
    string ProtectionMode,
    string CiphertextBase64,
    string KeyVersion);

public sealed record WorkerCameraRoute(
    Guid CameraId,
    int ChannelNumber,
    string StreamingChannelMap,
    string Alias = "",
    bool SupportsPtz = false);

public sealed record WorkerRecorderAssignment(
    Guid RecorderId,
    Guid DefaultRegionId,
    string RecorderCode,
    string RecorderName,
    string Vendor,
    string AdapterType,
    string TimeZoneId,
    IReadOnlyList<WorkerRecorderEndpoint> Endpoints,
    IReadOnlyList<WorkerProtectedCredential> Credentials,
    IReadOnlyList<WorkerCameraRoute> Cameras,
    string DeviceKind = DeviceKinds.Recorder,
    string? PluginKey = null,
    string? PluginRuntimeType = null);

public sealed record WorkerCameraInventory(
    int ChannelNumber,
    string Alias,
    bool SupportsPtz,
    string StreamingChannelMap);

public sealed record WorkerInventoryReport(
    Guid RecorderId,
    RecorderCapabilities Capabilities,
    IReadOnlyList<WorkerCameraInventory> Cameras,
    DateTimeOffset ObservedAt);

public sealed record WorkerHealthReport(
    Guid RecorderId,
    bool RecorderReachable,
    IReadOnlyDictionary<int, bool> ChannelStates,
    string? FailureKind,
    DateTimeOffset ObservedAt);

public sealed record WorkerClockObservation(
    DateTimeOffset DeviceTime,
    DateTimeOffset RequestStartedAt,
    DateTimeOffset ResponseReceivedAt);

public sealed record WorkerClockReport(
    Guid RecorderId,
    WorkerClockObservation? Observation,
    string? FailureKind,
    DateTimeOffset ObservedAt);

public sealed record WorkerOperationStatus(
    string OperationType,
    bool IsReady,
    string? FailureKind,
    Guid RecorderId = default);

public sealed record WorkerOperationStatusReport(
    IReadOnlyList<WorkerOperationStatus> Operations);

public sealed record WorkerEdgeCommand(
    Guid Id,
    Guid RecorderId,
    string CommandType,
    string AggregateType,
    Guid AggregateId,
    string PayloadJson,
    int Attempt,
    DateTimeOffset CreatedAt,
    DateTimeOffset DeliveryExpiresAt,
    string DeliveryToken);

public sealed record WorkerEdgeCommandCompletion(
    string DeliveryToken,
    bool Succeeded,
    string? ResultJson,
    string? FailureKind);
