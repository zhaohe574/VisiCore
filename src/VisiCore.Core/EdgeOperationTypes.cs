namespace VisiCore.Core;

public static class EdgeOperationTypes
{
    public const string OnvifRecordingSearch = "onvif.recording-search";
    public const string OnvifPlaybackRelay = "onvif.playback-relay";
    public const string OnvifPtz = "onvif.ptz";
    public const string PluginRecordingSearch = "plugin.recording-search";
    public const string PluginPlaybackRelay = "plugin.playback-relay";
    public const string PluginPtz = "plugin.ptz";
    public const string PluginPlaybackExport = "plugin.playback-export";

    public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        OnvifRecordingSearch,
        OnvifPlaybackRelay,
        OnvifPtz,
        PluginRecordingSearch,
        PluginPlaybackRelay,
        PluginPtz,
        PluginPlaybackExport
    };
}
