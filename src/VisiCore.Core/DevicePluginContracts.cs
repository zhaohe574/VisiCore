namespace VisiCore.Core;

public static class DeviceKinds
{
    public const string Camera = "camera";
    public const string Recorder = "recorder";
    public const string Matrix = "matrix";
    public const string Encoder = "encoder";
    public const string Decoder = "decoder";
    public const string Gateway = "gateway";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Camera, Recorder, Matrix, Encoder, Decoder, Gateway, Other
    };
}

public static class CameraSourceTypes
{
    public const string RecorderChannel = "recorder-channel";
    public const string Direct = "direct";
}

public static class CameraProvisioningModes
{
    public const string Discovered = "discovered";
    public const string Manual = "manual";
}

public static class DevicePluginRuntimeTypes
{
    public const string Onvif = "onvif";
    public const string DirectRtsp = "direct-rtsp";
    public const string ExternalEdge = "external-edge";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Onvif, DirectRtsp, ExternalEdge
    };
}

public sealed record DevicePluginEndpointDefinition(
    string Protocol,
    string Label,
    int DefaultPort,
    bool Required = true,
    bool SupportsTls = false);

public sealed record DevicePluginCapabilities(
    bool LiveView,
    bool ChannelDiscovery,
    bool Playback,
    bool Ptz,
    bool Export,
    bool ClockSynchronization);

public sealed record DevicePluginPackage(
    string ImageReference,
    string ImageDigest,
    string PackageSha256,
    string SigningKeyId,
    string Signature);

public sealed record DevicePluginManifest(
    string Key,
    string Name,
    string Version,
    string ProtocolType,
    string RuntimeType,
    string AdapterType,
    IReadOnlyList<string> SupportedDeviceKinds,
    IReadOnlyList<DevicePluginEndpointDefinition> Endpoints,
    DevicePluginCapabilities Capabilities,
    string? Vendor = null,
    IReadOnlyList<string>? Models = null,
    string? Description = null,
    string MinimumPlatformVersion = "1.0.0",
    DevicePluginPackage? Package = null,
    string? ConfigurationSchema = null);
