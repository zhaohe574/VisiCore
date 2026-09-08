internal interface IDevice : IDisposable
{
    long Id { get; }
    DeviceOptions Options { get; }
    bool Simulated { get; }
    DeviceSnapshot Sync();
    IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end);
    IPlaybackSource OpenPlayback(int channel, DateTimeOffset start, DateTimeOffset end, uint fileIndex, Action<uint, IntPtr, uint> receive);
    Task DownloadAsync(int channel, DateTimeOffset start, DateTimeOffset end, string path, Action<int> progress, CancellationToken cancellationToken);
    void Ptz(PtzRequest request);
    void Preset(PresetRequest request);
}

internal interface IPlaybackSource : IDisposable
{
    void Start();
    void Pause(bool pause);
    void SetSpeed(double speed);
    DateTimeOffset? CurrentTime { get; }
    bool Completed { get; }
}
