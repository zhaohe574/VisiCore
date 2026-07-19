namespace VisiCore.Api;

/// <summary>
/// 受信任设备插件签名公钥。密钥只允许由服务部署配置注入，不能由管理端写入。
/// </summary>
public sealed class DevicePluginTrustOptions
{
    public Dictionary<string, string> PublicKeys { get; init; } = new(StringComparer.Ordinal);
}
