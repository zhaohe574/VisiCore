using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed record ViewerPlaybackExportUser(UserEntity User);

/// <summary>
/// 查看端录像导出的账号与摄像头权限边界。
/// </summary>
public sealed class ViewerPlaybackExportAccessService(
    PlatformDbContext dbContext,
    PlatformAccessService platformAccessService)
{
    public async Task<ViewerPlaybackExportUser?> FindAuthorizedUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!await platformAccessService.HasSystemPermissionAsync(
                principal,
                SystemPermission.ManageExports,
                cancellationToken))
        {
            return null;
        }

        return new ViewerPlaybackExportUser(
            await platformAccessService.FindUserAsync(principal, cancellationToken));
    }

    public async Task<IReadOnlyList<PlaybackExportEntity>> ListOwnVisibleAsync(
        ClaimsPrincipal principal,
        ViewerPlaybackExportUser user,
        int limit,
        CancellationToken cancellationToken)
    {
        var visibleCameraIds = (await platformAccessService.GetCamerasAsync(
                principal,
                CameraPermission.Export,
                cancellationToken))
            .Select(item => item.Id)
            .ToHashSet();
        if (visibleCameraIds.Count == 0)
        {
            return [];
        }

        return await dbContext.PlaybackExports.AsNoTracking()
            .Where(item => item.RequestedByUserId == user.User.Id && visibleCameraIds.Contains(item.CameraId))
            .OrderByDescending(item => item.RequestedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
    }

    public Task<PlaybackExportEntity?> FindOwnAsync(
        Guid playbackExportId,
        ViewerPlaybackExportUser user,
        CancellationToken cancellationToken) =>
        dbContext.PlaybackExports.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == playbackExportId && item.RequestedByUserId == user.User.Id,
            cancellationToken);
}
