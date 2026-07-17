namespace VideoPlatform.Core;

public interface IRecorderAdapter
{
    string Vendor { get; }

    Task<RecorderCapabilities> DetectCapabilitiesAsync(
        Recorder recorder,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Camera>> SynchronizeAsync(Recorder recorder, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, bool>> GetChannelOnlineStatesAsync(
        Recorder recorder,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordingSegment>> FindRecordingsAsync(
        Recorder recorder,
        Camera camera,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken);

    Task<LiveUpstreamStreamSession> CreateLiveSessionAsync(
        Recorder recorder,
        Camera camera,
        string profile,
        CancellationToken cancellationToken);

    Task<PlaybackRelaySession> CreatePlaybackSessionAsync(
        Recorder recorder,
        Camera camera,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        CancellationToken cancellationToken);

    Task ExecutePtzAsync(
        Recorder recorder,
        Camera camera,
        PtzCommand command,
        CancellationToken cancellationToken);
}
