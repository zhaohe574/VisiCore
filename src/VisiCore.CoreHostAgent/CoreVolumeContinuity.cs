using System.Text.Json;

namespace VisiCore.CoreHostAgent;

internal static class CoreVolumeContinuity
{
    private static readonly (string ComposeKey, string Destination)[] RequiredVolumes =
    [
        ("postgres-data", "/var/lib/postgresql/data"),
        ("visicore-config", "/var/lib/visicore/config"),
        ("api-exports", "/var/lib/visicore/exports"),
        ("visicore-backups", "/var/lib/visicore/backups")
    ];

    public static bool TryReadExpectedVolumes(string composeConfigurationJson, out IReadOnlyDictionary<string, string> expectedVolumes)
    {
        expectedVolumes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(composeConfigurationJson);
            if (!document.RootElement.TryGetProperty("volumes", out var volumes) || volumes.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (composeKey, destination) in RequiredVolumes)
            {
                if (!volumes.TryGetProperty(composeKey, out var volume) ||
                    volume.ValueKind != JsonValueKind.Object ||
                    !volume.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()))
                {
                    return false;
                }

                parsed[destination] = name.GetString()!;
            }

            expectedVolumes = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool MatchesRunningContainer(string inspectJson, IReadOnlyDictionary<string, string> expectedVolumes)
    {
        try
        {
            using var document = JsonDocument.Parse(inspectJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != 1)
            {
                return false;
            }

            var container = document.RootElement[0];
            if (!container.TryGetProperty("Mounts", out var mounts) || mounts.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var mountedVolumes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mount in mounts.EnumerateArray())
            {
                if (!mount.TryGetProperty("Type", out var type) ||
                    !string.Equals(type.GetString(), "volume", StringComparison.Ordinal) ||
                    !mount.TryGetProperty("Destination", out var destination) ||
                    destination.ValueKind != JsonValueKind.String ||
                    !expectedVolumes.ContainsKey(destination.GetString()!) ||
                    !mount.TryGetProperty("Name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(name.GetString()))
                {
                    continue;
                }

                if (!mountedVolumes.TryAdd(destination.GetString()!, name.GetString()!))
                {
                    return false;
                }
            }

            return expectedVolumes.Count == mountedVolumes.Count &&
                   expectedVolumes.All(expected => mountedVolumes.TryGetValue(expected.Key, out var name) &&
                                                   string.Equals(name, expected.Value, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
