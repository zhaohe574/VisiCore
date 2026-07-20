using System.Text.Json;

namespace VisiCore.EdgeAgent;

/// <summary>
/// 边缘运行时可由控制面发布的最小配置集。
/// 设备地址和设备凭据永远不属于该配置，分别通过分配与加密信封下发。
/// </summary>
public sealed record EdgeAgentRuntimeConfiguration(
    int SchemaVersion,
    int? InventorySyncIntervalSeconds,
    int? ClockSyncIntervalSeconds,
    bool? OnvifEnabled,
    bool? DirectRtspEnabled)
{
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "inventorySyncIntervalSeconds",
        "clockSyncIntervalSeconds",
        "onvifEnabled",
        "directRtspEnabled"
    };

    public static bool TryParse(
        string? configurationJson,
        EdgeAgentRuntimeSettingsSnapshot defaults,
        out EdgeAgentRuntimeSettingsSnapshot settings,
        out string failureKind)
    {
        settings = defaults;
        failureKind = string.Empty;
        if (string.IsNullOrWhiteSpace(configurationJson) || configurationJson.Trim() == "{}")
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(configurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Any(item => !AllowedProperties.Contains(item.Name)))
            {
                failureKind = "configuration_schema_invalid";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<EdgeAgentRuntimeConfiguration>(
                document.RootElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null || parsed.SchemaVersion != 1 ||
                parsed.InventorySyncIntervalSeconds is < 60 or > 86400 ||
                parsed.ClockSyncIntervalSeconds is < 300 or > 86400)
            {
                failureKind = "configuration_schema_invalid";
                return false;
            }

            settings = defaults with
            {
                InventorySyncIntervalSeconds = parsed.InventorySyncIntervalSeconds ?? defaults.InventorySyncIntervalSeconds,
                ClockSyncIntervalSeconds = parsed.ClockSyncIntervalSeconds ?? defaults.ClockSyncIntervalSeconds,
                OnvifEnabled = parsed.OnvifEnabled ?? defaults.OnvifEnabled,
                DirectRtspEnabled = parsed.DirectRtspEnabled ?? defaults.DirectRtspEnabled
            };
            return true;
        }
        catch (JsonException)
        {
            failureKind = "configuration_schema_invalid";
            return false;
        }
    }
}

public sealed class EdgeAgentRuntimeSettings(EdgeAgentOptions options)
{
    private readonly object gate = new();
    private readonly EdgeAgentRuntimeSettingsSnapshot defaults = new(
        options.InventorySyncIntervalSeconds,
        options.ClockSyncIntervalSeconds,
        true,
        true);
    private EdgeAgentRuntimeSettingsSnapshot current = new(
        options.InventorySyncIntervalSeconds,
        options.ClockSyncIntervalSeconds,
        true,
        true);

    public EdgeAgentRuntimeSettingsSnapshot Snapshot()
    {
        lock (gate)
        {
            return current;
        }
    }

    public bool TryApply(string? configurationJson, out string failureKind)
    {
        if (!EdgeAgentRuntimeConfiguration.TryParse(configurationJson, defaults, out var next, out failureKind))
        {
            return false;
        }

        lock (gate)
        {
            current = next;
        }
        return true;
    }
}

public sealed record EdgeAgentRuntimeSettingsSnapshot(
    int InventorySyncIntervalSeconds,
    int ClockSyncIntervalSeconds,
    bool OnvifEnabled,
    bool DirectRtspEnabled);
