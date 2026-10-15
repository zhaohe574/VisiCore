using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Nvr;

public sealed class NvrChannelManager(Database db, SecretStore secrets, IDeviceAdapter adapter, ILogger<NvrChannelManager>? logger = null) : INvrChannelManager
{
    // 通道下发策略（P1 阶段 2 第 5 步）：按插件声明的具名策略派发，消除厂商字符串分支。
    // 取第一个 AppliesTo 命中的实现；都不命中（新厂商）时回退到适配器转发，并保持服务可用。
    private readonly IReadOnlyList<IChannelProvisioner> _provisioners =
    [
        new HikvisionIsapiChannelProvisioner(logger),
        new JovisionUnsupportedChannelProvisioner()
    ];
    private readonly AdapterChannelProvisioner _fallbackProvisioner = new(adapter, logger);

    private IChannelProvisioner ProvisionerFor(string pluginId)
        => _provisioners.FirstOrDefault(p => p.AppliesTo(pluginId)) ?? _fallbackProvisioner;

    private static ChannelProvisionContext BuildContext(
        JsonObject dev, string nvrPwd, int channelNumber,
        string? ipcHost, int? ipcPort, string? ipcUser, string? ipcPwd, string? protocolType, string? channelName)
        => new(
            dev.Id(), dev.Text("name"), dev.Text("host"), (int)dev.Id("port"),
            dev.Text("username"), nvrPwd, channelNumber,
            ipcHost, ipcPort, ipcUser, ipcPwd, protocolType, channelName);

    public async Task<IReadOnlyList<NvrAvailableChannelDto>> GetAvailableChannelsAsync(CancellationToken ct = default)
    {
        var devices = await db.QueryAsync($@"
            select 
                d.id, d.name, d.model, d.host, d.port, coalesce(d.plugin_id, 'hikvision') as plugin_id,
                coalesce((d.extra_config->>'max_channels')::int, 0) as configured_channels,
                (select count(*) from channels where device_id = d.id and status <> 'disabled') as used_channels
            from devices d
            where d.enabled and d.status <> 'disabled' and d.plugin_id <> '{PluginIds.EsightPon}'
            order by d.name", ct: ct);

        var result = new List<NvrAvailableChannelDto>();

        foreach (var dev in devices)
        {
            var deviceId = dev.Id();
            var model = dev.Text("model", "");
            var configuredTotal = (int)dev.Id("configuredChannels");
            var used = (int)dev.Id("usedChannels");

            // 查询已被占用的通道号
            var occupiedRows = await db.QueryAsync("select device_channel from channels where device_id = @deviceId and status <> 'disabled'", new { deviceId }, ct);
            var occupiedSet = occupiedRows.Select(r => (int)r.Id("deviceChannel")).ToHashSet();
            var maxExistingChannel = occupiedSet.Count > 0 ? occupiedSet.Max() : 0;

            int total;
            if (configuredTotal > 0)
            {
                total = configuredTotal;
            }
            else
            {
                total = ResolveDeviceCapacity(model, maxExistingChannel);
            }

            var availableNumbers = new List<int>();
            for (var i = 1; i <= total; i++)
            {
                if (!occupiedSet.Contains(i))
                    availableNumbers.Add(i);
            }

            result.Add(new NvrAvailableChannelDto(
                deviceId,
                dev.Text("name"),
                dev.Text("host"),
                (int)dev.Id("port"),
                total,
                used,
                Math.Max(0, total - used),
                availableNumbers,
                dev.Text("pluginId", "hikvision"),
                model
            ));
        }

        return result;
    }

    public static int ResolveDeviceCapacity(string model, int maxExistingChannel)
    {
        var upper = (model ?? "").ToUpperInvariant();
        // 1. 海康 CVR / 云存储 / 大容量阵列机系列（如 DS-A72124R, DS-A71..., DS-A8... 等，规格为 1024 路）
        if (upper.Contains("A72") || upper.Contains("A71") || upper.Contains("A8") || upper.Contains("CVR") || upper.Contains("1024"))
        {
            return 1024;
        }

        // 2. 根据型号末尾/中间的通道数字推算（如 DS-8632 -> 32, DS-7616 -> 16, DS-9664 -> 64, DS-96128 -> 128, DS-96256 -> 256）
        var match = System.Text.RegularExpressions.Regex.Match(upper, @"(?:DS-[A-Z0-9]*?)(\d{2,4})(?:[A-Z]|$)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
        {
            if (num is 4 or 8 or 16 or 32 or 64 or 128 or 256 or 512 or 1024)
            {
                return Math.Max(num, maxExistingChannel);
            }
        }

        // 3. 兜底动态匹配阶梯规格（32 / 64 / 128 / 256 / 512 / 1024）
        var cap = 32;
        while (cap < maxExistingChannel + 8 && cap < 1024)
        {
            cap = cap <= 32 ? 64 : (cap <= 64 ? 128 : (cap <= 128 ? 256 : (cap <= 256 ? 512 : 1024)));
        }
        return Math.Max(cap, maxExistingChannel);
    }

    public async Task<int> AddIpcChannelAsync(
        long nvrDeviceId,
        int? channelNumber,
        string ipcHost,
        int ipcPort,
        string ipcUsername,
        string ipcPassword,
        string protocolType,
        string channelName,
        CancellationToken ct = default)
    {
        var dev = await db.OneAsync(@"
            select id, name, host, port, username, password_cipher, coalesce(plugin_id, 'hikvision') as plugin_id
            from devices where id = @nvrDeviceId and enabled", new { nvrDeviceId }, ct)
            ?? throw new PlatformException(404, "nvr.not_found", "目标录像机不存在或已停用");

        // 仅保留日志与策略所需的值；host/port/username 由 BuildContext 直接从 dev 读取。
        var nvrHost = dev.Text("host");
        var nvrPwd = secrets.Unprotect(dev.Text("passwordCipher"));
        var pluginId = dev.Text("pluginId");

        // 确定分配的通道号
        var occupiedRows = await db.QueryAsync("select device_channel from channels where device_id = @nvrDeviceId and status <> 'disabled'", new { nvrDeviceId }, ct);
        var occupiedSet = occupiedRows.Select(r => (int)r.Id("deviceChannel")).ToHashSet();

        int targetChan;
        if (channelNumber.HasValue && channelNumber.Value > 0)
        {
            targetChan = channelNumber.Value;
            Rules.Require(!occupiedSet.Contains(targetChan), $"录像机通道号 {targetChan} 已被占用，请选择其他通道号");
        }
        else
        {
            targetChan = 1;
            while (occupiedSet.Contains(targetChan)) targetChan++;
        }

        logger?.LogInformation("准备将 IPC [{IpcHost}:{IpcPort}] 写入录像机 [{NvrHost}] 通道 {Channel}", ipcHost, ipcPort, nvrHost, targetChan);

        // 厂商差异由策略承载：派发点不再出现 pluginId 分支。
        await ProvisionerFor(pluginId).AddAsync(
            BuildContext(dev, nvrPwd, targetChan, ipcHost, ipcPort, ipcUsername, ipcPassword, protocolType, channelName), ct);

        return targetChan;
    }

    public async Task UpdateIpcChannelAsync(
        long nvrDeviceId,
        int channelNumber,
        string? ipcHost,
        int? ipcPort,
        string? ipcUsername,
        string? ipcPassword,
        string? protocolType,
        string? channelName,
        CancellationToken ct = default)
    {
        var dev = await db.OneAsync(@"
            select id, name, host, port, username, password_cipher, coalesce(plugin_id, 'hikvision') as plugin_id, enabled
            from devices where id = @nvrDeviceId", new { nvrDeviceId }, ct)
            ?? throw new PlatformException(404, "nvr.not_found", "目标录像机不存在");

        if (!dev.Flag("enabled"))
        {
            throw new PlatformException(400, "nvr.disabled", $"目标录像机【{dev.Text("name")}】已停用，无法向其下发通道变更");
        }

        // 仅保留日志与策略所需的值；host/port/username 由 BuildContext 直接从 dev 读取。
        var nvrHost = dev.Text("host");
        var nvrPwd = secrets.Unprotect(dev.Text("passwordCipher"));
        var pluginId = dev.Text("pluginId");

        logger?.LogInformation("准备向录像机 [{NvrHost}] 通道 {Channel} 下发更新: IP={IpcHost}, Name={ChannelName}", nvrHost, channelNumber, ipcHost, channelName);

        // 厂商差异由策略承载：派发点不再出现 pluginId 分支。
        await ProvisionerFor(pluginId).UpdateAsync(
            BuildContext(dev, nvrPwd, channelNumber, ipcHost, ipcPort, ipcUsername, ipcPassword, protocolType, channelName), ct);
    }
}
