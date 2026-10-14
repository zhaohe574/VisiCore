namespace VideoPlatform.Contracts;

public sealed record DigitalTwinSceneDto(
    long Id,
    string SceneCode,
    string Name,
    string LevelType,
    long? WorkshopId,
    string? WorkshopName,
    string BgImageUrl,
    int NaturalWidth,
    int NaturalHeight,
    decimal DefaultZoom,
    decimal MinZoom,
    decimal MaxZoom,
    decimal CenterXPct,
    decimal CenterYPct,
    int SortOrder,
    int NodeCount,
    int CableCount,
    int OnlineCount,
    int TotalCount
);

public sealed record DigitalTwinNodeDto(
    long CoordinateId,
    long SceneId,
    string TargetType,
    long TargetId,
    string Name,
    string? Code,
    string? Ip,
    string Status,
    decimal XPct,
    decimal YPct,
    decimal? AltitudeM,
    decimal? FovYaw,
    decimal? FovAngle,
    decimal? FovRadiusM,
    string? IconStyle,
    string? Remarks,
    decimal? RxPower,
    decimal? TxPower,
    int? OnuCount,
    int? OnuOnlineCount,
    int? ComputerCount,
    int? CameraCount,
    string? SubSwitchModel,
    int? ResponseTimeMs,
    string? LiveStreamUrl,
    long? ChannelId,
    long? DeviceId,
    string? Location,
    string? ResponsiblePerson,
    string? Department
);

public sealed record DigitalTwinCableDto(
    long Id,
    long SceneId,
    string Name,
    string CableType,
    string? FromDeviceType,
    long? FromDeviceId,
    string? FromDeviceName,
    string? ToDeviceType,
    long? ToDeviceId,
    string? ToDeviceName,
    string Color,
    string PointsJson,
    string Status,
    string? Remarks
);

public sealed record DigitalTwinStatsDto(
    int TotalNodes,
    int OnlineNodes,
    int AbnormalNodes,
    int OfflineNodes,
    int TerminalBoxCount,
    int CameraCount,
    int ComputerCount,
    int NetworkDeviceCount,
    decimal OpticalHealthPercent
);

public sealed record DigitalTwinSceneDetailDto(
    DigitalTwinSceneDto Scene,
    IReadOnlyList<DigitalTwinNodeDto> Nodes,
    IReadOnlyList<DigitalTwinCableDto> Cables,
    DigitalTwinStatsDto Stats
);

public sealed record SaveCoordinateRequest(
    string TargetType,
    long TargetId,
    decimal XPct,
    decimal YPct,
    decimal? AltitudeM = 1.5m,
    decimal? FovYaw = 0m,
    decimal? FovAngle = 60m,
    decimal? FovRadiusM = 25m,
    string? IconStyle = null,
    string? Remarks = null
);

public sealed record BatchSaveCoordinatesRequest(
    IReadOnlyList<SaveCoordinateRequest> Items
);

public sealed record SaveCablePathRequest(
    string Name,
    string CableType = "fiber_core",
    string? FromDeviceType = null,
    long? FromDeviceId = null,
    string? ToDeviceType = null,
    long? ToDeviceId = null,
    string Color = "#38bdf8",
    string PointsJson = "[]",
    string Status = "normal",
    string? Remarks = null
);

public sealed record CandidateNodeDto(
    string TargetType,
    long TargetId,
    string Name,
    string? Code,
    string? Ip,
    string? Location,
    long? AreaId,
    string? AreaName,
    bool IsPlaced
);
