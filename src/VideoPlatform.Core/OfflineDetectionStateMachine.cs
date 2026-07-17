namespace VideoPlatform.Core;

public sealed class OfflineDetectionStateMachine
{
    private readonly int _suspectFailures;
    private readonly int _recoverSuccesses;
    private readonly TimeSpan _offlineAfter;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private DateTimeOffset? _suspectedAt;

    public OfflineDetectionStateMachine(
        int suspectFailures = 3,
        int recoverSuccesses = 3,
        TimeSpan? offlineAfter = null,
        CameraConnectivity current = CameraConnectivity.Unknown,
        int consecutiveFailures = 0,
        int consecutiveSuccesses = 0,
        DateTimeOffset? suspectedAt = null)
    {
        _suspectFailures = suspectFailures;
        _recoverSuccesses = recoverSuccesses;
        _offlineAfter = offlineAfter ?? TimeSpan.FromMinutes(2);
        Current = current;
        _consecutiveFailures = consecutiveFailures;
        _consecutiveSuccesses = consecutiveSuccesses;
        _suspectedAt = suspectedAt;
    }

    public CameraConnectivity Current { get; private set; } = CameraConnectivity.Unknown;

    public OfflineDetectionSnapshot Snapshot => new(Current, _consecutiveFailures, _consecutiveSuccesses, _suspectedAt);

    public CameraConnectivity Observe(bool isOnline, DateTimeOffset observedAt)
    {
        if (isOnline)
        {
            _consecutiveFailures = 0;
            _consecutiveSuccesses++;

            if (Current == CameraConnectivity.Unknown)
            {
                Current = CameraConnectivity.Online;
                return Current;
            }

            if (Current is CameraConnectivity.Offline or CameraConnectivity.SuspectedOffline)
            {
                Current = CameraConnectivity.Recovering;
            }

            if (Current == CameraConnectivity.Recovering && _consecutiveSuccesses >= _recoverSuccesses)
            {
                Current = CameraConnectivity.Online;
                _suspectedAt = null;
            }

            return Current;
        }

        _consecutiveSuccesses = 0;
        _consecutiveFailures++;

        if (Current == CameraConnectivity.Unknown && _consecutiveFailures >= _suspectFailures)
        {
            Current = CameraConnectivity.SuspectedOffline;
            _suspectedAt = observedAt;
            return Current;
        }

        if (Current == CameraConnectivity.Recovering)
        {
            Current = CameraConnectivity.Offline;
            return Current;
        }

        if (Current == CameraConnectivity.Online && _consecutiveFailures >= _suspectFailures)
        {
            Current = CameraConnectivity.SuspectedOffline;
            _suspectedAt = observedAt;
        }
        else if (Current == CameraConnectivity.SuspectedOffline &&
                 _suspectedAt is not null &&
                 observedAt - _suspectedAt >= _offlineAfter)
        {
            Current = CameraConnectivity.Offline;
        }

        return Current;
    }
}

public sealed record OfflineDetectionSnapshot(
    CameraConnectivity Connectivity,
    int ConsecutiveFailures,
    int ConsecutiveSuccesses,
    DateTimeOffset? SuspectedAt);
