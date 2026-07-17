using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class DeviceWorkerAccessService(
    PlatformDbContext dbContext,
    ILogger<DeviceWorkerAccessService> logger)
{
    public async Task<DeviceWorkerEntity?> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var token = request.Headers["X-Device-Worker-Token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var worker = await dbContext.DeviceWorkers.SingleOrDefaultAsync(
            item => item.TokenHash == tokenHash && item.DisabledAt == null,
            cancellationToken);
        if (worker is null)
        {
            return null;
        }

        worker.LastSeenAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return worker;
    }

    public Task<bool> IsAssignedAsync(Guid workerId, Guid recorderId, CancellationToken cancellationToken) =>
        dbContext.DeviceWorkerAssignments.AnyAsync(
            item => item.WorkerId == workerId && item.RecorderId == recorderId,
            cancellationToken);

    public async Task<IReadOnlyList<WorkerRecorderAssignment>> GetAssignmentsAsync(Guid workerId, CancellationToken cancellationToken)
    {
        var assignments = await dbContext.DeviceWorkerAssignments.AsNoTracking()
            .Where(item => item.WorkerId == workerId)
            .ToListAsync(cancellationToken);
        if (assignments.Count == 0)
        {
            return [];
        }

        var recorderIds = assignments.Select(item => item.RecorderId).ToList();
        var recorders = await dbContext.Recorders.AsNoTracking().Where(item => recorderIds.Contains(item.Id)).ToListAsync(cancellationToken);
        var pluginIds = recorders.Where(item => item.DevicePluginId != null).Select(item => item.DevicePluginId!.Value).Distinct().ToList();
        var pluginsById = await dbContext.DevicePlugins.AsNoTracking().Where(item => pluginIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var endpoints = await dbContext.RecorderEndpoints.AsNoTracking().Where(item => recorderIds.Contains(item.RecorderId)).ToListAsync(cancellationToken);
        var cameras = await dbContext.Cameras.AsNoTracking().Where(item => recorderIds.Contains(item.RecorderId)).ToListAsync(cancellationToken);
        var credentialNames = endpoints.Select(item => item.CredentialReference).Distinct().ToList();
        // 旧 Device Worker 只能接收其本机 DPAPI 密文。Linux Agent 信封必须通过独立控制面拉取，不能降级下发到此接口。
        var credentials = await dbContext.DeviceCredentials.AsNoTracking()
            .Where(item => credentialNames.Contains(item.Name) && item.ProtectionMode == DeviceCredentialProtectionMode.WindowsDpapiLocalMachine && item.DisabledAt == null)
            .ToListAsync(cancellationToken);
        var credentialsByName = credentials.ToDictionary(item => item.Name, StringComparer.Ordinal);

        var result = new List<WorkerRecorderAssignment>();
        foreach (var assignment in assignments)
        {
            var recorder = recorders.Single(item => item.Id == assignment.RecorderId);
            var plugin = recorder.DevicePluginId is { } pluginId ? pluginsById.GetValueOrDefault(pluginId) : null;
            var assignedEndpoints = endpoints.Where(item => item.RecorderId == recorder.Id).ToList();
            var missingCredential = assignedEndpoints.Select(item => item.CredentialReference).FirstOrDefault(name => !credentialsByName.ContainsKey(name));
            if (missingCredential is not null)
            {
                var registeredReferences = string.Join(",", credentialsByName.Keys.OrderBy(item => item, StringComparer.Ordinal));
                logger.LogWarning(
                    "设备 Worker {WorkerId} 的录像机 {RecorderId} 缺少凭据引用 {CredentialReference}，已跳过该录像机分配。当前可用引用：{RegisteredReferences}。",
                    workerId,
                    recorder.Id,
                    missingCredential,
                    registeredReferences);
                continue;
            }

            result.Add(new WorkerRecorderAssignment(
                recorder.Id, assignment.DefaultRegionId, recorder.Code, recorder.Name, recorder.Vendor, recorder.AdapterType,
                recorder.TimeZoneId,
                assignedEndpoints.Select(item => new WorkerRecorderEndpoint(item.Protocol.ToString(), item.Host, item.Port, item.UseTls, item.CredentialReference, item.CertificateThumbprint)).ToList(),
                assignedEndpoints.Select(item => credentialsByName[item.CredentialReference]).DistinctBy(item => item.Name)
                    .Select(item => new WorkerProtectedCredential(item.Name, item.ProtectionMode.ToString(), Convert.ToBase64String(item.Ciphertext), item.KeyVersion)).ToList(),
                cameras.Where(item => item.RecorderId == recorder.Id)
                    .Select(item => new WorkerCameraRoute(item.Id, item.InputChannelNumber, item.StreamingChannelMap, item.Alias, item.SupportsPtz)).ToList(),
                recorder.DeviceKind,
                plugin?.Key,
                plugin?.RuntimeType));
        }
        return result;
    }
}

public sealed class DeviceWorkerSyncService(PlatformDbContext dbContext)
{
    public async Task ApplyInventoryAsync(WorkerInventoryReport report, CancellationToken cancellationToken)
    {
        var recorder = await dbContext.Recorders.SingleOrDefaultAsync(item => item.Id == report.RecorderId, cancellationToken)
            ?? throw new InvalidOperationException("录像机不存在。 ");
        var assignment = await dbContext.DeviceWorkerAssignments.SingleOrDefaultAsync(item => item.RecorderId == report.RecorderId, cancellationToken)
            ?? throw new InvalidOperationException("录像机尚未分配给设备 Worker。 ");
        var capability = await dbContext.RecorderCapabilities.SingleOrDefaultAsync(
            item => item.RecorderId == report.RecorderId && item.Version == report.Capabilities.Version,
            cancellationToken);
        if (capability is null)
        {
            dbContext.RecorderCapabilities.Add(new RecorderCapabilityEntity
            {
                Id = Guid.NewGuid(), RecorderId = report.RecorderId, Version = report.Capabilities.Version,
                CapabilityJson = JsonSerializer.Serialize(report.Capabilities), VerifiedAt = report.ObservedAt
            });
        }
        else
        {
            capability.CapabilityJson = JsonSerializer.Serialize(report.Capabilities);
            capability.VerifiedAt = report.ObservedAt;
        }

        var existingByChannel = await dbContext.Cameras.Where(item => item.RecorderId == report.RecorderId)
            .ToDictionaryAsync(item => item.InputChannelNumber, cancellationToken);
        var created = 0;
        var updated = 0;
        foreach (var cameraReport in report.Cameras)
        {
            if (cameraReport.ChannelNumber <= 0)
            {
                continue;
            }
            if (existingByChannel.TryGetValue(cameraReport.ChannelNumber, out var existing))
            {
                if (!existing.ProvisioningMode.Equals(CameraProvisioningModes.Manual, StringComparison.OrdinalIgnoreCase))
                {
                    existing.SupportsPtz = cameraReport.SupportsPtz;
                    existing.StreamingChannelMap = ValidateStreamingMap(cameraReport.StreamingChannelMap);
                }
                existing.LastVerifiedAt = report.ObservedAt;
                updated++;
                continue;
            }

            dbContext.Cameras.Add(new CameraEntity
            {
                Id = CreateCameraId(report.RecorderId, cameraReport.ChannelNumber), RecorderId = report.RecorderId,
                RegionId = assignment.DefaultRegionId, Code = $"{recorder.Code}-CH-{cameraReport.ChannelNumber:D2}",
                Alias = string.IsNullOrWhiteSpace(cameraReport.Alias) ? $"通道 {cameraReport.ChannelNumber}" : cameraReport.Alias.Trim(),
                InputChannelNumber = cameraReport.ChannelNumber, StreamingChannelMap = ValidateStreamingMap(cameraReport.StreamingChannelMap),
                SourceType = CameraSourceTypes.RecorderChannel, ProvisioningMode = CameraProvisioningModes.Discovered,
                SupportsPtz = cameraReport.SupportsPtz, Connectivity = CameraConnectivity.Unknown, LastVerifiedAt = report.ObservedAt,
                CreatedAt = report.ObservedAt
            });
            created++;
        }

        dbContext.AuditLogs.Add(new AuditLogEntity
        {
            Id = Guid.NewGuid(), Action = "camera.sync", ResourceType = "recorder", ResourceId = report.RecorderId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { discovered = report.Cameras.Count, created, updated }), OccurredAt = report.ObservedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyHealthAsync(WorkerHealthReport report, CancellationToken cancellationToken)
    {
        var recorder = await dbContext.Recorders.SingleOrDefaultAsync(item => item.Id == report.RecorderId, cancellationToken)
            ?? throw new InvalidOperationException("录像机不存在。 ");
        var cameras = await dbContext.Cameras.Where(item => item.RecorderId == report.RecorderId).ToListAsync(cancellationToken);
        if (string.Equals(recorder.DeviceKind, DeviceKinds.Camera, StringComparison.OrdinalIgnoreCase))
        {
            // 直连摄像头的 Recorder 仅作为连接载体，健康状态必须归属于实际对外展示的摄像头。
            foreach (var camera in cameras)
            {
                var isOnline = report.ChannelStates.TryGetValue(camera.InputChannelNumber, out var reportedState)
                    ? reportedState
                    : report.RecorderReachable;
                ApplyCameraObservation(camera, isOnline, report.ObservedAt);
            }
        }
        else
        {
            ApplyRecorderObservation(recorder, report.RecorderReachable, report.ObservedAt);
            if (report.RecorderReachable)
            {
                foreach (var camera in cameras)
                {
                    if (report.ChannelStates.TryGetValue(camera.InputChannelNumber, out var isOnline))
                    {
                        ApplyCameraObservation(camera, isOnline, report.ObservedAt);
                    }
                }
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyClockAsync(WorkerClockReport report, ClockMonitoringOptions settings, CancellationToken cancellationToken)
    {
        if (report.Observation is null)
        {
            return;
        }
        var observation = report.Observation;
        var roundTrip = observation.ResponseReceivedAt - observation.RequestStartedAt;
        if (roundTrip < TimeSpan.Zero || roundTrip > TimeSpan.FromSeconds(settings.MaximumRoundTripSeconds))
        {
            throw new InvalidOperationException("设备 Worker 上报的时钟测量窗口无效。 ");
        }
        var midpoint = observation.RequestStartedAt.AddTicks(roundTrip.Ticks / 2);
        var offsetMilliseconds = (observation.DeviceTime - midpoint).TotalMilliseconds;
        if (double.IsNaN(offsetMilliseconds) || double.IsInfinity(offsetMilliseconds) ||
            offsetMilliseconds < int.MinValue || offsetMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException("设备 Worker 上报的时钟偏差超出可记录范围。 ");
        }

        var recorder = await dbContext.Recorders.SingleOrDefaultAsync(item => item.Id == report.RecorderId, cancellationToken)
            ?? throw new InvalidOperationException("录像机不存在。 ");
        var offset = checked((int)Math.Round(offsetMilliseconds, MidpointRounding.AwayFromZero));
        var previous = recorder.ClockSynchronization;
        var snapshot = RecorderClockSynchronizationStateMachine.Observe(
            previous,
            recorder.ClockConsecutiveDrifts,
            recorder.ClockConsecutiveSynchronizations,
            recorder.ClockDriftSinceAt,
            Math.Abs(offset) <= settings.MaximumAbsoluteOffsetSeconds * 1000L,
            report.ObservedAt,
            settings.RequiredConsecutiveObservations);

        recorder.ClockSynchronization = snapshot.ClockSynchronization;
        recorder.ClockConsecutiveDrifts = snapshot.ConsecutiveDrifts;
        recorder.ClockConsecutiveSynchronizations = snapshot.ConsecutiveSynchronizations;
        recorder.ClockDriftSinceAt = snapshot.DriftSinceAt;
        recorder.LastClockOffsetMilliseconds = offset;
        recorder.LastClockObservedAt = report.ObservedAt;
        dbContext.RecorderClockObservations.Add(new RecorderClockObservationEntity
        {
            Id = Guid.NewGuid(),
            RecorderId = recorder.Id,
            DeviceTime = observation.DeviceTime,
            RequestStartedAt = observation.RequestStartedAt,
            ResponseReceivedAt = observation.ResponseReceivedAt,
            OffsetMilliseconds = offset,
            ClockSynchronization = snapshot.ClockSynchronization
        });
        if (previous != snapshot.ClockSynchronization &&
            (snapshot.ClockSynchronization == ClockSynchronization.Drifted || previous == ClockSynchronization.Drifted))
        {
            AddClockSynchronizationEvent(recorder, previous, snapshot.ClockSynchronization, offset, report.ObservedAt);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void ApplyRecorderObservation(RecorderEntity recorder, bool isOnline, DateTimeOffset observedAt)
    {
        var previous = recorder.Connectivity;
        var snapshot = Observe(previous, recorder.ConsecutiveFailures, recorder.ConsecutiveSuccesses, recorder.SuspectedAt, isOnline, observedAt);
        recorder.Connectivity = snapshot.Connectivity;
        recorder.ConsecutiveFailures = snapshot.ConsecutiveFailures;
        recorder.ConsecutiveSuccesses = snapshot.ConsecutiveSuccesses;
        recorder.SuspectedAt = snapshot.SuspectedAt;
        recorder.LastVerifiedAt = observedAt;
        if (previous != snapshot.Connectivity)
        {
            recorder.LastStateChangedAt = observedAt;
            AddHealthEvent("recorder", recorder.Id, previous, snapshot, isOnline, observedAt);
        }
    }

    private void ApplyCameraObservation(CameraEntity camera, bool isOnline, DateTimeOffset observedAt)
    {
        var previous = camera.Connectivity;
        var snapshot = Observe(previous, camera.ConsecutiveFailures, camera.ConsecutiveSuccesses, camera.SuspectedAt, isOnline, observedAt);
        camera.Connectivity = snapshot.Connectivity;
        camera.ConsecutiveFailures = snapshot.ConsecutiveFailures;
        camera.ConsecutiveSuccesses = snapshot.ConsecutiveSuccesses;
        camera.SuspectedAt = snapshot.SuspectedAt;
        camera.LastVerifiedAt = observedAt;
        if (previous != snapshot.Connectivity)
        {
            camera.LastStateChangedAt = observedAt;
            AddHealthEvent("camera", camera.Id, previous, snapshot, isOnline, observedAt);
        }
    }

    private static OfflineDetectionSnapshot Observe(CameraConnectivity state, int failures, int successes, DateTimeOffset? suspectedAt, bool isOnline, DateTimeOffset observedAt)
    {
        var machine = new OfflineDetectionStateMachine(current: state, consecutiveFailures: failures, consecutiveSuccesses: successes, suspectedAt: suspectedAt);
        machine.Observe(isOnline, observedAt);
        return machine.Snapshot;
    }

    private void AddHealthEvent(string resourceType, Guid resourceId, CameraConnectivity previous, OfflineDetectionSnapshot current, bool observedOnline, DateTimeOffset observedAt)
    {
        var detailsJson = JsonSerializer.Serialize(new { observedOnline, current.ConsecutiveFailures, current.ConsecutiveSuccesses });
        dbContext.HealthStateEvents.Add(new HealthStateEventEntity
        {
            Id = Guid.NewGuid(), ResourceType = resourceType, ResourceId = resourceId, PreviousConnectivity = previous,
            CurrentConnectivity = current.Connectivity,
            DetailsJson = detailsJson, OccurredAt = observedAt
        });
        dbContext.OutboxEvents.Add(new OutboxEventEntity
        {
            Id = Guid.NewGuid(), EventType = "health.state.changed", AggregateType = resourceType, AggregateId = resourceId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                resourceType,
                resourceId,
                previousConnectivity = previous,
                currentConnectivity = current.Connectivity,
                observedAt
            }),
            OccurredAt = observedAt,
            NextAttemptAt = observedAt
        });
    }

    private void AddClockSynchronizationEvent(
        RecorderEntity recorder,
        ClockSynchronization previous,
        ClockSynchronization current,
        int offsetMilliseconds,
        DateTimeOffset observedAt)
    {
        dbContext.OutboxEvents.Add(new OutboxEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "clock.synchronization.changed",
            AggregateType = "recorder",
            AggregateId = recorder.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                resourceType = "recorder",
                resourceId = recorder.Id,
                previousSynchronization = previous,
                currentSynchronization = current,
                offsetMilliseconds,
                observedAt
            }),
            OccurredAt = observedAt,
            NextAttemptAt = observedAt
        });
    }

    private static string ValidateStreamingMap(string streamingChannelMap)
    {
        if (string.IsNullOrWhiteSpace(streamingChannelMap))
        {
            throw new InvalidOperationException("设备 Worker 未上报码流映射。");
        }
        using var document = JsonDocument.Parse(streamingChannelMap);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("main", out _) ||
            !document.RootElement.TryGetProperty("sub", out _))
        {
            throw new InvalidOperationException("设备 Worker 上报的码流映射必须包含 main 和 sub。");
        }
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static Guid CreateCameraId(Guid recorderId, int channelNumber)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{recorderId:N}:{channelNumber}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
