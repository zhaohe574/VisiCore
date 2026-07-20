using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public interface IOnvifPlaybackRelayManager
{
    Task<OnvifPlaybackRelayStartResult> StartAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        WorkerEdgeCommand command,
        PlaybackRelayStartCommandPayload payload,
        CancellationToken cancellationToken);

    Task<bool> StopAsync(Guid playbackSessionId, CancellationToken cancellationToken);

    Task<OnvifPlaybackRelayTransportResult> ControlAsync(
        PlaybackRelayControlCommandPayload payload,
        CancellationToken cancellationToken);
}

public sealed class OnvifPlaybackRelayManager(
    IOnvifProfileGClient profileGClient,
    IOnvifFfmpegPlaybackRelay ffmpegRelay,
    IOnvifPlaybackRelayAuthorization authorization,
    OnvifEdgeOptions options,
    ILogger<OnvifPlaybackRelayManager> logger) : IOnvifPlaybackRelayManager, IAsyncDisposable
{
    private static readonly TimeSpan RevocationTombstoneLifetime = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<Guid, RelayEntry> entries = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> revokedSessions = new();
    private readonly SemaphoreSlim capacity = new(Math.Max(1, options.PlaybackRelay.MaxConcurrentRelays));
    private int disposed;

    public async Task<OnvifPlaybackRelayStartResult> StartAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        WorkerEdgeCommand command,
        PlaybackRelayStartCommandPayload payload,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        options.PlaybackRelay.ValidateEnabled();
        ValidatePayload(payload, camera.CameraId);
        if (!options.PlaybackRelay.IsRecorderExplicitlyValidated(assignment.RecorderId))
        {
            throw new NotSupportedException("当前录像机尚未完成 ONVIF 回放中继验收。 ");
        }
        if (!await authorization.CanStartAsync(command, cancellationToken))
        {
            throw new InvalidOperationException("回放会话已撤销或启动命令已失效。 ");
        }

        PruneRevocationTombstones(DateTimeOffset.UtcNow);
        if (revokedSessions.ContainsKey(payload.PlaybackSessionId))
        {
            throw new InvalidOperationException("回放会话已撤销，拒绝启动中继。 ");
        }
        if (entries.TryGetValue(payload.PlaybackSessionId, out var current))
        {
            return current.ToResult();
        }

        await capacity.WaitAsync(cancellationToken);
        var entry = new RelayEntry(payload.PlaybackSessionId, payload.CameraId);
        if (!entries.TryAdd(payload.PlaybackSessionId, entry))
        {
            capacity.Release();
            if (entries.TryGetValue(payload.PlaybackSessionId, out current))
            {
                return current.ToResult();
            }
            return await StartAsync(assignment, camera, command, payload, cancellationToken);
        }

        IOnvifPlaybackRelayProcess? process = null;
        var entryOwnsProcess = false;
        try
        {
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Cancellation.Token);
            entry.ThrowIfStopRequested();
            await ffmpegRelay.EnsureRtspClockRangeSupportedAsync(startupCancellation.Token);
            entry.ThrowIfStopRequested();
            var source = await profileGClient.GetReplaySourceAsync(assignment, camera, startupCancellation.Token);
            entry.ThrowIfStopRequested();
            process = ffmpegRelay.Start(payload.PlaybackSessionId, source, payload.StartedAt, payload.EndedAt);
            entry.Initialize(process, source, payload.StartedAt, payload.EndedAt, options.PlaybackRelay.MaxRelayLifetimeSeconds);
            process = null;
            entry.RunTask = RunAsync(entry);
            entryOwnsProcess = true;
            entry.CompleteStartDecision(started: true);
            await Task.Delay(options.PlaybackRelay.StartupProbeMilliseconds, startupCancellation.Token);
            if (entry.Process is null || entry.Process.HasExited || entry.RunTask.IsCompleted)
            {
                throw new InvalidOperationException("FFmpeg ONVIF 回放中继在启动探测窗口内结束。 ");
            }
            logger.LogInformation("已启动 ONVIF 回放中继：会话 {PlaybackSessionId}，摄像头 {CameraId}。", payload.PlaybackSessionId, payload.CameraId);
            return entry.ToResult();
        }
        catch
        {
            entries.TryRemove(payload.PlaybackSessionId, out _);
            try
            {
                if (entryOwnsProcess)
                {
                    entry.Stop();
                    try
                    {
                        await entry.RunTask;
                    }
                    catch
                    {
                        // 启动失败时以原始失败原因返回给可靠命令通道。
                    }
                }
                else
                {
                    if (process is not null)
                    {
                        await process.DisposeAsync();
                    }
                    capacity.Release();
                }
            }
            finally
            {
                entry.CompleteStartDecision(started: false);
            }
            throw;
        }
    }

    public async Task<bool> StopAsync(Guid playbackSessionId, CancellationToken cancellationToken)
    {
        PruneRevocationTombstones(DateTimeOffset.UtcNow);
        revokedSessions[playbackSessionId] = DateTimeOffset.UtcNow.Add(RevocationTombstoneLifetime);
        if (!entries.TryGetValue(playbackSessionId, out var entry))
        {
            return false;
        }
        entry.Stop();
        if (await entry.WaitForStartDecisionAsync(cancellationToken))
        {
            await entry.RunTask.WaitAsync(cancellationToken);
        }
        return true;
    }

    public async Task<OnvifPlaybackRelayTransportResult> ControlAsync(
        PlaybackRelayControlCommandPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.PlaybackSessionId == Guid.Empty || payload.CameraId == Guid.Empty || payload.ClientRequestId == Guid.Empty)
        {
            throw new InvalidOperationException("ONVIF 回放控制命令缺少必要参数。 ");
        }
        if (!entries.TryGetValue(payload.PlaybackSessionId, out var entry) || entry.CameraId != payload.CameraId)
        {
            throw new InvalidOperationException("ONVIF 回放会话已结束或不属于指定摄像头。 ");
        }
        await entry.ControlGate.WaitAsync(cancellationToken);
        try
        {
            if (entry.TryGetControlResult(payload.ClientRequestId, out var cachedResult))
            {
                return cachedResult;
            }
            entry.ThrowIfStopRequested();
            ValidateControlPayload(payload, entry);
            switch (payload.Action)
            {
                case PlaybackTransportAction.Pause:
                    await entry.PauseAsync();
                    break;
                case PlaybackTransportAction.Resume:
                    await entry.ResumeAsync(ffmpegRelay);
                    break;
                case PlaybackTransportAction.Seek:
                    await entry.SeekAsync(ffmpegRelay, payload.Position!.Value);
                    break;
                case PlaybackTransportAction.SetSpeed:
                    await entry.SetSpeedAsync(ffmpegRelay, payload.Speed!.Value);
                    break;
                default:
                    throw new InvalidOperationException("ONVIF 回放控制动作无效。 ");
            }
            if (!entry.IsPaused)
            {
                await entry.EnsureCurrentProcessStartedAsync(options.PlaybackRelay.StartupProbeMilliseconds, cancellationToken);
            }
            var result = entry.ToTransportResult();
            entry.RememberControlResult(payload.ClientRequestId, result);
            return result;
        }
        finally
        {
            entry.ControlGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        var tasks = entries.Values.Select(async entry =>
        {
            entry.Stop();
            try
            {
                await entry.RunTask;
            }
            catch
            {
                // 服务停止时仅确保每个中继都已获得停止信号。
            }
        });
        await Task.WhenAll(tasks);
        capacity.Dispose();
    }

    private async Task RunAsync(RelayEntry entry)
    {
        try
        {
            while (true)
            {
                var exitTask = entry.WaitForCurrentProcessExitAsync(entry.Cancellation.Token);
                var authorizationDelay = Task.Delay(
                    TimeSpan.FromSeconds(options.PlaybackRelay.AuthorizationCheckSeconds),
                    entry.Cancellation.Token);
                if (await Task.WhenAny(exitTask, authorizationDelay) == exitTask)
                {
                    await exitTask;
                    if (entry.IsPaused || entry.HasProcessChanged())
                    {
                        continue;
                    }
                    logger.LogInformation("ONVIF 回放中继已自然结束：会话 {PlaybackSessionId}。", entry.PlaybackSessionId);
                    return;
                }

                if (!await authorization.CanContinueAsync(entry.PlaybackSessionId, entry.Cancellation.Token))
                {
                    logger.LogInformation("ONVIF 回放中继授权已失效，停止会话 {PlaybackSessionId}。", entry.PlaybackSessionId);
                    entry.Stop();
                }
            }
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            logger.LogInformation("ONVIF 回放中继已停止：会话 {PlaybackSessionId}。", entry.PlaybackSessionId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ONVIF 回放中继异常结束：会话 {PlaybackSessionId}。", entry.PlaybackSessionId);
        }
        finally
        {
            entries.TryRemove(entry.PlaybackSessionId, out _);
            await entry.DisposeProcessAsync();
            entry.ControlGate.Dispose();
            entry.Cancellation.Dispose();
            capacity.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    private void PruneRevocationTombstones(DateTimeOffset now)
    {
        foreach (var pair in revokedSessions)
        {
            if (pair.Value <= now)
            {
                revokedSessions.TryRemove(pair.Key, out _);
            }
        }
    }

    private static void ValidatePayload(PlaybackRelayStartCommandPayload payload, Guid expectedCameraId)
    {
        if (payload.PlaybackSessionId == Guid.Empty || payload.CameraId != expectedCameraId ||
            payload.StartedAt >= payload.EndedAt || payload.EndedAt - payload.StartedAt > TimeSpan.FromDays(31))
        {
            throw new InvalidOperationException("ONVIF 回放中继命令参数无效。 ");
        }
    }

    private static void ValidateControlPayload(PlaybackRelayControlCommandPayload payload, RelayEntry entry)
    {
        if ((payload.Action == PlaybackTransportAction.Seek &&
             (payload.Position is null || payload.Position < entry.StartedAt || payload.Position > entry.EndedAt)) ||
            (payload.Action == PlaybackTransportAction.SetSpeed && payload.Speed is not (0.5 or 1.0 or 2.0 or 4.0)) ||
            (payload.Action is PlaybackTransportAction.Pause or PlaybackTransportAction.Resume &&
             (payload.Position is not null || payload.Speed is not null)))
        {
            throw new InvalidOperationException("ONVIF 回放控制参数无效。 ");
        }
    }

    private sealed class RelayEntry(Guid playbackSessionId, Guid cameraId)
    {
        public Guid PlaybackSessionId { get; } = playbackSessionId;
        public Guid CameraId { get; } = cameraId;
        public CancellationTokenSource Cancellation { get; } = new();
        private readonly TaskCompletionSource<bool> startDecision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IOnvifPlaybackRelayProcess? Process { get; private set; }
        public Task RunTask { get; set; } = Task.CompletedTask;
        public SemaphoreSlim ControlGate { get; } = new(1, 1);
        public OnvifProfileGReplaySource Source { get; private set; } = null!;
        public DateTimeOffset StartedAt { get; private set; }
        public DateTimeOffset EndedAt { get; private set; }
        public DateTimeOffset Position { get; private set; }
        public double Speed { get; private set; } = 1.0;
        public bool IsPaused { get; private set; }
        private DateTimeOffset positionUpdatedAt;
        private IOnvifPlaybackRelayProcess? observedProcess;
        private readonly Dictionary<Guid, OnvifPlaybackRelayTransportResult> completedControls = new();

        public void Initialize(
            IOnvifPlaybackRelayProcess process,
            OnvifProfileGReplaySource source,
            DateTimeOffset startedAt,
            DateTimeOffset endedAt,
            int maxRelayLifetimeSeconds)
        {
            Process = process;
            Source = source;
            StartedAt = startedAt;
            EndedAt = endedAt;
            Position = startedAt;
            positionUpdatedAt = DateTimeOffset.UtcNow;
            Cancellation.CancelAfter(TimeSpan.FromSeconds(maxRelayLifetimeSeconds));
        }

        public async Task PauseAsync()
        {
            Position = CurrentPosition(DateTimeOffset.UtcNow);
            positionUpdatedAt = DateTimeOffset.UtcNow;
            IsPaused = true;
            await DisposeProcessAsync();
        }

        public Task ResumeAsync(IOnvifFfmpegPlaybackRelay relay)
        {
            if (!IsPaused && Process is not null)
            {
                return Task.CompletedTask;
            }
            IsPaused = false;
            positionUpdatedAt = DateTimeOffset.UtcNow;
            StartProcess(relay);
            return Task.CompletedTask;
        }

        public async Task SeekAsync(IOnvifFfmpegPlaybackRelay relay, DateTimeOffset position)
        {
            Position = position;
            positionUpdatedAt = DateTimeOffset.UtcNow;
            if (!IsPaused)
            {
                await DisposeProcessAsync();
                StartProcess(relay);
            }
        }

        public async Task SetSpeedAsync(IOnvifFfmpegPlaybackRelay relay, double speed)
        {
            Position = CurrentPosition(DateTimeOffset.UtcNow);
            Speed = speed;
            positionUpdatedAt = DateTimeOffset.UtcNow;
            if (!IsPaused)
            {
                await DisposeProcessAsync();
                StartProcess(relay);
            }
        }

        public Task WaitForCurrentProcessExitAsync(CancellationToken cancellationToken)
        {
            var process = Process;
            observedProcess = process;
            return process is null
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : process.WaitForExitAsync(cancellationToken);
        }

        public bool HasProcessChanged() => !ReferenceEquals(observedProcess, Process);

        public async Task DisposeProcessAsync()
        {
            var process = Process;
            Process = null;
            if (process is not null)
            {
                await process.DisposeAsync();
            }
        }

        public async Task EnsureCurrentProcessStartedAsync(int probeMilliseconds, CancellationToken cancellationToken)
        {
            var process = Process ?? throw new InvalidOperationException("ONVIF 回放控制后未创建中继进程。 ");
            await Task.Delay(probeMilliseconds, cancellationToken);
            if (!ReferenceEquals(process, Process) || process.HasExited)
            {
                throw new InvalidOperationException("FFmpeg ONVIF 回放中继在控制后启动探测窗口内结束。 ");
            }
        }

        public OnvifPlaybackRelayTransportResult ToTransportResult() => new(
            PlaybackSessionId,
            CameraId,
            IsPaused,
            CurrentPosition(DateTimeOffset.UtcNow),
            Speed,
            true,
            true,
            true,
            null);

        public bool TryGetControlResult(Guid requestId, out OnvifPlaybackRelayTransportResult result) =>
            completedControls.TryGetValue(requestId, out result!);

        public void RememberControlResult(Guid requestId, OnvifPlaybackRelayTransportResult result) =>
            completedControls[requestId] = result;

        private DateTimeOffset CurrentPosition(DateTimeOffset now)
        {
            if (IsPaused)
            {
                return Position;
            }
            var current = Position.AddSeconds(Math.Max(0, (now - positionUpdatedAt).TotalSeconds) * Speed);
            return current > EndedAt ? EndedAt : current;
        }

        private void StartProcess(IOnvifFfmpegPlaybackRelay relay)
        {
            ThrowIfStopRequested();
            Process = relay.Start(PlaybackSessionId, Source, Position, EndedAt, Speed);
        }

        public void Stop() => Cancellation.Cancel();

        public bool IsStopRequested => Cancellation.IsCancellationRequested;

        public void ThrowIfStopRequested()
        {
            if (IsStopRequested)
            {
                throw new OperationCanceledException("回放中继在启动前已收到停止命令。", Cancellation.Token);
            }
        }

        public void CompleteStartDecision(bool started) => startDecision.TrySetResult(started);

        public Task<bool> WaitForStartDecisionAsync(CancellationToken cancellationToken) =>
            startDecision.Task.WaitAsync(cancellationToken);

        public OnvifPlaybackRelayStartResult ToResult() => new(
            PlaybackSessionId,
            CameraId,
            $"playback/{PlaybackSessionId:N}");
    }
}

public sealed record OnvifPlaybackRelayStartResult(Guid PlaybackSessionId, Guid CameraId, string PathName);
public sealed record OnvifPlaybackRelayTransportResult(
    Guid PlaybackSessionId,
    Guid CameraId,
    bool IsPaused,
    DateTimeOffset Position,
    double Speed,
    bool CanPause,
    bool CanSeek,
    bool CanChangeSpeed,
    string? Detail);
