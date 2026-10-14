// 此文件由 OpenAPI 自动生成，请运行客户端生成工具更新。
export type paths = {
    "/api/v2/alarms": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListAlarms"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/alarms/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetAlarm"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/alarms/{id}/actions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ActOnAlarm"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/alarms/{id}/image": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetAlarmImage"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/assets/{type}/{id}/context": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetAssetContext"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/assets/search": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["SearchAssets"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/assets/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetAssetSummary"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/audit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetAudit"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/csrf": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetCsrfToken"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["Login"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["Logout"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetCurrentUser"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["ChangePassword"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/preferences": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPreferences"];
        put: operations["UpdatePreferences"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/profile": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateProfile"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/auth/refresh": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RefreshSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListChannels"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetChannel"];
        put: operations["UpdateChannel"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/{id}/ptz": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["StartPtz"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/{id}/ptz/presets/{preset}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["CallPtzPreset"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/{id}/ptz/stop": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["StopPtz"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/{id}/streams/probe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ProbeChannelStream"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/assignment": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["AssignChannels"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/batch-claim": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["BatchClaimChannels"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/batch-network-bindings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["BatchSetNetworkBindings"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/sort": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["SortChannels"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/channels/unassigned": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetUnassignedChannels"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/computers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListComputers"];
        put?: never;
        post: operations["CreateComputer"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/computers/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetComputer"];
        put: operations["UpdateComputer"];
        post?: never;
        delete: operations["DeleteComputer"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/computers/{id}/probe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ProbeComputer"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/computers/batch-update": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["BatchUpdateComputers"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/computers/probe-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ProbeAllComputers"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/dashboard": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetDashboard"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/device-links/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["DisconnectDeviceLink"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/device-links/connect": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ConnectDeviceLink"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/device-ports/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateDevicePort"];
        post?: never;
        delete: operations["DeleteDevicePort"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/device-ports/candidates": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPortCandidates"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/devices": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListDevices"];
        put?: never;
        post: operations["CreateDevice"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/devices/{deviceType}/{deviceId}/ports": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListDevicePorts"];
        put?: never;
        post: operations["CreateDevicePort"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/devices/{deviceType}/{deviceId}/ports/batch-generate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["BatchGenerateDevicePorts"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/devices/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetDevice"];
        put: operations["UpdateDevice"];
        post?: never;
        delete: operations["DeleteDevice"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/devices/{id}/sync": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["SyncDevice"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/devices/{id}/test": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["TestDevice"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/cables/{cableId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["DeleteDigitalTwinCable"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/coordinates/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["DeleteDigitalTwinCoordinate"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListDigitalTwinScenes"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetDigitalTwinSceneDetail"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}/auto-align": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["AutoAlignDigitalTwinCad"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}/cables": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["CreateDigitalTwinCable"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}/cables/{cableId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateDigitalTwinCable"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}/candidates": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetDigitalTwinCandidateNodes"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}/coordinates": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["SaveDigitalTwinCoordinate"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/digital-twin/scenes/{id}/coordinates/batch": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["BatchSaveDigitalTwinCoordinates"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/exports": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListExports"];
        put?: never;
        post: operations["CreateExport"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/exports/{id}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["CancelExport"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/exports/{id}/download": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["DownloadExport"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/exports/{id}/retry": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RetryExport"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/favorites": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetFavorites"];
        put: operations["UpdateFavorites"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ip-pools": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListIpPools"];
        put?: never;
        post: operations["CreateIpPool"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ip-pools/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetIpPool"];
        put: operations["UpdateIpPool"];
        post?: never;
        delete: operations["DeleteIpPool"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ip-pools/{id}/detail": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetIpPoolDetail"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ip-pools/{id}/scan": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ScanIpPool"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ip-pools/check-ip": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["CheckIpConflict"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ip-pools/recommend-ip": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["RecommendFreeIp"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/layouts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetLayouts"];
        put?: never;
        post: operations["CreateLayout"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/layouts/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateLayout"];
        post?: never;
        delete: operations["DeleteLayout"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/live-sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["StartLive"];
        delete: operations["StopActiveliveSessions"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/live-sessions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetliveSession"];
        put?: never;
        post?: never;
        delete: operations["StopliveSession"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/live-sessions/{id}/renew": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RenewliveSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/network-devices": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListNetworkDevices"];
        put?: never;
        post: operations["CreateNetworkDevice"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/network-devices/{hostDeviceId}/vms": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListHostVms"];
        put?: never;
        post: operations["CreateVirtualMachine"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/network-devices/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetNetworkDevice"];
        put: operations["UpdateNetworkDevice"];
        post?: never;
        delete: operations["DeleteNetworkDevice"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/network-devices/{id}/probe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ProbeNetworkDevice"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/network-devices/probe-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ProbeAllNetworkDevices"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/nvr/available-channels": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetNvrAvailableChannels"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/organization": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetOrganization"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/organization/{kind}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["CreateOrganizationNode"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/organization/{kind}/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateOrganizationNode"];
        post?: never;
        delete: operations["DeleteOrganizationNode"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/organization/channels/add-camera": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["AddCameraDirect"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/organization/stats": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetOrganizationStats"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/permissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPermissions"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/playback-sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["StartPlayback"];
        delete: operations["StopActiveplaybackSessions"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/playback-sessions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetplaybackSession"];
        put?: never;
        post?: never;
        delete: operations["StopplaybackSession"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/playback-sessions/{id}/control": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ControlPlayback"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/playback-sessions/{id}/renew": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RenewplaybackSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListPlugins"];
        put?: never;
        post: operations["RegisterPlugin"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["DeletePlugin"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins/{id}/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ExportPlugin"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins/{id}/health": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPluginHealth"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins/{id}/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdatePluginStatus"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins/install": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["InstallPluginPackage"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/plugins/probe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ProbePlugin"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/bindings/apply": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ApplyTopologyCorrections"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/bindings/auto-discover": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["AutoDiscoverTopologyBindings"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/dashboard/history": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetDeviceOfflineHistory"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/dashboard/kanban": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPonKanbanDashboard"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/health-index": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetOpticalHealthIndex"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/olts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetOltsWithPorts"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/onus": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListOnus"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/onus/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetOnu"];
        put: operations["UpdateOnu"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/onus/{id}/diagnosis": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["DiagnoseOnu"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/onus/{id}/reboot": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RebootOnu"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/statistics": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPonStatistics"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/pon/sync": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["SyncPonFromEsight"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/public/releases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListPublicReleases"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/public/releases/latest": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetLatestRelease"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/recordings/search": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["SearchRecordings"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListReleases"];
        put?: never;
        post: operations["UploadRelease"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateRelease"];
        post?: never;
        delete: operations["DeleteRelease"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases/{id}/download": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["DownloadRelease"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases/{id}/publish": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["PublishRelease"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases/{id}/revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RevokeRelease"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases/version/{version}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["DeleteReleaseByVersion"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/releases/version/{version}/publish": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["PublishReleaseByVersion"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/roles": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetRoles"];
        put?: never;
        post: operations["CreateRole"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/roles/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateRole"];
        post?: never;
        delete: operations["DeleteRole"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/roles/{id}/permissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["SetRolePermissions"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/scopes/{kind}/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetScopes"];
        put: operations["UpdateScopes"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSessions"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/sessions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: operations["RevokeSession"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/settings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSettings"];
        put: operations["UpdateSettings"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/certificates": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSslCertificates"];
        put?: never;
        post: operations["UploadSslCertificate"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/certificates/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSslCertificate"];
        put?: never;
        post?: never;
        delete: operations["DeleteSslCertificate"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/certificates/{id}/active": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["SetActiveSslCertificate"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/certificates/{id}/download": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["DownloadSslCertificate"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/domains": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSslDomains"];
        put?: never;
        post: operations["CreateSslDomain"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/domains/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateSslDomain"];
        post?: never;
        delete: operations["DeleteSslDomain"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/domains/{id}/primary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["SetPrimarySslDomain"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/nginx-config": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetNginxConfig"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/ssl/overview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSslOverview"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/system": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetSystemStatistics"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/terminal-boxes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["ListTerminalBoxes"];
        put?: never;
        post: operations["CreateTerminalBox"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/terminal-boxes/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetTerminalBox"];
        put: operations["UpdateTerminalBox"];
        post?: never;
        delete: operations["DeleteTerminalBox"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/terminal-boxes/{id}/topology": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetTerminalBoxTopology"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/terminal-boxes/topology-tree": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetTerminalBoxTopologyTree"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/users": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetUsers"];
        put?: never;
        post: operations["CreateUser"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/users/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateUser"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v2/virtual-machines/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateVirtualMachine"];
        post?: never;
        delete: operations["DeleteVirtualMachine"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
};
export type webhooks = Record<string, never>;
export type components = {
    schemas: {
        AddCameraRequest: {
            alias: null | string;
            /** Format: int32 */
            channelNumber: null | number | string;
            ipcHost: string;
            /** @default  */
            ipcPassword: string;
            /**
             * Format: int32
             * @default 8000
             */
            ipcPort: number | string;
            /** @default admin */
            ipcUsername: string;
            /** @default false */
            isSwitchPort: boolean;
            name?: null | string;
            /** Format: int64 */
            nvrDeviceId: number | string;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            portLabel?: null | string;
            /** Format: int32 */
            portNumber?: null | number | string;
            /** @default HIKVISION */
            protocolType: string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
            /** Format: int64 */
            unitId?: null | number | string;
        };
        AdministrationAuditDto: {
            action: string;
            clientIp: null | string;
            /** Format: date-time */
            createdAt: string;
            /** Format: int64 */
            id: number | string;
            resource: string;
            summary: null | string;
            username: null | string;
        };
        AdministrationLayoutDto: {
            channelIds: (null | number | string)[];
            /** Format: int64 */
            id: number | string;
            /** Format: int32 */
            intervalSeconds: number | string;
            kind: string;
            /** Format: int32 */
            layout: number | string;
            name: string;
            shared: boolean;
        };
        AdministrationRoleDto: {
            code: string;
            /** Format: int64 */
            id: number | string;
            name: string;
            permissionCodes: string[];
            status: string;
            /** Format: int64 */
            userCount: number | string;
        };
        AdministrationSessionDto: {
            clientType: string;
            clientVersion: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: date-time */
            expiresAt: string;
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            lastSeenAt: string;
            /** Format: int64 */
            userId: number | string;
            username: string;
        };
        AlarmActionRequest: {
            action: string;
            note: null | string;
        };
        AlarmActionResponse: {
            /** Format: int64 */
            id: number | string;
            state: string;
        };
        AlarmDetailDto: {
            /** Format: int64 */
            channelId: null | number | string;
            channelName: null | string;
            /** Format: int64 */
            deviceId: number | string;
            deviceName: string;
            eventType: string;
            history: components["schemas"]["AlarmHistoryDto"][];
            /** Format: int64 */
            id: number | string;
            imageAvailable: boolean;
            note: string;
            /** Format: date-time */
            occurredAt: string;
            /** Format: int64 */
            ownerId: null | number | string;
            ownerName: null | string;
            payload: unknown;
            recovered: boolean;
            state: string;
            /** Format: int64 */
            version: number | string;
        };
        AlarmDto: {
            /** Format: int64 */
            channelId: null | number | string;
            channelName: null | string;
            /** Format: int64 */
            deviceId: number | string;
            deviceName: string;
            eventType: string;
            /** Format: int64 */
            id: number | string;
            imageAvailable: boolean;
            note: string;
            /** Format: date-time */
            occurredAt: string;
            /** Format: int64 */
            ownerId: null | number | string;
            ownerName: null | string;
            payload: unknown;
            recovered: boolean;
            state: string;
            /** Format: int64 */
            version: number | string;
        };
        AlarmHistoryDto: {
            action: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: int64 */
            id: number | string;
            note: string;
            username: null | string;
        };
        ApplyTopologyCorrectionRequest: {
            bindings: components["schemas"]["ApplyTopologyItemRequest"][];
        };
        ApplyTopologyItemRequest: {
            /** Format: int64 */
            channelId: number | string;
            /** @default false */
            isSwitchPort: boolean;
            /** Format: int64 */
            onuDeviceId: number | string;
            portLabel?: null | string;
            /**
             * Format: int32
             * @default 1
             */
            portNumber: number | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
        };
        AssetAlarmDto: {
            /** Format: int64 */
            channelId: null | number | string;
            eventType: string;
            /** Format: int64 */
            id: number | string;
            /** Format: date-time */
            occurredAt: string;
            recovered: boolean;
            state: string;
        };
        AssetBatchResultDto: {
            ids: (number | string)[];
            /** Format: int32 */
            updated: number | string;
        };
        AssetContextDto: {
            alarms: components["schemas"]["AssetAlarmDto"][];
            asset: components["schemas"]["AssetDto"];
            evidence: components["schemas"]["AssetEvidenceDto"][];
            offlineHistory: components["schemas"]["AssetHistoryDto"][];
            organization: components["schemas"]["AssetOrganizationDto"][];
            ports: components["schemas"]["AssetPortDto"][];
            relatedAssets: components["schemas"]["AssetDto"][];
        };
        AssetDto: {
            /** Format: int64 */
            areaId: null | number | string;
            code: string;
            /** Format: int64 */
            deviceId: null | number | string;
            healthStatus: null | string;
            ip: null | string;
            isSwitchPort: boolean;
            name: string;
            /** Format: date-time */
            observedAt: null | string;
            /** Format: int64 */
            onuDeviceId: null | number | string;
            /** Format: int32 */
            portNumber: null | number | string;
            ref: components["schemas"]["AssetRef"];
            rootCause: null | string;
            /** Format: double */
            rxPower: null | number | string;
            status: string;
            /** Format: int64 */
            terminalBoxId: null | number | string;
            /** Format: int64 */
            unitId: null | number | string;
            /** Format: int64 */
            workshopId: null | number | string;
        };
        AssetEvidenceDto: {
            label: string;
            /** Format: date-time */
            observedAt: null | string;
            source: string;
            value: string;
        };
        AssetHistoryDto: {
            deviceName: string;
            /** Format: date-time */
            downTime: string;
            eventType: string;
            /** Format: int64 */
            id: number | string;
            reason: null | string;
            /** Format: date-time */
            upTime: null | string;
        };
        AssetOrganizationDto: {
            /** Format: int64 */
            id: number | string;
            name: string;
            type: string;
        };
        AssetPortDto: {
            cableLabel: null | string;
            /** Format: int64 */
            id: number | string;
            name: string;
            /** Format: int64 */
            peerPortId: null | number | string;
            status: string;
        };
        AssetRef: {
            /** Format: int64 */
            id: number | string;
            type: string;
        };
        AssetSummaryDto: {
            /** Format: int64 */
            missingNetworkBinding: number | string;
            /** Format: int64 */
            missingOrganization: number | string;
            /** Format: int64 */
            offline: number | string;
            /** Format: int64 */
            online: number | string;
            /** Format: int64 */
            total: number | string;
            types: components["schemas"]["AssetTypeCountDto"][];
            /** Format: int64 */
            unknown: number | string;
        };
        AssetTypeCountDto: {
            /** Format: int64 */
            offline: number | string;
            /** Format: int64 */
            online: number | string;
            /** Format: int64 */
            total: number | string;
            type: string;
            /** Format: int64 */
            unknown: number | string;
        };
        AssignmentRequest: {
            channelIds: (number | string)[];
            /** Format: int64 */
            unitId: null | number | string;
        };
        AuthResponse: {
            accessToken: null | string;
            /** Format: date-time */
            expiresAt: string;
            user: components["schemas"]["UserDto"];
        };
        AutoTopologyBindingSuggestionDto: {
            channelAlias: null | string;
            /** Format: int64 */
            channelId: number | string;
            channelIp: null | string;
            channelName: string;
            /** Format: int32 */
            confidenceScore: number | string;
            /** Format: int64 */
            currentOnuId: null | number | string;
            currentOnuName: null | string;
            matchReason: string;
            needsCorrection: boolean;
            suggestedOnuAlias: null | string;
            /** Format: int64 */
            suggestedOnuId: number | string;
            suggestedOnuName: string;
            /** Format: int32 */
            suggestedPortNumber: number | string;
            /** Format: int64 */
            suggestedTerminalBoxId: null | number | string;
            suggestedTerminalBoxName: null | string;
        };
        AutoTopologyDiscoverResultDto: {
            /** Format: int32 */
            suggestedCount: number | string;
            suggestions: components["schemas"]["AutoTopologyBindingSuggestionDto"][];
            /** Format: int32 */
            totalEvaluated: number | string;
        };
        BatchChannelNetworkBindingRequest: {
            channelIds: (number | string)[];
            isSwitchPort?: null | boolean;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            /** Format: int32 */
            startPortNumber?: null | number | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
        };
        BatchClaimChannelsRequest: {
            channelIds: (number | string)[];
            isSwitchPort?: null | boolean;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            /** Format: int32 */
            startPortNumber?: null | number | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
            /** Format: int64 */
            unitId: number | string;
        };
        BatchGeneratePortsRequest: {
            /**
             * Format: int32
             * @default 0
             */
            copperCount: number | string;
            /** @default GE 1/0/ */
            copperPrefix: string;
            /** @default 1Gbps */
            copperSpeed: string;
            /**
             * Format: int32
             * @default 0
             */
            fiberCount: number | string;
            /** @default XGE 1/0/ */
            fiberPrefix: string;
            /** @default 10Gbps */
            fiberSpeed: string;
            /**
             * Format: int32
             * @default 0
             */
            potsCount: number | string;
            /** @default POTS */
            potsPrefix: string;
            /** @default true */
            replaceExisting: boolean;
        };
        BatchSaveCoordinatesRequest: {
            items: components["schemas"]["SaveCoordinateRequest"][];
        };
        BoundDeviceInfoDto: {
            detail?: null | string;
            /** Format: int64 */
            deviceId: number | string;
            deviceName: string;
            deviceType: string;
            interfaceName?: null | string;
            location?: null | string;
            mac?: null | string;
            owner?: null | string;
            relatedIps?: null | string[];
            role?: null | string;
        };
        CandidateNodeDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            code: null | string;
            ip: null | string;
            isPlaced: boolean;
            location: null | string;
            name: string;
            /** Format: int64 */
            targetId: number | string;
            targetType: string;
        };
        ChannelDto: {
            alias: null | string;
            codec: null | string;
            /** Format: int32 */
            deviceChannel: number | string;
            deviceHost?: null | string;
            /** Format: int64 */
            deviceId: number | string;
            deviceModel?: null | string;
            deviceName: string;
            /** Format: int32 */
            devicePort?: null | number | string;
            deviceSerial?: null | string;
            firmwareVersion?: null | string;
            /** @default false */
            hasPassword: boolean;
            /** Format: int64 */
            id: number | string;
            ip?: null | string;
            isSwitchPort?: null | boolean;
            model: null | string;
            name: string;
            /** Format: int32 */
            networkPort?: null | number | string;
            onuAlias?: null | string;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            onuHealthStatus?: null | string;
            onuIfDesc?: null | string;
            onuName?: null | string;
            onuRootCause?: null | string;
            /** Format: double */
            onuRxPower?: null | number | string;
            onuStatus?: null | string;
            opticalLink?: null | components["schemas"]["OpticalLinkInfoDto"];
            pluginId?: null | string;
            pluginName?: null | string;
            portLabel?: null | string;
            ptzCapable: boolean;
            remark?: null | string;
            /**
             * Format: int32
             * @default 0
             */
            sortOrder: number | string;
            status: string;
            terminalBoxCode?: null | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
            terminalBoxName?: null | string;
            /** Format: int64 */
            unitId: null | number | string;
            username?: null | string;
        };
        ChannelSortRequest: {
            channelIds: (number | string)[];
            /** Format: int64 */
            unitId: number | string;
        };
        ChannelUpdateRequest: {
            alias: null | string;
            clearNetworkBinding?: null | boolean;
            /** @default false */
            clearPassword: boolean;
            ip?: null | string;
            isSwitchPort?: null | boolean;
            /** Format: int32 */
            networkPort?: null | number | string;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            password?: null | string;
            portLabel?: null | string;
            remark?: null | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
            /** Format: int64 */
            unitId?: null | number | string;
            username?: null | string;
        };
        ComputerBatchUpdateRequest: {
            /** Format: int64 */
            areaId?: null | number | string;
            computerIds: (number | string)[];
            /** @default false */
            isSwitchPort: boolean;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            /** Format: int32 */
            startPortNumber?: null | number | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
            /** Format: int64 */
            unitId?: null | number | string;
            /** @default false */
            updateNetworkBinding: boolean;
            /** @default false */
            updateOrganization: boolean;
            /** Format: int64 */
            workshopId?: null | number | string;
        };
        ComputerDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            /** Format: date-time */
            createdAt: string;
            department: null | string;
            /** Format: int64 */
            id: number | string;
            ip: string;
            isSwitchPort: boolean;
            /** Format: date-time */
            lastProbedAt: null | string;
            /** Format: date-time */
            lastSeenAt: null | string;
            location: null | string;
            mac: null | string;
            name: string;
            onuAlias: null | string;
            /** Format: int64 */
            onuDeviceId: null | number | string;
            onuName: null | string;
            os: null | string;
            owner: null | string;
            portLabel: null | string;
            /** Format: int32 */
            portNumber: number | string;
            remarks: null | string;
            /** Format: int32 */
            responseTimeMs: null | number | string;
            status: string;
            terminalBoxCode: null | string;
            /** Format: int64 */
            terminalBoxId: null | number | string;
            terminalBoxName: null | string;
            /** Format: int64 */
            unitId: null | number | string;
            unitName: null | string;
            /** Format: date-time */
            updatedAt: string;
            /** Format: int64 */
            workshopId: null | number | string;
            workshopName: null | string;
        };
        ComputerProbeResultDto: {
            /** Format: int64 */
            id: number | string;
            ip: string;
            /** Format: date-time */
            probedAt: string;
            /** Format: int32 */
            responseTimeMs: null | number | string;
            status: string;
        };
        ComputerRequest: {
            /** Format: int64 */
            areaId?: null | number | string;
            department?: null | string;
            ip: string;
            /** @default false */
            isSwitchPort: boolean;
            location?: null | string;
            mac?: null | string;
            name: string;
            /** Format: int64 */
            onuDeviceId?: null | number | string;
            os?: null | string;
            owner?: null | string;
            portLabel?: null | string;
            /**
             * Format: int32
             * @default 1
             */
            portNumber: number | string;
            remarks?: null | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
            /** Format: int64 */
            unitId?: null | number | string;
            /** Format: int64 */
            workshopId?: null | number | string;
        };
        ConnectLinkRequest: {
            cableLabel?: null | string;
            /** @default copper */
            cableType: string;
            /** Format: int64 */
            localPortId: number | string;
            /** Format: int64 */
            peerPortId: number | string;
            remarks?: null | string;
            /** @default false */
            replaceExisting: boolean;
        };
        CsrfTokenResponse: {
            token: string;
        };
        DashboardDto: {
            /** Format: int64 */
            alarmsToday: number | string;
            /** Format: int64 */
            channels: number | string;
            /** Format: int64 */
            devices: number | string;
            /** Format: int64 */
            liveSessions: number | string;
            /** Format: int64 */
            onlineChannels: number | string;
            /** Format: int64 */
            onlineDevices: number | string;
            /** Format: int64 */
            onlineSessions: number | string;
            /** Format: int64 */
            pendingAlarms: number | string;
            /** Format: int64 */
            playbackSessions: number | string;
            /** Format: int64 */
            roles: number | string;
            /** Format: int64 */
            users: number | string;
        };
        DeviceDto: {
            /** Format: int64 */
            channelCount: number | string;
            enabled: boolean;
            host: string;
            /** Format: int64 */
            id: number | string;
            /** Format: date-time */
            lastSeenAt: null | string;
            model: null | string;
            name: string;
            /** Format: int64 */
            onlineChannels: number | string;
            /** @default hikvision */
            pluginId: null | string;
            pluginName?: null | string;
            /** Format: int32 */
            port: number | string;
            serialNumber: null | string;
            status: string;
            syncError?: null | string;
            username: string;
        };
        DeviceLinkDto: {
            cableLabel: null | string;
            cableType: string;
            /** Format: int64 */
            id: number | string;
            /** Format: int64 */
            localPortId: number | string;
            /** Format: int64 */
            peerDeviceId: number | string;
            peerDeviceName: string;
            peerDeviceType: string;
            /** Format: int64 */
            peerPortId: number | string;
            peerPortName: string;
            remarks: null | string;
        };
        DeviceOfflineHistoryDto: {
            /** Format: int64 */
            channelId: null | number | string;
            /** Format: date-time */
            createdAt: string;
            deviceIdentifier: null | string;
            deviceName: string;
            deviceType: string;
            /** Format: date-time */
            downTime: string;
            /** Format: int32 */
            durationSeconds: null | number | string;
            eventType: string;
            /** Format: int64 */
            id: number | string;
            /** Format: int64 */
            onuDeviceId: null | number | string;
            reason: null | string;
            terminalBoxCode: null | string;
            /** Format: int64 */
            terminalBoxId: null | number | string;
            terminalBoxName: null | string;
            /** Format: date-time */
            upTime: null | string;
        };
        DevicePluginDto: {
            capabilities: string[];
            configSchema: null | string;
            /** Format: date-time */
            createdAt: string;
            description: null | string;
            /** Format: int64 */
            deviceCount: number | string;
            endpointUrl: string;
            healthStatus: null | string;
            id: string;
            name: string;
            status: string;
            /** Format: date-time */
            updatedAt: string;
            vendor: string;
            version: string;
        };
        "DevicePluginDto[]": components["schemas"]["DevicePluginDto"][];
        DevicePortDto: {
            /** Format: int64 */
            deviceId: number | string;
            deviceType: string;
            /** Format: int64 */
            id: number | string;
            link: null | components["schemas"]["DeviceLinkDto"];
            /** Format: int32 */
            portIndex: number | string;
            portName: string;
            portType: string;
            remarks: null | string;
            /** Format: int32 */
            slotNumber: number | string;
            speed: string;
            status: string;
        };
        DevicePortRequest: {
            /**
             * Format: int32
             * @default 1
             */
            portIndex: number | string;
            portName: string;
            /** @default copper */
            portType: string;
            remarks?: null | string;
            /**
             * Format: int32
             * @default 0
             */
            slotNumber: number | string;
            /** @default 1Gbps */
            speed: string;
            /** @default unused */
            status: string;
        };
        DeviceRequest: {
            /** @default true */
            enabled: boolean;
            extraConfig?: null | string;
            host: string;
            name: string;
            password: null | string;
            /** @default hikvision */
            pluginId: null | string;
            /** Format: int32 */
            port: number | string;
            username: string;
        };
        DeviceSyncResponse: {
            /** Format: int32 */
            channels: number | string;
            success: boolean;
        };
        DigitalTwinCableDto: {
            cableType: string;
            color: string;
            /** Format: int64 */
            fromDeviceId: null | number | string;
            fromDeviceName: null | string;
            fromDeviceType: null | string;
            /** Format: int64 */
            id: number | string;
            name: string;
            pointsJson: string;
            remarks: null | string;
            /** Format: int64 */
            sceneId: number | string;
            status: string;
            /** Format: int64 */
            toDeviceId: null | number | string;
            toDeviceName: null | string;
            toDeviceType: null | string;
        };
        DigitalTwinNodeDto: {
            /** Format: double */
            altitudeM: null | number | string;
            /** Format: int32 */
            cameraCount: null | number | string;
            /** Format: int64 */
            channelId: null | number | string;
            code: null | string;
            /** Format: int32 */
            computerCount: null | number | string;
            /** Format: int64 */
            coordinateId: number | string;
            department: null | string;
            /** Format: int64 */
            deviceId: null | number | string;
            /** Format: double */
            fovAngle: null | number | string;
            /** Format: double */
            fovRadiusM: null | number | string;
            /** Format: double */
            fovYaw: null | number | string;
            iconStyle: null | string;
            ip: null | string;
            liveStreamUrl: null | string;
            location: null | string;
            name: string;
            /** Format: int32 */
            onuCount: null | number | string;
            /** Format: int32 */
            onuOnlineCount: null | number | string;
            remarks: null | string;
            /** Format: int32 */
            responseTimeMs: null | number | string;
            responsiblePerson: null | string;
            /** Format: double */
            rxPower: null | number | string;
            /** Format: int64 */
            sceneId: number | string;
            status: string;
            subSwitchModel: null | string;
            /** Format: int64 */
            targetId: number | string;
            targetType: string;
            /** Format: double */
            txPower: null | number | string;
            /** Format: double */
            xPct: number | string;
            /** Format: double */
            yPct: number | string;
        };
        DigitalTwinSceneDetailDto: {
            cables: components["schemas"]["DigitalTwinCableDto"][];
            nodes: components["schemas"]["DigitalTwinNodeDto"][];
            scene: components["schemas"]["DigitalTwinSceneDto"];
            stats: components["schemas"]["DigitalTwinStatsDto"];
        };
        DigitalTwinSceneDto: {
            bgImageUrl: string;
            /** Format: int32 */
            cableCount: number | string;
            /** Format: double */
            centerXPct: number | string;
            /** Format: double */
            centerYPct: number | string;
            /** Format: double */
            defaultZoom: number | string;
            /** Format: int64 */
            id: number | string;
            levelType: string;
            /** Format: double */
            maxZoom: number | string;
            /** Format: double */
            minZoom: number | string;
            name: string;
            /** Format: int32 */
            naturalHeight: number | string;
            /** Format: int32 */
            naturalWidth: number | string;
            /** Format: int32 */
            nodeCount: number | string;
            /** Format: int32 */
            onlineCount: number | string;
            sceneCode: string;
            /** Format: int32 */
            sortOrder: number | string;
            /** Format: int32 */
            totalCount: number | string;
            /** Format: int64 */
            workshopId: null | number | string;
            workshopName: null | string;
        };
        DigitalTwinStatsDto: {
            /** Format: int32 */
            abnormalNodes: number | string;
            /** Format: int32 */
            cameraCount: number | string;
            /** Format: int32 */
            computerCount: number | string;
            /** Format: int32 */
            networkDeviceCount: number | string;
            /** Format: int32 */
            offlineNodes: number | string;
            /** Format: int32 */
            onlineNodes: number | string;
            /** Format: double */
            opticalHealthPercent: number | string;
            /** Format: int32 */
            terminalBoxCount: number | string;
            /** Format: int32 */
            totalNodes: number | string;
        };
        DiskInfoDto: {
            /** Format: int64 */
            freeBytes: null | number | string;
            isDataPath: boolean;
            label: null | string;
            name: string;
            /** Format: int64 */
            totalBytes: null | number | string;
            /** Format: int64 */
            usedBytes: null | number | string;
            /** Format: double */
            usedPercent: null | number | string;
        };
        ErrorResponse: {
            code: string;
            message: string;
            traceId: string;
        };
        ExportCreatedResponse: {
            /** Format: uuid */
            id: string;
            /** Format: int32 */
            progress: number | string;
            state: string;
        };
        ExportDto: {
            /** Format: int64 */
            channelId: number | string;
            channelName: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: date-time */
            end: string;
            error: null | string;
            /** Format: date-time */
            expiresAt: null | string;
            fileName: null | string;
            /** Format: int64 */
            fileSize: number | string;
            /** Format: uuid */
            id: string;
            /** Format: int32 */
            progress: number | string;
            /** Format: date-time */
            start: string;
            state: string;
        };
        ExportQueuedResponse: {
            /** Format: uuid */
            id: string;
            state: string;
        };
        FavoritesRequest: {
            channelIds: (number | string)[];
        };
        HostInfoDto: {
            framework: string;
            machineName: string;
            osArchitecture: string;
            osDescription: string;
            /** Format: date-time */
            serverTime: string;
            /** Format: int64 */
            systemUptimeSeconds: number | string;
        };
        IpAddressStatusDto: {
            boundDevices: components["schemas"]["BoundDeviceInfoDto"][];
            ip: string;
            /** Format: date-time */
            lastProbedAt: null | string;
            /** Format: int32 */
            responseTimeMs: null | number | string;
            status: string;
            subnetSlice: null | string;
        };
        IpConflictCheckResultDto: {
            boundDetail: null | string;
            boundName: null | string;
            boundType: null | string;
            occupied: boolean;
        };
        IpPoolDetailDto: {
            addresses: components["schemas"]["IpAddressStatusDto"][];
            pool: components["schemas"]["IpPoolDto"];
            subnetSlices: string[];
        };
        IpPoolDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            autoScan: boolean;
            /** Format: int32 */
            availableCount: number | string;
            cidr: string;
            /** Format: int32 */
            conflictCount: number | string;
            /** Format: date-time */
            createdAt: string;
            description: null | string;
            endIp: string;
            gateway: null | string;
            /** Format: int64 */
            id: number | string;
            /** Format: date-time */
            lastScannedAt: null | string;
            name: string;
            netmask: string;
            /** Format: int32 */
            registeredCount: number | string;
            /** Format: int32 */
            reservedCount: number | string;
            startIp: string;
            /** Format: int32 */
            totalCapacity: number | string;
            /** Format: int32 */
            unregisteredActiveCount: number | string;
            /** Format: date-time */
            updatedAt: string;
            /** Format: int32 */
            vlanId: null | number | string;
            /** Format: int64 */
            workshopId: null | number | string;
            workshopName: null | string;
        };
        IpPoolRequest: {
            /** Format: int64 */
            areaId?: null | number | string;
            /** @default true */
            autoScan: boolean;
            cidr: string;
            description?: null | string;
            endIp?: null | string;
            gateway?: null | string;
            name: string;
            netmask?: null | string;
            startIp?: null | string;
            /** Format: int32 */
            vlanId?: null | number | string;
            /** Format: int64 */
            workshopId?: null | number | string;
        };
        IpPoolScanResultDto: {
            /** Format: int32 */
            activeCount: number | string;
            /** Format: int32 */
            inactiveCount: number | string;
            /** Format: int64 */
            poolId: number | string;
            /** Format: date-time */
            scannedAt: string;
            /** Format: int32 */
            scannedCount: number | string;
        };
        IpRecommendResultDto: {
            gateway: null | string;
            ip: string;
            netmask: string;
            /** Format: int64 */
            poolId: number | string;
            poolName: string;
        };
        JsonElement: unknown;
        LatestReleaseDto: {
            /** Format: date-time */
            createdAt: string;
            /** Format: int64 */
            downloadCount: number | string;
            downloadUrl: string;
            fileName: string;
            /** Format: int64 */
            fileSize: number | string;
            forceUpdate: boolean;
            /** Format: int64 */
            id: number | string;
            minimumVersion: null | string;
            packages: components["schemas"]["ReleaseDto"][];
            /** Format: date-time */
            publishedAt: null | string;
            releaseNotes: string;
            sha256: string;
            status: string;
            updateAvailable: boolean;
            version: string;
        };
        "LatestReleaseDto[]": components["schemas"]["LatestReleaseDto"][];
        LayoutRequest: {
            channelIds: (null | number | string)[];
            /** Format: int32 */
            intervalSeconds: number | string;
            kind: string;
            /** Format: int32 */
            layout: number | string;
            name: string;
            shared: boolean;
        };
        LiveRequest: {
            /** Format: int64 */
            channelId: number | string;
            /** Format: uuid */
            consumerId?: null | string;
            /** @default browser */
            profile: string;
            /**
             * Format: int32
             * @default 2
             */
            streamType: number | string;
        };
        LiveSessionDto: {
            /** Format: int32 */
            bitrateKbps?: null | number | string;
            /** Format: int64 */
            channelId: number | string;
            codec: string;
            /** Format: date-time */
            expiresAt: string;
            /** Format: int32 */
            height?: null | number | string;
            hlsUrl: string;
            httpFlvUrl: string;
            httpTsUrl?: null | string;
            /** Format: uuid */
            id: string;
            rtspUrl: string;
            state: string;
            /** Format: int32 */
            streamType: number | string;
            transcoded: boolean;
            /** Format: int32 */
            width?: null | number | string;
        };
        LoginRequest: {
            /** @default web */
            clientType: string;
            /** @default 2.0.0 */
            clientVersion: string;
            password: string;
            username: string;
        };
        MediaStatisticsDto: {
            /** Format: int64 */
            liveSessions: number | string;
            /** Format: int64 */
            playbackSessions: number | string;
            /** Format: int64 */
            transcodes: number | string;
        };
        NameCountDto: {
            /** Format: int64 */
            count: number | string;
            name: string;
        };
        NetworkDeviceDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            assignedIps: components["schemas"]["NetworkDeviceIpDto"][];
            /** Format: date-time */
            createdAt: string;
            deviceType: string;
            /** Format: int64 */
            id: number | string;
            /** Format: date-time */
            lastProbedAt: null | string;
            /**
             * Format: int32
             * @default 0
             */
            linkedPortCount: number | string;
            location: null | string;
            mac: null | string;
            managementIp: null | string;
            model: null | string;
            name: string;
            owner: null | string;
            /**
             * Format: int32
             * @default 0
             */
            portCount: number | string;
            ports?: null | components["schemas"]["DevicePortDto"][];
            remarks: null | string;
            /** Format: int32 */
            responseTimeMs: null | number | string;
            status: string;
            /** Format: date-time */
            updatedAt: string;
            vendor: null | string;
            virtualMachines?: null | components["schemas"]["VirtualMachineDto"][];
            /**
             * Format: int32
             * @default 0
             */
            vmCount: number | string;
            /** Format: int64 */
            workshopId: null | number | string;
            workshopName: null | string;
        };
        NetworkDeviceIpDto: {
            description: null | string;
            /** Format: int64 */
            deviceId: number | string;
            /** Format: int64 */
            id: number | string;
            interfaceName: string;
            ip: string;
            role: string;
            /** Format: int32 */
            vlanId: null | number | string;
        };
        NetworkDeviceIpRequest: {
            description?: null | string;
            /** @default Vlanif */
            interfaceName: string;
            ip: string;
            /** @default gateway */
            role: string;
            /** Format: int32 */
            vlanId?: null | number | string;
        };
        NetworkDeviceProbeResultDto: {
            /** Format: int64 */
            id: number | string;
            managementIp: null | string;
            name: string;
            /** Format: date-time */
            probedAt: string;
            /** Format: int32 */
            responseTimeMs: null | number | string;
            status: string;
        };
        NetworkDeviceRequest: {
            /** Format: int64 */
            areaId?: null | number | string;
            assignedIps?: null | components["schemas"]["NetworkDeviceIpRequest"][];
            deviceType: string;
            location?: null | string;
            mac?: null | string;
            managementIp?: null | string;
            model?: null | string;
            name: string;
            owner?: null | string;
            remarks?: null | string;
            /** @default online */
            status: string;
            vendor?: null | string;
            /** Format: int64 */
            workshopId?: null | number | string;
        };
        NetworkInterfaceDto: {
            /** Format: int64 */
            bytesReceived: number | string;
            /** Format: int64 */
            bytesSent: number | string;
            description: string;
            ipAddress: null | string;
            name: string;
            /** Format: int64 */
            speed: number | string;
            status: string;
            type: string;
        };
        NetworkMetricsDto: {
            interfaces: components["schemas"]["NetworkInterfaceDto"][];
            /** Format: double */
            rxBytesPerSecond: number | string;
            /** Format: int64 */
            totalBytesReceived: number | string;
            /** Format: int64 */
            totalBytesSent: number | string;
            /** Format: double */
            txBytesPerSecond: number | string;
        };
        NetworkTopologyOnuDto: {
            alias: null | string;
            hasSubSwitch: boolean;
            /** Format: int64 */
            id: number | string;
            onuName: string;
            ports: components["schemas"]["NetworkTopologyPortDto"][];
            /** Format: double */
            rxPower: null | number | string;
            sn: string;
            status: string;
            subSwitchModel: null | string;
            /** Format: int32 */
            subSwitchPorts: number | string;
            terminalType: string;
        };
        NetworkTopologyPortDto: {
            /** Format: int64 */
            boundChannelId: null | number | string;
            boundChannelIp: null | string;
            boundChannelName: null | string;
            boundChannelStatus: null | string;
            boundComputerDepartment?: null | string;
            /** Format: int64 */
            boundComputerId?: null | number | string;
            boundComputerIp?: null | string;
            boundComputerName?: null | string;
            boundComputerOwner?: null | string;
            boundComputerStatus?: null | string;
            isSwitchPort: boolean;
            portLabel: string;
            /** Format: int32 */
            portNumber: number | string;
        };
        NginxConfigDto: {
            configPath: string;
            content: string;
            reloadCommand: string;
        };
        NvrAvailableChannelDto: {
            availableChannelNumbers: (number | string)[];
            /** Format: int64 */
            deviceId: number | string;
            deviceName: string;
            /** Format: int32 */
            freeChannels: number | string;
            host: string;
            model?: null | string;
            /** @default hikvision */
            pluginId: null | string;
            /** Format: int32 */
            port: number | string;
            /** Format: int32 */
            totalChannels: number | string;
            /** Format: int32 */
            usedChannels: number | string;
        };
        OltDeviceDto: {
            /** Format: int64 */
            id: number | string;
            ip: string;
            model: string;
            name: string;
            neDn: string;
            osVersion: null | string;
            patchVersion: null | string;
            ports?: null | components["schemas"]["PonPortSummaryDto"][];
            status: string;
            /** Format: date-time */
            syncedAt: string;
            /**
             * Format: int32
             * @default 8
             */
            totalPorts: number | string;
            vendor: string;
        };
        OnuDeviceDto: {
            alias: null | string;
            /** Format: int64 */
            boundChannelCount: number | string;
            description?: null | string;
            groupName?: null | string;
            hasSubSwitch: boolean;
            /** @default healthy */
            healthStatus: string;
            /** Format: int64 */
            id: number | string;
            ifDesc?: null | string;
            ip?: null | string;
            lastDownCause: null | string;
            /** Format: date-time */
            lastDownTime: null | string;
            /** Format: date-time */
            lastDyingGaspTime?: null | string;
            /** Format: date-time */
            lastStateChangeTime?: null | string;
            /** Format: date-time */
            lastSyncedAt: null | string;
            /** Format: date-time */
            lastUpTime: null | string;
            mac: null | string;
            neDn: string;
            /** Format: int64 */
            oltId?: null | number | string;
            oltIp: string;
            oltName: string;
            onuId: string;
            onuName: string;
            /**
             * Format: int32
             * @default 0
             */
            portId: number | string;
            portsConfig: null | string;
            rootCause?: null | string;
            runStatus: null | string;
            /** Format: double */
            rxPower: null | number | string;
            sn: string;
            status: string;
            subSwitchModel: null | string;
            /** Format: int32 */
            subSwitchPorts: number | string;
            syncWarning?: null | string;
            terminalBoxCode: null | string;
            /** Format: int64 */
            terminalBoxId: null | number | string;
            terminalBoxName: null | string;
            terminalType: string;
            /** Format: double */
            txPower: null | number | string;
        };
        OnuDiagnosisDto: {
            fiberStatus?: null | string;
            items?: null | components["schemas"]["OnuDiagnosisItemDto"][];
            lastDownCause: null | string;
            lastDownTime: null | string;
            lastUpTime: null | string;
            neDn: string;
            onuId: string;
            opticalStatus?: null | string;
            ports: components["schemas"]["OnuPortStatusDto"][];
            /** Format: double */
            rxPower: null | number | string;
            status: string;
            /** Format: double */
            txPower: null | number | string;
        };
        OnuDiagnosisItemDto: {
            key: string;
            /** Format: int32 */
            status: number | string;
            suggestion?: null | string;
            value: null | string;
        };
        OnuPortStatusDto: {
            duplex?: null | string;
            name: string;
            /** Format: int32 */
            portId: number | string;
            speed?: null | string;
            status: string;
            terminalMac?: null | string;
        };
        OnuUpdateRequest: {
            alias?: null | string;
            hasSubSwitch?: null | boolean;
            portsConfig?: null | string;
            subSwitchModel?: null | string;
            /** Format: int32 */
            subSwitchPorts?: null | number | string;
            /** Format: int64 */
            terminalBoxId?: null | number | string;
        };
        OperationCountResponse: {
            /** Format: int32 */
            count: number | string;
        };
        OpticalHealthIndexDto: {
            /** Format: double */
            availabilityRate: number | string;
            /** Format: double */
            avgRxPower: null | number | string;
            /** Format: date-time */
            evaluatedAt: string;
            /** Format: int32 */
            fiberCutOnus: number | string;
            grade: string;
            /** Format: int32 */
            healthyOnus: number | string;
            /** Format: int32 */
            offlineOnus: number | string;
            /** Format: int32 */
            onlineOnus: number | string;
            /** Format: int32 */
            powerOffOnus: number | string;
            /** Format: int32 */
            score: number | string;
            /** Format: int32 */
            subHealthOnus: number | string;
            /** Format: int32 */
            totalOnus: number | string;
            trend?: null | components["schemas"]["OpticalHealthTrendItemDto"][];
            /** Format: int32 */
            trunkBrokenOnus: number | string;
        };
        OpticalHealthTrendItemDto: {
            /** Format: double */
            availabilityRate: number | string;
            /** Format: int32 */
            offlineCount: number | string;
            /** Format: int32 */
            score: number | string;
            /** Format: int32 */
            subHealthCount: number | string;
            /** Format: date-time */
            time: string;
        };
        OpticalLinkInfoDto: {
            boxCode: null | string;
            boxName: null | string;
            healthStatus: null | string;
            ifDesc: null | string;
            onuAlias: null | string;
            /** Format: int64 */
            onuId: null | number | string;
            onuName: null | string;
            portLabel: null | string;
            /** Format: int32 */
            portNumber: null | number | string;
            rootCause: null | string;
            /** Format: double */
            rxPower: null | number | string;
            /** Format: int64 */
            terminalBoxId: null | number | string;
        };
        OrganizationNodeDto: {
            code: string;
            /** Format: int64 */
            id: number | string;
            name: string;
            /** Format: int64 */
            parentId: null | number | string;
            status: string;
        };
        OrganizationRequest: {
            code: string;
            name: string;
            /** Format: int64 */
            parentId?: null | number | string;
            /** @default active */
            status: string;
        };
        OrganizationStatsDto: {
            /** Format: int32 */
            totalAreas: number | string;
            /** Format: int32 */
            totalChannels: number | string;
            /** Format: int32 */
            totalComputers: number | string;
            /** Format: int32 */
            totalTerminalBoxes: number | string;
            /** Format: int32 */
            totalUnits: number | string;
            /** Format: int32 */
            totalWorkshops: number | string;
        };
        OrganizationTreeDto: {
            areas: components["schemas"]["OrganizationNodeDto"][];
            units: components["schemas"]["OrganizationNodeDto"][];
            workshops: components["schemas"]["OrganizationNodeDto"][];
        };
        PagedAlarmResponse: {
            items: components["schemas"]["AlarmDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedAssetResponse: {
            items: components["schemas"]["AssetDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedChannelResponse: {
            items: components["schemas"]["ChannelDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedDeviceResponse: {
            items: components["schemas"]["DeviceDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedExportResponse: {
            items: components["schemas"]["ExportDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedReleaseResponse: {
            items: components["schemas"]["ReleaseDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfAdministrationAuditDto: {
            items: components["schemas"]["AdministrationAuditDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfAdministrationSessionDto: {
            items: components["schemas"]["AdministrationSessionDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfAssetDto: {
            items: components["schemas"]["AssetDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfComputerDto: {
            items: components["schemas"]["ComputerDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfDeviceOfflineHistoryDto: {
            items: components["schemas"]["DeviceOfflineHistoryDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfNetworkDeviceDto: {
            items: components["schemas"]["NetworkDeviceDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfOnuDeviceDto: {
            items: components["schemas"]["OnuDeviceDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PagedResponseOfUserDto: {
            items: components["schemas"]["UserDto"][];
            /** Format: int32 */
            page: number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int64 */
            total: number | string;
        };
        PasswordRequest: {
            currentPassword: string;
            newPassword: string;
        };
        PermissionDto: {
            code: string;
            name: string;
        };
        PermissionRequest: {
            codes: string[];
        };
        PlatformSettings: {
            /**
             * Format: int32
             * @default 180
             */
            alarmRetentionDays: number | string;
            /**
             * Format: int32
             * @default 180
             */
            auditRetentionDays: number | string;
            /**
             * Format: int32
             * @default 2
             */
            exportGlobal: number | string;
            /**
             * Format: int32
             * @default 1
             */
            exportPerDevice: number | string;
            /**
             * Format: int32
             * @default 100
             */
            exportQuotaGb: number | string;
            /**
             * Format: int32
             * @default 7
             */
            exportRetentionDays: number | string;
            /**
             * Format: int32
             * @default 25
             */
            livePerUser: number | string;
            /**
             * Format: int32
             * @default 32
             */
            playbackGlobal: number | string;
            /**
             * Format: int32
             * @default 20
             */
            playbackPerDevice: number | string;
            /**
             * Format: int32
             * @default 4
             */
            playbackPerUser: number | string;
            /**
             * Format: int32
             * @default 4
             */
            transcodeGlobal: number | string;
        };
        PlaybackControlRequest: {
            action: string;
            /** Format: date-time */
            position?: null | string;
            /** Format: double */
            speed?: null | number | string;
        };
        PlaybackRequest: {
            /** Format: int64 */
            channelId: number | string;
            /** Format: uuid */
            consumerId?: null | string;
            /** Format: date-time */
            end: string;
            /** @default browser */
            profile: string;
            /** Format: date-time */
            start: string;
            /**
             * Format: int32
             * @default 1
             */
            streamType: number | string;
        };
        PlaybackSessionDto: {
            /** Format: int32 */
            bitrateKbps?: null | number | string;
            /** Format: int64 */
            channelId: number | string;
            codec: string;
            /** Format: date-time */
            currentTime: string;
            /** Format: date-time */
            end: string;
            error?: null | string;
            /** Format: date-time */
            expiresAt: string;
            /** Format: int32 */
            height?: null | number | string;
            hlsUrl: string;
            httpFlvUrl: string;
            httpTsUrl?: null | string;
            /** Format: uuid */
            id: string;
            /** Format: int32 */
            progress: number | string;
            rtspUrl: string;
            segments: components["schemas"]["RecordingSegmentDto"][];
            /** Format: double */
            speed: number | string;
            /** Format: date-time */
            start: string;
            state: string;
            /** Format: int32 */
            streamType: number | string;
            transcoded: boolean;
            /** Format: int32 */
            width?: null | number | string;
        };
        PluginCreateRequest: {
            capabilities?: null | string[];
            configSchema?: null | string;
            description: null | string;
            endpointUrl: string;
            id: string;
            name: string;
            vendor: string;
            version: string;
        };
        PluginProbeRequest: {
            endpointUrl: string;
        };
        PluginStatusRequest: {
            status: string;
        };
        PonKanbanBoxDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            code: string;
            directCameras: components["schemas"]["PonKanbanCameraDto"][];
            directComputers?: null | components["schemas"]["PonKanbanComputerDto"][];
            healthState: string;
            /** Format: int64 */
            id: number | string;
            location: null | string;
            name: string;
            /** Format: int32 */
            offlineCameras: number | string;
            /**
             * Format: int32
             * @default 0
             */
            offlineComputers: number | string;
            /** Format: int32 */
            offlineOnus: number | string;
            /** Format: int32 */
            onlineCameras: number | string;
            /**
             * Format: int32
             * @default 0
             */
            onlineComputers: number | string;
            /** Format: int32 */
            onlineOnus: number | string;
            onus: components["schemas"]["PonKanbanOnuDto"][];
            /** Format: int32 */
            totalCameras: number | string;
            /**
             * Format: int32
             * @default 0
             */
            totalComputers: number | string;
            /** Format: int32 */
            totalOnus: number | string;
            /** Format: int64 */
            unitId?: null | number | string;
            unitName?: null | string;
        };
        PonKanbanCameraDto: {
            alias: null | string;
            /** Format: int64 */
            channelId: number | string;
            /** Format: int32 */
            deviceChannel: number | string;
            ip: null | string;
            isSwitchPort: boolean;
            model?: null | string;
            name: string;
            portLabel: string;
            /** Format: int32 */
            portNumber: number | string;
            status: string;
        };
        PonKanbanComputerDto: {
            department: null | string;
            /** Format: int64 */
            id: number | string;
            ip: string;
            isSwitchPort: boolean;
            mac: null | string;
            name: string;
            owner: null | string;
            portLabel: string;
            /** Format: int32 */
            portNumber: number | string;
            /** Format: int32 */
            responseTimeMs?: null | number | string;
            status: string;
        };
        PonKanbanOnuDto: {
            alias: null | string;
            cameras: components["schemas"]["PonKanbanCameraDto"][];
            computers?: null | components["schemas"]["PonKanbanComputerDto"][];
            hasSubSwitch: boolean;
            /** Format: int64 */
            id: number | string;
            lastDownCause: null | string;
            /** Format: date-time */
            lastDownTime: null | string;
            /** Format: date-time */
            lastUpTime: null | string;
            onuName: string;
            runStatus: null | string;
            /** Format: double */
            rxPower: null | number | string;
            sn: string;
            status: string;
            subSwitchModel: null | string;
            /** Format: int32 */
            subSwitchPorts: number | string;
            terminalType: string;
        };
        PonKanbanResponseDto: {
            boxes: components["schemas"]["PonKanbanBoxDto"][];
            summary: components["schemas"]["PonKanbanSummaryDto"];
            unassignedCameras: components["schemas"]["PonKanbanCameraDto"][];
            unassignedComputers?: null | components["schemas"]["PonKanbanComputerDto"][];
            unassignedOnus: components["schemas"]["PonKanbanOnuDto"][];
        };
        PonKanbanSummaryDto: {
            /** Format: int32 */
            dangerBoxes: number | string;
            /** Format: int32 */
            healthyBoxes: number | string;
            /** Format: int32 */
            offlineCameras: number | string;
            /**
             * Format: int32
             * @default 0
             */
            offlineComputers: number | string;
            /** Format: int32 */
            offlineOnus: number | string;
            /** Format: int32 */
            ongoingOutagesCount: number | string;
            /** Format: int32 */
            onlineCameras: number | string;
            /**
             * Format: int32
             * @default 0
             */
            onlineComputers: number | string;
            /** Format: int32 */
            onlineOnus: number | string;
            /** Format: int32 */
            todayOutagesCount: number | string;
            /** Format: int32 */
            totalBoxes: number | string;
            /** Format: int32 */
            totalCameras: number | string;
            /**
             * Format: int32
             * @default 0
             */
            totalComputers: number | string;
            /** Format: int32 */
            totalOnus: number | string;
            /** Format: int32 */
            warningBoxes: number | string;
        };
        PonPortSummaryDto: {
            brokenCauses?: null | string[];
            ifDesc: string;
            /** Format: int32 */
            offlineOnus: number | string;
            /** Format: int32 */
            onlineOnus: number | string;
            /** Format: int32 */
            portId: number | string;
            status: string;
            /** Format: int32 */
            subHealthOnus: number | string;
            /** Format: int32 */
            totalOnus: number | string;
        };
        PonRebootResponse: {
            message: string;
            success: boolean;
        };
        PonStatisticsDto: {
            models: components["schemas"]["NameCountDto"][];
            /** Format: int64 */
            offlineCount: number | string;
            /** Format: int64 */
            onlineCount: number | string;
            /** Format: int64 */
            totalCount: number | string;
        };
        PonSyncResponse: {
            /** Format: int32 */
            count: number | string;
            success: boolean;
        };
        PortCandidateDto: {
            /** Format: int64 */
            deviceId: number | string;
            deviceName: string;
            deviceType: string;
            isLinked: boolean;
            linkedPeerDesc: null | string;
            /** Format: int64 */
            portId: number | string;
            portName: string;
            portType: string;
            speed: string;
        };
        ProfileRequest: {
            displayName: null | string;
            phone: null | string;
        };
        PtzRequest: {
            command: string;
            /**
             * Format: int32
             * @default 4
             */
            speed: number | string;
        };
        PublishRequest: {
            /** @default false */
            forceUpdate: boolean;
            minimumVersion?: null | string;
        };
        RecordingDto: {
            /** Format: date-time */
            end: string;
            /** Format: uint32 */
            fileIndex: number | string;
            fileName: string;
            /** Format: int64 */
            fileSize: number | string;
            /** Format: int32 */
            fileType: number | string;
            /** Format: date-time */
            start: string;
            /** Format: int32 */
            streamType: number | string;
        };
        RecordingListResponse: components["schemas"]["RecordingDto"][];
        RecordingRequest: {
            /** Format: int64 */
            channelId: number | string;
            /** Format: date-time */
            end: string;
            /** Format: date-time */
            start: string;
        };
        RecordingSegmentDto: {
            /** Format: date-time */
            end: string;
            /** Format: uint32 */
            fileIndex?: null | number | string;
            fileName?: null | string;
            /** Format: int64 */
            fileSize?: null | number | string;
            /** Format: int32 */
            fileType?: null | number | string;
            /** Format: date-time */
            start: string;
            /** Format: int32 */
            streamType?: null | number | string;
        };
        ReleaseDto: {
            /** Format: date-time */
            createdAt: string;
            /** Format: int64 */
            downloadCount: number | string;
            downloadUrl: string;
            fileName: string;
            /** Format: int64 */
            fileSize: number | string;
            forceUpdate: boolean;
            /** Format: int64 */
            id: number | string;
            minimumVersion: null | string;
            /** Format: date-time */
            publishedAt: null | string;
            releaseNotes: string;
            sha256: string;
            status: string;
            version: string;
        };
        ReleaseUpdateRequest: {
            forceUpdate?: null | boolean;
            minimumVersion?: null | string;
            releaseNotes?: null | string;
            version?: null | string;
        };
        ResourceCreatedResponse: {
            /** Format: int64 */
            id: number | string;
        };
        RoleRequest: {
            code: string;
            name: string;
            permissionCodes?: null | string[];
            /** @default active */
            status: string;
        };
        SaveCablePathRequest: {
            /** @default fiber_core */
            cableType: string;
            /** @default #38bdf8 */
            color: string;
            /** Format: int64 */
            fromDeviceId?: null | number | string;
            fromDeviceType?: null | string;
            name: string;
            /** @default [] */
            pointsJson: string;
            remarks?: null | string;
            /** @default normal */
            status: string;
            /** Format: int64 */
            toDeviceId?: null | number | string;
            toDeviceType?: null | string;
        };
        SaveCoordinateRequest: {
            /**
             * Format: double
             * @default 1.5
             */
            altitudeM: null | number | string;
            /**
             * Format: double
             * @default 60
             */
            fovAngle: null | number | string;
            /**
             * Format: double
             * @default 25
             */
            fovRadiusM: null | number | string;
            /**
             * Format: double
             * @default 0
             */
            fovYaw: null | number | string;
            iconStyle?: null | string;
            remarks?: null | string;
            /** Format: int64 */
            targetId: number | string;
            targetType: string;
            /** Format: double */
            xPct: number | string;
            /** Format: double */
            yPct: number | string;
        };
        ScopeItem: {
            /** Format: int64 */
            id: number | string;
            type: string;
        };
        ScopeRequest: {
            allChannels: boolean;
            scopes: components["schemas"]["ScopeItem"][];
        };
        ServerPerformanceDto: {
            /** Format: int32 */
            cpuCores: number | string;
            /** Format: int64 */
            gcHeapBytes: null | number | string;
            /** Format: double */
            processCpuPercent: null | number | string;
            /** Format: int64 */
            processPrivateMemoryBytes: number | string;
            /** Format: int32 */
            processThreads: number | string;
            /** Format: int64 */
            processWorkingSetBytes: number | string;
            /** Format: int64 */
            swapTotalBytes: null | number | string;
            /** Format: int64 */
            swapUsedBytes: null | number | string;
        };
        ServiceHealthDto: {
            name: string;
            reason?: null | string;
            status: string;
        };
        SslCertificateDto: {
            /** Format: int64 */
            boundDomainCount: number | string;
            certPem?: null | string;
            commonName: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: int32 */
            daysRemaining: number | string;
            dnsNames: string[];
            /** Format: int64 */
            id: number | string;
            isActive: boolean;
            issuerDn: string;
            name: string;
            serialNumber: string;
            status: string;
            subjectDn: string;
            thumbprint: string;
            /** Format: date-time */
            validFrom: string;
            /** Format: date-time */
            validTo: string;
        };
        "SslCertificateDto[]": components["schemas"]["SslCertificateDto"][];
        SslDomainDto: {
            certificateCommonName: null | string;
            /** Format: int64 */
            certificateId: null | number | string;
            certificateName: null | string;
            /** Format: date-time */
            certificateValidTo: null | string;
            certMatchStatus: string;
            /** Format: date-time */
            createdAt: string;
            description: string;
            domain: string;
            enabled: boolean;
            forceHttps: boolean;
            hstsEnabled: boolean;
            /** Format: int64 */
            id: number | string;
            isPrimary: boolean;
            /** Format: int32 */
            port: number | string;
            protocol: string;
            /** Format: date-time */
            updatedAt: string;
        };
        "SslDomainDto[]": components["schemas"]["SslDomainDto"][];
        SslDomainRequest: {
            /** Format: int64 */
            certificateId?: null | number | string;
            /** @default  */
            description: string;
            domain: string;
            /** @default true */
            enabled: boolean;
            /** @default true */
            forceHttps: boolean;
            /** @default true */
            hstsEnabled: boolean;
            /** @default false */
            isPrimary: boolean;
            /**
             * Format: int32
             * @default 443
             */
            port: number | string;
            /** @default https */
            protocol: string;
        };
        SslOverviewDto: {
            activeCertificate: null | components["schemas"]["SslCertificateDto"];
            certFilePath: string;
            /** Format: int64 */
            expiringCertificates: number | string;
            fileSynced: boolean;
            httpsEnforced: boolean;
            keyFilePath: string;
            primaryDomain: null | components["schemas"]["SslDomainDto"];
            /** Format: int64 */
            totalCertificates: number | string;
            /** Format: int64 */
            totalDomains: number | string;
        };
        StreamCapabilityDto: {
            available: boolean;
            /** Format: int32 */
            bitrateKbps?: null | number | string;
            codec?: null | string;
            error?: null | string;
            /** Format: int32 */
            height?: null | number | string;
            /** Format: int32 */
            streamType: number | string;
            /** Format: int32 */
            width?: null | number | string;
        };
        StreamProbeRequest: {
            /** Format: int32 */
            streamType: number | string;
        };
        SystemStatisticsDto: {
            /** Format: double */
            cpuPercent: null | number | string;
            /** Format: int64 */
            diskFreeBytes: null | number | string;
            disks?: null | components["schemas"]["DiskInfoDto"][];
            /** Format: int64 */
            diskTotalBytes: null | number | string;
            host?: null | components["schemas"]["HostInfoDto"];
            media: components["schemas"]["MediaStatisticsDto"];
            /** Format: int64 */
            memoryTotalBytes: null | number | string;
            /** Format: int64 */
            memoryUsedBytes: null | number | string;
            network?: null | components["schemas"]["NetworkMetricsDto"];
            performance?: null | components["schemas"]["ServerPerformanceDto"];
            services: components["schemas"]["ServiceHealthDto"][];
            /** Format: int64 */
            uptimeSeconds: number | string;
            version: string;
        };
        TerminalBoxDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            /** Format: int64 */
            channelCount: number | string;
            code: string;
            /**
             * Format: int64
             * @default 0
             */
            computerCount: number | string;
            /** Format: date-time */
            createdAt: string;
            /** Format: int64 */
            id: number | string;
            location: null | string;
            name: string;
            /** Format: int64 */
            onuCount: number | string;
            remarks: null | string;
            /** Format: int64 */
            unitId?: null | number | string;
            unitName?: null | string;
            /** Format: date-time */
            updatedAt: string;
        };
        TerminalBoxRequest: {
            /** Format: int64 */
            areaId?: null | number | string;
            code: string;
            location?: null | string;
            name: string;
            remarks?: null | string;
            /** Format: int64 */
            unitId?: null | number | string;
        };
        TerminalBoxTopologyDto: {
            /** Format: int64 */
            areaId: null | number | string;
            areaName: null | string;
            code: string;
            /** Format: int64 */
            id: number | string;
            location: null | string;
            name: string;
            onus: components["schemas"]["NetworkTopologyOnuDto"][];
            /** Format: int64 */
            unitId?: null | number | string;
            unitName?: null | string;
        };
        TopologyCorrectionResponse: {
            /** Format: int32 */
            appliedCount: number | string;
            success: boolean;
        };
        UserDto: {
            displayName: null | string;
            /** Format: int64 */
            id: number | string;
            permissions: string[];
            phone: null | string;
            roleIds: (number | string)[];
            status: string;
            username: string;
        };
        UserPreferencesDto: {
            /** @default true */
            hardwareDecoding: boolean;
            /**
             * Format: int32
             * @default 800
             */
            networkCachingMs: number | string;
            /** @default true */
            preferSubStreamInGrid: boolean;
            /** @default false */
            showDiagnostics: boolean;
            /** @default light */
            theme: string;
        };
        UserRequest: {
            displayName: null | string;
            password: null | string;
            phone: null | string;
            roleIds: (number | string)[];
            status: string;
            username: string;
        };
        VirtualMachineDto: {
            businessDesc: null | string;
            /** Format: int32 */
            cpuCores: null | number | string;
            /** Format: date-time */
            createdAt: string;
            /** Format: int64 */
            hostDeviceId: number | string;
            hostDeviceName: string;
            /** Format: int64 */
            id: number | string;
            ip: string;
            /** Format: double */
            memoryGb: null | number | string;
            os: null | string;
            remarks: null | string;
            status: string;
            /** Format: date-time */
            updatedAt: string;
            /** Format: int32 */
            vlanId: null | number | string;
            vmName: string;
        };
        VirtualMachineRequest: {
            businessDesc?: null | string;
            /**
             * Format: int32
             * @default 4
             */
            cpuCores: null | number | string;
            ip: string;
            /**
             * Format: double
             * @default 8
             */
            memoryGb: null | number | string;
            os?: null | string;
            remarks?: null | string;
            /** @default running */
            status: string;
            /** Format: int32 */
            vlanId?: null | number | string;
            vmName: string;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
};
export type $defs = Record<string, never>;
export interface operations {
    ListAlarms: {
        parameters: {
            query?: {
                channelId?: number | string;
                deviceId?: number | string;
                eventType?: string;
                from?: string;
                page?: number;
                pageSize?: number;
                search?: string;
                state?: string;
                to?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedAlarmResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetAlarm: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AlarmDetailDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ActOnAlarm: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["AlarmActionRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AlarmActionResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetAlarmImage: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "image/jpeg": Blob;
                    "image/png": Blob;
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetAssetContext: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
                type: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AssetContextDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SearchAssets: {
        parameters: {
            query?: {
                areaId?: number | string;
                page?: number;
                pageSize?: number;
                search?: string;
                status?: string;
                type?: string;
                unitId?: number | string;
                workshopId?: number | string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedAssetResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetAssetSummary: {
        parameters: {
            query?: {
                areaId?: number | string;
                search?: string;
                status?: string;
                type?: string;
                unitId?: number | string;
                workshopId?: number | string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AssetSummaryDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetAudit: {
        parameters: {
            query?: {
                action?: string;
                from?: string;
                page?: number;
                pageSize?: number;
                search?: string;
                to?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfAdministrationAuditDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetCsrfToken: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CsrfTokenResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    Login: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["LoginRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AuthResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    Logout: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetCurrentUser: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["UserDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ChangePassword: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PasswordRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetPreferences: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["UserPreferencesDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdatePreferences: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UserPreferencesDto"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["UserPreferencesDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateProfile: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ProfileRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["UserDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RefreshSession: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AuthResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListChannels: {
        parameters: {
            query?: {
                areaId?: number | string;
                assigned?: boolean;
                deviceId?: number | string;
                online?: boolean;
                page?: number;
                pageSize?: number;
                search?: string;
                unitId?: number | string;
                workshopId?: number | string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedChannelResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetChannel: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ChannelDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateChannel: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ChannelUpdateRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ChannelDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StartPtz: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PtzRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CallPtzPreset: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
                preset: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StopPtz: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ProbeChannelStream: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["StreamProbeRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StreamCapabilityDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    AssignChannels: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["AssignmentRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    BatchClaimChannels: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["BatchClaimChannelsRequest"];
            };
        };
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    BatchSetNetworkBindings: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["BatchChannelNetworkBindingRequest"];
            };
        };
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SortChannels: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ChannelSortRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetUnassignedChannels: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ChannelDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListComputers: {
        parameters: {
            query?: {
                areaId?: number | string;
                status?: string;
                unitId?: number | string;
                workshopId?: number | string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfComputerDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateComputer: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ComputerRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ComputerDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetComputer: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ComputerDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateComputer: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ComputerRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ComputerDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteComputer: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ProbeComputer: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ComputerProbeResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    BatchUpdateComputers: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ComputerBatchUpdateRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AssetBatchResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ProbeAllComputers: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ComputerProbeResultDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetDashboard: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DashboardDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DisconnectDeviceLink: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ConnectDeviceLink: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ConnectLinkRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DeviceLinkDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateDevicePort: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["DevicePortRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePortDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteDevicePort: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetPortCandidates: {
        parameters: {
            query?: {
                deviceType?: string;
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PortCandidateDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListDevices: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedDeviceResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["DeviceRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ResourceCreatedResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListDevicePorts: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                deviceId: number;
                deviceType: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePortDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateDevicePort: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                deviceId: number;
                deviceType: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["DevicePortRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePortDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    BatchGenerateDevicePorts: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                deviceId: number;
                deviceType: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["BatchGeneratePortsRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePortDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetDevice: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["DeviceRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SyncDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DeviceSyncResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    TestDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DeviceSyncResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteDigitalTwinCable: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                cableId: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteDigitalTwinCoordinate: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListDigitalTwinScenes: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DigitalTwinSceneDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetDigitalTwinSceneDetail: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DigitalTwinSceneDetailDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    AutoAlignDigitalTwinCad: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OperationCountResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateDigitalTwinCable: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SaveCablePathRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DigitalTwinCableDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateDigitalTwinCable: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                cableId: number;
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SaveCablePathRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DigitalTwinCableDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetDigitalTwinCandidateNodes: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CandidateNodeDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SaveDigitalTwinCoordinate: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SaveCoordinateRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DigitalTwinNodeDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    BatchSaveDigitalTwinCoordinates: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["BatchSaveCoordinatesRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OperationCountResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListExports: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedExportResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateExport: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RecordingRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            202: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ExportCreatedResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CancelExport: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DownloadExport: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/zip": Blob;
                    "video/mp4": Blob;
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RetryExport: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            202: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ExportQueuedResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetFavorites: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ChannelDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateFavorites: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["FavoritesRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ChannelDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListIpPools: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpPoolDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateIpPool: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["IpPoolRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpPoolDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetIpPool: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpPoolDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateIpPool: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["IpPoolRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpPoolDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteIpPool: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetIpPoolDetail: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpPoolDetailDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ScanIpPool: {
        parameters: {
            query?: {
                subnetSlice?: string;
            };
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpPoolScanResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CheckIpConflict: {
        parameters: {
            query: {
                excludeId?: number | string;
                excludeType?: string;
                ip: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpConflictCheckResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RecommendFreeIp: {
        parameters: {
            query?: {
                poolId?: number | string;
                workshopId?: number | string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["IpRecommendResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetLayouts: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationLayoutDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateLayout: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["LayoutRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationLayoutDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateLayout: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["LayoutRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationLayoutDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteLayout: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StartLive: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["LiveRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LiveSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StopActiveliveSessions: {
        parameters: {
            query?: {
                all?: string;
            };
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetliveSession: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LiveSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StopliveSession: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RenewliveSession: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LiveSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListNetworkDevices: {
        parameters: {
            query?: {
                deviceType?: string;
                status?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfNetworkDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateNetworkDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["NetworkDeviceRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NetworkDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListHostVms: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                hostDeviceId: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["VirtualMachineDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateVirtualMachine: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                hostDeviceId: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["VirtualMachineRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["VirtualMachineDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetNetworkDevice: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NetworkDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateNetworkDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["NetworkDeviceRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NetworkDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteNetworkDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ProbeNetworkDevice: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NetworkDeviceProbeResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ProbeAllNetworkDevices: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NetworkDeviceProbeResultDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetNvrAvailableChannels: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NvrAvailableChannelDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetOrganization: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OrganizationTreeDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateOrganizationNode: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                kind: "workshops" | "areas" | "units";
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["OrganizationRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OrganizationNodeDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateOrganizationNode: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
                kind: "workshops" | "areas" | "units";
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["OrganizationRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OrganizationNodeDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteOrganizationNode: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
                kind: "workshops" | "areas" | "units";
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    AddCameraDirect: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["AddCameraRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ChannelDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetOrganizationStats: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OrganizationStatsDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetPermissions: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PermissionDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StartPlayback: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PlaybackRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PlaybackSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StopActiveplaybackSessions: {
        parameters: {
            query?: {
                all?: string;
            };
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetplaybackSession: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PlaybackSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    StopplaybackSession: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ControlPlayback: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PlaybackControlRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PlaybackSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RenewplaybackSession: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PlaybackSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListPlugins: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePluginDto[]"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RegisterPlugin: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PluginCreateRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePluginDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeletePlugin: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ExportPlugin: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/zip": Blob;
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetPluginHealth: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["JsonElement"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdatePluginStatus: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PluginStatusRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePluginDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    InstallPluginPackage: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DevicePluginDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ProbePlugin: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PluginProbeRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["JsonElement"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ApplyTopologyCorrections: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ApplyTopologyCorrectionRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TopologyCorrectionResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    AutoDiscoverTopologyBindings: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AutoTopologyDiscoverResultDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetDeviceOfflineHistory: {
        parameters: {
            query?: {
                channelId?: number | string;
                deviceType?: string;
                from?: string;
                onuDeviceId?: number | string;
                page?: number | string;
                pageSize?: number | string;
                search?: string;
                size?: number | string;
                status?: string;
                terminalBoxId?: number | string;
                to?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfDeviceOfflineHistoryDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetPonKanbanDashboard: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PonKanbanResponseDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetOpticalHealthIndex: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OpticalHealthIndexDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetOltsWithPorts: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OltDeviceDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListOnus: {
        parameters: {
            query?: {
                page?: number | string;
                pageSize?: number | string;
                search?: string;
                size?: number | string;
                status?: string;
                terminalBoxId?: number | string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfOnuDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetOnu: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OnuDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateOnu: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["OnuUpdateRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OnuDeviceDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DiagnoseOnu: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OnuDiagnosisDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RebootOnu: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PonRebootResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetPonStatistics: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PonStatisticsDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SyncPonFromEsight: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PonSyncResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListPublicReleases: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LatestReleaseDto[]"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetLatestRelease: {
        parameters: {
            query?: {
                currentVersion?: string;
                packageType?: "msi" | "zip";
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["LatestReleaseDto"];
                };
            };
            /** @description 尚无匹配安装包；默认优先返回 MSI，同版本包列于 packages。 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SearchRecordings: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RecordingRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RecordingListResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListReleases: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedReleaseResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UploadRelease: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "multipart/form-data": {
                    /** Format: binary */
                    file: Blob;
                    /** @default false */
                    forceUpdate?: boolean;
                    minimumVersion?: string;
                    releaseNotes?: string;
                    version: string;
                };
            };
        };
        responses: {
            /** @description 成功响应 */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ReleaseDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateRelease: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ReleaseUpdateRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ReleaseDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteRelease: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DownloadRelease: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/octet-stream": Blob;
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    PublishRelease: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PublishRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RevokeRelease: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteReleaseByVersion: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                version: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    PublishReleaseByVersion: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                version: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PublishRequest"];
            };
        };
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetRoles: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationRoleDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateRole: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RoleRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationRoleDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateRole: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RoleRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationRoleDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteRole: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SetRolePermissions: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PermissionRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdministrationRoleDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetScopes: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
                kind: "user" | "role";
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ScopeRequest"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateScopes: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
                kind: "user" | "role";
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ScopeRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ScopeRequest"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSessions: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfAdministrationSessionDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    RevokeSession: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSettings: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PlatformSettings"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateSettings: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PlatformSettings"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PlatformSettings"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSslCertificates: {
        parameters: {
            query?: {
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslCertificateDto[]"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UploadSslCertificate: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslCertificateDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSslCertificate: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslCertificateDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteSslCertificate: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SetActiveSslCertificate: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslCertificateDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DownloadSslCertificate: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/x-x509-ca-cert": Blob;
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSslDomains: {
        parameters: {
            query?: {
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslDomainDto[]"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateSslDomain: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SslDomainRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslDomainDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateSslDomain: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SslDomainRequest"];
            };
        };
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslDomainDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteSslDomain: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 操作完成，无响应正文 */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    SetPrimarySslDomain: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslDomainDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetNginxConfig: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NginxConfigDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSslOverview: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description 成功响应 */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SslOverviewDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetSystemStatistics: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SystemStatisticsDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    ListTerminalBoxes: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TerminalBoxDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateTerminalBox: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["TerminalBoxRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TerminalBoxDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetTerminalBox: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TerminalBoxDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateTerminalBox: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["TerminalBoxRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TerminalBoxDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteTerminalBox: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetTerminalBoxTopology: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TerminalBoxTopologyDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetTerminalBoxTopologyTree: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TerminalBoxTopologyDto"][];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    GetUsers: {
        parameters: {
            query?: {
                page?: number;
                pageSize?: number;
                search?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PagedResponseOfUserDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    CreateUser: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UserRequest"];
            };
        };
        responses: {
            /** @description Created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["UserDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateUser: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UserRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["UserDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    UpdateVirtualMachine: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["VirtualMachineRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["VirtualMachineDto"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
    DeleteVirtualMachine: {
        parameters: {
            query?: never;
            header?: {
                /** @description 使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。 */
                "X-CSRF-Token"?: string;
            };
            path: {
                id: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
            /** @description 请求失败；认证中间件也可能返回空正文。 */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ErrorResponse"];
                };
            };
        };
    };
}
