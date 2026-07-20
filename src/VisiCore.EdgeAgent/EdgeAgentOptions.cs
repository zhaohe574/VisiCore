using System.Runtime.InteropServices;
using System.Text.Json;

namespace VisiCore.EdgeAgent;

public sealed class EdgeAgentOptions
{
    public string? ControlPlaneBaseUri { get; init; }
    public string? EnrollmentCode { get; init; }
    public string? BootstrapFilePath { get; init; }
    public string StateDirectory { get; init; } = GetDefaultStateDirectory();
    public string? AgentVersion { get; init; }
    public string[] Capabilities { get; init; } =
    [
        "health",
        "identity",
        "configuration-v1",
        "diagnostics",
        "credential-envelope",
        "onvif-readonly",
        "direct-rtsp-probe"
    ];
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public int InventorySyncIntervalSeconds { get; init; } = 300;
    public int ClockSyncIntervalSeconds { get; init; } = 900;
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
            InventorySyncIntervalSeconds is < 60 or > 86400 ||
            ClockSyncIntervalSeconds is < 300 or > 86400 ||
            string.IsNullOrWhiteSpace(StateDirectory) ||
            !Path.IsPathFullyQualified(StateDirectory) ||
            (!string.IsNullOrWhiteSpace(BootstrapFilePath) && !Path.IsPathFullyQualified(BootstrapFilePath)) ||
            Capabilities.Length == 0 ||
            Capabilities.Length > 32 ||
            Capabilities.Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 64))
        {
            validationError = "Edge Agent 的状态目录、引导文件、能力列表、同步间隔或心跳间隔无效。";
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
            declared = Capabilities,
            platform = GetPlatform(),
            architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        });
    }

    private static string GetDefaultStateDirectory() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeAgent")
        : "/var/lib/visicore/edge-agent";

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
    public string OperationInboxDirectory { get; init; } = GetDefaultHostPath("inbox");
    public string OperationReceiptDirectory { get; init; } = GetDefaultHostPath("receipts");
    public string? SigningPublicKeyPath { get; init; }
    public string? SigningPublicKeyId { get; init; }
    public bool AllowExecution { get; init; }
    public string ReleaseArtifactDirectory { get; init; } = GetDefaultHostPath("releases");
    public string[] AllowedArtifactHosts { get; init; } = [];
    public long MaximumArtifactBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public string OperationStateDirectory { get; init; } = GetDefaultHostPath("state");
    public string? DockerComposeExecutablePath { get; init; }
    public string? ComposeFilePath { get; init; }
    public string? RollbackComposeFilePath { get; init; }
    public string? WindowsInstallerExecutablePath { get; init; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.SystemDirectory, "msiexec.exe")
        : null;
    public string? WindowsInstallerPath { get; init; }
    public string? RollbackWindowsInstallerPath { get; init; }
    public string? ConfigurationSocketPath { get; init; } = OperatingSystem.IsWindows()
        ? null
        : GetDefaultHostPath("config/host-agent.sock");
    public string? ConfigurationTokenPath { get; init; } = OperatingSystem.IsWindows()
        ? null
        : GetDefaultHostPath("config/access.token");
    public string? ManagedEdgeAgentConfigurationPath { get; init; } = OperatingSystem.IsWindows()
        ? null
        : "/var/lib/visicore/edge-agent/edge-agent.json";
    public string? ManagedEdgeAgentBootstrapPath { get; init; } = OperatingSystem.IsWindows()
        ? null
        : "/var/lib/visicore/edge-agent/bootstrap/bootstrap.json";
    public string? ManagedHostAgentConfigurationPath { get; init; } = OperatingSystem.IsWindows()
        ? null
        : "/etc/visicore/edge-host-agent/edge-host-agent.json";
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
            string.IsNullOrWhiteSpace(OperationReceiptDirectory) ||
            !Path.IsPathFullyQualified(OperationReceiptDirectory) ||
            string.IsNullOrWhiteSpace(ReleaseArtifactDirectory) || !Path.IsPathFullyQualified(ReleaseArtifactDirectory) ||
            string.IsNullOrWhiteSpace(OperationStateDirectory) || !Path.IsPathFullyQualified(OperationStateDirectory) ||
            PollIntervalSeconds is < 5 or > 300)
        {
            validationError = "Host Agent 的操作目录、签名公钥路径或轮询间隔无效。";
            return false;
        }

        if (AllowExecution &&
            (string.IsNullOrWhiteSpace(SigningPublicKeyPath) ||
             !Path.IsPathFullyQualified(SigningPublicKeyPath) ||
             string.IsNullOrWhiteSpace(SigningPublicKeyId) || SigningPublicKeyId.Length > 128 ||
             AllowedArtifactHosts.Length == 0 ||
             AllowedArtifactHosts.Any(host => string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Contains('/') || host.Contains(':')) ||
             MaximumArtifactBytes is < 1_048_576 or > 8L * 1024 * 1024 * 1024 ||
             ExecutionTimeoutSeconds is < 30 or > 3600))
        {
            validationError = "Host Agent 的发行制品目录、受信下载域名、容量上限或超时参数无效。";
            return false;
        }

        if (AllowExecution && OperatingSystem.IsWindows() &&
            (string.IsNullOrWhiteSpace(WindowsInstallerExecutablePath) ||
             !Path.IsPathFullyQualified(WindowsInstallerExecutablePath) ||
             !File.Exists(WindowsInstallerExecutablePath)))
        {
            validationError = "Host Agent 的固定 MSI 执行器路径无效。";
            return false;
        }

        if (AllowExecution && !OperatingSystem.IsWindows() &&
            (string.IsNullOrWhiteSpace(DockerComposeExecutablePath) ||
             !Path.IsPathFullyQualified(DockerComposeExecutablePath) ||
             !File.Exists(DockerComposeExecutablePath)))
        {
            validationError = "Host Agent 的固定 Docker Compose 执行器路径无效。";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    private static string GetDefaultHostPath(string leaf) => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeHostAgent", leaf)
        : Path.Combine("/var/lib/visicore/edge-host-agent", leaf);
}
