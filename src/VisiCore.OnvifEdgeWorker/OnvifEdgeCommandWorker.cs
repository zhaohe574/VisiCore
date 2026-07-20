using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public sealed class OnvifEdgeCommandWorker(
    OnvifEdgeControlPlaneClient controlPlaneClient,
    OnvifPtzCommandExecutor commandExecutor,
    OnvifProfileGCommandExecutor profileGCommandExecutor,
    OnvifPlaybackRelayCommandExecutor playbackRelayCommandExecutor,
    IOnvifPlaybackRelayAuthorization playbackRelayAuthorization,
    OnvifEdgeOptions options,
    ILogger<OnvifEdgeCommandWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IReadOnlyDictionary<Guid, WorkerRecorderAssignment> assignments = new Dictionary<Guid, WorkerRecorderAssignment>();
        var nextAssignmentRefreshAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextAssignmentRefreshAt)
                {
                    assignments = (await controlPlaneClient.GetAssignmentsAsync(stoppingToken)).ToDictionary(item => item.RecorderId);
                    nextAssignmentRefreshAt = DateTimeOffset.UtcNow.AddSeconds(options.AssignmentRefreshSeconds);
                }
                var commands = await controlPlaneClient.ClaimCommandsAsync(stoppingToken);
                if (commands.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(options.CommandPollMilliseconds), stoppingToken);
                    continue;
                }
                foreach (var command in commands)
                {
                    await ProcessCommandAsync(command, assignments, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ONVIF 边缘 Worker 轮询失败，5 秒后重试。 ");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcessCommandAsync(
        WorkerEdgeCommand command,
        IReadOnlyDictionary<Guid, WorkerRecorderAssignment> assignments,
        CancellationToken stoppingToken)
    {
        assignments.TryGetValue(command.RecorderId, out var assignment);
        var isAssignmentIndependentCommand = command.CommandType is EdgeCommandTypes.OnvifPlaybackRelayStop or
            EdgeCommandTypes.OnvifPlaybackRelayControl or EdgeCommandTypes.OnvifPtzControl;
        if (assignment is null && !isAssignmentIndependentCommand)
        {
            await CompleteFailureAsync(command, "assignment_missing", stoppingToken);
            return;
        }
        if (assignment is not null &&
            assignment.PluginRuntimeType?.Equals(DevicePluginRuntimeTypes.Onvif, StringComparison.OrdinalIgnoreCase) != true)
        {
            await CompleteFailureAsync(command, "unsupported_vendor", stoppingToken);
            return;
        }
        if (command.CommandType == EdgeCommandTypes.OnvifPlaybackRelayStart &&
            !await playbackRelayAuthorization.CanStartAsync(command, stoppingToken))
        {
            logger.LogInformation("ONVIF 回放启动命令已撤销或会话已过期：命令 {CommandId}。", command.Id);
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var remaining = command.DeliveryExpiresAt - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5);
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }
        deadline.CancelAfter(remaining);
        try
        {
            var resultJson = command.CommandType switch
            {
                EdgeCommandTypes.OnvifRecordingSearch => await profileGCommandExecutor.ExecuteAsync(assignment, command, deadline.Token),
                EdgeCommandTypes.OnvifPlaybackRelayStart or EdgeCommandTypes.OnvifPlaybackRelayStop or EdgeCommandTypes.OnvifPlaybackRelayControl =>
                    await playbackRelayCommandExecutor.ExecuteAsync(assignment, command, deadline.Token),
                _ => await commandExecutor.ExecuteAsync(assignment, command, deadline.Token)
            };
            await controlPlaneClient.CompleteCommandAsync(
                command.Id,
                new WorkerEdgeCommandCompletion(command.DeliveryToken, true, resultJson, null),
                deadline.Token);
            logger.LogInformation("ONVIF 边缘命令完成：命令 {CommandId}，类型 {CommandType}，尝试 {Attempt}。", command.Id, command.CommandType, command.Attempt);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // 领取已临近过期时不再确认，让中心自动重新投递。
        }
        catch (Exception exception)
        {
            var failureKind = exception switch
            {
                NotSupportedException => "unsupported_command",
                JsonException => "invalid_payload",
                InvalidOperationException => "invalid_operation",
                _ => exception.GetType().Name
            };
            logger.LogError(exception, "ONVIF 边缘命令失败：命令 {CommandId}，类型 {CommandType}，失败类别 {FailureKind}。", command.Id, command.CommandType, failureKind);
            await CompleteFailureAsync(command, failureKind, stoppingToken);
        }
    }

    private Task CompleteFailureAsync(WorkerEdgeCommand command, string failureKind, CancellationToken cancellationToken) =>
        controlPlaneClient.CompleteCommandAsync(
            command.Id,
            new WorkerEdgeCommandCompletion(command.DeliveryToken, false, null, failureKind),
            cancellationToken);
}
