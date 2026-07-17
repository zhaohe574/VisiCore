using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VideoPlatform.Core;
using VideoPlatform.Persistence;

namespace VideoPlatform.Api;

public sealed class PlaybackExportService(
    PlatformDbContext dbContext,
    LocalExportArtifactStore artifactStore,
    PluginOperationEligibilityService eligibilityService)
{
    public const string AggregateType = "playback_export";

    public async Task<PlaybackExportEntity> CreateAsync(
        Guid userId,
        CameraEntity camera,
        CreatePlaybackExportRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (!await eligibilityService.CanRouteAsync(
                camera.RecorderId,
                EdgeOperationTypes.PluginPlaybackExport,
                cancellationToken))
        {
            throw new PlaybackExportUnavailableException("当前录像机未安装通过验证的导出插件。");
        }
        try
        {
            _ = artifactStore.GetSettings();
        }
        catch (InvalidOperationException exception)
        {
            throw new PlaybackExportUnavailableException($"中心导出归档不可用：{exception.Message}");
        }
        var workerId = await eligibilityService.GetReadyWorkerIdAsync(
            camera.RecorderId,
            EdgeOperationTypes.PluginPlaybackExport,
            cancellationToken);
        if (workerId is null)
        {
            throw new PlaybackExportUnavailableException("摄像头所在录像机未分配可用边缘 Worker。");
        }

        var now = DateTimeOffset.UtcNow;
        var export = new PlaybackExportEntity
        {
            Id = Guid.NewGuid(),
            CameraId = camera.Id,
            RequestedByUserId = userId,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            Container = request.Container,
            RequestedAt = now,
            NextAttemptAt = now
        };
        dbContext.PlaybackExports.Add(export);
        dbContext.EdgeCommands.Add(new EdgeCommandEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = workerId.Value,
            RecorderId = camera.RecorderId,
            CommandType = EdgeCommandTypes.PluginPlaybackExport,
            AggregateType = AggregateType,
            AggregateId = export.Id,
            PayloadJson = JsonSerializer.Serialize(new PlaybackExportCommandPayload(
                export.Id, camera.Id, export.StartedAt, export.EndedAt, export.Container)),
            CreatedAt = now,
            NextAttemptAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return export;
    }

    private static void Validate(CreatePlaybackExportRequest request)
    {
        if (request.StartedAt >= request.EndedAt || request.EndedAt - request.StartedAt > TimeSpan.FromDays(31) ||
            !string.Equals(request.Container, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "录像导出时间范围或容器格式无效。");
        }
    }
}

public sealed record CreatePlaybackExportRequest(DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Container);
public sealed class PlaybackExportUnavailableException(string message) : Exception(message);
