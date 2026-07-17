using System.Runtime.InteropServices;
using System.Text.Json;

namespace VideoPlatform.EdgeAgent;

public sealed class EdgeAgentOptions
{
    public string? ControlPlaneBaseUri { get; init; }
    public string? EnrollmentCode { get; init; }
    public string StateDirectory { get; init; } = GetDefaultStateDirectory();
    public string? AgentVersion { get; init; }
    public string[] Capabilities { get; init; } =
    [
        "health",
        "identity",
        "configuration-placeholder",
        "diagnostics",
        "credential-envelope-placeholder"
    ];
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public bool AllowInsecureHttpForDevelopment { get; init; }

    public bool TryGetControlPlaneBaseUri(out Uri baseUri, out string validationError)
    {
        baseUri = null!;
        if (string.IsNullOrWhiteSpace(ControlPlaneBaseUri))
        {
            validationError = "未配置 EdgeAgent:ControlPlaneBaseUri。";
            return false;
        }

        if (!Uri.TryCreate(ControlPlaneBaseUri, UriKind.Absolute, out var parsed) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            (!parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(AllowInsecureHttpForDevelopment &&
               parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
               parsed.IsLoopback)))
        {
            validationError = "控制面必须配置为无凭据、无查询参数的 HTTPS 地址；开发环境仅允许回环 HTTP。";
            return false;
        }

        if (HeartbeatIntervalSeconds is < 5 or > 300 ||
            string.IsNullOrWhiteSpace(StateDirectory) ||
            !Path.IsPathFullyQualified(StateDirectory) ||
            Capabilities.Length == 0 ||
            Capabilities.Length > 32 ||
            Capabilities.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 64))
        {
            validationError = "Edge Agent 的状态目录、能力列表或心跳间隔无效。";
            return false;
        }

        baseUri = new Uri(parsed.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        validationError = string.Empty;
        return true;
    }

    public string GetAgentVersion() =>
        string.IsNullOrWhiteSpace(AgentVersion)
            ? typeof(EdgeAgentOptions).Assembly.GetName().Version?.ToString() ?? "0.1.0"
            : AgentVersion.Trim();

    public string GetPlatform() =>
        $"{GetOperatingSystemName()}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    public string GetCapabilitiesJson()
    {
        return JsonSerializer.Serialize(new
        {
            declared = Capabilities
        });
    }

    private static string GetDefaultStateDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VideoPlatform", "EdgeAgent")
        : "/var/lib/video-platform/edge-agent";

    private static string GetOperatingSystemName()
    {
        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }
        return "unknown";
    }

}

public sealed class HostAgentOptions
{
    public bool Enabled { get; init; }
    public string OperationInboxDirectory { get; init; } = "/var/lib/video-platform/host-agent/inbox";
    public string? SigningPublicKeyPath { get; init; }
    public string? SigningPublicKeyId { get; init; }
    public bool AllowExecution { get; init; }
    public string OperationStateDirectory { get; init; } = "/var/lib/video-platform/host-agent/state";
    public string? DockerComposeExecutablePath { get; init; }
    public string? ComposeFilePath { get; init; }
    public string? RollbackComposeFilePath { get; init; }
    public int ExecutionTimeoutSeconds { get; init; } = 600;
    public int PollIntervalSeconds { get; init; } = 15;

    public bool TryValidate(out string validationError)
    {
        if (!Enabled)
        {
            validationError = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(OperationInboxDirectory) ||
            !Path.IsPathFullyQualified(OperationInboxDirectory) ||
            string.IsNullOrWhiteSpace(SigningPublicKeyPath) ||
            !Path.IsPathFullyQualified(SigningPublicKeyPath) ||
            string.IsNullOrWhiteSpace(SigningPublicKeyId) || SigningPublicKeyId.Length > 128 ||
            string.IsNullOrWhiteSpace(OperationStateDirectory) || !Path.IsPathFullyQualified(OperationStateDirectory) ||
            PollIntervalSeconds is < 5 or > 300)
        {
            validationError = "Host Agent 的操作目录、签名公钥路径或轮询间隔无效。";
            return false;
        }

        if (AllowExecution &&
            (string.IsNullOrWhiteSpace(DockerComposeExecutablePath) ||
             !Path.IsPathFullyQualified(DockerComposeExecutablePath) ||
             string.IsNullOrWhiteSpace(ComposeFilePath) ||
             !Path.IsPathFullyQualified(ComposeFilePath) ||
             string.IsNullOrWhiteSpace(RollbackComposeFilePath) ||
             !Path.IsPathFullyQualified(RollbackComposeFilePath) ||
             ExecutionTimeoutSeconds is < 30 or > 3600))
        {
            validationError = "Host Agent 的固定 Compose 执行路径或超时参数无效。";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}
