using Microsoft.Extensions.Configuration;

namespace VideoPlatform.Infrastructure;

public sealed class PlatformOptions(IConfiguration configuration)
{
    public string DatabaseUrl { get; } = configuration["PLATFORM_DATABASE_URL"] ?? throw new InvalidOperationException("缺少环境变量：PLATFORM_DATABASE_URL");
    public string DataPath { get; } = Path.GetFullPath(configuration["PLATFORM_DATA_PATH"] ?? (OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform", "server-v2") : "/var/lib/video-platform/v2"));
    public string AdapterUrl { get; } = configuration["HIK_ADAPTER_API_URL"] ?? "http://127.0.0.1:5092";
    public string AdapterKey { get; } = configuration["HIK_ADAPTER_INTERNAL_KEY"] ?? throw new InvalidOperationException("缺少环境变量：HIK_ADAPTER_INTERNAL_KEY");
    public string ZlmUrl { get; } = configuration["ZLM_API_URL"] ?? "http://127.0.0.1:18080";
    public string ZlmSecret { get; } = configuration["ZLM_API_SECRET"] ?? "";
    public string PublicBase { get; } = (configuration["PLATFORM_PUBLIC_URL"] ?? "https://10.37.200.74").TrimEnd('/');
    public string RtspsBase { get; } = (configuration["PLATFORM_RTSP_URL"] ?? "rtsp://10.37.200.74:18554").TrimEnd('/');
    public string BootstrapUser { get; } = configuration["PLATFORM_ADMIN_USER"] ?? "admin";
    public string? BootstrapPassword { get; } = configuration["PLATFORM_ADMIN_PASSWORD"];
    public string ReleasesPath => Path.Combine(DataPath, "releases");
    public string ExportsPath => Path.Combine(DataPath, "exports");
    public string KeysPath => Path.Combine(DataPath, "keys");
}
