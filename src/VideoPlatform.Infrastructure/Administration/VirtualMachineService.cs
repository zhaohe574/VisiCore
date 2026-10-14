using System.Text.Json.Nodes;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public sealed class VirtualMachineService(Database db, AuditStore audit)
{
    private const string BaseColumns = @"
        vm.id, vm.host_device_id, nd.name as host_device_name,
        vm.vm_name, vm.ip, vm.vlan_id, vm.os, vm.cpu_cores,
        vm.memory_gb, vm.status, vm.business_desc, vm.remarks,
        vm.created_at, vm.updated_at";

    private const string FromSql = @"
        virtual_machines vm
        join network_devices nd on vm.host_device_id = nd.id";

    public async Task<IReadOnlyList<VirtualMachineDto>> ListAsync(long hostDeviceId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            $"select {BaseColumns} from {FromSql} where vm.host_device_id = @hostDeviceId order by vm.id asc",
            new { hostDeviceId }, ct);
        return rows.Select(ToDto).ToArray();
    }

    public async Task<VirtualMachineDto> GetAsync(long id, CancellationToken ct = default)
    {
        var row = await db.OneAsync(
            $"select {BaseColumns} from {FromSql} where vm.id = @id",
            new { id }, ct) ?? throw new PlatformException(404, "vm.not_found", "虚拟机不存在");
        return ToDto(row);
    }

    public async Task<VirtualMachineDto> CreateAsync(long hostDeviceId, VirtualMachineRequest req, string actor, string? ip, CancellationToken ct = default)
    {
        var row = await db.OneAsync(@"
            insert into virtual_machines(host_device_id, vm_name, ip, vlan_id, os, cpu_cores, memory_gb, status, business_desc, remarks)
            values(@hostDeviceId, @vmName, @ipAddress, @vlanId, @os, @cpuCores, @memoryGb, @status, @businessDesc, @remarks)
            returning id",
            new
            {
                hostDeviceId,
                vmName = req.VmName.Trim(),
                ipAddress = req.Ip.Trim(),
                vlanId = req.VlanId,
                os = req.Os?.Trim(),
                cpuCores = req.CpuCores ?? 4,
                memoryGb = req.MemoryGb ?? 8.0m,
                status = req.Status.Trim(),
                businessDesc = req.BusinessDesc?.Trim(),
                remarks = req.Remarks?.Trim()
            }, ct) ?? throw new PlatformException(500, "db.error", "创建虚拟机失败");

        var id = row.Id("id");
        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "virtual_machine.create", id.ToString(),
            $"新增虚拟机：{req.VmName} ({req.Ip})", ip, ct);

        return await GetAsync(id, ct);
    }

    public async Task<VirtualMachineDto> UpdateAsync(long id, VirtualMachineRequest req, string actor, string? ip, CancellationToken ct = default)
    {
        await db.ExecuteAsync(@"
            update virtual_machines
            set vm_name = @vmName, ip = @ipAddress, vlan_id = @vlanId, os = @os,
                cpu_cores = @cpuCores, memory_gb = @memoryGb, status = @status,
                business_desc = @businessDesc, remarks = @remarks, updated_at = now()
            where id = @id",
            new
            {
                id,
                vmName = req.VmName.Trim(),
                ipAddress = req.Ip.Trim(),
                vlanId = req.VlanId,
                os = req.Os?.Trim(),
                cpuCores = req.CpuCores ?? 4,
                memoryGb = req.MemoryGb ?? 8.0m,
                status = req.Status.Trim(),
                businessDesc = req.BusinessDesc?.Trim(),
                remarks = req.Remarks?.Trim()
            }, ct);

        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "virtual_machine.update", id.ToString(),
            $"更新虚拟机：{req.VmName} ({req.Ip})", ip, ct);

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, string actor, string? ip, CancellationToken ct = default)
    {
        await db.ExecuteAsync("delete from virtual_machines where id = @id", new { id }, ct);
        await audit.WriteAsync(long.TryParse(actor, out var uid) ? uid : null, "virtual_machine.delete", id.ToString(),
            $"删除虚拟机 ID: {id}", ip, ct);
    }

    private static VirtualMachineDto ToDto(JsonObject r)
    {
        return new VirtualMachineDto(
            r.Id("id"),
            r.Id("hostDeviceId"),
            r.Text("hostDeviceName"),
            r.Text("vmName"),
            r.Text("ip"),
            r.TryGetPropertyValue("vlanId", out var v) && v != null ? (int?)r.Id("vlanId") : null,
            r.Text("os"),
            r.TryGetPropertyValue("cpuCores", out var c) && c != null ? (int?)r.Id("cpuCores") : null,
            r.TryGetPropertyValue("memoryGb", out var m) && m != null && decimal.TryParse(m.ToString(), out var mem) ? mem : null,
            r.Text("status"),
            r.Text("businessDesc"),
            r.Text("remarks"),
            DateTimeOffset.Parse(r.Text("createdAt")),
            DateTimeOffset.Parse(r.Text("updatedAt"))
        );
    }
}
