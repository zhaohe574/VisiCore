using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using VisiCore.Core;

/// <summary>
/// 平台级指标的唯一入口。业务代码只记录事实，聚合快照由后台采集器负责刷新。
/// </summary>
public sealed class PlatformTelemetry : IDisposable
{
    public const string ServiceName = "VisiCore.Api";
    public const string MeterName = "VisiCore.Platform";
    public const string ActivitySourceName = "VisiCore.Platform";

    private readonly Meter meter = new(MeterName, RuntimeVersion.ProductVersion);
    private readonly Counter<long> streamSessionsIssued;
    private readonly Counter<long> edgeHeartbeats;
    private readonly ActivitySource activitySource = new(ActivitySourceName, RuntimeVersion.ProductVersion);
    private ImmutableArray<KeyValuePair<string, long>> upgradeFailures = [];
    private ImmutableArray<KeyValuePair<string, long>> backupResults = [];
    private long activeStreamSessions;
    private long onlineEdgeAgents;
    private long staleEdgeAgents;
    private DateTimeOffset collectedAt;

    public PlatformTelemetry()
    {
        streamSessionsIssued = meter.CreateCounter<long>("visicore.stream.sessions.issued", unit: "{session}", description: "成功创建的实时或回放会话数。");
        edgeHeartbeats = meter.CreateCounter<long>("visicore.edge.heartbeats", unit: "{heartbeat}", description: "成功处理的边缘节点心跳数。");
        meter.CreateObservableGauge("visicore.stream.sessions.active", () => Volatile.Read(ref activeStreamSessions), unit: "{session}", description: "当前未撤销且未过期的媒体会话数。");
        meter.CreateObservableGauge("visicore.edge.agents.online", () => Volatile.Read(ref onlineEdgeAgents), unit: "{agent}", description: "两分钟内有心跳的启用边缘节点数。");
        meter.CreateObservableGauge("visicore.edge.agents.stale", () => Volatile.Read(ref staleEdgeAgents), unit: "{agent}", description: "超过两分钟未心跳的启用边缘节点数。");
        meter.CreateObservableGauge("visicore.upgrade.failures", ObserveUpgradeFailures, unit: "{failure}", description: "按失败码聚合的升级失败目标数。");
        meter.CreateObservableGauge("visicore.backup.results", ObserveBackupResults, unit: "{backup}", description: "按结果状态聚合的备份数。");
    }

    public PlatformTelemetrySnapshot Snapshot => new(
        Volatile.Read(ref activeStreamSessions),
        Volatile.Read(ref onlineEdgeAgents),
        Volatile.Read(ref staleEdgeAgents),
        upgradeFailures.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
        backupResults.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
        collectedAt);

    public Activity? StartActivity(string name) => activitySource.StartActivity(name, ActivityKind.Internal);

    public void RecordStreamSessionIssued(string operation) => streamSessionsIssued.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void RecordEdgeHeartbeat(string platform) => edgeHeartbeats.Add(1, new KeyValuePair<string, object?>("platform", platform));

    public void Update(PlatformTelemetrySnapshot snapshot)
    {
        Interlocked.Exchange(ref activeStreamSessions, snapshot.ActiveStreamSessions);
        Interlocked.Exchange(ref onlineEdgeAgents, snapshot.OnlineEdgeAgents);
        Interlocked.Exchange(ref staleEdgeAgents, snapshot.StaleEdgeAgents);
        upgradeFailures = snapshot.UpgradeFailures.OrderBy(item => item.Key, StringComparer.Ordinal).ToImmutableArray();
        backupResults = snapshot.BackupResults.OrderBy(item => item.Key, StringComparer.Ordinal).ToImmutableArray();
        collectedAt = snapshot.CollectedAt;
    }

    public void Dispose()
    {
        activitySource.Dispose();
        meter.Dispose();
    }

    private IEnumerable<Measurement<long>> ObserveUpgradeFailures() =>
        upgradeFailures.Select(item => new Measurement<long>(item.Value, new KeyValuePair<string, object?>("failure.code", item.Key)));

    private IEnumerable<Measurement<long>> ObserveBackupResults() =>
        backupResults.Select(item => new Measurement<long>(item.Value, new KeyValuePair<string, object?>("result", item.Key)));
}

public sealed record PlatformTelemetrySnapshot(
    long ActiveStreamSessions,
    long OnlineEdgeAgents,
    long StaleEdgeAgents,
    IReadOnlyDictionary<string, long> UpgradeFailures,
    IReadOnlyDictionary<string, long> BackupResults,
    DateTimeOffset CollectedAt);
