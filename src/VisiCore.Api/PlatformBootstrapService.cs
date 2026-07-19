using Microsoft.EntityFrameworkCore;
using VisiCore.Persistence;

namespace VisiCore.Api;

public sealed class PlatformBootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PlatformBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        if (configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
        {
            throw new InvalidOperationException("开源版不支持运行时迁移。请先运行 Docker setup 容器创建全新数据库。 ");
        }

        var pluginService = scope.ServiceProvider.GetRequiredService<DevicePluginService>();
        await pluginService.EnsureBuiltInPluginsAsync(cancellationToken);

        var password = configuration["Bootstrap:Password"];
        if (string.IsNullOrWhiteSpace(password) || await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var username = configuration["Bootstrap:Username"] ?? "admin";
        var hasher = scope.ServiceProvider.GetRequiredService<Argon2PasswordHasher>();
        dbContext.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = hasher.Hash(password),
            IsSystemAdministrator = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("已创建一次性平台初始管理员 {Username}。", username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
