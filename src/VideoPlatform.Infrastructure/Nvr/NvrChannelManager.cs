using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Nvr;

public sealed class NvrChannelManager(Database db, SecretStore secrets, IDeviceAdapter adapter, ILogger<NvrChannelManager>? logger = null) : INvrChannelManager
{
    public async Task<IReadOnlyList<NvrAvailableChannelDto>> GetAvailableChannelsAsync(CancellationToken ct = default)
    {
        var devices = await db.QueryAsync(@"
            select 
                d.id, d.name, d.model, d.host, d.port, coalesce(d.plugin_id, 'hikvision') as plugin_id,
                coalesce((d.extra_config->>'max_channels')::int, 0) as configured_channels,
                (select count(*) from channels where device_id = d.id and status <> 'disabled') as used_channels
            from devices d
            where d.enabled and d.status <> 'disabled' and d.plugin_id <> 'esight_pon'
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

        var nvrHost = dev.Text("host");
        var nvrPort = (int)dev.Id("port");
        var nvrUser = dev.Text("username");
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

        if (pluginId == "hikvision")
        {
            // 通过海康 ISAPI 协议写入录像机数字通道
            await SetHikvisionIsapiChannelAsync(nvrHost, nvrPort, nvrUser, nvrPwd, targetChan, ipcHost, ipcPort, ipcUsername, ipcPassword, protocolType, channelName, ct);
        }
        else if (pluginId == "jovision")
        {
            throw new PlatformException(400, "nvr.unsupported",
                $"中维录像机【{dev.Text("name")}】不支持由平台远程下发添加通道。请先在中维录像机本地界面或客户端完成摄像头【{ipcHost}】的搜索绑定，完成后在系统【设备管理】点击该录像机的【同步】，视枢将自动探测识别并挂载上线。");
        }
        else
        {
            // 其他品牌录像机（大华/宇视等）：通过设备适配器插件转发通道新增指令
            try
            {
                await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{nvrDeviceId}/channels", new
                {
                    channel = targetChan,
                    ip = ipcHost,
                    port = ipcPort,
                    username = ipcUsername,
                    password = ipcPassword,
                    protocol = protocolType,
                    name = channelName
                }, ct);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "调用设备适配器添加通道失败");
                throw new PlatformException(502, "nvr.channel_add_failed", $"向录像机下发添加通道失败：{ex.Message}");
            }
        }

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

        var nvrHost = dev.Text("host");
        var nvrPort = (int)dev.Id("port");
        var nvrUser = dev.Text("username");
        var nvrPwd = secrets.Unprotect(dev.Text("passwordCipher"));
        var pluginId = dev.Text("pluginId");

        logger?.LogInformation("准备向录像机 [{NvrHost}] 通道 {Channel} 下发更新: IP={IpcHost}, Name={ChannelName}", nvrHost, channelNumber, ipcHost, channelName);

        if (pluginId == "hikvision")
        {
            await UpdateHikvisionIsapiChannelAsync(nvrHost, nvrPort, nvrUser, nvrPwd, channelNumber, ipcHost, ipcPort, ipcUsername, ipcPassword, protocolType, channelName, ct);
        }
        else if (pluginId == "jovision")
        {
            throw new PlatformException(400, "nvr.unsupported",
                $"中维录像机【{dev.Text("name")}】不支持由平台远程下发修改通道配置。请先在中维录像机本地界面完成修改，完成后在系统【设备管理】点击该录像机的【同步】。");
        }
        else
        {
            try
            {
                await adapter.SendAsync(HttpMethod.Put, $"/internal/devices/{nvrDeviceId}/channels/{channelNumber}", new
                {
                    channel = channelNumber,
                    ip = ipcHost,
                    port = ipcPort,
                    username = ipcUsername,
                    password = ipcPassword,
                    protocol = protocolType,
                    name = channelName
                }, ct);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "调用设备适配器更新通道失败");
                throw new PlatformException(502, "nvr.channel_update_failed", $"向录像机下发更新通道失败：{ex.Message}");
            }
        }
    }

    private async Task UpdateHikvisionIsapiChannelAsync(
        string nvrHost, int nvrPort, string nvrUser, string nvrPwd,
        int channelId, string? ipcHost, int? ipcPort, string? ipcUser, string? ipcPwd,
        string? protocolType, string? channelName, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(nvrUser, nvrPwd),
            PreAuthenticate = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var putUrl = $"http://{nvrHost}/ISAPI/ContentMgmt/InputProxy/channels/{channelId}";

        XDocument? doc = null;
        try
        {
            var getRes = await client.GetAsync(putUrl, ct);
            if (getRes.IsSuccessStatusCode)
            {
                var getBody = await getRes.Content.ReadAsStringAsync(ct);
                doc = XDocument.Parse(getBody);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning("尝试 GET 海康录像机 [{Host}] 通道 {Channel} 原始配置异常：{Error}", nvrHost, channelId, ex.Message);
        }

        string xml;
        if (doc is not null && doc.Root is not null)
        {
            var root = doc.Root;
            XNamespace ns = root.Name.Namespace;
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                var nameEl = root.Element(ns + "name") ?? root.Element("name");
                if (nameEl != null) nameEl.Value = channelName;
                else root.Add(new XElement(ns + "name", channelName));
            }
            var sourceEl = root.Element(ns + "sourceInputPortDescriptor") ?? root.Element("sourceInputPortDescriptor");
            if (sourceEl is not null)
            {
                XNamespace srcNs = sourceEl.Name.Namespace;
                if (!string.IsNullOrWhiteSpace(ipcHost))
                {
                    var ipEl = sourceEl.Element(srcNs + "ipAddress") ?? sourceEl.Element("ipAddress");
                    if (ipEl != null) ipEl.Value = ipcHost;
                    else sourceEl.Add(new XElement(srcNs + "ipAddress", ipcHost));
                }
                if (ipcPort.HasValue && ipcPort.Value > 0)
                {
                    var portEl = sourceEl.Element(srcNs + "managePortNo") ?? sourceEl.Element("managePortNo");
                    if (portEl != null) portEl.Value = ipcPort.Value.ToString();
                    else sourceEl.Add(new XElement(srcNs + "managePortNo", ipcPort.Value));
                }
                if (!string.IsNullOrWhiteSpace(ipcUser))
                {
                    var userEl = sourceEl.Element(srcNs + "userName") ?? sourceEl.Element("userName");
                    if (userEl != null) userEl.Value = ipcUser;
                    else sourceEl.Add(new XElement(srcNs + "userName", ipcUser));
                }
                if (!string.IsNullOrWhiteSpace(ipcPwd))
                {
                    var pwdEl = sourceEl.Element(srcNs + "password") ?? sourceEl.Element("password");
                    if (pwdEl != null) pwdEl.Value = ipcPwd;
                    else sourceEl.Add(new XElement(srcNs + "password", ipcPwd));
                }
                if (!string.IsNullOrWhiteSpace(protocolType))
                {
                    var protoVal = protocolType.Equals("ONVIF", StringComparison.OrdinalIgnoreCase) ? "ONVIF" : "HIKVISION";
                    var protoEl = sourceEl.Element(srcNs + "proxyProtocol") ?? sourceEl.Element("proxyProtocol");
                    if (protoEl != null) protoEl.Value = protoVal;
                    else sourceEl.Add(new XElement(srcNs + "proxyProtocol", protoVal));
                }
            }
            xml = doc.ToString();
        }
        else
        {
            var proto = protocolType?.Equals("ONVIF", StringComparison.OrdinalIgnoreCase) == true ? "ONVIF" : "HIKVISION";
            var port = ipcPort ?? 8000;
            xml = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <InputProxyChannel version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
                    <id>{channelId}</id>
                    <name>{System.Security.SecurityElement.Escape(channelName ?? $"通道 {channelId}")}</name>
                    <sourceInputPortDescriptor>
                        <proxyProtocol>{proto}</proxyProtocol>
                        <addressingFormatType>ipaddress</addressingFormatType>
                        <ipAddress>{ipcHost ?? ""}</ipAddress>
                        <managePortNo>{port}</managePortNo>
                        <srcInputPort>1</srcInputPort>
                        <userName>{System.Security.SecurityElement.Escape(ipcUser ?? "admin")}</userName>
                        <password>{System.Security.SecurityElement.Escape(ipcPwd ?? "")}</password>
                    </sourceInputPortDescriptor>
                </InputProxyChannel>
                """;
        }

        try
        {
            using var putContent = new StringContent(xml, Encoding.UTF8, "application/xml");
            var putRes = await client.PutAsync(putUrl, putContent, ct);
            var putBody = await putRes.Content.ReadAsStringAsync(ct);

            if (putRes.IsSuccessStatusCode)
            {
                logger?.LogInformation("成功向海康录像机 [{Host}] PUT 更新通道 {Channel}", nvrHost, channelId);
                return;
            }

            logger?.LogWarning("向海康录像机 [{Host}] PUT 更新通道返回 {Code}：{Body}", nvrHost, putRes.StatusCode, putBody);
            var lastError = ExtractHikvisionError(putBody, $"HTTP {putRes.StatusCode}");
            throw new PlatformException(502, "nvr.channel_update_failed", $"录像机 [{nvrHost}] 更新通道 {channelId} 失败：{lastError}");
        }
        catch (PlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "与海康录像机 [{Host}] 通信下发通道更新异常", nvrHost);
            throw new PlatformException(502, "nvr.channel_update_failed", $"与录像机 [{nvrHost}] 通信失败：{ex.Message}");
        }
    }

    private async Task SetHikvisionIsapiChannelAsync(
        string nvrHost, int nvrPort, string nvrUser, string nvrPwd,
        int channelId, string ipcHost, int ipcPort, string ipcUser, string ipcPwd,
        string protocolType, string channelName, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(nvrUser, nvrPwd),
            PreAuthenticate = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        var proto = protocolType.Equals("ONVIF", StringComparison.OrdinalIgnoreCase) ? "ONVIF" : "HIKVISION";
        // 海康 ISAPI 标准命名空间为 http://www.isapi.org/ver20/XMLSchema
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <InputProxyChannel version="2.0" xmlns="http://www.isapi.org/ver20/XMLSchema">
                <id>{channelId}</id>
                <name>{System.Security.SecurityElement.Escape(channelName)}</name>
                <sourceInputPortDescriptor>
                    <proxyProtocol>{proto}</proxyProtocol>
                    <addressingFormatType>ipaddress</addressingFormatType>
                    <ipAddress>{ipcHost}</ipAddress>
                    <managePortNo>{ipcPort}</managePortNo>
                    <srcInputPort>1</srcInputPort>
                    <userName>{System.Security.SecurityElement.Escape(ipcUser)}</userName>
                    <password>{System.Security.SecurityElement.Escape(ipcPwd)}</password>
                </sourceInputPortDescriptor>
            </InputProxyChannel>
            """;

        // 海康 ISAPI 规范：
        // 1. 新增通道：向集合端点 POST /ISAPI/ContentMgmt/InputProxy/channels
        // 2. 更新通道：向具体通道 PUT /ISAPI/ContentMgmt/InputProxy/channels/{channelId}
        var postUrl = $"http://{nvrHost}/ISAPI/ContentMgmt/InputProxy/channels";
        var putUrl = $"http://{nvrHost}/ISAPI/ContentMgmt/InputProxy/channels/{channelId}";

        bool success = false;
        string? lastError = null;

        try
        {
            using var postContent = new StringContent(xml, Encoding.UTF8, "application/xml");
            var postRes = await client.PostAsync(postUrl, postContent, ct);
            var postBody = await postRes.Content.ReadAsStringAsync(ct);

            if (postRes.IsSuccessStatusCode)
            {
                logger?.LogInformation("成功向海康录像机 [{Host}] POST 创建通道 {Channel}", nvrHost, channelId);
                success = true;
                // 成功创建后，尝试通过 PUT 同步一次以确保别名、名称及属性同步
                try
                {
                    using var putContent = new StringContent(xml, Encoding.UTF8, "application/xml");
                    await client.PutAsync(putUrl, putContent, ct);
                }
                catch { /* 忽略补充 PUT 的非关键告警 */ }
            }
            else
            {
                logger?.LogWarning("向海康录像机 [{Host}] POST 创建通道返回 {Code}：{Body}", nvrHost, postRes.StatusCode, postBody);
                lastError = ExtractHikvisionError(postBody, $"HTTP {postRes.StatusCode}");

                // 如果提示已存在（如 networkSourceIpAlreadyExist 或已分配），尝试 PUT 更新已有通道
                if (postRes.StatusCode == HttpStatusCode.Forbidden || postRes.StatusCode == HttpStatusCode.BadRequest || postRes.StatusCode == HttpStatusCode.Conflict || postBody.Contains("AlreadyExist", StringComparison.OrdinalIgnoreCase))
                {
                    using var putContent = new StringContent(xml, Encoding.UTF8, "application/xml");
                    var putRes = await client.PutAsync(putUrl, putContent, ct);
                    var putBody = await putRes.Content.ReadAsStringAsync(ct);

                    if (putRes.IsSuccessStatusCode)
                    {
                        logger?.LogInformation("成功向海康录像机 [{Host}] PUT 更新通道 {Channel}", nvrHost, channelId);
                        success = true;
                    }
                    else
                    {
                        logger?.LogWarning("向海康录像机 [{Host}] PUT 更新通道返回 {Code}：{Body}", nvrHost, putRes.StatusCode, putBody);
                        lastError = ExtractHikvisionError(putBody, $"HTTP {putRes.StatusCode}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "与海康录像机 [{Host}] 通信下发通道异常", nvrHost);
            lastError = ex.Message;
        }

        if (!success)
        {
            logger?.LogError("海康录像机 [{Host}] 通道 {Channel} 下发失败：{Error}", nvrHost, channelId, lastError);
            throw new PlatformException(502, "nvr.channel_add_failed", $"录像机 [{nvrHost}] 下发通道失败：{lastError}");
        }
    }

    private static string ExtractHikvisionError(string xml, string defaultError)
    {
        if (string.IsNullOrWhiteSpace(xml)) return defaultError;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var subStatus = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "subStatusString")?.Value;
            if (!string.IsNullOrWhiteSpace(subStatus)) return subStatus;

            var subCode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "subStatusCode")?.Value;
            if (!string.IsNullOrWhiteSpace(subCode)) return subCode;

            var statusStr = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "statusString")?.Value;
            if (!string.IsNullOrWhiteSpace(statusStr)) return statusStr;
        }
        catch { }
        return defaultError;
    }
}
