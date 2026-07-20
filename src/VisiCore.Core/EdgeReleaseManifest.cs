using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VisiCore.Core;

/// <summary>
/// 由离线密钥签名的边缘发行清单。签名由控制面和 Host Agent 分别验证，
/// 此类型只约束不可变制品、目标平台和生命周期字段。
/// </summary>
public sealed record EdgeReleaseManifest(
    string ReleaseId,
    string OperationType,
    string TargetPlatform,
    string TargetArchitecture,
    string ArtifactUrl,
    string ArtifactSha256,
    string MinimumHostAgentVersion,
    string SigningPublicKeyId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt)
{
    public static bool TryParse(string? manifestJson, out EdgeReleaseManifest manifest, out string failureKind)
    {
        manifest = null!;
        failureKind = "release_manifest_invalid";
        if (string.IsNullOrWhiteSpace(manifestJson) || manifestJson.Length > 65_536)
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(manifestJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !ReadString(root, "releaseId", out var releaseId) || releaseId.Length > 128 ||
                !ReadString(root, "operationType", out var operationType) ||
                !ReadString(root, "targetPlatform", out var platform) ||
                !ReadString(root, "targetArchitecture", out var architecture) ||
                !ReadString(root, "artifactUrl", out var artifactUrl) ||
                !ReadString(root, "artifactSha256", out var artifactSha256) ||
                !ReadString(root, "minimumHostAgentVersion", out var minimumHostAgentVersion) ||
                !ReadString(root, "signingPublicKeyId", out var signingPublicKeyId) ||
                !ReadDate(root, "issuedAt", out var issuedAt) ||
                !ReadDate(root, "expiresAt", out var expiresAt) ||
                operationType is not ("deployment" or "rollback") ||
                platform is not ("linux" or "windows") ||
                !IsSupportedArchitecture(platform, architecture) ||
                !Uri.TryCreate(artifactUrl, UriKind.Absolute, out var artifactUri) ||
                !artifactUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(artifactUri.UserInfo) || !string.IsNullOrEmpty(artifactUri.Query) || !string.IsNullOrEmpty(artifactUri.Fragment) ||
                !Regex.IsMatch(artifactSha256, "^[A-Fa-f0-9]{64}$") ||
                !Version.TryParse(minimumHostAgentVersion, out _) ||
                !Regex.IsMatch(signingPublicKeyId, "^[A-Za-z0-9._-]{1,128}$") ||
                expiresAt <= DateTimeOffset.UtcNow || expiresAt > issuedAt.AddHours(24) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return false;
            }

            manifest = new EdgeReleaseManifest(
                releaseId,
                operationType,
                platform,
                architecture,
                artifactUri.AbsoluteUri,
                artifactSha256.ToLowerInvariant(),
                minimumHostAgentVersion,
                signingPublicKeyId,
                issuedAt,
                expiresAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public bool TargetsCurrentHost() =>
        TargetPlatform == (OperatingSystem.IsWindows() ? "windows" : "linux") &&
        IsCurrentArchitecture(TargetArchitecture);

    private static bool IsSupportedArchitecture(string platform, string architecture) =>
        platform == "linux"
            ? architecture is "amd64" or "arm64"
            : architecture == "x64";

    private static bool IsCurrentArchitecture(string architecture) => architecture switch
    {
        "amd64" => RuntimeInformation.ProcessArchitecture == Architecture.X64,
        "arm64" => RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
        "x64" => RuntimeInformation.ProcessArchitecture == Architecture.X64,
        _ => false
    };

    private static bool ReadString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private static bool ReadDate(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(property.GetString(), out value);
    }
}
