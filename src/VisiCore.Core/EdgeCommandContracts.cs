namespace VisiCore.Core;

public static class EdgeCommandTypes
{
    public const string OnvifRecordingSearch = "onvif.recordings.search";
    public const string OnvifPlaybackRelayStart = "onvif.playback.relay.start";
    public const string OnvifPlaybackRelayStop = "onvif.playback.relay.stop";
    public const string OnvifPlaybackRelayControl = "onvif.playback.relay.control";
    public const string OnvifPtzControl = "onvif.ptz.control";
    public const string PluginRecordingSearch = "plugin.recordings.search";
    public const string PluginPlaybackRelayStart = "plugin.playback.relay.start";
    public const string PluginPlaybackRelayStop = "plugin.playback.relay.stop";
    public const string PluginPlaybackRelayControl = "plugin.playback.relay.control";
    public const string PluginPtzControl = "plugin.ptz.control";
    public const string PluginPlaybackExport = "plugin.playback.export";

    public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        OnvifRecordingSearch,
        OnvifPlaybackRelayStart,
        OnvifPlaybackRelayStop,
        OnvifPlaybackRelayControl,
        OnvifPtzControl,
        PluginRecordingSearch,
        PluginPlaybackRelayStart,
        PluginPlaybackRelayStop,
        PluginPlaybackRelayControl,
        PluginPtzControl,
        PluginPlaybackExport
    };

    public static bool IsPtzControl(string commandType) =>
        string.Equals(commandType, OnvifPtzControl, StringComparison.Ordinal) ||
        string.Equals(commandType, PluginPtzControl, StringComparison.Ordinal);

    public static bool IsPlaybackRelayStart(string commandType) =>
        string.Equals(commandType, OnvifPlaybackRelayStart, StringComparison.Ordinal) ||
        string.Equals(commandType, PluginPlaybackRelayStart, StringComparison.Ordinal);

    public static string? GetPlaybackRelayStopCommandType(string startCommandType) =>
        startCommandType switch
        {
            OnvifPlaybackRelayStart => OnvifPlaybackRelayStop,
            PluginPlaybackRelayStart => PluginPlaybackRelayStop,
            _ => null
        };

    public static string? GetPlaybackRelayControlCommandType(string startCommandType) =>
        startCommandType switch
        {
            OnvifPlaybackRelayStart => OnvifPlaybackRelayControl,
            PluginPlaybackRelayStart => PluginPlaybackRelayControl,
            _ => null
        };
}

public sealed record RecordingSearchCommandPayload(
    Guid CameraId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int? MaxResults);

public sealed record PlaybackSampleCommandPayload(
    Guid CameraId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int? DurationSeconds);

public sealed record PlaybackRelayStartCommandPayload(
    Guid PlaybackSessionId,
    Guid CameraId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public sealed record PlaybackRelayStopCommandPayload(Guid PlaybackSessionId);

public enum PlaybackTransportAction
{
    Pause,
    Resume,
    Seek,
    SetSpeed
}

public sealed record PlaybackRelayControlCommandPayload(
    Guid PlaybackSessionId,
    Guid CameraId,
    PlaybackTransportAction Action,
    DateTimeOffset? Position,
    double? Speed,
    Guid ClientRequestId);

public sealed record PtzControlCommandPayload(
    Guid LeaseId,
    Guid CameraId,
    PtzAction Action,
    PtzMotion Motion,
    int Speed,
    long Sequence,
    int MaxDurationMilliseconds);

public sealed record PlaybackExportCommandPayload(
    Guid ExportId,
    Guid CameraId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Container);
