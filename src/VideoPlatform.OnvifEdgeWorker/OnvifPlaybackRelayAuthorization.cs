using VideoPlatform.Core;

namespace VideoPlatform.OnvifEdgeWorker;

public interface IOnvifPlaybackRelayAuthorization
{
    Task<bool> CanStartAsync(WorkerEdgeCommand command, CancellationToken cancellationToken);
    Task<bool> CanContinueAsync(Guid playbackSessionId, CancellationToken cancellationToken);
}

public sealed class OnvifPlaybackRelayAuthorization(OnvifEdgeControlPlaneClient controlPlaneClient) : IOnvifPlaybackRelayAuthorization
{
    public Task<bool> CanStartAsync(WorkerEdgeCommand command, CancellationToken cancellationToken) =>
        controlPlaneClient.CanStartPlaybackRelayAsync(command, cancellationToken);

    public Task<bool> CanContinueAsync(Guid playbackSessionId, CancellationToken cancellationToken) =>
        controlPlaneClient.CanContinuePlaybackRelayAsync(playbackSessionId, cancellationToken);
}
