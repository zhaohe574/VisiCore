using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VideoPlatform.Application;
using VideoPlatform.Contracts;
using VideoPlatform.Domain;

namespace VideoPlatform.Infrastructure;

public static class Bootstrap
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var options = new PlatformOptions(configuration);
        Directory.CreateDirectory(options.KeysPath);
        Directory.CreateDirectory(options.ReleasesPath);
        Directory.CreateDirectory(options.ExportsPath);
        Directory.CreateDirectory(options.PluginsPath);
        services.AddSingleton(options);
        services.AddSingleton(NpgsqlDataSource.Create(options.DatabaseUrl));
        services.AddSingleton<Database>();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(options.KeysPath)).SetApplicationName("VideoPlatform.v2");
        services.AddSingleton<SecretStore>();
        services.AddSingleton<AccessService>();
        services.AddSingleton<IAccessService>(sp => sp.GetRequiredService<AccessService>());
        services.AddSingleton<SessionStore>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddHttpClient<IDeviceAdapter, DeviceAdapter>(client => client.Timeout = TimeSpan.FromSeconds(45));
        services.AddHttpClient("zlm", client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<AuditStore>();
        services.AddSingleton<DeviceService>();
        services.AddSingleton<MediaService>();
        services.AddSingleton<AdministrationService>();
        return services;
    }

    public static async Task InitializeAsync(IServiceProvider provider, CancellationToken ct = default)
    {
        var db = provider.GetRequiredService<Database>();
        var options = provider.GetRequiredService<PlatformOptions>();
        await db.MigrateAsync(ct);
        await db.TransactionAsync(async tx =>
        {
            await tx.ExecuteAsync("select pg_advisory_xact_lock(72002001)", ct: ct);
            foreach (var (code, name) in Rules.Permissions)
                await tx.ExecuteAsync("insert into permissions(code,name) values(@code,@name) on conflict(code) do update set name=excluded.name", new { code, name }, ct);
            foreach (var (code, role) in Rules.DefaultRoles)
            {
                var inserted = await tx.OneAsync("insert into roles(name,code,all_channels) values(@name,@code,@all) on conflict(code) do nothing returning id", new { name = role.Name, code, all = code == "admin" }, ct);
                if (inserted is null) continue;
                foreach (var permission in code == "admin" ? Rules.Permissions.Keys : role.Codes)
                    await tx.ExecuteAsync("insert into role_permissions(role_id,permission_code) values(@id,@permission) on conflict do nothing", new { id = inserted.Id(), permission }, ct);
            }
            await tx.ExecuteAsync("insert into role_permissions(role_id,permission_code) select r.id, p.code from roles r cross join permissions p where r.code = 'admin' on conflict do nothing", ct: ct);
            var count = await tx.OneAsync("select count(*) as count from users", ct: ct);
            if (count.Id("count") == 0)
            {
                Rules.Password(options.BootstrapPassword);
                var user = await tx.OneAsync("insert into users(username,password_hash,display_name) values(@username,@hash,'平台管理员') returning id", new { username = options.BootstrapUser, hash = Passwords.Hash(options.BootstrapPassword!) }, ct);
                await tx.ExecuteAsync("insert into user_roles(user_id,role_id) select @id,id from roles where code='admin'", new { id = user.Id() }, ct);
            }
            await tx.ExecuteAsync("insert into settings(id,value) values(1,cast(@value as jsonb)) on conflict do nothing", new { value = JsonSerializer.Serialize(new PlatformSettings(), JsonDefaults.Options) }, ct);
            return true;
        }, ct);
    }
}

public sealed class SettingsStore(Database db) : ISettingsStore
{
    public async Task<PlatformSettings> ReadAsync(CancellationToken ct = default)
        => (await db.OneAsync("select value from settings where id=1", ct: ct))?["value"]?.Deserialize<PlatformSettings>(JsonDefaults.Options) ?? new();
    public async Task WriteAsync(PlatformSettings settings, CancellationToken ct = default)
    {
        Rules.Require(settings.LivePerUser is >= 1 and <= 64 && settings.PlaybackPerUser is >= 1 and <= 16 && settings.PlaybackPerDevice is >= 1 and <= 128 && settings.PlaybackGlobal is >= 1 and <= 256 && settings.TranscodeGlobal is >= 0 and <= 32, "媒体配额超出允许范围");
        Rules.Require(settings.ExportGlobal is >= 1 and <= 16 && settings.ExportPerDevice is >= 1 and <= 4 && settings.ExportRetentionDays is >= 1 and <= 365 && settings.ExportQuotaGb is >= 1 and <= 10000 && settings.AlarmRetentionDays is >= 1 and <= 3650 && settings.AuditRetentionDays is >= 1 and <= 3650, "任务或保留配置超出允许范围");
        await db.ExecuteAsync("update settings set value=cast(@value as jsonb),updated_at=now() where id=1", new { value = JsonSerializer.Serialize(settings, JsonDefaults.Options) }, ct);
    }
}

public sealed class AuditStore(Database db)
{
    public Task WriteAsync(long? userId, string action, string resource, string summary, string? ip = null, CancellationToken ct = default)
        => db.ExecuteAsync("insert into audit_logs(user_id,action,resource,summary,client_ip) values(@userId,@action,@resource,@summary,cast(@ip as inet))", new { userId, action, resource, summary, ip }, ct);
    public Task NotifyAsync(string kind, string id, long? userId = null, long? channelId = null, long? deviceId = null, CancellationToken ct = default)
        => db.ExecuteAsync("insert into outbox(kind,resource_id,user_id,channel_id,device_id) values(@kind,@id,@userId,@channelId,@deviceId)", new { kind, id, userId, channelId, deviceId }, ct);
}
