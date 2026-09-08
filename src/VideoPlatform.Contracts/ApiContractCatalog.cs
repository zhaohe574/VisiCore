namespace VideoPlatform.Contracts;

public sealed record ApiResponseContract(int Status, Type? BodyType, string? SchemaName = null, string[]? ContentTypes = null);
public sealed record ApiQueryContract(string Name, Type ValueType, int? Default = null, int? Minimum = null, int? Maximum = null, string[]? Values = null);

// 元数据补充只描述已实现的接口，服务端和生成工具共用同一目录，避免双份契约漂移。
public static class ApiContractCatalog
{
    public static readonly IReadOnlyDictionary<string, ApiResponseContract> Responses = BuildResponses();
    public static readonly IReadOnlyDictionary<string, ApiQueryContract[]> Queries = BuildQueries();
    public static readonly IReadOnlySet<string> PublicOperations = new HashSet<string>(["Login", "GetCsrfToken", "GetLatestRelease", "DownloadRelease"]);

    private static Dictionary<string, ApiResponseContract> BuildResponses()
    {
        var values = new Dictionary<string, ApiResponseContract>
        {
            ["GetCsrfToken"] = new(200, typeof(CsrfTokenResponse)),
            ["ListDevices"] = new(200, typeof(PagedResponse<DeviceDto>), "PagedDeviceResponse"),
            ["CreateDevice"] = new(201, typeof(ResourceCreatedResponse)),
            ["TestDevice"] = new(200, typeof(DeviceSyncResponse)), ["SyncDevice"] = new(200, typeof(DeviceSyncResponse)),
            ["ListChannels"] = new(200, typeof(PagedResponse<ChannelDto>), "PagedChannelResponse"),
            ["SearchRecordings"] = new(200, typeof(RecordingDto[]), "RecordingListResponse"),
            ["ControlPlayback"] = new(200, typeof(PlaybackSessionDto)),
            ["ListAlarms"] = new(200, typeof(PagedResponse<AlarmDto>), "PagedAlarmResponse"), ["GetAlarm"] = new(200, typeof(AlarmDetailDto)),
            ["ActOnAlarm"] = new(200, typeof(AlarmActionResponse)),
            ["ListExports"] = new(200, typeof(PagedResponse<ExportDto>), "PagedExportResponse"),
            ["CreateExport"] = new(202, typeof(ExportCreatedResponse)), ["RetryExport"] = new(202, typeof(ExportQueuedResponse)),
            ["ListReleases"] = new(200, typeof(PagedResponse<ReleaseDto>), "PagedReleaseResponse"),
            ["UploadRelease"] = new(201, typeof(ReleaseDto)), ["GetLatestRelease"] = new(200, typeof(LatestReleaseDto)),
            ["DownloadExport"] = new(200, null, ContentTypes: ["video/mp4", "application/zip"]),
            ["DownloadRelease"] = new(200, null, ContentTypes: ["application/octet-stream"]),
            ["GetAlarmImage"] = new(200, null, ContentTypes: ["image/jpeg", "image/png"]),
            ["ListPlugins"] = new(200, typeof(DevicePluginDto[])),
            ["UpdatePluginStatus"] = new(200, typeof(DevicePluginDto)),
            ["GetPluginHealth"] = new(200, null),
            ["InstallPluginPackage"] = new(201, typeof(DevicePluginDto)),
            ["RegisterPlugin"] = new(201, typeof(DevicePluginDto)),
            ["ProbePlugin"] = new(200, null),
            ["ExportPlugin"] = new(200, null, ContentTypes: ["application/zip"])
        };
        foreach (var id in new[] { "StartLive", "GetliveSession", "RenewliveSession" }) values[id] = new(200, typeof(LiveSessionDto));
        foreach (var id in new[] { "StartPlayback", "GetplaybackSession", "RenewplaybackSession" }) values[id] = new(200, typeof(PlaybackSessionDto));
        foreach (var id in new[] { "Logout", "ChangePassword", "UpdateDevice", "DisableDevice", "StopliveSession", "StopplaybackSession", "StartPtz", "StopPtz", "CallPtzPreset",
            "CancelExport", "PublishRelease", "RevokeRelease", "DeleteOrganizationNode", "AssignChannels", "DeleteRole", "RevokeSession", "DeleteLayout", "DeletePlugin" }) values[id] = new(204, null);
        return values;
    }

    private static Dictionary<string, ApiQueryContract[]> BuildQueries()
    {
        ApiQueryContract[] page = [new("page", typeof(int), 1, 1, 100000), new("pageSize", typeof(int), 50, 1, 200)];
        var result = new Dictionary<string, ApiQueryContract[]>();
        foreach (var id in new[] { "GetUsers", "GetSessions", "ListDevices", "ListChannels", "ListAlarms" }) result[id] = [.. page, new("search", typeof(string))];
        foreach (var id in new[] { "ListExports", "ListReleases" }) result[id] = page;
        result["GetAudit"] = [.. page, new("search", typeof(string)), new("action", typeof(string)), new("from", typeof(DateTimeOffset)), new("to", typeof(DateTimeOffset))];
        result["GetLatestRelease"] = [new("currentVersion", typeof(string)), new("packageType", typeof(string), Values: ["msi", "zip"])];
        return result;
    }
}
