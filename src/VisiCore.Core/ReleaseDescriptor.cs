using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace VisiCore.Core;

/// <summary>
/// 统一产品发行描述。它只保存可公开验证的版本和制品元数据，签名由控制面和宿主执行器分别验证。
/// </summary>
public sealed record ReleaseDescriptor(
    string ProductVersion,
    string Channel,
    IReadOnlyList<ReleaseArtifactDescriptor> Artifacts,
    string MinimumCoreVersion,
    string MinimumEdgeVersion,
    string DatabaseMigrationMode,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string SigningPublicKeyId)
{
    private static readonly Regex SemVer = new("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedComponents = new(StringComparer.Ordinal)
    {
        "core", "edge-docker", "edge-windows"
    };

    public static bool TryParse(string? value, out ReleaseDescriptor descriptor, out string failureKind)
    {
        descriptor = null!;
        failureKind = "release_descriptor_invalid";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 131_072)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !ReadString(root, "productVersion", out var productVersion) || !IsSemVer(productVersion) ||
                !ReadString(root, "channel", out var channel) || channel is not "stable" or "preview" ||
                !ReadString(root, "minimumCoreVersion", out var minimumCoreVersion) || !IsSemVer(minimumCoreVersion) ||
                !ReadString(root, "minimumEdgeVersion", out var minimumEdgeVersion) || !IsSemVer(minimumEdgeVersion) ||
                !ReadString(root, "databaseMigrationMode", out var databaseMigrationMode) || databaseMigrationMode is not ("automatic-backup" or "none") ||
                !ReadDate(root, "issuedAt", out var issuedAt) ||
                !ReadDate(root, "expiresAt", out var expiresAt) ||
                !ReadString(root, "signingPublicKeyId", out var signingPublicKeyId) || !IsKeyId(signingPublicKeyId) ||
                !root.TryGetProperty("artifacts", out var artifactsElement) || artifactsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var artifacts = new List<ReleaseArtifactDescriptor>();
            foreach (var element in artifactsElement.EnumerateArray())
            {
                if (!ReleaseArtifactDescriptor.TryParse(element, out var artifact))
                {
                    return false;
                }
                artifacts.Add(artifact);
            }

            if (artifacts.Count == 0 || artifacts.Count > 8 ||
                artifacts.GroupBy(item => (item.Component, item.Platform, item.Architecture)).Any(group => group.Count() != 1) ||
                artifacts.Any(item => !SupportedComponents.Contains(item.Component)) ||
                expiresAt <= issuedAt || expiresAt > issuedAt.AddDays(365) || issuedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return false;
            }

            descriptor = new ReleaseDescriptor(
                productVersion,
                channel,
                artifacts,
                minimumCoreVersion,
                minimumEdgeVersion,
                databaseMigrationMode,
                issuedAt,
                expiresAt,
                signingPublicKeyId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public IEnumerable<ReleaseArtifactDescriptor> FindArtifacts(string component, string platform, string architecture) =>
        Artifacts.Where(item =>
            string.Equals(item.Component, component, StringComparison.Ordinal) &&
            string.Equals(item.Platform, platform, StringComparison.Ordinal) &&
            string.Equals(item.Architecture, architecture, StringComparison.Ordinal));

    public static bool IsSemVer(string value) => SemVer.IsMatch(value);

    /// <summary>
    /// 发行描述签名使用稳定的紧凑 JSON 表示，避免空白、转义方式或字段顺序造成验签歧义。
    /// 描述字段只包含字符串、整数、对象和数组，因此不接受重复属性或未定义的数字形式。
    /// </summary>
    public static bool TryCanonicalizeJson(string? value, out string canonicalJson)
    {
        canonicalJson = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 131_072)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteCanonicalJson(document.RootElement, writer);
            }
            canonicalJson = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var properties = element.EnumerateObject().ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                {
                    throw new JsonException("发行描述包含重复属性。");
                }
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            }
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(item, writer);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var integer))
                {
                    throw new JsonException("发行描述仅允许整数数值。");
                }
                writer.WriteNumberValue(integer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("发行描述包含不支持的 JSON 值。");
        }
    }

    private static bool ReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private static bool ReadDate(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(property.GetString(), out value);
    }

    private static bool IsKeyId(string value) => Regex.IsMatch(value, "^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
}

public sealed record ReleaseArtifactDescriptor(
    string Component,
    string Platform,
    string Architecture,
    string ArtifactReference,
    string ArtifactSha256,
    long SizeBytes,
    string MinimumHostAgentVersion)
{
    private static readonly Regex Sha256 = new("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant);

    internal static bool TryParse(JsonElement element, out ReleaseArtifactDescriptor descriptor)
    {
        descriptor = null!;
        if (element.ValueKind != JsonValueKind.Object ||
            !ReadString(element, "component", out var component) ||
            !ReadString(element, "platform", out var platform) ||
            !ReadString(element, "architecture", out var architecture) ||
            !ReadString(element, "artifactReference", out var artifactReference) ||
            !ReadString(element, "artifactSha256", out var artifactSha256) || !Sha256.IsMatch(artifactSha256) ||
            !element.TryGetProperty("sizeBytes", out var sizeElement) || !sizeElement.TryGetInt64(out var sizeBytes) || sizeBytes is <= 0 or > 8L * 1024 * 1024 * 1024 ||
            !ReadString(element, "minimumHostAgentVersion", out var minimumHostAgentVersion) || !ReleaseDescriptor.IsSemVer(minimumHostAgentVersion) ||
            !IsValidTarget(component, platform, architecture) ||
            !IsValidReference(component, artifactReference))
        {
            return false;
        }

        descriptor = new ReleaseArtifactDescriptor(
            component,
            platform,
            architecture,
            artifactReference,
            artifactSha256.ToLowerInvariant(),
            sizeBytes,
            minimumHostAgentVersion);
        return true;
    }

    private static bool IsValidTarget(string component, string platform, string architecture) => component switch
    {
        "core" => platform == "linux" && architecture is "amd64" or "arm64",
        "edge-docker" => platform == "linux" && architecture is "amd64" or "arm64",
        "edge-windows" => platform == "windows" && architecture == "x64",
        _ => false
    };

    private static bool IsValidReference(string component, string value)
    {
        if (component is "core" or "edge-docker")
        {
            return value.StartsWith("ghcr.io/", StringComparison.OrdinalIgnoreCase) &&
                   value.Contains("@sha256:", StringComparison.OrdinalIgnoreCase) &&
                   !value.Contains(' ') && !value.Contains('?') && !value.Contains('#');
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool ReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}

public static class RuntimeVersion
{
    public static string ProductVersion =>
        typeof(RuntimeVersion).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+', 2)[0]
        ?? typeof(RuntimeVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
