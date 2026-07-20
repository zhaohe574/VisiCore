using System.Security.Claims;
using VisiCore.Api;
using VisiCore.Core;
using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.IntegrationTests;

public sealed class SystemPermissionAccessTests(StreamSessionPostgreSqlFixture fixture)
    : IClassFixture<StreamSessionPostgreSqlFixture>
{
    [Fact(DisplayName = "角色系统权限允许资产管理但不隐含通知管理")]
    public async Task RoleSystemPermissionIsEnforced()
    {
        var roleId = Guid.NewGuid();
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Roles.Add(new RoleEntity
            {
                Id = roleId,
                Code = $"ASSET-{roleId:N}",
                Name = "资产管理员",
                SystemPermissions = (long)SystemPermission.ManageAssets
            });
            setup.UserRoles.Add(new UserRoleEntity { UserId = fixture.UserId, RoleId = roleId });
            await setup.SaveChangesAsync();
        }

        await using var dbContext = fixture.CreateDbContext();
        var accessService = new PlatformAccessService(dbContext);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, fixture.UserId.ToString())],
        "test"));

        Assert.True(await accessService.HasSystemPermissionAsync(
            principal,
            SystemPermission.ManageAssets,
            CancellationToken.None));
        Assert.False(await accessService.HasSystemPermissionAsync(
            principal,
            SystemPermission.ManageNotifications,
            CancellationToken.None));
    }
}
