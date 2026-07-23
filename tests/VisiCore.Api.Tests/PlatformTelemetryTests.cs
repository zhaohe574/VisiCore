using Xunit;

namespace VisiCore.Api.Tests;

public sealed class PlatformTelemetryTests
{
    [Theory]
    [InlineData(null, UpgradeFailureMetricCode.Unspecified)]
    [InlineData("core_upgrade_failed", "core_upgrade_failed")]
    [InlineData(" CORE_UPGRADE_FAILED ", "core_upgrade_failed")]
    [InlineData("Docker pull failed: registry timeout", UpgradeFailureMetricCode.Unclassified)]
    public void UpgradeFailureMetricCodeShouldUseFiniteVocabulary(string? failureSummary, string expected)
    {
        Assert.Equal(expected, UpgradeFailureMetricCode.Normalize(failureSummary));
    }

    [Fact]
    public void UpdateShouldExposeLatestOperationalSnapshot()
    {
        using var telemetry = new PlatformTelemetry();
        var collectedAt = DateTimeOffset.UtcNow;

        telemetry.Update(new PlatformTelemetrySnapshot(
            ActiveStreamSessions: 8,
            OnlineEdgeAgents: 3,
            StaleEdgeAgents: 1,
            UpgradeFailures: new Dictionary<string, long> { ["signature_invalid"] = 2 },
            BackupResults: new Dictionary<string, long> { ["available"] = 5 },
            CollectedAt: collectedAt));

        var snapshot = telemetry.Snapshot;

        Assert.Equal(8, snapshot.ActiveStreamSessions);
        Assert.Equal(3, snapshot.OnlineEdgeAgents);
        Assert.Equal(1, snapshot.StaleEdgeAgents);
        Assert.Equal(2, snapshot.UpgradeFailures["signature_invalid"]);
        Assert.Equal(5, snapshot.BackupResults["available"]);
        Assert.Equal(collectedAt, snapshot.CollectedAt);
    }
}
