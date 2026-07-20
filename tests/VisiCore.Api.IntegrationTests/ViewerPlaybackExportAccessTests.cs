using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.IntegrationTests;

public sealed class ViewerPlaybackExportAccessTests(StreamSessionPostgreSqlFixture fixture)
    : IClassFixture<StreamSessionPostgreSqlFixture>
{
    [Fact(DisplayName = "查看端导出仅返回本人且仍拥有导出摄像头权限的任务")]
    public async Task ListOwnVisibleExportsRequiresBothPermissionLayers()
    {
        await fixture.ResetPlaybackExportStateAsync();
        var exportRoleId = Guid.NewGuid();
        var ownExportId = Guid.NewGuid();
        var anotherExportId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Roles.Add(new RoleEntity
            {
                Id = exportRoleId,
                Code = $"VIEWER-EXPORT-{exportRoleId:N}",
                Name = "查看端导出测试角色",
                SystemPermissions = (long)SystemPermission.ManageExports
            });
            setup.UserRoles.Add(new UserRoleEntity { UserId = fixture.UserId, RoleId = exportRoleId });
            setup.PlaybackExports.AddRange(
                CreateExport(ownExportId, fixture.UserId, fixture.CameraId, now),
                CreateExport(anotherExportId, fixture.SecondUserId, fixture.CameraId, now.AddMinutes(-1)));
            await setup.SaveChangesAsync();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, fixture.UserId.ToString())],
            "test"));
        await using var dbContext = fixture.CreateDbContext();
        var platformAccessService = new PlatformAccessService(dbContext);
        var viewerAccessService = new ViewerPlaybackExportAccessService(dbContext, platformAccessService);

        var user = await viewerAccessService.FindAuthorizedUserAsync(principal, CancellationToken.None);
        Assert.NotNull(user);
        var exports = await viewerAccessService.ListOwnVisibleAsync(principal, user!, 100, CancellationToken.None);
        var export = Assert.Single(exports);
        Assert.Equal(ownExportId, export.Id);

        var own = await viewerAccessService.FindOwnAsync(ownExportId, user!, CancellationToken.None);
        var other = await viewerAccessService.FindOwnAsync(anotherExportId, user!, CancellationToken.None);
        Assert.NotNull(own);
        Assert.Null(other);

        await using var revoke = fixture.CreateDbContext();
        var scope = await revoke.RoleCameraScopes.SingleAsync(CancellationToken.None);
        scope.Permissions &= ~CameraPermission.Export;
        await revoke.SaveChangesAsync();

        await using var restrictedDbContext = fixture.CreateDbContext();
        var restrictedService = new ViewerPlaybackExportAccessService(
            restrictedDbContext,
            new PlatformAccessService(restrictedDbContext));
        var restrictedUser = await restrictedService.FindAuthorizedUserAsync(principal, CancellationToken.None);
        Assert.NotNull(restrictedUser);
        Assert.Empty(await restrictedService.ListOwnVisibleAsync(
            principal,
            restrictedUser!,
            100,
            CancellationToken.None));
    }

    private static PlaybackExportEntity CreateExport(
        Guid id,
        Guid userId,
        Guid cameraId,
        DateTimeOffset requestedAt) => new()
    {
        Id = id,
        RequestedByUserId = userId,
        CameraId = cameraId,
        StartedAt = requestedAt.AddMinutes(-5),
        EndedAt = requestedAt,
        RequestedAt = requestedAt,
        NextAttemptAt = requestedAt,
        Container = "mp4"
    };
}
