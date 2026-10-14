namespace VideoPlatform.Contracts;

public sealed record ErrorResponse(string Code, string Message, string TraceId);

public sealed record LoginRequest(string Username, string Password, string ClientType = "web", string ClientVersion = "2.0.0");
public sealed record UserDto(long Id, string Username, string? DisplayName, string? Phone, string Status, string[] Permissions, long[] RoleIds);
public sealed record AuthResponse(UserDto User, string? AccessToken, DateTimeOffset ExpiresAt);
public sealed record ProfileRequest(string? DisplayName, string? Phone);
public sealed record PasswordRequest(string CurrentPassword, string NewPassword);
public sealed record DeviceRequest(string Name, string Host, int Port, string Username, string? Password, bool Enabled = true, string? PluginId = "hikvision", string? ExtraConfig = null, long? TerminalBoxId = null, long? OnuDeviceId = null, int? PortNumber = null, bool? IsSwitchPort = null, string? PortLabel = null);
public sealed record DeviceDto(long Id, string Name, string Host, int Port, string Username, bool Enabled, string Status, string? Model, string? SerialNumber, DateTimeOffset? LastSeenAt, long ChannelCount, long OnlineChannels, string? PluginId = "hikvision", string? PluginName = null, string? SyncError = null, long? TerminalBoxId = null, string? TerminalBoxName = null, string? TerminalBoxCode = null, long? OnuDeviceId = null, string? OnuName = null, string? OnuAlias = null, int? NetworkPort = null, bool? IsSwitchPort = null, string? PortLabel = null, decimal? OnuRxPower = null);
public sealed record OpticalLinkInfoDto(
    long? TerminalBoxId,
    string? BoxName,
    string? BoxCode,
    long? OnuId,
    string? OnuName,
    string? OnuAlias,
    string? HealthStatus,
    string? RootCause,
    decimal? RxPower,
    string? IfDesc,
    int? PortNumber,
    string? PortLabel
);

public sealed record ChannelDto(
    long Id,
    long DeviceId,
    string DeviceName,
    int DeviceChannel,
    string Name,
    string? Alias,
    string? Model,
    string Status,
    long? UnitId,
    bool PtzCapable,
    string? Codec,
    string? Ip = null,
    string? Username = null,
    bool HasPassword = false,
    string? Remark = null,
    string? DeviceModel = null,
    string? DeviceHost = null,
    int? DevicePort = null,
    string? DeviceSerial = null,
    string? PluginId = null,
    string? PluginName = null,
    string? FirmwareVersion = null,
    int SortOrder = 0,
    long? TerminalBoxId = null,
    string? TerminalBoxName = null,
    string? TerminalBoxCode = null,
    long? OnuDeviceId = null,
    string? OnuName = null,
    string? OnuAlias = null,
    string? OnuStatus = null,
    int? NetworkPort = null,
    bool? IsSwitchPort = null,
    string? PortLabel = null,
    string? OnuHealthStatus = null,
    string? OnuRootCause = null,
    decimal? OnuRxPower = null,
    string? OnuIfDesc = null,
    OpticalLinkInfoDto? OpticalLink = null
);
public sealed record ChannelUpdateRequest(string? Alias, long? UnitId = null, string? Ip = null, string? Username = null, string? Password = null, string? Remark = null, long? TerminalBoxId = null, long? OnuDeviceId = null, int? NetworkPort = null, bool? IsSwitchPort = null, string? PortLabel = null, bool? ClearNetworkBinding = null, bool ClearPassword = false);

// 光网与终端箱台账 DTO
public sealed record TerminalBoxDto(long Id, string Code, string Name, string? Location, long? AreaId, string? AreaName, string? Remarks, long OnuCount, long ChannelCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long? UnitId = null, string? UnitName = null, long ComputerCount = 0);
public sealed record TerminalBoxRequest(string Code, string Name, string? Location = null, long? AreaId = null, string? Remarks = null, long? UnitId = null);

public sealed record OnuDeviceDto(
    long Id,
    string NeDn,
    string OltName,
    string OltIp,
    string OnuId,
    string OnuName,
    string? Alias,
    string Sn,
    string? Mac,
    string TerminalType,
    string Status,
    string? RunStatus,
    DateTimeOffset? LastUpTime,
    DateTimeOffset? LastDownTime,
    string? LastDownCause,
    decimal? RxPower,
    decimal? TxPower,
    long? TerminalBoxId,
    string? TerminalBoxName,
    string? TerminalBoxCode,
    bool HasSubSwitch,
    string? SubSwitchModel,
    int SubSwitchPorts,
    string? PortsConfig,
    DateTimeOffset? LastSyncedAt,
    long BoundChannelCount,
    string? Description = null,
    string? GroupName = null,
    string? Ip = null,
    string? SyncWarning = null,
    long? OltId = null,
    int PortId = 0,
    string? IfDesc = null,
    DateTimeOffset? LastDyingGaspTime = null,
    string HealthStatus = "healthy",
    string? RootCause = null,
    DateTimeOffset? LastStateChangeTime = null
);
public sealed record OnuUpdateRequest(string? Alias = null, long? TerminalBoxId = null, bool? HasSubSwitch = null, string? SubSwitchModel = null, int? SubSwitchPorts = null, string? PortsConfig = null);

// OLT 网元与 PON 端口汇总 DTO
public sealed record PonPortSummaryDto(
    int PortId,
    string IfDesc,
    int TotalOnus,
    int OnlineOnus,
    int OfflineOnus,
    int SubHealthOnus,
    string Status,
    IReadOnlyList<string>? BrokenCauses = null
);

public sealed record OltDeviceDto(
    long Id,
    string NeDn,
    string Name,
    string Ip,
    string Model,
    string Vendor,
    string? OsVersion,
    string? PatchVersion,
    string Status,
    DateTimeOffset SyncedAt,
    int TotalPorts = 8,
    IReadOnlyList<PonPortSummaryDto>? Ports = null
);

// 全网光链路健康度与亚健康预测大屏 DTO
public sealed record OpticalHealthTrendItemDto(
    DateTimeOffset Time,
    int Score,
    decimal AvailabilityRate,
    int OfflineCount,
    int SubHealthCount
);

public sealed record OpticalHealthIndexDto(
    int Score,
    string Grade,
    int TotalOnus,
    int OnlineOnus,
    int OfflineOnus,
    int HealthyOnus,
    int SubHealthOnus,
    int PowerOffOnus,
    int FiberCutOnus,
    int TrunkBrokenOnus,
    decimal AvailabilityRate,
    decimal? AvgRxPower,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<OpticalHealthTrendItemDto>? Trend = null,
    int SplitterBrokenOnus = 0
);

// MAC/IP 自动拓扑发现与纠偏 DTO
public sealed record AutoTopologyBindingSuggestionDto(
    long ChannelId,
    string ChannelName,
    string? ChannelAlias,
    string? ChannelIp,
    long? CurrentOnuId,
    string? CurrentOnuName,
    long SuggestedOnuId,
    string SuggestedOnuName,
    string? SuggestedOnuAlias,
    int SuggestedPortNumber,
    long? SuggestedTerminalBoxId,
    string? SuggestedTerminalBoxName,
    int ConfidenceScore,
    string MatchReason,
    bool NeedsCorrection
);

public sealed record AutoTopologyDiscoverResultDto(
    int TotalEvaluated,
    int SuggestedCount,
    IReadOnlyList<AutoTopologyBindingSuggestionDto> Suggestions
);

public sealed record ApplyTopologyItemRequest(
    long ChannelId,
    long OnuDeviceId,
    long? TerminalBoxId = null,
    int PortNumber = 1,
    bool IsSwitchPort = false,
    string? PortLabel = null
);

public sealed record ApplyTopologyCorrectionRequest(
    IReadOnlyList<ApplyTopologyItemRequest> Bindings
);

public sealed record OnuPortStatusDto(int PortId, string Name, string Status, string? Speed = null, string? Duplex = null, string? TerminalMac = null, string? DeviceName = null, string? DeviceIp = null, string? DeviceType = null, string? DeviceStatus = null);
public sealed record OnuDiagnosisItemDto(string Key, string? Value, int Status, string? Suggestion = null);
public sealed record OnuDiagnosisDto(string NeDn, string OnuId, string Status, decimal? RxPower, decimal? TxPower, string? LastDownCause, string? LastUpTime, string? LastDownTime, IReadOnlyList<OnuPortStatusDto> Ports, string? OpticalStatus = null, string? FiberStatus = null, IReadOnlyList<OnuDiagnosisItemDto>? Items = null, string? HealthStatus = null, string? RootCause = null);

public sealed record NameCountDto(string Name, long Count);
public sealed record PonStatisticsDto(long TotalCount, long OnlineCount, long OfflineCount, IReadOnlyList<NameCountDto> Models);

public sealed record NetworkTopologyPortDto(int PortNumber, bool IsSwitchPort, string PortLabel, long? BoundChannelId, string? BoundChannelName, string? BoundChannelIp, string? BoundChannelStatus, long? BoundComputerId = null, string? BoundComputerName = null, string? BoundComputerIp = null, string? BoundComputerStatus = null, string? BoundComputerOwner = null, string? BoundComputerDepartment = null, long? BoundDeviceId = null, string? BoundDeviceName = null, string? BoundDeviceHost = null, string? BoundDeviceStatus = null, string? BoundDeviceModel = null);
public sealed record NetworkTopologyOnuDto(long Id, string OnuName, string? Alias, string Sn, string TerminalType, string Status, decimal? RxPower, bool HasSubSwitch, string? SubSwitchModel, int SubSwitchPorts, IReadOnlyList<NetworkTopologyPortDto> Ports);
public sealed record TerminalBoxTopologyDto(long Id, string Code, string Name, string? Location, long? AreaId, string? AreaName, IReadOnlyList<NetworkTopologyOnuDto> Onus, long? UnitId = null, string? UnitName = null);

// 终端箱向下维度的 ONU、摄像头与电脑终端看板 DTO
public sealed record PonKanbanComputerDto(long Id, string Name, string Ip, string? Mac, string? Owner, string? Department, string Status, int PortNumber, bool IsSwitchPort, string PortLabel, int? ResponseTimeMs = null);
public sealed record PonKanbanCameraDto(long ChannelId, int DeviceChannel, string Name, string? Alias, string? Ip, string Status, int PortNumber, bool IsSwitchPort, string PortLabel, string? Model = null);
public sealed record PonKanbanDeviceDto(long DeviceId, string Name, string Host, int Port, string Status, int PortNumber, bool IsSwitchPort, string PortLabel, string? Model = null, long ChannelCount = 0, long OnlineChannels = 0);
public sealed record PonKanbanOnuDto(long Id, string OnuName, string? Alias, string Sn, string TerminalType, string Status, string? RunStatus, DateTimeOffset? LastDownTime, string? LastDownCause, DateTimeOffset? LastUpTime, decimal? RxPower, bool HasSubSwitch, string? SubSwitchModel, int SubSwitchPorts, IReadOnlyList<PonKanbanCameraDto> Cameras, IReadOnlyList<PonKanbanComputerDto>? Computers = null, IReadOnlyList<PonKanbanDeviceDto>? Devices = null);
public sealed record PonKanbanBoxDto(long Id, string Code, string Name, string? Location, long? AreaId, string? AreaName, string HealthState, int TotalOnus, int OnlineOnus, int OfflineOnus, int TotalCameras, int OnlineCameras, int OfflineCameras, IReadOnlyList<PonKanbanOnuDto> Onus, IReadOnlyList<PonKanbanCameraDto> DirectCameras, long? UnitId = null, string? UnitName = null, int TotalComputers = 0, int OnlineComputers = 0, int OfflineComputers = 0, IReadOnlyList<PonKanbanComputerDto>? DirectComputers = null, IReadOnlyList<PonKanbanDeviceDto>? DirectDevices = null);
public sealed record PonKanbanSummaryDto(int TotalBoxes, int HealthyBoxes, int WarningBoxes, int DangerBoxes, int TotalOnus, int OnlineOnus, int OfflineOnus, int TotalCameras, int OnlineCameras, int OfflineCameras, int OngoingOutagesCount, int TodayOutagesCount, int TotalComputers = 0, int OnlineComputers = 0, int OfflineComputers = 0);
public sealed record PonKanbanResponseDto(PonKanbanSummaryDto Summary, IReadOnlyList<PonKanbanBoxDto> Boxes, IReadOnlyList<PonKanbanOnuDto> UnassignedOnus, IReadOnlyList<PonKanbanCameraDto> UnassignedCameras, IReadOnlyList<PonKanbanComputerDto>? UnassignedComputers = null);

// 设备掉线与恢复历史记录 DTO
public sealed record DeviceOfflineHistoryDto(long Id, string DeviceType, long? TerminalBoxId, string? TerminalBoxCode, string? TerminalBoxName, long? OnuDeviceId, long? ChannelId, string DeviceName, string? DeviceIdentifier, string EventType, DateTimeOffset DownTime, DateTimeOffset? UpTime, int? DurationSeconds, string? Reason, DateTimeOffset CreatedAt);

// 录像机通道分配与一键新增摄像头 DTO
public sealed record NvrAvailableChannelDto(
    long DeviceId,
    string DeviceName,
    string Host,
    int Port,
    int TotalChannels,
    int UsedChannels,
    int FreeChannels,
    IReadOnlyList<int> AvailableChannelNumbers,
    string? PluginId = "hikvision",
    string? Model = null);
public sealed record AddCameraRequest(long NvrDeviceId, int? ChannelNumber, string? Alias, string IpcHost, int IpcPort = 8000, string IpcUsername = "admin", string IpcPassword = "", string ProtocolType = "HIKVISION", long? UnitId = null, long? TerminalBoxId = null, long? OnuDeviceId = null, int? PortNumber = null, bool IsSwitchPort = false, string? PortLabel = null, string? Name = null);
/// <summary>码流档位能力探测请求。StreamType：1 主码流、2 子码流。</summary>
public sealed record StreamProbeRequest(int StreamType);
/// <summary>
/// 码流档位能力。<c>Available</c> 为 false 表示设备确认该档位不存在；
/// 分辨率与码率探测不到时为 null，调用方必须按“未知”处理，不得用估计值顶替。
/// </summary>
public sealed record StreamCapabilityDto(int StreamType, bool Available, string? Codec = null,
    int? Width = null, int? Height = null, int? BitrateKbps = null, string? Error = null);
public sealed record OrganizationRequest(string Name, string Code, string Status = "active", long? ParentId = null);
public sealed record AssignmentRequest(long[] ChannelIds, long? UnitId);
public sealed record BatchChannelNetworkBindingRequest(long[] ChannelIds, long? TerminalBoxId = null, long? OnuDeviceId = null, int? StartPortNumber = null, bool? IsSwitchPort = null);
public sealed record BatchClaimChannelsRequest(long[] ChannelIds, long UnitId, long? TerminalBoxId = null, long? OnuDeviceId = null, int? StartPortNumber = null, bool? IsSwitchPort = null);
public sealed record ChannelSortRequest(long UnitId, long[] ChannelIds);
public sealed record UserRequest(string Username, string? Password, string? DisplayName, string? Phone, string Status, long[] RoleIds);
public sealed record RoleRequest(string Name, string Code, string Status = "active", string[]? PermissionCodes = null);
public sealed record PermissionRequest(string[] Codes);
public sealed record ScopeItem(string Type, long Id);
public sealed record ScopeRequest(bool AllChannels, ScopeItem[] Scopes);
public sealed record LiveRequest(long ChannelId, int StreamType = 2, string Profile = "browser", Guid? ConsumerId = null);
public sealed record RecordingRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End);
public sealed record PlaybackRequest(long ChannelId, DateTimeOffset Start, DateTimeOffset End, string Profile = "browser", int StreamType = 1, Guid? ConsumerId = null);
public sealed record PlaybackControlRequest(string Action, DateTimeOffset? Position = null, double? Speed = null);
public sealed record PtzRequest(string Command, int Speed = 4);
public sealed record FavoritesRequest(long[] ChannelIds);
public sealed record LayoutRequest(string Name, string Kind, bool Shared, int Layout, int IntervalSeconds, long?[] ChannelIds);
public sealed record AlarmActionRequest(string Action, string? Note);
public sealed record BatchAlarmFilter(string? State, long? DeviceId, long? ChannelId, string? EventType, DateTimeOffset? From, DateTimeOffset? To, string? Search);
public sealed record BatchAlarmActionRequest(long[]? Ids, string Action, string? Note, bool AllMatching = false, BatchAlarmFilter? Filter = null);
public sealed record PublishRequest(string? MinimumVersion = null, bool ForceUpdate = false);
public sealed record ReleaseUpdateRequest(string? Version = null, string? ReleaseNotes = null, string? MinimumVersion = null, bool? ForceUpdate = null);
public sealed record EventNotice(string Id, long Version, string Kind);
public sealed record PlatformSettings(
    int LivePerUser = 25, int PlaybackPerUser = 4, int PlaybackPerDevice = 20, int PlaybackGlobal = 32,
    int TranscodeGlobal = 4, int ExportGlobal = 2, int ExportPerDevice = 1, int ExportRetentionDays = 7,
    int ExportQuotaGb = 100, int AlarmRetentionDays = 180, int AuditRetentionDays = 180);
public sealed record DevicePluginDto(string Id, string Name, string Vendor, string Version, string? Description, string Status, string EndpointUrl, string[] Capabilities, string? ConfigSchema, long DeviceCount, string? HealthStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PluginStatusRequest(string Status);
public sealed record PluginCreateRequest(string Id, string Name, string Vendor, string Version, string? Description, string EndpointUrl, string[]? Capabilities = null, string? ConfigSchema = null);
public sealed record PluginProbeRequest(string EndpointUrl);
public sealed record SslCertificateDto(
    long Id, string Name, string CommonName, string[] DnsNames, string IssuerDn, string SubjectDn,
    string SerialNumber, string Thumbprint, DateTimeOffset ValidFrom, DateTimeOffset ValidTo,
    int DaysRemaining, string Status, bool IsActive, long BoundDomainCount, DateTimeOffset CreatedAt,
    string? CertPem = null);
public sealed record SslCertificateUploadRequest(string Name, string CertPem, string KeyPem);
public sealed record SslDomainDto(
    long Id, string Domain, int Port, string Protocol, string Description,
    bool IsPrimary, bool ForceHttps, bool HstsEnabled, long? CertificateId,
    string? CertificateName, string? CertificateCommonName, DateTimeOffset? CertificateValidTo,
    string CertMatchStatus, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SslDomainRequest(
    string Domain, int Port = 443, string Protocol = "https", string Description = "",
    bool IsPrimary = false, bool ForceHttps = true, bool HstsEnabled = true,
    long? CertificateId = null, bool Enabled = true);
public sealed record SslOverviewDto(
    SslCertificateDto? ActiveCertificate, long TotalCertificates, long ExpiringCertificates,
    long TotalDomains, SslDomainDto? PrimaryDomain, bool HttpsEnforced,
    string CertFilePath, string KeyFilePath, bool FileSynced);
public sealed record NginxConfigDto(string Content, string ConfigPath, string ReloadCommand);

// 电脑台账 DTO
public sealed record ComputerDto(
    long Id,
    string Name,
    string Ip,
    string? Mac,
    string? Owner,
    string? Department,
    string? Os,
    string? Location,
    long? WorkshopId,
    string? WorkshopName,
    long? AreaId,
    string? AreaName,
    long? UnitId,
    string? UnitName,
    long? TerminalBoxId,
    string? TerminalBoxName,
    string? TerminalBoxCode,
    long? OnuDeviceId,
    string? OnuName,
    string? OnuAlias,
    int PortNumber,
    bool IsSwitchPort,
    string? PortLabel,
    string Status,
    int? ResponseTimeMs,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? LastProbedAt,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record ComputerRequest(
    string Name,
    string Ip,
    string? Mac = null,
    string? Owner = null,
    string? Department = null,
    string? Os = null,
    string? Location = null,
    long? WorkshopId = null,
    long? AreaId = null,
    long? UnitId = null,
    long? TerminalBoxId = null,
    long? OnuDeviceId = null,
    int PortNumber = 1,
    bool IsSwitchPort = false,
    string? PortLabel = null,
    string? Remarks = null
);

public sealed record ComputerProbeResultDto(
    long Id,
    string Ip,
    string Status,
    int? ResponseTimeMs,
    DateTimeOffset ProbedAt
);
// IP 池管理与占用探测 DTO
public sealed record IpPoolDto(
    long Id,
    string Name,
    string Cidr,
    string StartIp,
    string EndIp,
    string? Gateway,
    string Netmask,
    int? VlanId,
    string? Description,
    long? WorkshopId,
    string? WorkshopName,
    long? AreaId,
    string? AreaName,
    bool AutoScan,
    int TotalCapacity,
    int AvailableCount,
    int RegisteredCount,
    int UnregisteredActiveCount,
    int ConflictCount,
    int ReservedCount,
    DateTimeOffset? LastScannedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record IpPoolRequest(
    string Name,
    string Cidr,
    string? StartIp = null,
    string? EndIp = null,
    string? Gateway = null,
    string? Netmask = null,
    int? VlanId = null,
    string? Description = null,
    long? WorkshopId = null,
    long? AreaId = null,
    bool AutoScan = true
);

public sealed record BoundDeviceInfoDto(
    string DeviceType,
    long DeviceId,
    string DeviceName,
    string? Detail = null,
    string? Mac = null,
    string? Location = null,
    string? Owner = null,
    string? InterfaceName = null,
    string? Role = null,
    IReadOnlyList<string>? RelatedIps = null
);

public sealed record IpAddressStatusDto(
    string Ip,
    string Status,
    string? SubnetSlice,
    int? ResponseTimeMs,
    DateTimeOffset? LastProbedAt,
    IReadOnlyList<BoundDeviceInfoDto> BoundDevices
);

public sealed record IpPoolDetailDto(
    IpPoolDto Pool,
    IReadOnlyList<string> SubnetSlices,
    IReadOnlyList<IpAddressStatusDto> Addresses
);

public sealed record IpPoolScanResultDto(
    long PoolId,
    int ScannedCount,
    int ActiveCount,
    int InactiveCount,
    DateTimeOffset ScannedAt
);

public sealed record IpConflictCheckResultDto(bool Occupied, string? BoundType, string? BoundName, string? BoundDetail);
public sealed record IpRecommendResultDto(string Ip, string PoolName, string? Gateway, string Netmask, long PoolId);

public sealed record OrganizationStatsDto(
    int TotalWorkshops,
    int TotalAreas,
    int TotalUnits,
    int TotalChannels,
    int TotalComputers,
    int TotalTerminalBoxes
);

// 网络管理设备（核心交换机、汇聚交换机、路由器等）多 IP / 多网关台账 DTO
public sealed record NetworkDeviceIpDto(
    long Id,
    long DeviceId,
    string Ip,
    int? VlanId,
    string InterfaceName,
    string Role,
    string? Description
);

public sealed record NetworkDeviceIpRequest(
    string Ip,
    int? VlanId = null,
    string InterfaceName = "Vlanif",
    string Role = "gateway",
    string? Description = null
);

public sealed record NetworkDeviceDto(
    long Id,
    string Name,
    string DeviceType,
    string? Vendor,
    string? Model,
    string? ManagementIp,
    string? Mac,
    string? Location,
    long? WorkshopId,
    string? WorkshopName,
    long? AreaId,
    string? AreaName,
    string? Owner,
    string Status,
    int? ResponseTimeMs,
    DateTimeOffset? LastProbedAt,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NetworkDeviceIpDto> AssignedIps,
    int PortCount = 0,
    int LinkedPortCount = 0,
    int VmCount = 0,
    IReadOnlyList<DevicePortDto>? Ports = null,
    IReadOnlyList<VirtualMachineDto>? VirtualMachines = null
);

public sealed record DeviceLinkDto(
    long Id,
    long LocalPortId,
    long PeerPortId,
    string PeerDeviceType,
    long PeerDeviceId,
    string PeerDeviceName,
    string PeerPortName,
    string CableType,
    string? CableLabel,
    string? Remarks
);

public sealed record DevicePortDto(
    long Id,
    long DeviceId,
    string DeviceType,
    string PortName,
    string PortType,
    string Speed,
    string Status,
    int SlotNumber,
    int PortIndex,
    string? Remarks,
    DeviceLinkDto? Link
);

public sealed record DevicePortRequest(
    string PortName,
    string PortType = "copper",
    string Speed = "1Gbps",
    string Status = "unused",
    int SlotNumber = 0,
    int PortIndex = 1,
    string? Remarks = null
);

public sealed record BatchGeneratePortsRequest(
    int CopperCount = 0,
    int FiberCount = 0,
    int PotsCount = 0,
    string CopperPrefix = "GE 1/0/",
    string FiberPrefix = "XGE 1/0/",
    string PotsPrefix = "POTS ",
    string CopperSpeed = "1Gbps",
    string FiberSpeed = "10Gbps",
    bool ReplaceExisting = true
);

public sealed record ConnectLinkRequest(
    long LocalPortId,
    long PeerPortId,
    string CableType = "copper",
    string? CableLabel = null,
    string? Remarks = null,
    bool ReplaceExisting = false
);

public sealed record PortCandidateDto(
    long PortId,
    long DeviceId,
    string DeviceType,
    string DeviceName,
    string PortName,
    string PortType,
    string Speed,
    bool IsLinked,
    string? LinkedPeerDesc
);

public sealed record VirtualMachineDto(
    long Id,
    long HostDeviceId,
    string HostDeviceName,
    string VmName,
    string Ip,
    int? VlanId,
    string? Os,
    int? CpuCores,
    decimal? MemoryGb,
    string Status,
    string? BusinessDesc,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record VirtualMachineRequest(
    string VmName,
    string Ip,
    int? VlanId = null,
    string? Os = null,
    int? CpuCores = 4,
    decimal? MemoryGb = 8.0m,
    string Status = "running",
    string? BusinessDesc = null,
    string? Remarks = null
);

public sealed record NetworkDeviceRequest(
    string Name,
    string DeviceType,
    string? Vendor = null,
    string? Model = null,
    string? ManagementIp = null,
    string? Mac = null,
    string? Location = null,
    long? WorkshopId = null,
    long? AreaId = null,
    string? Owner = null,
    string Status = "online",
    string? Remarks = null,
    IReadOnlyList<NetworkDeviceIpRequest>? AssignedIps = null
);

public sealed record NetworkDeviceProbeResultDto(
    long Id,
    string Name,
    string? ManagementIp,
    string Status,
    int? ResponseTimeMs,
    DateTimeOffset ProbedAt
);
