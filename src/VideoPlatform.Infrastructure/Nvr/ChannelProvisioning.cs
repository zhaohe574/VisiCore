using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using VideoPlatform.Application;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure.Nvr;

// ════════════════════════════════════════════════════════════════════════
// 通道下发策略（P1 阶段 2 第 5 步）
//
// 目的：消除 NvrChannelManager 中 `if (pluginId == "hikvision") / else if (pluginId == "jovision")`
// 的厂商字符串分支，改为**按插件声明的具名策略**派发。
//
// 本步是**纯机械抽取**：所有 ISAPI 调用、XML 构造、错误码、日志文案、回退顺序
// 均与抽取前逐字一致，未改变任何行为。厂商差异原本就存在，这里只是把它从
// "调用点分支" 变成 "策略实现"，使新增厂商不再需要改动派发主路径。
//
// 策略选择顺序：按注册顺序取第一个 AppliesTo 命中的实现；
// 都不命中（新厂商）时回退到 AdapterChannelProvisioner 并记录告警 ——
// **不得抛异常**，否则新增厂商即破坏服务。
// ════════════════════════════════════════════════════════════════════════

/// <summary>通道下发所需的全部输入。字段与抽取前的各个方法参数一一对应。</summary>
public sealed record ChannelProvisionContext(
    long NvrDeviceId,
    string NvrName,
    string NvrHost,
    int NvrPort,
    string NvrUser,
    string NvrPwd,
    int ChannelNumber,
    string? IpcHost,
    int? IpcPort,
    string? IpcUser,
    string? IpcPwd,
    string? ProtocolType,
    string? ChannelName);

/// <summary>录像机通道下发策略。实现按插件标识声明适用范围。</summary>
public interface IChannelProvisioner
{
    /// <summary>本策略是否适用于该插件（plugin_id）。</summary>
    bool AppliesTo(string pluginId);

    /// <summary>新增通道。失败时抛出与抽取前一致的 PlatformException。</summary>
    Task AddAsync(ChannelProvisionContext context, CancellationToken ct);

    /// <summary>更新通道。失败时抛出与抽取前一致的 PlatformException。</summary>
    Task UpdateAsync(ChannelProvisionContext context, CancellationToken ct);
}

/// <summary>
/// 海康：由平台经 ISAPI 直连录像机下发数字通道。
/// 方法体自 NvrChannelManager 原样搬移（含 POST 创建失败回退 PUT 的既有策略）。
/// </summary>
public sealed class HikvisionIsapiChannelProvisioner(ILogger? logger = null) : IChannelProvisioner
{
    public bool AppliesTo(string pluginId) => pluginId == "hikvision";

    public Task AddAsync(ChannelProvisionContext context, CancellationToken ct)
        => SetIsapiChannelAsync(
            context.NvrHost, context.NvrPort, context.NvrUser, context.NvrPwd, context.ChannelNumber,
            // AddIpcChannelAsync 的调用方保证这些值非空；沿用非空语义（原方法签名为非空）。
            context.IpcHost ?? "", context.IpcPort ?? 8000, context.IpcUser ?? "", context.IpcPwd ?? "",
            context.ProtocolType ?? "HIKVISION", context.ChannelName ?? $"通道 {context.ChannelNumber}", ct);

    public Task UpdateAsync(ChannelProvisionContext context, CancellationToken ct)
        => UpdateIsapiChannelAsync(
            context.NvrHost, context.NvrPort, context.NvrUser, context.NvrPwd, context.ChannelNumber,
            context.IpcHost, context.IpcPort, context.IpcUser, context.IpcPwd,
            context.ProtocolType, context.ChannelName, ct);

    private async Task UpdateIsapiChannelAsync(
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

    private async Task SetIsapiChannelAsync(
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

/// <summary>
/// 中维：**明确不支持**由平台远程下发通道，返回既有的 400/nvr.unsupported 与原文案。
/// 这是业务语义（引导用户改用录像机本地界面），必须与"未注册策略的新厂商"区分开。
/// </summary>
public sealed class JovisionUnsupportedChannelProvisioner : IChannelProvisioner
{
    public bool AppliesTo(string pluginId) => pluginId == "jovision";

    public Task AddAsync(ChannelProvisionContext context, CancellationToken ct)
        => throw new PlatformException(400, "nvr.unsupported",
            $"中维录像机【{context.NvrName}】不支持由平台远程下发添加通道。请先在中维录像机本地界面或客户端完成摄像头【{context.IpcHost}】的搜索绑定，完成后在系统【设备管理】点击该录像机的【同步】，视枢将自动探测识别并挂载上线。");

    public Task UpdateAsync(ChannelProvisionContext context, CancellationToken ct)
        => throw new PlatformException(400, "nvr.unsupported",
            $"中维录像机【{context.NvrName}】不支持由平台远程下发修改通道配置。请先在中维录像机本地界面完成修改，完成后在系统【设备管理】点击该录像机的【同步】。");
}

/// <summary>
/// 通用回退：经设备适配器插件转发通道新增/更新指令（大华、宇视等，以及任何未注册策略的厂商）。
/// </summary>
public sealed class AdapterChannelProvisioner(IDeviceAdapter adapter, ILogger? logger = null) : IChannelProvisioner
{
    /// <summary>回退策略：对任何插件都适用（作为最后兜底，不参与"具名声明"）。</summary>
    public bool AppliesTo(string pluginId) => true;

    public async Task AddAsync(ChannelProvisionContext context, CancellationToken ct)
    {
        try
        {
            await adapter.SendAsync(HttpMethod.Post, $"/internal/devices/{context.NvrDeviceId}/channels", new
            {
                channel = context.ChannelNumber,
                ip = context.IpcHost,
                port = context.IpcPort,
                username = context.IpcUser,
                password = context.IpcPwd,
                protocol = context.ProtocolType,
                name = context.ChannelName
            }, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "调用设备适配器添加通道失败");
            throw new PlatformException(502, "nvr.channel_add_failed", $"向录像机下发添加通道失败：{ex.Message}");
        }
    }

    public async Task UpdateAsync(ChannelProvisionContext context, CancellationToken ct)
    {
        try
        {
            await adapter.SendAsync(HttpMethod.Put, $"/internal/devices/{context.NvrDeviceId}/channels/{context.ChannelNumber}", new
            {
                channel = context.ChannelNumber,
                ip = context.IpcHost,
                port = context.IpcPort,
                username = context.IpcUser,
                password = context.IpcPwd,
                protocol = context.ProtocolType,
                name = context.ChannelName
            }, ct);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "调用设备适配器更新通道失败");
            throw new PlatformException(502, "nvr.channel_update_failed", $"向录像机下发更新通道失败：{ex.Message}");
        }
    }
}
