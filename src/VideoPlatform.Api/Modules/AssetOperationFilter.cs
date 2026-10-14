using VideoPlatform.Contracts;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

/// <summary>将现有台账路由接入统一资产应用边界。</summary>
public sealed class AssetOperationFilter(AssetOperations operations) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var name = http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName ?? "";
        var type = name switch
        {
            "GetNetworkDevice" or "CreateNetworkDevice" or "UpdateNetworkDevice" or "DeleteNetworkDevice" or "ProbeNetworkDevice" => "network-device",
            "GetIpPool" or "CreateIpPool" or "UpdateIpPool" or "DeleteIpPool" or "GetIpPoolDetail" or "ScanIpPool" => "ip-pool",
            "GetTerminalBox" or "CreateTerminalBox" or "UpdateTerminalBox" or "DeleteTerminalBox" or "GetTerminalBoxTopology" => "terminal-box",
            "GetOnu" or "UpdateOnu" or "DiagnoseOnu" or "RebootOnu" => "onu",
            "ListHostVms" or "CreateVirtualMachine" => "network-device",
            "UpdateVirtualMachine" or "DeleteVirtualMachine" => "virtual-machine",
            "UpdateDevicePort" or "DeleteDevicePort" => "port",
            "DisconnectDeviceLink" => "link",
            "DeleteDigitalTwinCoordinate" => "map-coordinate",
            "UpdateDigitalTwinCable" or "DeleteDigitalTwinCable" => "map-cable",
            "AutoAlignDigitalTwinCad" => "map-scene",
            _ => null
        };
        long? id = long.TryParse(http.Request.RouteValues.GetValueOrDefault("id")?.ToString(), out var parsed) ? parsed : null;
        if (long.TryParse(http.Request.RouteValues.GetValueOrDefault("hostDeviceId")?.ToString(), out parsed)) id = parsed;
        if (long.TryParse(http.Request.RouteValues.GetValueOrDefault("cableId")?.ToString(), out parsed)) id = parsed;
        if (name is "SaveDigitalTwinCoordinate" or "BatchSaveDigitalTwinCoordinates" or "CreateDigitalTwinCable") id = null;
        if (http.Request.RouteValues.GetValueOrDefault("deviceType") is { } deviceType)
        {
            type = AssetScope.PortOwnerType(deviceType.ToString()!);
            id = long.Parse(http.Request.RouteValues["deviceId"]!.ToString()!);
        }
        var actor = ApiSupport.Actor(http);
        var input = context.Arguments.FirstOrDefault(a => a is TerminalBoxRequest or OnuUpdateRequest or IpPoolRequest or NetworkDeviceRequest or VirtualMachineRequest or ConnectLinkRequest or ApplyTopologyCorrectionRequest or SaveCoordinateRequest or BatchSaveCoordinatesRequest or SaveCablePathRequest);
        var permission = name switch
        {
            "CreateNetworkDevice" or "UpdateNetworkDevice" or "DeleteNetworkDevice" => "network_device.manage",
            "CreateIpPool" or "UpdateIpPool" or "DeleteIpPool" => "ip.manage",
            "CreateTerminalBox" or "UpdateTerminalBox" or "DeleteTerminalBox" or "UpdateOnu" or "ApplyTopologyCorrections" => "pon.manage",
            "CreateVirtualMachine" or "UpdateVirtualMachine" or "DeleteVirtualMachine" => "virtual_machine.manage",
            "BatchGenerateDevicePorts" or "CreateDevicePort" or "UpdateDevicePort" or "DeleteDevicePort" or "ConnectDeviceLink" or "DisconnectDeviceLink" => "device_port.manage",
            "SaveDigitalTwinCoordinate" or "BatchSaveDigitalTwinCoordinates" or "DeleteDigitalTwinCoordinate" or "CreateDigitalTwinCable" or "UpdateDigitalTwinCable" or "DeleteDigitalTwinCable" or "AutoAlignDigitalTwinCad" => "map.manage",
            _ => null
        };
        if (permission is not null)
            return await operations.ExecuteAsync(actor, permission, type, id, input, () => next(context).AsTask(), http.RequestAborted);
        if (type is not null && id is not null) await operations.DemandAsync(actor, type, id.Value, http.RequestAborted);
        return await next(context);
    }
}
