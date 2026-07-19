using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VisiCore.Core;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class PlatformAccessService(PlatformDbContext dbContext)
{
    public async Task<IReadOnlyList<CameraEntity>> GetCamerasAsync(
        ClaimsPrincipal principal,
        CameraPermission requiredPermission,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(principal, cancellationToken);
        if (user.IsSystemAdministrator)
        {
            return await dbContext.Cameras.AsNoTracking().OrderBy(item => item.Code).ToListAsync(cancellationToken);
        }

        var roleIds = await dbContext.UserRoles
            .Where(item => item.UserId == user.Id)
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);
        if (roleIds.Count == 0)
        {
            return [];
        }

        var scopes = await dbContext.RoleCameraScopes
            .AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId) && (item.Permissions & requiredPermission) == requiredPermission)
            .ToListAsync(cancellationToken);
        if (scopes.Count == 0)
        {
            return [];
        }

        var directCameraIds = scopes.Where(item => item.CameraId is not null).Select(item => item.CameraId!.Value).ToHashSet();
        var regionIds = await ExpandRegionsAsync(scopes.Where(item => item.RegionId is not null).Select(item => item.RegionId!.Value), cancellationToken);

        return await dbContext.Cameras
            .AsNoTracking()
            .Where(item => directCameraIds.Contains(item.Id) || regionIds.Contains(item.RegionId))
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<CameraEntity?> FindAuthorizedCameraAsync(
        ClaimsPrincipal principal,
        Guid cameraId,
        CameraPermission requiredPermission,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(principal, cancellationToken);
        var camera = await dbContext.Cameras.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == cameraId, cancellationToken);
        if (camera is null || user.IsSystemAdministrator)
        {
            return camera;
        }

        var roleIds = await dbContext.UserRoles.AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);
        if (roleIds.Count == 0)
        {
            return null;
        }

        var directAccess = await dbContext.RoleCameraScopes.AsNoTracking()
            .AnyAsync(item => roleIds.Contains(item.RoleId) && item.CameraId == cameraId &&
                              (item.Permissions & requiredPermission) == requiredPermission, cancellationToken);
        if (directAccess)
        {
            return camera;
        }

        var grantedRegionIds = await dbContext.RoleCameraScopes.AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId) && item.RegionId != null &&
                           (item.Permissions & requiredPermission) == requiredPermission)
            .Select(item => item.RegionId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (grantedRegionIds.Count == 0)
        {
            return null;
        }

        var parents = await dbContext.Regions.AsNoTracking()
            .Select(item => new { item.Id, item.ParentId })
            .ToDictionaryAsync(item => item.Id, item => item.ParentId, cancellationToken);
        var currentRegionId = camera.RegionId;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentRegionId))
        {
            if (grantedRegionIds.Contains(currentRegionId))
            {
                return camera;
            }
            if (!parents.TryGetValue(currentRegionId, out var parentId) || parentId is null)
            {
                break;
            }
            currentRegionId = parentId.Value;
        }
        return null;
    }

    public async Task<bool> HasCameraPermissionAsync(
        Guid userId,
        Guid cameraId,
        CameraPermission requiredPermission,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || cameraId == Guid.Empty || requiredPermission == CameraPermission.None)
        {
            return false;
        }

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == userId && item.DisabledAt == null,
            cancellationToken);
        if (user is null)
        {
            return false;
        }
        if (user.IsSystemAdministrator)
        {
            return await dbContext.Cameras.AsNoTracking().AnyAsync(item => item.Id == cameraId, cancellationToken);
        }

        var camera = await dbContext.Cameras.AsNoTracking().SingleOrDefaultAsync(item => item.Id == cameraId, cancellationToken);
        if (camera is null)
        {
            return false;
        }
        var roleIds = await dbContext.UserRoles.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);
        if (roleIds.Count == 0)
        {
            return false;
        }

        if (await dbContext.RoleCameraScopes.AsNoTracking().AnyAsync(item =>
                roleIds.Contains(item.RoleId) && item.CameraId == cameraId &&
                (item.Permissions & requiredPermission) == requiredPermission,
                cancellationToken))
        {
            return true;
        }

        var grantedRegionIds = await dbContext.RoleCameraScopes.AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId) && item.RegionId != null &&
                           (item.Permissions & requiredPermission) == requiredPermission)
            .Select(item => item.RegionId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (grantedRegionIds.Count == 0)
        {
            return false;
        }

        var parents = await dbContext.Regions.AsNoTracking()
            .Select(item => new { item.Id, item.ParentId })
            .ToDictionaryAsync(item => item.Id, item => item.ParentId, cancellationToken);
        var currentRegionId = camera.RegionId;
        var visited = new HashSet<Guid>();
        while (visited.Add(currentRegionId))
        {
            if (grantedRegionIds.Contains(currentRegionId))
            {
                return true;
            }
            if (!parents.TryGetValue(currentRegionId, out var parentId) || parentId is null)
            {
                break;
            }
            currentRegionId = parentId.Value;
        }
        return false;
    }

    public async Task<UserEntity> FindUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
        {
            throw new UnauthorizedAccessException("会话缺少用户标识。 ");
        }

        return await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId && item.DisabledAt == null, cancellationToken)
            ?? throw new UnauthorizedAccessException("用户已失效。 ");
    }

    public async Task<bool> HasSystemPermissionAsync(
        ClaimsPrincipal principal,
        SystemPermission requiredPermission,
        CancellationToken cancellationToken)
    {
        if (requiredPermission == SystemPermission.None)
        {
            return false;
        }

        var user = await FindUserAsync(principal, cancellationToken);
        if (user.IsSystemAdministrator)
        {
            return true;
        }

        var permissions = await dbContext.UserRoles
            .Where(item => item.UserId == user.Id)
            .Join(
                dbContext.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (_, role) => role.SystemPermissions)
            .ToListAsync(cancellationToken);
        var combinedPermissions = permissions.Aggregate(0L, (current, value) => current | value);
        return (((SystemPermission)combinedPermissions) & requiredPermission) == requiredPermission;
    }

    private async Task<HashSet<Guid>> ExpandRegionsAsync(IEnumerable<Guid> rootRegionIds, CancellationToken cancellationToken)
    {
        var granted = rootRegionIds.ToHashSet();
        if (granted.Count == 0)
        {
            return granted;
        }

        var pending = granted.ToHashSet();
        while (pending.Count > 0)
        {
            var children = await dbContext.Regions
                .AsNoTracking()
                .Where(item => item.ParentId != null && pending.Contains(item.ParentId.Value))
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            pending.Clear();
            foreach (var child in children.Where(granted.Add))
            {
                pending.Add(child);
            }
        }

        return granted;
    }
}
