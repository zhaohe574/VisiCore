namespace VideoPlatform.StreamGateway;

public sealed class StreamAuthorizationMonitor(
    GatewayControlPlaneClient controlPlaneClient,
    StreamAuthorizationStore authorizationStore,
    GatewayOptions options,
    ILogger<StreamAuthorizationMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SessionInspectionSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await InspectAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "流网关会话巡检失败，下个周期继续重试。");
            }
        }
    }

    private async Task InspectAsync(CancellationToken cancellationToken)
    {
        var sessionIds = authorizationStore.SnapshotSessionIds();
        foreach (var batch in sessionIds.Chunk(1000))
        {
            var statuses = await controlPlaneClient.InspectSessionsAsync(batch, cancellationToken);
            var byId = statuses.ToDictionary(item => item.Id);
            foreach (var sessionId in batch)
            {
                try
                {
                    if (!byId.TryGetValue(sessionId, out var status) || !status.Active ||
                        status.LeaseExpiresAt <= DateTimeOffset.UtcNow)
                    {
                        await authorizationStore.RevokeAsync(sessionId, cancellationToken);
                    }
                    else
                    {
                        authorizationStore.UpdateLease(sessionId, status.LeaseExpiresAt);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "流网关会话 {SessionId} 巡检处理失败，继续处理同批其他会话。",
                        sessionId);
                }
            }
        }
    }
}
