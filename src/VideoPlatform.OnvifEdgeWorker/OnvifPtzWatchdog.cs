using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VideoPlatform.Core;

namespace VideoPlatform.OnvifEdgeWorker;

public sealed class OnvifPtzWatchdog(
    IOnvifPtzClient ptzClient,
    OnvifEdgeOptions options,
    ILogger<OnvifPtzWatchdog> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, ActiveMotion> motions = new();
    private readonly ConcurrentDictionary<Guid, byte> unconfirmedStops = new();
    private int disposed;

    public bool HasUnconfirmedStop => !unconfirmedStops.IsEmpty;

    public async Task StartAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        PtzControlCommandPayload payload,
        CancellationToken cancellationToken)
    {
        options.Ptz.ValidateEnabled();
        Validate(payload, camera.CameraId, PtzMotion.Start);
        if (unconfirmedStops.ContainsKey(camera.CameraId))
        {
            throw new InvalidOperationException("ONVIF PTZ 停止未确认，禁止再次启动该摄像头。 ");
        }
        try
        {
            await StopActiveAsync(camera.CameraId, CancellationToken.None);
            await ptzClient.ExecuteAsync(assignment, camera, payload.Action, PtzMotion.Start, payload.Speed, cancellationToken);
        }
        catch
        {
            unconfirmedStops[camera.CameraId] = 0;
            throw;
        }
        var motion = new ActiveMotion(assignment, camera, payload.Action);
        if (!motions.TryAdd(camera.CameraId, motion))
        {
            await ptzClient.ExecuteAsync(assignment, camera, payload.Action, PtzMotion.Stop, 1, CancellationToken.None);
            throw new InvalidOperationException("ONVIF PTZ 动作状态重复。 ");
        }
        motion.Watchdog = RunWatchdogAsync(motion, Math.Min(payload.MaxDurationMilliseconds, options.Ptz.MaxPulseMilliseconds));
    }

    public async Task StopAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        PtzAction action,
        CancellationToken cancellationToken)
    {
        options.Ptz.ValidateEnabled();
        if (await StopActiveAsync(camera.CameraId, cancellationToken))
        {
            return;
        }
        await ptzClient.ExecuteAsync(assignment, camera, action, PtzMotion.Stop, 1, cancellationToken);
    }

    public async Task<bool> StopActiveAsync(Guid cameraId, CancellationToken cancellationToken)
    {
        options.Ptz.ValidateEnabled();
        if (!motions.TryRemove(cameraId, out var motion))
        {
            return false;
        }
        try
        {
            await ExecuteStopAsync(motion, cancellationToken);
            return true;
        }
        catch
        {
            unconfirmedStops[cameraId] = 0;
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        var motionsToStop = motions.Values.ToList();
        motions.Clear();
        await Task.WhenAll(motionsToStop.Select(item => StopWithSafetyMarkAsync(item, CancellationToken.None)));
    }

    private async Task RunWatchdogAsync(ActiveMotion motion, int maxDurationMilliseconds)
    {
        try
        {
            await Task.Delay(maxDurationMilliseconds, motion.Cancellation.Token);
            if (motions.TryRemove(motion.Camera.CameraId, out _))
            {
                try
                {
                    await ExecuteStopAsync(motion, CancellationToken.None);
                    logger.LogInformation("ONVIF PTZ Watchdog 已停止摄像头 {CameraId} 的超时动作。", motion.Camera.CameraId);
                }
                catch (Exception exception)
                {
                    unconfirmedStops[motion.Camera.CameraId] = 0;
                    logger.LogError(exception, "ONVIF PTZ Watchdog 无法确认摄像头 {CameraId} 已停止。", motion.Camera.CameraId);
                }
            }
        }
        catch (OperationCanceledException) when (motion.Cancellation.IsCancellationRequested)
        {
            // 显式 STOP 或服务退出会统一执行停止路径。
        }
    }

    private async Task ExecuteStopAsync(ActiveMotion motion, CancellationToken cancellationToken)
    {
        motion.Cancellation.Cancel();
        try
        {
            await ptzClient.ExecuteAsync(motion.Assignment, motion.Camera, motion.Action, PtzMotion.Stop, 1, cancellationToken);
        }
        finally
        {
            motion.Cancellation.Dispose();
        }
    }

    private async Task StopWithSafetyMarkAsync(ActiveMotion motion, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteStopAsync(motion, cancellationToken);
        }
        catch
        {
            unconfirmedStops[motion.Camera.CameraId] = 0;
            throw;
        }
    }

    private static void Validate(PtzControlCommandPayload payload, Guid expectedCameraId, PtzMotion expectedMotion)
    {
        if (payload.CameraId != expectedCameraId || payload.Motion != expectedMotion || payload.Speed is < 1 or > 7 ||
            payload.Sequence < 1 || payload.MaxDurationMilliseconds is < 250 or > 5000)
        {
            throw new InvalidOperationException("ONVIF PTZ 边缘命令参数无效。 ");
        }
    }

    private sealed class ActiveMotion(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, PtzAction action)
    {
        public WorkerRecorderAssignment Assignment { get; } = assignment;
        public WorkerCameraRoute Camera { get; } = camera;
        public PtzAction Action { get; } = action;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Watchdog { get; set; } = Task.CompletedTask;
    }
}
