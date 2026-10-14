using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.Api.Modules;

public static class VirtualMachineEndpoints
{
    public static void MapVirtualMachineEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2").RequireAuthorization().WithTags("虚拟机台账").AddEndpointFilter<AssetOperationFilter>();

        group.MapGet("/network-devices/{hostDeviceId:long}/vms", async (long hostDeviceId, HttpContext context, VirtualMachineService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "network_device.read", context.RequestAborted);
            var list = await service.ListAsync(hostDeviceId, context.RequestAborted);
            return Results.Ok(list);
        }).Produces<IReadOnlyList<VirtualMachineDto>>().WithName("ListHostVms");

        group.MapPost("/network-devices/{hostDeviceId:long}/vms", async (long hostDeviceId, VirtualMachineRequest req, HttpContext context, VirtualMachineService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "virtual_machine.manage", context.RequestAborted);
            var created = await service.CreateAsync(hostDeviceId, req, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Created($"/api/v2/virtual-machines/{created.Id}", created);
        }).Produces<VirtualMachineDto>(201).WithName("CreateVirtualMachine");

        group.MapPut("/virtual-machines/{id:long}", async (long id, VirtualMachineRequest req, HttpContext context, VirtualMachineService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "virtual_machine.manage", context.RequestAborted);
            var updated = await service.UpdateAsync(id, req, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.Ok(updated);
        }).Produces<VirtualMachineDto>().WithName("UpdateVirtualMachine");

        group.MapDelete("/virtual-machines/{id:long}", async (long id, HttpContext context, VirtualMachineService service, AccessService access) =>
        {
            var actor = ApiSupport.Actor(context);
            await access.DemandAsync(actor, "virtual_machine.manage", context.RequestAborted);
            await service.DeleteAsync(id, actor.UserId.ToString(), ApiSupport.Ip(context), context.RequestAborted);
            return Results.NoContent();
        }).Produces(204).WithName("DeleteVirtualMachine");
    }
}
