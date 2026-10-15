namespace VideoPlatform.Infrastructure;

/// <summary>
/// 插件标识与种类的唯一定义点。
///
/// 背景：平台存在两类"设备驱动插件"，它们在调用路径上根本不同 ——
///  1) **进程内插件**：由平台进程直接处理，没有独立的适配器 HTTP 服务。
///     目前只有华为网管网关（NeoSight/eSight），其 <c>EsightPonClient</c> 直接访问网管北向接口。
///  2) **HTTP 适配器插件**：独立进程，经 <c>IDeviceAdapter</c> 按 <c>device_plugins.endpoint_url</c> 路由。
///     目前为海康与中维。
///
/// 此前"是不是进程内插件"这一判定以字面量形式散落在 6 处（3 处 C# 判定 + 3 处 SQL 过滤），
/// 任一处遗漏都会造成不一致行为。现收敛到本类的 <see cref="IsInProcess"/> 与
/// <see cref="EsightPon"/>，新增进程内插件时只需改这里。
///
/// 后续演进：若进程内插件增多，应把该标志下沉为 <c>device_plugins</c> 的一列
/// （如 <c>kind</c>），由数据驱动而非代码常量；当前只有 1 个进程内插件，尚不值得加列与迁移。
/// </summary>
public static class PluginIds
{
    /// <summary>华为网管网关（NeoSight/eSight）的插件标识。</summary>
    public const string EsightPon = "esight_pon";

    /// <summary>该插件是否由平台进程内直接处理（即没有独立的适配器 HTTP 端点）。</summary>
    public static bool IsInProcess(string? pluginId)
        => string.Equals(pluginId, EsightPon, StringComparison.Ordinal);
}
