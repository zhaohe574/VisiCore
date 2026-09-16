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
            ["GetPreferences"] = new(200, typeof(UserPreferencesDto)), ["UpdatePreferences"] = new(200, typeof(UserPreferencesDto)),
            ["ListDevices"] = new(200, typeof(PagedResponse<DeviceDto>), "PagedDeviceResponse"),
            ["CreateDevice"] = new(201, typeof(ResourceCreatedResponse)),
            ["TestDevice"] = new(200, typeof(DeviceSyncResponse)), ["SyncDevice"] = new(200, typeof(DeviceSyncResponse)),
            ["ListChannels"] = new(200, typeof(PagedResponse<ChannelDto>), "PagedChannelResponse"),
            ["ProbeChannelStream"] = new(200, typeof(StreamCapabilityDto)),
            ["SearchRecordings"] = new(200, typeof(RecordingDto[]), "RecordingListResponse"),
            ["ControlPlayback"] = new(200, typeof(PlaybackSessionDto)),
            ["ListAlarms"] = new(200, typeof(PagedResponse<AlarmDto>), "PagedAlarmResponse"), ["GetAlarm"] = new(200, typeof(AlarmDetailDto)),
            ["ActOnAlarm"] = new(200, typeof(AlarmActionResponse)),
            ["ListExports"] = new(200, typeof(PagedResponse<ExportDto>), "PagedExportResponse"),
            ["CreateExport"] = new(202, typeof(ExportCreatedResponse)), ["RetryExport"] = new(202, typeof(ExportQueuedResponse)),
            ["ListReleases"] = new(200, typeof(PagedResponse<ReleaseDto>), "PagedReleaseResponse"),
            ["UploadRelease"] = new(201, typeof(ReleaseDto)), ["GetLatestRelease"] = new(200, typeof(LatestReleaseDto)),
            ["ListPublicReleases"] = new(200, typeof(LatestReleaseDto[])),
            ["UpdateRelease"] = new(200, typeof(ReleaseDto)), ["DeleteRelease"] = new(204, null),
            ["DeleteReleaseByVersion"] = new(204, null), ["PublishReleaseByVersion"] = new(204, null),
            ["DownloadExport"] = new(200, null, ContentTypes: ["video/mp4", "application/zip"]),
            ["DownloadRelease"] = new(200, null, ContentTypes: ["application/octet-stream"]),
            ["GetAlarmImage"] = new(200, null, ContentTypes: ["image/jpeg", "image/png"]),
            ["ListPlugins"] = new(200, typeof(DevicePluginDto[])),
            ["UpdatePluginStatus"] = new(200, typeof(DevicePluginDto)),
            ["GetPluginHealth"] = new(200, null),
            ["InstallPluginPackage"] = new(201, typeof(DevicePluginDto)),
            ["RegisterPlugin"] = new(201, typeof(DevicePluginDto)),
            ["ProbePlugin"] = new(200, null),
            ["ExportPlugin"] = new(200, null, ContentTypes: ["application/zip"]),
            ["GetSslOverview"] = new(200, typeof(SslOverviewDto)),
            ["GetSslDomains"] = new(200, typeof(SslDomainDto[])),
            ["CreateSslDomain"] = new(201, typeof(SslDomainDto)),
            ["UpdateSslDomain"] = new(200, typeof(SslDomainDto)),
            ["SetPrimarySslDomain"] = new(200, typeof(SslDomainDto)),
            ["GetSslCertificates"] = new(200, typeof(SslCertificateDto[])),
            ["GetSslCertificate"] = new(200, typeof(SslCertificateDto)),
            ["UploadSslCertificate"] = new(201, typeof(SslCertificateDto)),
            ["SetActiveSslCertificate"] = new(200, typeof(SslCertificateDto)),
            ["DownloadSslCertificate"] = new(200, null, ContentTypes: ["application/x-x509-ca-cert"]),
            ["GetNginxConfig"] = new(200, typeof(NginxConfigDto))
        };
        foreach (var id in new[] { "StartLive", "GetliveSession", "RenewliveSession" }) values[id] = new(200, typeof(LiveSessionDto));
        foreach (var id in new[] { "StartPlayback", "GetplaybackSession", "RenewplaybackSession" }) values[id] = new(200, typeof(PlaybackSessionDto));
        foreach (var id in new[] { "Logout", "ChangePassword", "UpdateDevice", "DisableDevice", "StopliveSession", "StopplaybackSession", "StartPtz", "StopPtz", "CallPtzPreset",
            // 清空当前账号的全部媒体会话（DELETE /live-sessions 与 /playback-sessions）：桌面端在退出登录、
            // 权限变化与压力测试收尾时调用，此前漏登记元数据，导致生成客户端无法为它生成调用。
            "StopActiveliveSessions", "StopActiveplaybackSessions",
            "CancelExport", "PublishRelease", "RevokeRelease", "DeleteOrganizationNode", "AssignChannels", "DeleteRole", "RevokeSession", "DeleteLayout", "DeletePlugin",
            "DeleteSslDomain", "DeleteSslCertificate" }) values[id] = new(204, null);
        return values;
    }

    private static Dictionary<string, ApiQueryContract[]> BuildQueries()
    {
        ApiQueryContract[] page = [new("page", typeof(int), 1, 1, 100000), new("pageSize", typeof(int), 50, 1, 200)];
        var result = new Dictionary<string, ApiQueryContract[]>();
        foreach (var id in new[] { "GetUsers", "GetSessions", "ListDevices", "ListChannels", "ListAlarms" }) result[id] = [.. page, new("search", typeof(string))];
        foreach (var id in new[] { "GetSslDomains", "GetSslCertificates" }) result[id] = [new("search", typeof(string))];
        foreach (var id in new[] { "ListExports", "ListReleases" }) result[id] = page;
        result["GetAudit"] = [.. page, new("search", typeof(string)), new("action", typeof(string)), new("from", typeof(DateTimeOffset)), new("to", typeof(DateTimeOffset))];
        result["GetLatestRelease"] = [new("currentVersion", typeof(string)), new("packageType", typeof(string), Values: ["msi", "zip"])];
        // 清空媒体会话支持 all=true 清空当前账号的全部会话；不传或 false 只清当前登录会话。
        // 端点从 HttpContext 读取该参数，OpenAPI 无法自动发现，必须显式声明，否则生成客户端发不出 all=true。
        foreach (var id in new[] { "StopActiveliveSessions", "StopActiveplaybackSessions" }) result[id] = [new("all", typeof(bool))];
        return result;
    }
}
