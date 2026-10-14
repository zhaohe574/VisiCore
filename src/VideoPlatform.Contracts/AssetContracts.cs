namespace VideoPlatform.Contracts;

public sealed record AssetRef(string Type, long Id);
public sealed record AssetDto(AssetRef Ref, string Name, string Code, string? Ip, string Status,
    long? WorkshopId, long? AreaId, long? UnitId, long? TerminalBoxId, long? OnuDeviceId,
    int? PortNumber, bool IsSwitchPort, DateTimeOffset? ObservedAt, string? HealthStatus,
    string? RootCause, decimal? RxPower, long? DeviceId);
public sealed record AssetTypeCountDto(string Type, long Total, long Online, long Offline, long Unknown);
public sealed record AssetSummaryDto(long Total, long Online, long Offline, long Unknown,
    long MissingOrganization, long MissingNetworkBinding, IReadOnlyList<AssetTypeCountDto> Types);
public sealed record AssetEvidenceDto(string Label, string Value, string Source, DateTimeOffset? ObservedAt);
public sealed record AssetOrganizationDto(string Type, long Id, string Name);
public sealed record AssetAlarmDto(long Id, long? ChannelId, string EventType, string State, bool Recovered, DateTimeOffset OccurredAt);
public sealed record AssetHistoryDto(long Id, string DeviceName, string EventType, DateTimeOffset DownTime,
    DateTimeOffset? UpTime, string? Reason);
public sealed record AssetPortDto(long Id, string Name, string Status, long? PeerPortId, string? CableLabel);
public sealed record AssetContextDto(AssetDto Asset, IReadOnlyList<AssetOrganizationDto> Organization,
    IReadOnlyList<AssetDto> RelatedAssets, IReadOnlyList<AssetEvidenceDto> Evidence,
    IReadOnlyList<AssetHistoryDto> OfflineHistory, IReadOnlyList<AssetAlarmDto> Alarms,
    IReadOnlyList<AssetPortDto> Ports);
public sealed record ComputerBatchUpdateRequest(long[] ComputerIds, bool UpdateOrganization = false,
    long? WorkshopId = null, long? AreaId = null, long? UnitId = null,
    bool UpdateNetworkBinding = false, long? TerminalBoxId = null, long? OnuDeviceId = null,
    int? StartPortNumber = null, bool IsSwitchPort = false);
public sealed record AssetBatchResultDto(int Updated, IReadOnlyList<long> Ids);
