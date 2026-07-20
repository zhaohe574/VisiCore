using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace VisiCore.StreamGateway;

public interface IStreamSessionLifecycle
{
    void Attach(Guid sessionId, string pathName);
    void Detach(Guid sessionId);
}

public interface ILiveTranscodeRelayManager : IStreamSessionLifecycle
{
    Task SynchronizeRoutesAsync(
        IReadOnlyCollection<LiveTranscodeRoute> routes,
        bool completeSnapshot,
        CancellationToken cancellationToken);
    Task EnsureReadyAsync(string pathName, CancellationToken cancellationToken);
    bool AuthorizePublisher(MediaMtxAuthRequest request);
    bool AuthorizeReader(MediaMtxAuthRequest request);
    LiveTranscodeRuntimeSnapshot Snapshot();
}

public sealed record LiveTranscodeRoute(
    string PublicPath,
    string InputPath,
    Uri InputUri,
    Uri OutputUri,
    string Fingerprint);

public sealed record LiveTranscodePublisherCredential(string Username, string Password);

public sealed record LiveTranscodeRuntimeSnapshot(
    int ConfiguredRoutes,
    int ActiveRelays,
    int AttachedSessions);

public sealed class LiveTranscodeRelayManager(
    LiveTranscodeOptions options,
    ILiveTranscodeProcessFactory processFactory,
    IMediaMtxClient mediaMtxClient,
    ILogger<LiveTranscodeRelayManager> logger)
    : BackgroundService, ILiveTranscodeRelayManager, IAsyncDisposable
{
    private readonly object stateGate = new();
    private readonly Dictionary<string, RelayEntry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> sessionPaths = [];
    private readonly SemaphoreSlim capacity = new(Math.Max(1, options.MaxConcurrentRelays));
    private readonly SemaphoreSlim cleanupGate = new(1, 1);
    private int stopping;
    private int disposed;

    public async Task SynchronizeRoutesAsync(
        IReadOnlyCollection<LiveTranscodeRoute> routes,
        bool completeSnapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Enabled || IsStopping)
        {
            return;
        }

        var desired = routes.ToDictionary(item => item.PublicPath, StringComparer.Ordinal);
        foreach (var route in desired.Values)
        {
            ValidateRoute(route);
        }

        var retiring = new List<RetiringEntry>();
        lock (stateGate)
        {
            foreach (var route in desired.Values)
            {
                if (entries.TryGetValue(route.PublicPath, out var current) &&
                    current.Route.Fingerprint == route.Fingerprint)
                {
                    continue;
                }

                var retirementCompleted = current is null
                    ? null
                    : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var replacement = new RelayEntry(route, retirementCompleted?.Task ?? Task.CompletedTask);
                foreach (var session in sessionPaths.Where(item => item.Value == route.PublicPath))
                {
                    replacement.Attach(session.Key);
                }
                entries[route.PublicPath] = replacement;
                if (current is not null)
                {
                    current.Retire();
                    retiring.Add(new RetiringEntry(current, retirementCompleted));
                }
            }

            if (completeSnapshot)
            {
                foreach (var pathName in entries.Keys.Except(desired.Keys, StringComparer.Ordinal).ToList())
                {
                    var stale = entries[pathName];
                    entries.Remove(pathName);
                    stale.Retire();
                    retiring.Add(new RetiringEntry(stale, null));
                }
            }
        }

        foreach (var retirement in retiring)
        {
            try
            {
                await RetireEntryAsync(retirement.Entry);
            }
            finally
            {
                retirement.Completion?.TrySetResult();
            }
        }
    }

    public void Attach(Guid sessionId, string pathName)
    {
        if (!options.Enabled || IsStopping || sessionId == Guid.Empty || string.IsNullOrWhiteSpace(pathName))
        {
            return;
        }

        lock (stateGate)
        {
            if (sessionPaths.TryGetValue(sessionId, out var previousPath))
            {
                if (previousPath == pathName)
                {
                    return;
                }
                if (entries.TryGetValue(previousPath, out var previousEntry))
                {
                    previousEntry.Detach(sessionId);
                }
            }

            sessionPaths[sessionId] = pathName;
            if (entries.TryGetValue(pathName, out var entry))
            {
                entry.Attach(sessionId);
            }
        }
    }

    public void Detach(Guid sessionId)
    {
        lock (stateGate)
        {
            if (!sessionPaths.Remove(sessionId, out var pathName))
            {
                return;
            }
            if (entries.TryGetValue(pathName, out var entry))
            {
                entry.Detach(sessionId);
            }
        }
    }

    public async Task EnsureReadyAsync(string pathName, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return;
        }
        if (IsStopping)
        {
            throw new LiveTranscodeUnavailableException("主码流转码服务正在停止。");
        }

        RelayEntry? entry;
        lock (stateGate)
        {
            entries.TryGetValue(pathName, out entry);
        }
        if (entry is null)
        {
            return;
        }

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            if (entry.IsRetired || !entry.HasSessions || IsStopping)
            {
                throw new LiveTranscodeUnavailableException("当前主码流观看会话已经结束。");
            }
            await entry.RetirementBarrier.WaitAsync(cancellationToken);
            if (entry.IsRetired || !entry.HasSessions || IsStopping)
            {
                throw new LiveTranscodeUnavailableException("当前主码流观看会话已经结束。");
            }

            if (entry.Process is not null && entry.Process.HasExited)
            {
                await StopProcessAsync(entry, failed: true);
            }
            if (entry.Process is not null && entry.IsReady)
            {
                return;
            }

            if (entry.Process is null)
            {
                if (DateTimeOffset.UtcNow < entry.RestartNotBefore)
                {
                    throw new LiveTranscodeUnavailableException("主码流转码正在等待重试。");
                }

                await ResetStalePublisherAsync(entry, cancellationToken);
                var capacityAcquired = capacity.Wait(0);
                if (!capacityAcquired)
                {
                    await TryReclaimIdleRelayAsync(entry);
                    capacityAcquired = capacity.Wait(0);
                }
                if (!capacityAcquired)
                {
                    throw new LiveTranscodeUnavailableException("主码流转码并发容量已满。");
                }

                entry.CapacityHeld = true;
                var credential = new LiveTranscodePublisherCredential(
                    "live-relay",
                    Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                entry.EnablePublisher(credential);
                try
                {
                    entry.Process = processFactory.Start(entry.Route, credential);
                    entry.MarkStarted(DateTimeOffset.UtcNow);
                    logger.LogInformation("已启动实时主码流兼容中继：{PathName}。", entry.Route.PublicPath);
                }
                catch (Exception exception)
                {
                    entry.DisablePublisher();
                    entry.Process = null;
                    ReleaseCapacity(entry);
                    entry.RestartNotBefore = DateTimeOffset.UtcNow.AddSeconds(options.RestartBackoffSeconds);
                    throw exception is LiveTranscodeUnavailableException
                        ? exception
                        : new LiveTranscodeUnavailableException("无法启动实时主码流兼容中继。", exception);
                }
            }

            await WaitUntilReadyAsync(entry, cancellationToken);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public bool AuthorizePublisher(MediaMtxAuthRequest request)
    {
        if (!options.Enabled ||
            !string.Equals(request.Action, "publish", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Protocol, "rtsp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Path) ||
            !IsLoopback(request.Ip))
        {
            return false;
        }

        RelayEntry? entry;
        lock (stateGate)
        {
            entries.TryGetValue(request.Path, out entry);
        }
        return entry is not null && entry.AuthorizeCredential(request.User, request.Password);
    }

    public bool AuthorizeReader(MediaMtxAuthRequest request)
    {
        if (!options.Enabled ||
            !string.Equals(request.Action, "read", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Protocol, "rtsp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Path) ||
            !IsLoopback(request.Ip))
        {
            return false;
        }

        RelayEntry? entry;
        lock (stateGate)
        {
            entry = entries.Values.SingleOrDefault(item => item.Route.InputPath == request.Path);
        }
        return entry is not null && entry.AuthorizeCredential(request.User, request.Password);
    }

    public LiveTranscodeRuntimeSnapshot Snapshot()
    {
        lock (stateGate)
        {
            var configuredPaths = entries.Keys.ToHashSet(StringComparer.Ordinal);
            return new LiveTranscodeRuntimeSnapshot(
                entries.Count,
                entries.Values.Count(item => item.IsRunning),
                sessionPaths.Count(item => configuredPaths.Contains(item.Value)));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref stopping, 1);
        await base.StopAsync(cancellationToken);
        await StopAllAsync();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
                await InspectEntriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "实时主码流中继巡检失败，下个周期继续重试。");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        Interlocked.Exchange(ref stopping, 1);
        base.Dispose();
        await StopAllAsync();
        capacity.Dispose();
        cleanupGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            base.Dispose();
            return;
        }
        Interlocked.Exchange(ref stopping, 1);
        base.Dispose();
        try
        {
            Task.Run(StopAllAsync).GetAwaiter().GetResult();
        }
        finally
        {
            capacity.Dispose();
            cleanupGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private bool IsStopping => Volatile.Read(ref stopping) != 0;

    private async Task ResetStalePublisherAsync(RelayEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            if (!await mediaMtxClient.IsPathReadyAsync(entry.Route.PublicPath, cancellationToken))
            {
                return;
            }
            entry.DisablePublisher();
            await mediaMtxClient.RemovePathAsync(entry.Route.PublicPath, cancellationToken);
            await mediaMtxClient.ApplyPublisherPathAsync(entry.Route.PublicPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new LiveTranscodeUnavailableException("无法重置遗留的主码流发布路径。", exception);
        }
    }

    private async Task WaitUntilReadyAsync(RelayEntry entry, CancellationToken cancellationToken)
    {
        var deadline = entry.StartedAt.AddSeconds(options.StartupTimeoutSeconds);
        Exception? lastControlPlaneException = null;
        while (true)
        {
            if (!entry.HasSessions || entry.IsRetired || IsStopping)
            {
                await StopProcessAsync(entry, failed: false);
                throw new LiveTranscodeUnavailableException("主码流观看会话在启动期间已经结束。");
            }
            if (entry.Process is null || entry.Process.HasExited)
            {
                await StopProcessAsync(entry, failed: true);
                throw new LiveTranscodeUnavailableException("FFmpeg 主码流中继在就绪前退出。");
            }

            try
            {
                if (await mediaMtxClient.IsPathReadyAsync(entry.Route.PublicPath, cancellationToken))
                {
                    entry.MarkReady();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastControlPlaneException = exception;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                await StopProcessAsync(entry, failed: true);
                throw new LiveTranscodeUnavailableException(
                    "FFmpeg 主码流中继在限定时间内未就绪。",
                    lastControlPlaneException);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private async Task TryReclaimIdleRelayAsync(RelayEntry requestedEntry)
    {
        RelayEntry[] candidates;
        lock (stateGate)
        {
            candidates = entries.Values
                .Where(item => !ReferenceEquals(item, requestedEntry) && item.IsRunning && !item.HasSessions)
                .ToArray();
        }

        foreach (var candidate in candidates)
        {
            if (!candidate.Gate.Wait(0))
            {
                continue;
            }
            try
            {
                if (!candidate.HasSessions)
                {
                    await StopProcessAsync(candidate, failed: false);
                    return;
                }
            }
            finally
            {
                candidate.Gate.Release();
            }
        }
    }

    private async Task InspectEntriesAsync(CancellationToken cancellationToken)
    {
        RelayEntry[] snapshot;
        lock (stateGate)
        {
            snapshot = entries.Values.ToArray();
        }

        foreach (var entry in snapshot)
        {
            try
            {
                await entry.Gate.WaitAsync(cancellationToken);
                try
                {
                    if (entry.Process is not null && entry.Process.HasExited)
                    {
                        await StopProcessAsync(entry, failed: entry.HasSessions);
                    }
                    else if (entry.IsStartupExpired(options.StartupTimeoutSeconds))
                    {
                        await StopProcessAsync(entry, failed: true);
                    }
                    else if (entry.ShouldProbeHealth(TimeSpan.FromSeconds(5)))
                    {
                        var pathReady = await mediaMtxClient.IsPathReadyAsync(
                            entry.Route.PublicPath,
                            cancellationToken);
                        entry.MarkHealthProbed();
                        if (!pathReady)
                        {
                            await StopProcessAsync(entry, failed: true);
                        }
                    }
                    else if (entry.ShouldCloseIdle(options.IdleCloseAfterSeconds))
                    {
                        await StopProcessAsync(entry, failed: false);
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "实时主码流路径 {PathName} 巡检失败，继续检查其他路径。", entry.Route.PublicPath);
            }
        }
    }

    private async Task RetireEntryAsync(RelayEntry entry)
    {
        await entry.Gate.WaitAsync(CancellationToken.None);
        try
        {
            await StopProcessAsync(entry, failed: false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private async Task StopProcessAsync(RelayEntry entry, bool failed)
    {
        var process = entry.Process;
        var terminationConfirmed = process is null && !entry.TerminationUnconfirmed;
        entry.Process = null;
        entry.MarkStopped();
        entry.DisablePublisher();
        try
        {
            if (process is not null)
            {
                var exitCode = process.ExitCode;
                var stderrCharacters = process.CapturedStderrCharacters;
                await process.DisposeAsync();
                terminationConfirmed = process.TerminationConfirmed;
                logger.LogInformation(
                    "实时主码流中继已停止：{PathName}，退出码 {ExitCode}，标准错误字符数 {StderrCharacters}。",
                    entry.Route.PublicPath,
                    exitCode,
                    stderrCharacters);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "实时主码流中继 {PathName} 清理失败。", entry.Route.PublicPath);
        }
        finally
        {
            if (terminationConfirmed)
            {
                entry.ClearTerminationFailure();
                ReleaseCapacity(entry);
            }
            else
            {
                entry.MarkTerminationUnconfirmed();
                logger.LogCritical(
                    "实时主码流中继 {PathName} 未确认退出，保留容量占用并拒绝超额启动。",
                    entry.Route.PublicPath);
            }
            if (failed)
            {
                entry.RestartNotBefore = DateTimeOffset.UtcNow.AddSeconds(options.RestartBackoffSeconds);
            }
        }
    }

    private async Task StopAllAsync()
    {
        await cleanupGate.WaitAsync(CancellationToken.None);
        try
        {
            RelayEntry[] snapshot;
            lock (stateGate)
            {
                snapshot = entries.Values.ToArray();
            }
            foreach (var entry in snapshot)
            {
                try
                {
                    await entry.Gate.WaitAsync(CancellationToken.None);
                    try
                    {
                        await StopProcessAsync(entry, failed: false);
                    }
                    finally
                    {
                        entry.Gate.Release();
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "停止实时主码流中继 {PathName} 时发生错误，继续清理其他路径。", entry.Route.PublicPath);
                }
            }
        }
        finally
        {
            cleanupGate.Release();
        }
    }

    private void ReleaseCapacity(RelayEntry entry)
    {
        if (!entry.CapacityHeld)
        {
            return;
        }
        entry.CapacityHeld = false;
        capacity.Release();
    }

    private static bool IsLoopback(string? value) =>
        IPAddress.TryParse(value, out var address) &&
        (IPAddress.IsLoopback(address) ||
         (address.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(address.MapToIPv4())));

    private static void ValidateRoute(LiveTranscodeRoute route)
    {
        if (!LiveTranscodePath.IsPublicMain(route.PublicPath) ||
            !LiveTranscodePath.IsInternalSource(route.InputPath) ||
            !IsExactRtspRoute(route.InputUri, route.InputPath) ||
            !IsExactRtspRoute(route.OutputUri, route.PublicPath) ||
            string.IsNullOrWhiteSpace(route.Fingerprint))
        {
            throw new InvalidOperationException("实时主码流中继路由无效。");
        }
    }

    private static bool IsExactRtspRoute(Uri uri, string pathName) =>
        uri.IsAbsoluteUri &&
        uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
        uri.IsLoopback &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.Equals(uri.AbsolutePath.Trim('/'), pathName, StringComparison.Ordinal);

    private sealed record RetiringEntry(RelayEntry Entry, TaskCompletionSource? Completion);

    private sealed class RelayEntry(LiveTranscodeRoute route, Task retirementBarrier)
    {
        private readonly object sessionGate = new();
        private readonly HashSet<Guid> sessionIds = [];
        private DateTimeOffset? idleSince = DateTimeOffset.UtcNow;
        private PublisherAuthorization? publisherAuthorization;
        private int retired;
        private int running;
        private int ready;
        private int terminationUnconfirmed;
        private long lastHealthCheckUtcTicks;

        public LiveTranscodeRoute Route { get; } = route;
        public Task RetirementBarrier { get; } = retirementBarrier;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ILiveTranscodeProcess? Process { get; set; }
        public DateTimeOffset StartedAt { get; private set; }
        public DateTimeOffset RestartNotBefore { get; set; }
        public bool CapacityHeld { get; set; }
        public bool IsRetired => Volatile.Read(ref retired) != 0;
        public bool IsRunning => Volatile.Read(ref running) != 0;
        public bool IsReady => Volatile.Read(ref ready) != 0;
        public bool TerminationUnconfirmed => Volatile.Read(ref terminationUnconfirmed) != 0;

        public bool HasSessions
        {
            get
            {
                lock (sessionGate)
                {
                    return sessionIds.Count > 0;
                }
            }
        }

        public void Attach(Guid sessionId)
        {
            lock (sessionGate)
            {
                sessionIds.Add(sessionId);
                idleSince = null;
            }
        }

        public void Detach(Guid sessionId)
        {
            lock (sessionGate)
            {
                sessionIds.Remove(sessionId);
                if (sessionIds.Count == 0)
                {
                    idleSince ??= DateTimeOffset.UtcNow;
                }
            }
        }

        public void Retire() => Interlocked.Exchange(ref retired, 1);

        public void MarkStarted(DateTimeOffset startedAt)
        {
            StartedAt = startedAt;
            Interlocked.Exchange(ref lastHealthCheckUtcTicks, startedAt.UtcDateTime.Ticks);
            Interlocked.Exchange(ref running, 1);
            Interlocked.Exchange(ref ready, 0);
        }

        public void MarkReady()
        {
            Interlocked.Exchange(ref lastHealthCheckUtcTicks, DateTimeOffset.UtcNow.UtcDateTime.Ticks);
            Interlocked.Exchange(ref ready, 1);
        }

        public void MarkHealthProbed() =>
            Interlocked.Exchange(ref lastHealthCheckUtcTicks, DateTimeOffset.UtcNow.UtcDateTime.Ticks);

        public void MarkStopped()
        {
            Interlocked.Exchange(ref running, 0);
            Interlocked.Exchange(ref ready, 0);
        }

        public void MarkTerminationUnconfirmed() => Interlocked.Exchange(ref terminationUnconfirmed, 1);

        public void ClearTerminationFailure() => Interlocked.Exchange(ref terminationUnconfirmed, 0);

        public bool ShouldCloseIdle(int idleCloseAfterSeconds)
        {
            lock (sessionGate)
            {
                return IsRunning &&
                    sessionIds.Count == 0 &&
                    idleSince is not null &&
                    DateTimeOffset.UtcNow - idleSince.Value >= TimeSpan.FromSeconds(idleCloseAfterSeconds);
            }
        }

        public bool IsStartupExpired(int startupTimeoutSeconds) =>
            IsRunning && !IsReady &&
            DateTimeOffset.UtcNow - StartedAt >= TimeSpan.FromSeconds(startupTimeoutSeconds);

        public bool ShouldProbeHealth(TimeSpan interval)
        {
            if (!IsRunning || !IsReady || !HasSessions)
            {
                return false;
            }
            var lastCheck = new DateTimeOffset(
                Interlocked.Read(ref lastHealthCheckUtcTicks),
                TimeSpan.Zero);
            return DateTimeOffset.UtcNow - lastCheck >= interval;
        }

        public void EnablePublisher(LiveTranscodePublisherCredential credential)
        {
            var hash = HashCredential(credential.Username, credential.Password);
            Volatile.Write(ref publisherAuthorization, new PublisherAuthorization(credential.Username, hash));
        }

        public void DisablePublisher() => Volatile.Write(ref publisherAuthorization, null);

        public bool AuthorizeCredential(string? username, string? password)
        {
            var authorization = Volatile.Read(ref publisherAuthorization);
            if (authorization is null || IsRetired || !HasSessions ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) ||
                !string.Equals(authorization.Username, username, StringComparison.Ordinal))
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(
                authorization.CredentialHash,
                HashCredential(username, password));
        }

        private static byte[] HashCredential(string username, string password) =>
            SHA256.HashData(Encoding.UTF8.GetBytes($"{username}\n{password}"));

        private sealed record PublisherAuthorization(string Username, byte[] CredentialHash);
    }
}
