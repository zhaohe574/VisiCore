using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Npgsql;
using VideoPlatform.Api;
using VideoPlatform.Core;
using VideoPlatform.Persistence;
using Xunit;

namespace VideoPlatform.Api.IntegrationTests;

public sealed class StreamSessionPostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? container;
    private readonly string? externalConnectionString;

    public Guid UserId { get; } = Guid.NewGuid();
    public Guid UserSessionId { get; } = Guid.NewGuid();
    public Guid SecondUserId { get; } = Guid.NewGuid();
    public Guid SecondUserSessionId { get; } = Guid.NewGuid();
    public Guid CameraId { get; } = Guid.NewGuid();
    public Guid SecondCameraId { get; } = Guid.NewGuid();
    public Guid RecorderId { get; } = Guid.NewGuid();
    public Guid WorkerId { get; } = Guid.NewGuid();
    public Guid PluginId { get; } = Guid.NewGuid();

    public StreamSessionPostgreSqlFixture()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable("VIDEO_PLATFORM_TEST_POSTGRES_CONNECTION");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var connectionBuilder = new NpgsqlConnectionStringBuilder(ValidateExternalConnectionString(configuredConnectionString))
            {
                Database = $"video_platform_integration_{Guid.NewGuid():N}"
            };
            externalConnectionString = connectionBuilder.ConnectionString;
            return;
        }

        container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("video_platform_tests")
            .WithUsername("video_platform")
            .WithPassword("integration-test-only")
            .Build();
    }

    public PlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(externalConnectionString ?? container!.GetConnectionString())
            .Options;
        return new PlatformDbContext(options);
    }

    public async Task InitializeAsync()
    {
        if (container is not null)
        {
            await container.StartAsync();
        }
        else
        {
            await ResetExternalDatabaseAsync();
        }
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();

        var regionId = Guid.NewGuid();
        var viewerRoleId = Guid.NewGuid();
        dbContext.Users.AddRange(new UserEntity
        {
            Id = UserId,
            Username = "stream-integration-user",
            PasswordHash = "not-used",
            CreatedAt = DateTimeOffset.UtcNow
        }, new UserEntity
        {
            Id = SecondUserId,
            Username = "stream-integration-user-2",
            PasswordHash = "not-used",
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.UserSessions.AddRange(new UserSessionEntity
        {
            Id = UserSessionId,
            UserId = UserId,
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        }, new UserSessionEntity
        {
            Id = SecondUserSessionId,
            UserId = SecondUserId,
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        dbContext.Regions.Add(new RegionEntity { Id = regionId, Code = "TEST", Name = "集成测试区域" });
        dbContext.Roles.Add(new RoleEntity
        {
            Id = viewerRoleId,
            Code = "STREAM-VIEWER",
            Name = "流会话测试查看者"
        });
        dbContext.UserRoles.AddRange(
            new UserRoleEntity { UserId = UserId, RoleId = viewerRoleId },
            new UserRoleEntity { UserId = SecondUserId, RoleId = viewerRoleId });
        dbContext.RoleCameraScopes.Add(new RoleCameraScopeEntity
        {
            Id = Guid.NewGuid(),
            RoleId = viewerRoleId,
            RegionId = regionId,
            Permissions = CameraPermission.LiveView | CameraPermission.Playback | CameraPermission.PtzControl | CameraPermission.Export
        });
        dbContext.DevicePlugins.Add(new DevicePluginEntity
        {
            Id = PluginId,
            Key = "external-integration-plugin",
            Name = "集成测试插件",
            Version = "1.0.0",
            ProtocolType = "plugin",
            RuntimeType = DevicePluginRuntimeTypes.ExternalEdge,
            AdapterType = "external-integration-plugin",
            ManifestJson = "{}",
            PackageHash = new string('A', 64),
            Enabled = true,
            InstalledAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        dbContext.Recorders.Add(new RecorderEntity
        {
            Id = RecorderId,
            Code = "TEST-NVR",
            Name = "集成测试录像机",
            Vendor = "Generic",
            AdapterType = "external-integration-plugin",
            DevicePluginId = PluginId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        dbContext.RecorderCapabilities.Add(new RecorderCapabilityEntity
        {
            Id = Guid.NewGuid(),
            RecorderId = RecorderId,
            Version = "integration-sdk-verified",
            CapabilityJson = JsonSerializer.Serialize(new RecorderCapabilities(
                CapabilityState.Supported,
                CapabilityState.Supported,
                CapabilityState.Unsupported,
                CapabilityState.Supported,
                CapabilityState.Unsupported,
                CapabilityState.Supported,
                CapabilityState.Unsupported,
                "integration-sdk-verified")),
            VerifiedAt = DateTimeOffset.UtcNow
        });
        dbContext.Cameras.AddRange(new CameraEntity
        {
            Id = CameraId,
            RecorderId = RecorderId,
            RegionId = regionId,
            Code = "TEST-CAM-001",
            Alias = "集成测试摄像头",
            InputChannelNumber = 1,
            SupportsPtz = true
        }, new CameraEntity
        {
            Id = SecondCameraId,
            RecorderId = RecorderId,
            RegionId = regionId,
            Code = "TEST-CAM-002",
            Alias = "集成测试摄像头二",
            InputChannelNumber = 2
        });
        dbContext.DeviceWorkers.Add(new DeviceWorkerEntity
        {
            Id = WorkerId,
            Name = "edge-command-integration-worker",
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        });
        dbContext.DeviceWorkerAssignments.Add(new DeviceWorkerAssignmentEntity
        {
            Id = Guid.NewGuid(),
            WorkerId = WorkerId,
            RecorderId = RecorderId,
            DefaultRegionId = regionId
        });
        dbContext.DeviceWorkerOperationStatuses.AddRange(
            CreateOperationStatus(EdgeOperationTypes.PluginRecordingSearch),
            CreateOperationStatus(EdgeOperationTypes.PluginPlaybackRelay),
            CreateOperationStatus(EdgeOperationTypes.PluginPlaybackExport),
            CreateOperationStatus(EdgeOperationTypes.PluginPtz));
        await dbContext.SaveChangesAsync();
    }

    public async Task ResetEdgeCommandStateAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.EdgeCommands.ExecuteDeleteAsync();
    }

    public async Task ResetPlaybackExportStateAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.ExportDownloadAudits.ExecuteDeleteAsync();
        await dbContext.ExportArtifacts.ExecuteDeleteAsync();
        await dbContext.EdgeCommands.Where(item => item.AggregateType == PlaybackExportService.AggregateType).ExecuteDeleteAsync();
        await dbContext.PlaybackExports.ExecuteDeleteAsync();
    }

    public async Task ResetPtzStateAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.EdgeCommands.Where(item => item.AggregateType == PtzControlService.AggregateType).ExecuteDeleteAsync();
        await dbContext.PtzControlLeases.ExecuteDeleteAsync();
    }

    public async Task ResetRecordingSearchStateAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.RecordingSearches.ExecuteDeleteAsync();
        await dbContext.EdgeCommands.Where(item => item.AggregateType == RecordingSearchService.AggregateType)
            .ExecuteDeleteAsync();
    }

    public async Task ResetStreamStateAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.OutboxEvents.Where(item => item.EventType == "stream.session.revoked").ExecuteDeleteAsync();
        await dbContext.EdgeCommands.Where(item => item.AggregateType == StreamSessionOrchestrator.PlaybackRelayAggregateType)
            .ExecuteDeleteAsync();
        await dbContext.StreamConnectionTickets.ExecuteDeleteAsync();
        await dbContext.StreamSessions.ExecuteDeleteAsync();
        await dbContext.StreamAssignments.ExecuteDeleteAsync();
        await dbContext.UserSessions.Where(item => item.Id == UserSessionId || item.Id == SecondUserSessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.RevokedAt, (DateTimeOffset?)null)
                .SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.AddHours(1)));
    }

    public async Task DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            return;
        }
        await ResetExternalDatabaseAsync(dropOnly: true);
    }

    private DeviceWorkerOperationStatusEntity CreateOperationStatus(string operationType) => new()
    {
        Id = Guid.NewGuid(),
        WorkerId = WorkerId,
        RecorderId = RecorderId,
        OperationType = operationType,
        IsReady = true,
        ReportedAt = DateTimeOffset.UtcNow
    };

    private static string ValidateExternalConnectionString(string value)
    {
        var builder = new NpgsqlConnectionStringBuilder(value) { Pooling = false };
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !Regex.IsMatch(builder.Database, "^video_platform_integration_[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant) ||
            !IsLoopbackHost(builder.Host))
        {
            throw new InvalidOperationException(
                "VIDEO_PLATFORM_TEST_POSTGRES_CONNECTION 仅允许指向回环地址、且数据库名以 video_platform_integration_ 开头的临时数据库标识。");
        }
        return builder.ConnectionString;
    }

    private async Task ResetExternalDatabaseAsync(bool dropOnly = false)
    {
        var testConnection = new NpgsqlConnectionStringBuilder(externalConnectionString!);
        var databaseName = testConnection.Database;
        testConnection.Database = "postgres";
        await using var connection = new NpgsqlConnection(testConnection.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
        if (!dropOnly)
        {
            command.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static bool IsLoopbackHost(string? host) =>
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
}
