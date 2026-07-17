using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class ClockMonitoringOptions
{
    public int MaximumAbsoluteOffsetSeconds { get; init; } = 5;
    public int RequiredConsecutiveObservations { get; init; } = 3;
    public int ObservationRetentionDays { get; init; } = 30;
    public int MaximumRoundTripSeconds { get; init; } = 30;

    public void Validate()
    {
        if (MaximumAbsoluteOffsetSeconds is < 1 or > 86400 ||
            RequiredConsecutiveObservations is < 1 or > 10 ||
            ObservationRetentionDays is < 1 or > 3650 ||
            MaximumRoundTripSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException("ClockMonitoring 参数超出允许范围。 ");
        }
    }
}

public static class RecorderClockSynchronizationStateMachine
{
    public static RecorderClockSynchronizationSnapshot Observe(
        ClockSynchronization current,
        int consecutiveDrifts,
        int consecutiveSynchronizations,
        DateTimeOffset? driftSinceAt,
        bool isWithinThreshold,
        DateTimeOffset observedAt,
        int requiredConsecutiveObservations)
    {
        if (isWithinThreshold)
        {
            var synchronizations = consecutiveSynchronizations + 1;
            var next = synchronizations >= requiredConsecutiveObservations
                ? ClockSynchronization.Synchronized
                : current;
            return new RecorderClockSynchronizationSnapshot(
                next,
                0,
                synchronizations,
                next == ClockSynchronization.Synchronized ? null : driftSinceAt);
        }

        var drifts = consecutiveDrifts + 1;
        var nextDriftSinceAt = driftSinceAt ?? observedAt;
        var nextState = drifts >= requiredConsecutiveObservations
            ? ClockSynchronization.Drifted
            : current;
        return new RecorderClockSynchronizationSnapshot(nextState, drifts, 0, nextDriftSinceAt);
    }
}

public readonly record struct RecorderClockSynchronizationSnapshot(
    ClockSynchronization ClockSynchronization,
    int ConsecutiveDrifts,
    int ConsecutiveSynchronizations,
    DateTimeOffset? DriftSinceAt);
