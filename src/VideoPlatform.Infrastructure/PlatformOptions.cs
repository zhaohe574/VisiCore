using Microsoft.Extensions.Configuration;

namespace VideoPlatform.Infrastructure;

public sealed class PlatformOptions(IConfiguration configuration)
{
    private volatile string _publicBase = (configuration["PLATFORM_PUBLIC_URL"] ?? "").TrimEnd('/');
    private volatile string _rtspsBase = (configuration["PLATFORM_RTSP_URL"] ?? "").TrimEnd('/');

    public string DatabaseUrl { get; } = configuration["PLATFORM_DATABASE_URL"] ?? throw new InvalidOperationException("缺少环境变量：PLATFORM_DATABASE_URL");
    public string DataPath { get; } = Path.GetFullPath(configuration["PLATFORM_DATA_PATH"] ?? (OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform", "server-v2") : "/var/lib/video-platform/v2"));
    public string AdapterUrl { get; } = configuration["HIK_ADAPTER_API_URL"] ?? "http://127.0.0.1:5092";
    public string AdapterKey { get; } = configuration["HIK_ADAPTER_INTERNAL_KEY"] ?? throw new InvalidOperationException("缺少环境变量：HIK_ADAPTER_INTERNAL_KEY");
    public string ZlmUrl { get; } = configuration["ZLM_API_URL"] ?? "http://127.0.0.1:18080";
    public string ZlmSecret { get; } = configuration["ZLM_API_SECRET"] ?? "";

    /// <summary>
    /// 受信任的自签名证书 SHA-256 指纹白名单（分号或逗号分隔，可含冒号与空格）。
    /// 内网设备（网管、录像机）常使用自签名证书；启用 TLS 校验后，运维通过此配置
    /// 显式信任已知证书，替代过去"无条件跳过校验"的做法。为空表示完全依赖系统信任链。
    /// </summary>
    public IReadOnlyList<string> TrustedCertificateThumbprints { get; } =
        (configuration["PLATFORM_TRUSTED_CERT_SHA256"] ?? "")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => TlsTrust.Normalize(value))
            .Where(value => value.Length > 0)
            .ToArray();

    public string PublicBase
    {
        get => _publicBase;
        set => _publicBase = (value ?? "").TrimEnd('/');
    }

    public string RtspsBase
    {
        get => _rtspsBase;
        set => _rtspsBase = (value ?? "").TrimEnd('/');
    }

    public void UpdateFromDomain(string protocol, string domain, int port)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        var isStandard = (protocol == "https" && port == 443) || (protocol == "http" && port == 80);
        PublicBase = isStandard ? $"{protocol}://{domain}" : $"{protocol}://{domain}:{port}";
        RtspsBase = $"rtsp://{domain}:18554";
    }

    public string BootstrapUser { get; } = configuration["PLATFORM_ADMIN_USER"] ?? "admin";
    public string? BootstrapPassword { get; } = configuration["PLATFORM_ADMIN_PASSWORD"];
    public string ReleasesPath => Path.Combine(DataPath, "releases");
    public string ExportsPath => Path.Combine(DataPath, "exports");
    public string KeysPath => Path.Combine(DataPath, "keys");
    public string PluginsPath => Path.Combine(DataPath, "plugins");
    public string SslPath => Path.GetFullPath(configuration["PLATFORM_SSL_PATH"] ?? (OperatingSystem.IsWindows() ? Path.Combine(DataPath, "ssl") : "/etc/nginx/ssl"));
    public string SslCertFile => Path.Combine(SslPath, "video-platform.crt");
    public string SslKeyFile => Path.Combine(SslPath, "video-platform.key");
}
