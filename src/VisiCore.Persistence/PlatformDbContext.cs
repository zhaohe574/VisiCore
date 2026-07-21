using Microsoft.EntityFrameworkCore;
using VisiCore.Core;

namespace VisiCore.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();
    public DbSet<RoleCameraScopeEntity> RoleCameraScopes => Set<RoleCameraScopeEntity>();
    public DbSet<RegionEntity> Regions => Set<RegionEntity>();
    public DbSet<DevicePluginEntity> DevicePlugins => Set<DevicePluginEntity>();
    public DbSet<RecorderEntity> Recorders => Set<RecorderEntity>();
    public DbSet<RecorderEndpointEntity> RecorderEndpoints => Set<RecorderEndpointEntity>();
    public DbSet<DeviceCredentialEntity> DeviceCredentials => Set<DeviceCredentialEntity>();
    public DbSet<DeviceCredentialVersionEntity> DeviceCredentialVersions => Set<DeviceCredentialVersionEntity>();
    public DbSet<DeviceCredentialEnvelopeEntity> DeviceCredentialEnvelopes => Set<DeviceCredentialEnvelopeEntity>();
    public DbSet<DeviceWorkerEntity> DeviceWorkers => Set<DeviceWorkerEntity>();
    public DbSet<EdgeAgentEntity> EdgeAgents => Set<EdgeAgentEntity>();
    public DbSet<EdgeAgentEnrollmentEntity> EdgeAgentEnrollments => Set<EdgeAgentEnrollmentEntity>();
    public DbSet<EdgeAgentConfigurationEntity> EdgeAgentConfigurations => Set<EdgeAgentConfigurationEntity>();
    public DbSet<PlatformOperationEntity> PlatformOperations => Set<PlatformOperationEntity>();
    public DbSet<ReleaseCatalogEntity> ReleaseCatalog => Set<ReleaseCatalogEntity>();
    public DbSet<UpgradePlanEntity> UpgradePlans => Set<UpgradePlanEntity>();
    public DbSet<UpgradeTargetEntity> UpgradeTargets => Set<UpgradeTargetEntity>();
    public DbSet<DeviceWorkerAssignmentEntity> DeviceWorkerAssignments => Set<DeviceWorkerAssignmentEntity>();
    public DbSet<DeviceWorkerOperationStatusEntity> DeviceWorkerOperationStatuses => Set<DeviceWorkerOperationStatusEntity>();
    public DbSet<CameraEntity> Cameras => Set<CameraEntity>();
    public DbSet<RecorderCapabilityEntity> RecorderCapabilities => Set<RecorderCapabilityEntity>();
    public DbSet<RecorderClockObservationEntity> RecorderClockObservations => Set<RecorderClockObservationEntity>();
    public DbSet<HealthStateEventEntity> HealthStateEvents => Set<HealthStateEventEntity>();
    public DbSet<StreamAssignmentEntity> StreamAssignments => Set<StreamAssignmentEntity>();
    public DbSet<StreamSessionEntity> StreamSessions => Set<StreamSessionEntity>();
    public DbSet<StreamConnectionTicketEntity> StreamConnectionTickets => Set<StreamConnectionTicketEntity>();
    public DbSet<PtzControlLeaseEntity> PtzControlLeases => Set<PtzControlLeaseEntity>();
    public DbSet<PlaybackExportEntity> PlaybackExports => Set<PlaybackExportEntity>();
    public DbSet<ExportArtifactEntity> ExportArtifacts => Set<ExportArtifactEntity>();
    public DbSet<ExportDownloadAuditEntity> ExportDownloadAudits => Set<ExportDownloadAuditEntity>();
    public DbSet<PlatformBackupEntity> PlatformBackups => Set<PlatformBackupEntity>();
    public DbSet<EdgeCommandEntity> EdgeCommands => Set<EdgeCommandEntity>();
    public DbSet<RecordingSearchEntity> RecordingSearches => Set<RecordingSearchEntity>();
    public DbSet<OutboxEventEntity> OutboxEvents => Set<OutboxEventEntity>();
    public DbSet<AlertRuleEntity> AlertRules => Set<AlertRuleEntity>();
    public DbSet<AlertRuleChannelEntity> AlertRuleChannels => Set<AlertRuleChannelEntity>();
    public DbSet<AlertIncidentEntity> AlertIncidents => Set<AlertIncidentEntity>();
    public DbSet<NotificationChannelEntity> NotificationChannels => Set<NotificationChannelEntity>();
    public DbSet<NotificationDeliveryEntity> NotificationDeliveries => Set<NotificationDeliveryEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Username).IsUnique();
            entity.Property(item => item.Username).HasMaxLength(64);
            entity.Property(item => item.PasswordHash).HasMaxLength(512);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DisabledAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<RoleEntity>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Code).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(64);
            entity.Property(item => item.Name).HasMaxLength(128);
        });

        modelBuilder.Entity<UserRoleEntity>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(item => new { item.UserId, item.RoleId });
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RoleEntity>().WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSessionEntity>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.HasIndex(item => new { item.UserId, item.ExpiresAt });
            entity.Property(item => item.TokenHash).HasMaxLength(128);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RevokedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoleCameraScopeEntity>(entity =>
        {
            entity.ToTable("role_camera_scopes");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.RoleId, item.CameraId });
            entity.HasIndex(item => new { item.RoleId, item.RegionId });
            entity.HasOne<RoleEntity>().WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CameraEntity>().WithMany().HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RegionEntity>().WithMany().HasForeignKey(item => item.RegionId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_role_camera_scopes_exactly_one_target",
                "(\"RegionId\" IS NOT NULL AND \"CameraId\" IS NULL) OR (\"RegionId\" IS NULL AND \"CameraId\" IS NOT NULL)"));
        });

        modelBuilder.Entity<RegionEntity>(entity =>
        {
            entity.ToTable("regions");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Code).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(64);
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.HasOne<RegionEntity>().WithMany().HasForeignKey(item => item.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DevicePluginEntity>(entity =>
        {
            entity.ToTable("device_plugins");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Key).IsUnique();
            entity.HasIndex(item => item.AdapterType).IsUnique();
            entity.Property(item => item.Key).HasMaxLength(64);
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.Version).HasMaxLength(32);
            entity.Property(item => item.ProtocolType).HasMaxLength(64);
            entity.Property(item => item.RuntimeType).HasMaxLength(64);
            entity.Property(item => item.AdapterType).HasMaxLength(64);
            entity.Property(item => item.Vendor).HasMaxLength(64);
            entity.Property(item => item.Description).HasMaxLength(512);
            entity.Property(item => item.ManifestJson).HasColumnType("jsonb");
            entity.Property(item => item.PackageHash).HasMaxLength(64);
            entity.Property(item => item.InstalledAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.UpdatedAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<RecorderEntity>(entity =>
        {
            entity.ToTable("recorders");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Code).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(64);
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.Vendor).HasMaxLength(64);
            entity.Property(item => item.Model).HasMaxLength(128);
            entity.Property(item => item.AdapterType).HasMaxLength(64);
            entity.Property(item => item.DeviceKind).HasMaxLength(32).HasDefaultValue(DeviceKinds.Recorder);
            entity.Property(item => item.SerialNumber).HasMaxLength(128);
            entity.Property(item => item.FirmwareVersion).HasMaxLength(128);
            entity.Property(item => item.Description).HasMaxLength(512);
            entity.Property(item => item.ConfigurationJson).HasColumnType("jsonb");
            entity.Property(item => item.TimeZoneId).HasMaxLength(64);
            entity.Property(item => item.Connectivity).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ClockSynchronization).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ClockSynchronization.Unknown);
            entity.Property(item => item.ClockConsecutiveDrifts).HasDefaultValue(0);
            entity.Property(item => item.ClockConsecutiveSynchronizations).HasDefaultValue(0);
            entity.Property(item => item.ClockDriftSinceAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastClockOffsetMilliseconds).HasColumnType("integer");
            entity.Property(item => item.LastClockObservedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastVerifiedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.SuspectedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastStateChangedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<DevicePluginEntity>().WithMany().HasForeignKey(item => item.DevicePluginId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecorderEndpointEntity>(entity =>
        {
            entity.ToTable("recorder_endpoints");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.RecorderId, item.Protocol }).IsUnique();
            entity.Property(item => item.Protocol).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Host).HasMaxLength(255);
            entity.Property(item => item.CertificateThumbprint).HasMaxLength(128);
            entity.Property(item => item.CredentialReference).HasMaxLength(256);
            entity.HasIndex(item => item.CredentialId);
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DeviceCredentialEntity>().WithMany().HasForeignKey(item => item.CredentialId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("CK_recorder_endpoints_valid_port", "\"Port\" BETWEEN 1 AND 65535"));
        });

        modelBuilder.Entity<DeviceCredentialEntity>(entity =>
        {
            entity.ToTable("device_credentials");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(256);
            entity.Property(item => item.ProtectionMode).HasConversion<string>().HasMaxLength(64);
            entity.Property(item => item.Ciphertext).HasColumnType("bytea");
            entity.Property(item => item.KeyVersion).HasMaxLength(64);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RotatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DisabledAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastVerifiedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastVerificationError).HasMaxLength(256);
        });

        modelBuilder.Entity<DeviceCredentialVersionEntity>(entity =>
        {
            entity.ToTable("device_credential_versions");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.CredentialId, item.Version }).IsUnique();
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RetiredAt).HasColumnType("timestamp with time zone");
            entity.HasOne<DeviceCredentialEntity>().WithMany().HasForeignKey(item => item.CredentialId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceCredentialEnvelopeEntity>(entity =>
        {
            entity.ToTable("device_credential_envelopes");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.CredentialVersionId, item.EdgeAgentId }).IsUnique();
            entity.Property(item => item.KeyId).HasMaxLength(128);
            entity.Property(item => item.KeyEncryptionAlgorithm).HasMaxLength(64);
            entity.Property(item => item.ContentEncryptionAlgorithm).HasMaxLength(64);
            entity.Property(item => item.EncryptedKey).HasColumnType("bytea");
            entity.Property(item => item.InitializationVector).HasColumnType("bytea");
            entity.Property(item => item.Ciphertext).HasColumnType("bytea");
            entity.Property(item => item.AuthenticationTag).HasColumnType("bytea");
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastVerifiedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastVerificationError).HasMaxLength(256);
            entity.HasOne<DeviceCredentialVersionEntity>().WithMany().HasForeignKey(item => item.CredentialVersionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EdgeAgentEntity>().WithMany().HasForeignKey(item => item.EdgeAgentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceWorkerEntity>(entity =>
        {
            entity.ToTable("device_workers");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.TokenHash).HasMaxLength(128);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DisabledAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastSeenAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<EdgeAgentEntity>(entity =>
        {
            entity.ToTable("edge_agents");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.HasIndex(item => item.DeviceWorkerId).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.Platform).HasMaxLength(32);
            entity.Property(item => item.AgentVersion).HasMaxLength(64);
            entity.Property(item => item.PublicKeyId).HasMaxLength(128);
            entity.Property(item => item.SubjectPublicKeyInfoBase64).HasMaxLength(8192);
            entity.Property(item => item.CapabilitiesJson).HasColumnType("jsonb");
            entity.Property(item => item.ServiceStatusJson).HasColumnType("jsonb");
            entity.Property(item => item.LastDiagnosticMessage).HasMaxLength(512);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastSeenAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastDiagnosticAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DisabledAt).HasColumnType("timestamp with time zone");
            entity.HasOne<DeviceWorkerEntity>().WithMany().HasForeignKey(item => item.DeviceWorkerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EdgeAgentEnrollmentEntity>(entity =>
        {
            entity.ToTable("edge_agent_enrollments");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.CodeHash).IsUnique();
            entity.HasIndex(item => item.UsedByAgentId);
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.CodeHash).HasMaxLength(128);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.UsedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<EdgeAgentEntity>().WithMany().HasForeignKey(item => item.UsedByAgentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EdgeAgentConfigurationEntity>(entity =>
        {
            entity.ToTable("edge_agent_configurations");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.EdgeAgentId, item.Version }).IsUnique();
            entity.Property(item => item.ConfigurationJson).HasColumnType("jsonb");
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.FailureSummary).HasMaxLength(512);
            entity.Property(item => item.PublishedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.AppliedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<EdgeAgentEntity>().WithMany().HasForeignKey(item => item.EdgeAgentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlatformOperationEntity>(entity =>
        {
            entity.ToTable("platform_operations");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.Status, item.RequestedAt });
            entity.Property(item => item.OperationType).HasMaxLength(64);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.Summary).HasMaxLength(256);
            entity.Property(item => item.DetailsJson).HasColumnType("jsonb");
            entity.Property(item => item.RequestedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<EdgeAgentEntity>().WithMany().HasForeignKey(item => item.EdgeAgentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReleaseCatalogEntity>(entity =>
        {
            entity.ToTable("release_catalog");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProductVersion, item.Channel }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.PublishedAt });
            entity.Property(item => item.ProductVersion).HasMaxLength(64);
            entity.Property(item => item.Channel).HasMaxLength(32);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.SigningPublicKeyId).HasMaxLength(128);
            entity.Property(item => item.DescriptorJson).HasColumnType("jsonb");
            entity.Property(item => item.SignatureBase64).HasMaxLength(32768);
            entity.Property(item => item.PublishedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<UpgradePlanEntity>(entity =>
        {
            entity.ToTable("upgrade_plans");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.Status, item.RequestedAt });
            entity.Property(item => item.TargetScope).HasMaxLength(32);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.RequestedBy).HasMaxLength(128);
            entity.Property(item => item.FailureSummary).HasMaxLength(512);
            entity.Property(item => item.RequestedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<ReleaseCatalogEntity>().WithMany().HasForeignKey(item => item.ReleaseCatalogId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UpgradeTargetEntity>(entity =>
        {
            entity.ToTable("upgrade_targets");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.UpgradePlanId, item.Batch, item.Status });
            entity.HasIndex(item => item.EdgeAgentId);
            entity.Property(item => item.Component).HasMaxLength(32);
            entity.Property(item => item.TargetType).HasMaxLength(32);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.ExpectedVersion).HasMaxLength(64);
            entity.Property(item => item.PreviousVersion).HasMaxLength(64);
            entity.Property(item => item.PreviousArtifactJson).HasColumnType("jsonb");
            entity.Property(item => item.FailureSummary).HasMaxLength(512);
            entity.Property(item => item.RequestedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.StableSince).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<UpgradePlanEntity>().WithMany().HasForeignKey(item => item.UpgradePlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<EdgeAgentEntity>().WithMany().HasForeignKey(item => item.EdgeAgentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DeviceWorkerAssignmentEntity>(entity =>
        {
            entity.ToTable("device_worker_assignments");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.RecorderId).IsUnique();
            entity.HasIndex(item => new { item.WorkerId, item.RecorderId }).IsUnique();
            entity.HasOne<DeviceWorkerEntity>().WithMany().HasForeignKey(item => item.WorkerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RegionEntity>().WithMany().HasForeignKey(item => item.DefaultRegionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceWorkerOperationStatusEntity>(entity =>
        {
            entity.ToTable("device_worker_operation_statuses");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_device_worker_operation_statuses_ready_failure",
                "(\"IsReady\" AND \"FailureKind\" IS NULL) OR (NOT \"IsReady\" AND \"FailureKind\" IS NOT NULL)"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.WorkerId, item.RecorderId, item.OperationType }).IsUnique();
            entity.HasIndex(item => new { item.RecorderId, item.OperationType, item.ReportedAt });
            entity.Property(item => item.OperationType).HasMaxLength(64);
            entity.Property(item => item.FailureKind).HasMaxLength(64);
            entity.Property(item => item.ReportedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<DeviceWorkerEntity>().WithMany().HasForeignKey(item => item.WorkerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CameraEntity>(entity =>
        {
            entity.ToTable("cameras");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Code).IsUnique();
            entity.HasIndex(item => new { item.RecorderId, item.InputChannelNumber }).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(64);
            entity.Property(item => item.Alias).HasMaxLength(128);
            entity.Property(item => item.StreamingChannelMap).HasMaxLength(512);
            entity.Property(item => item.SourceType).HasMaxLength(32).HasDefaultValue(CameraSourceTypes.RecorderChannel);
            entity.Property(item => item.ProvisioningMode).HasMaxLength(32).HasDefaultValue(CameraProvisioningModes.Manual);
            entity.Property(item => item.Manufacturer).HasMaxLength(64);
            entity.Property(item => item.Model).HasMaxLength(128);
            entity.Property(item => item.SerialNumber).HasMaxLength(128);
            entity.Property(item => item.Description).HasMaxLength(512);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.Connectivity).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.SuspectedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastStateChangedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RegionEntity>().WithMany().HasForeignKey(item => item.RegionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecorderCapabilityEntity>(entity =>
        {
            entity.ToTable("recorder_capabilities");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.RecorderId, item.Version }).IsUnique();
            entity.Property(item => item.Version).HasMaxLength(128);
            entity.Property(item => item.CapabilityJson).HasColumnType("jsonb");
            entity.Property(item => item.VerifiedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecorderClockObservationEntity>(entity =>
        {
            entity.ToTable("recorder_clock_observations");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_recorder_clock_observations_time_window",
                "\"ResponseReceivedAt\" >= \"RequestStartedAt\" AND \"ResponseReceivedAt\" <= \"RequestStartedAt\" + INTERVAL '2 minutes'"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.RecorderId, item.ResponseReceivedAt });
            entity.HasIndex(item => item.ResponseReceivedAt);
            entity.Property(item => item.DeviceTime).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RequestStartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ResponseReceivedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ClockSynchronization).HasConversion<string>().HasMaxLength(32);
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HealthStateEventEntity>(entity =>
        {
            entity.ToTable("health_state_events");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ResourceType, item.ResourceId, item.OccurredAt });
            entity.Property(item => item.ResourceType).HasMaxLength(32);
            entity.Property(item => item.PreviousConnectivity).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.CurrentConnectivity).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.DetailsJson).HasColumnType("jsonb");
            entity.Property(item => item.OccurredAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<StreamAssignmentEntity>(entity =>
        {
            entity.ToTable("stream_assignments");
            entity.ToTable(table => table.HasCheckConstraint("CK_stream_assignments_ReferenceCount", "\"ReferenceCount\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.StreamKey).IsUnique();
            entity.Property(item => item.StreamKey).HasMaxLength(160);
            entity.Property(item => item.GatewayName).HasMaxLength(64);
            entity.Property(item => item.LastAccessedAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<StreamSessionEntity>(entity =>
        {
            entity.ToTable("stream_sessions");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_stream_sessions_ActiveClientIdentity",
                "\"RevokedAt\" IS NOT NULL OR (\"UserSessionId\" IS NOT NULL AND \"ClientRequestId\" IS NOT NULL AND \"SlotNumber\" BETWEEN 0 AND 63)"));
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_stream_sessions_OperationRange",
                "(\"Operation\" = 'LiveView' AND \"PlaybackStartedAt\" IS NULL AND \"PlaybackEndedAt\" IS NULL) OR " +
                "(\"Operation\" = 'Playback' AND \"PlaybackStartedAt\" IS NOT NULL AND \"PlaybackEndedAt\" > \"PlaybackStartedAt\" AND \"PlaybackEndedAt\" <= \"PlaybackStartedAt\" + INTERVAL '31 days')"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.UserId, item.ExpiresAt, item.RevokedAt });
            entity.HasIndex(item => new { item.CameraId, item.ExpiresAt, item.RevokedAt });
            entity.HasIndex(item => new { item.UserSessionId, item.ExpiresAt, item.RevokedAt });
            entity.HasIndex(item => new { item.UserSessionId, item.SlotNumber })
                .IsUnique()
                .HasFilter("\"RevokedAt\" IS NULL AND \"UserSessionId\" IS NOT NULL AND \"SlotNumber\" IS NOT NULL");
            entity.HasIndex(item => new { item.UserSessionId, item.ClientRequestId })
                .IsUnique()
                .HasFilter("\"ClientRequestId\" IS NOT NULL");
            entity.Property(item => item.Operation).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Profile).HasMaxLength(16);
            entity.Property(item => item.StreamKey).HasMaxLength(160);
            entity.Property(item => item.GatewayName).HasMaxLength(64);
            entity.Property(item => item.RevocationReason).HasMaxLength(128);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastRenewedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RevokedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.PlaybackStartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.PlaybackEndedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserSessionEntity>().WithMany().HasForeignKey(item => item.UserSessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CameraEntity>().WithMany().HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StreamConnectionTicketEntity>(entity =>
        {
            entity.ToTable("stream_connection_tickets");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.HasIndex(item => new { item.SessionId, item.ExpiresAt, item.ConsumedAt });
            entity.Property(item => item.TokenHash).HasMaxLength(128);
            entity.Property(item => item.GatewayName).HasMaxLength(64);
            entity.Property(item => item.ConsumedByGateway).HasMaxLength(64);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ConsumedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RevokedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<StreamSessionEntity>().WithMany().HasForeignKey(item => item.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PtzControlLeaseEntity>(entity =>
        {
            entity.ToTable("ptz_control_leases");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ptz_control_leases_valid_window",
                "\"ExpiresAt\" > \"AcquiredAt\" AND \"LastSequence\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.LeaseTokenHash).IsUnique();
            entity.HasIndex(item => new { item.CameraId, item.ExpiresAt, item.ReleasedAt, item.RevokedAt });
            entity.HasIndex(item => item.CameraId)
                .IsUnique()
                .HasFilter("\"ReleasedAt\" IS NULL AND \"RevokedAt\" IS NULL");
            entity.Property(item => item.LeaseTokenHash).HasMaxLength(128);
            entity.Property(item => item.RevocationReason).HasMaxLength(128);
            entity.Property(item => item.AcquiredAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastRenewedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ReleasedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RevokedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<CameraEntity>().WithMany().HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserSessionEntity>().WithMany().HasForeignKey(item => item.UserSessionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlaybackExportEntity>(entity =>
        {
            entity.ToTable("playback_exports");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_playback_exports_valid_range",
                "\"EndedAt\" > \"StartedAt\" AND \"EndedAt\" <= \"StartedAt\" + INTERVAL '31 days' AND \"Attempts\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.Status, item.NextAttemptAt, item.LockedUntil });
            entity.HasIndex(item => new { item.CameraId, item.RequestedAt });
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Container).HasMaxLength(16);
            entity.Property(item => item.FailureCode).HasMaxLength(64);
            entity.Property(item => item.FailureDetail).HasMaxLength(1024);
            entity.Property(item => item.LockedBy).HasMaxLength(128);
            entity.Property(item => item.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RequestedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ProcessingStartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CancellationRequestedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.NextAttemptAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LockedUntil).HasColumnType("timestamp with time zone");
            entity.HasOne<CameraEntity>().WithMany().HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExportArtifactEntity>(entity =>
        {
            entity.ToTable("export_artifacts");
            entity.ToTable(table => table.HasCheckConstraint("CK_export_artifacts_size", "\"SizeBytes\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.PlaybackExportId).IsUnique();
            entity.HasIndex(item => new { item.ExpiresAt, item.DeletedAt });
            entity.Property(item => item.StorageKey).HasMaxLength(512);
            entity.Property(item => item.FileName).HasMaxLength(256);
            entity.Property(item => item.ContentType).HasMaxLength(128);
            entity.Property(item => item.Sha256).HasMaxLength(64);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DeletedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<PlaybackExportEntity>().WithMany().HasForeignKey(item => item.PlaybackExportId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExportDownloadAuditEntity>(entity =>
        {
            entity.ToTable("export_download_audits");
            entity.ToTable(table => table.HasCheckConstraint("CK_export_download_audits_bytes", "\"BytesServed\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ExportArtifactId, item.StartedAt });
            entity.HasIndex(item => new { item.UserId, item.StartedAt });
            entity.Property(item => item.Result).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ClientAddressHash).HasMaxLength(128);
            entity.Property(item => item.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<ExportArtifactEntity>().WithMany().HasForeignKey(item => item.ExportArtifactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserSessionEntity>().WithMany().HasForeignKey(item => item.UserSessionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlatformBackupEntity>(entity =>
        {
            entity.ToTable("platform_backups");
            entity.ToTable(table => table.HasCheckConstraint("CK_platform_backups_size", "\"SizeBytes\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.Kind, item.CreatedAt });
            entity.HasIndex(item => new { item.Status, item.RetainUntil });
            entity.Property(item => item.Kind).HasMaxLength(32);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.StorageKey).HasMaxLength(512);
            entity.Property(item => item.FileName).HasMaxLength(256);
            entity.Property(item => item.Sha256).HasMaxLength(64);
            entity.Property(item => item.FailureDetail).HasMaxLength(1024);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.RetainUntil).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastRestoredAt).HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<EdgeCommandEntity>(entity =>
        {
            entity.ToTable("edge_commands");
            entity.ToTable(table => table.HasCheckConstraint("CK_edge_commands_attempts", "\"Attempts\" >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.WorkerId, item.CompletedAt, item.DeadLetteredAt, item.NextAttemptAt, item.LockedUntil });
            entity.HasIndex(item => new { item.AggregateType, item.AggregateId });
            entity.Property(item => item.CommandType).HasMaxLength(64);
            entity.Property(item => item.AggregateType).HasMaxLength(64);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.Property(item => item.ResultJson).HasColumnType("jsonb");
            entity.Property(item => item.LockedBy).HasMaxLength(128);
            entity.Property(item => item.LastError).HasMaxLength(1024);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.NextAttemptAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LockedUntil).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DeadLetteredAt).HasColumnType("timestamp with time zone");
            entity.HasOne<DeviceWorkerEntity>().WithMany().HasForeignKey(item => item.WorkerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RecorderEntity>().WithMany().HasForeignKey(item => item.RecorderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecordingSearchEntity>(entity =>
        {
            entity.ToTable("recording_searches");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_recording_searches_valid_range",
                "\"EndedAt\" > \"StartedAt\" AND \"EndedAt\" <= \"StartedAt\" + INTERVAL '31 days' AND \"MaxResults\" BETWEEN 1 AND 200"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.UserSessionId, item.ClientRequestId }).IsUnique();
            entity.HasIndex(item => new { item.UserId, item.CreatedAt });
            entity.HasIndex(item => new { item.Status, item.ExpiresAt });
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ResultJson).HasColumnType("jsonb");
            entity.Property(item => item.FailureKind).HasMaxLength(64);
            entity.Property(item => item.StartedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.EndedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.CompletedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ExpiresAt).HasColumnType("timestamp with time zone");
            entity.HasOne<CameraEntity>().WithMany().HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserSessionEntity>().WithMany().HasForeignKey(item => item.UserSessionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OutboxEventEntity>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProcessedAt, item.DeadLetteredAt, item.NextAttemptAt, item.LockedUntil });
            entity.Property(item => item.EventType).HasMaxLength(128);
            entity.Property(item => item.AggregateType).HasMaxLength(64);
            entity.Property(item => item.PayloadJson).HasColumnType("jsonb");
            entity.Property(item => item.OccurredAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.NextAttemptAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ProcessedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.DeadLetteredAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LockedUntil).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LockedBy).HasMaxLength(128);
            entity.Property(item => item.LastError).HasMaxLength(1024);
        });

        modelBuilder.Entity<NotificationChannelEntity>(entity =>
        {
            entity.ToTable("notification_channels");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ConfigurationJson).HasColumnType("jsonb");
            entity.Property(item => item.SecretReference).HasMaxLength(128);
            entity.Property(item => item.WebhookCiphertext).HasColumnType("bytea");
            entity.Property(item => item.WebhookProtectionMode).HasConversion<string>().HasMaxLength(64);
            entity.Property(item => item.WebhookKeyVersion).HasMaxLength(64);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_notification_channels_webhook_secret_complete",
                "(\"WebhookCiphertext\" IS NULL AND \"WebhookProtectionMode\" IS NULL AND \"WebhookKeyVersion\" IS NULL) OR (\"WebhookCiphertext\" IS NOT NULL AND \"WebhookProtectionMode\" IS NOT NULL AND \"WebhookKeyVersion\" IS NOT NULL)"));
        });

        modelBuilder.Entity<AlertRuleEntity>(entity =>
        {
            entity.ToTable("alert_rules");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Name).IsUnique();
            entity.Property(item => item.Name).HasMaxLength(128);
            entity.Property(item => item.ResourceType).HasMaxLength(32);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<RegionEntity>().WithMany().HasForeignKey(item => item.RegionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AlertRuleChannelEntity>(entity =>
        {
            entity.ToTable("alert_rule_channels");
            entity.HasKey(item => new { item.AlertRuleId, item.NotificationChannelId });
            entity.HasOne<AlertRuleEntity>().WithMany().HasForeignKey(item => item.AlertRuleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NotificationChannelEntity>().WithMany().HasForeignKey(item => item.NotificationChannelId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertIncidentEntity>(entity =>
        {
            entity.ToTable("alert_incidents");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ResourceType, item.ResourceId, item.IncidentType })
                .IsUnique()
                .HasFilter("\"ResolvedAt\" IS NULL");
            entity.Property(item => item.ResourceType).HasMaxLength(32);
            entity.Property(item => item.ResourceName).HasMaxLength(256);
            entity.Property(item => item.IncidentType).HasMaxLength(64);
            entity.Property(item => item.OpenedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ResolvedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastObservedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<RegionEntity>().WithMany().HasForeignKey(item => item.RegionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificationDeliveryEntity>(entity =>
        {
            entity.ToTable("notification_deliveries");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.AlertIncidentId, item.NotificationChannelId, item.EventType }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.NextAttemptAt, item.LockedUntil });
            entity.Property(item => item.EventType).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.NextAttemptAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.SentAt).HasColumnType("timestamp with time zone");
            entity.Property(item => item.LastError).HasMaxLength(1024);
            entity.Property(item => item.LockedBy).HasMaxLength(128);
            entity.Property(item => item.LockedUntil).HasColumnType("timestamp with time zone");
            entity.HasOne<AlertIncidentEntity>().WithMany().HasForeignKey(item => item.AlertIncidentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<NotificationChannelEntity>().WithMany().HasForeignKey(item => item.NotificationChannelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.OccurredAt, item.ActorUserId });
            entity.Property(item => item.Action).HasMaxLength(128);
            entity.Property(item => item.ResourceType).HasMaxLength(64);
            entity.Property(item => item.ResourceId).HasMaxLength(128);
            entity.Property(item => item.DetailsJson).HasColumnType("jsonb");
            entity.Property(item => item.OccurredAt).HasColumnType("timestamp with time zone");
        });
    }
}

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool RequiresPasswordChange { get; set; }
    public bool IsSystemAdministrator { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class RoleEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SystemPermissions { get; set; }
}

public sealed class UserRoleEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

public sealed class UserSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class RoleCameraScopeEntity
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public Guid? RegionId { get; set; }
    public Guid? CameraId { get; set; }
    public CameraPermission Permissions { get; set; }
}

public sealed class RegionEntity
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class RecorderEntity
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string AdapterType { get; set; } = string.Empty;
    public Guid? DevicePluginId { get; set; }
    public string DeviceKind { get; set; } = DeviceKinds.Recorder;
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Description { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string TimeZoneId { get; set; } = "Asia/Shanghai";
    public DateTimeOffset CreatedAt { get; set; }
    public CameraConnectivity Connectivity { get; set; } = CameraConnectivity.Unknown;
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public ClockSynchronization ClockSynchronization { get; set; } = ClockSynchronization.Unknown;
    public int ClockConsecutiveDrifts { get; set; }
    public int ClockConsecutiveSynchronizations { get; set; }
    public DateTimeOffset? ClockDriftSinceAt { get; set; }
    public int? LastClockOffsetMilliseconds { get; set; }
    public DateTimeOffset? LastClockObservedAt { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset? SuspectedAt { get; set; }
    public DateTimeOffset? LastStateChangedAt { get; set; }
}

public sealed class DevicePluginEntity
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ProtocolType { get; set; } = string.Empty;
    public string RuntimeType { get; set; } = string.Empty;
    public string AdapterType { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Description { get; set; }
    public string ManifestJson { get; set; } = "{}";
    public string PackageHash { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset InstalledAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum RecorderEndpointProtocol
{
    Rtsp,
    Onvif
}

public sealed class RecorderEndpointEntity
{
    public Guid Id { get; set; }
    public Guid RecorderId { get; set; }
    public RecorderEndpointProtocol Protocol { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseTls { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string CredentialReference { get; set; } = string.Empty;
    public Guid? CredentialId { get; set; }
}

public enum DeviceCredentialProtectionMode
{
    WindowsDpapiLocalMachine,
    AgentEnvelope
}

public sealed class DeviceCredentialEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DeviceCredentialProtectionMode ProtectionMode { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public string KeyVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public string? LastVerificationError { get; set; }
}

public sealed class DeviceCredentialVersionEntity
{
    public Guid Id { get; set; }
    public Guid CredentialId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

public sealed class DeviceCredentialEnvelopeEntity
{
    public Guid Id { get; set; }
    public Guid CredentialVersionId { get; set; }
    public Guid EdgeAgentId { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string KeyEncryptionAlgorithm { get; set; } = string.Empty;
    public string ContentEncryptionAlgorithm { get; set; } = string.Empty;
    public byte[] EncryptedKey { get; set; } = [];
    public byte[] InitializationVector { get; set; } = [];
    public byte[] Ciphertext { get; set; } = [];
    public byte[] AuthenticationTag { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public string? LastVerificationError { get; set; }
}

public sealed class DeviceWorkerEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class EdgeAgentEntity
{
    public Guid Id { get; set; }
    public Guid DeviceWorkerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = "linux";
    public string AgentVersion { get; set; } = string.Empty;
    public string PublicKeyId { get; set; } = string.Empty;
    public string SubjectPublicKeyInfoBase64 { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "{}";
    public string ServiceStatusJson { get; set; } = "{}";
    public int ConfigurationVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? LastDiagnosticAt { get; set; }
    public bool? LastDiagnosticSucceeded { get; set; }
    public string? LastDiagnosticMessage { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class EdgeAgentEnrollmentEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedByAgentId { get; set; }
}

public sealed class EdgeAgentConfigurationEntity
{
    public Guid Id { get; set; }
    public Guid EdgeAgentId { get; set; }
    public int Version { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string Status { get; set; } = "published";
    public string? FailureSummary { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class PlatformOperationEntity
{
    public Guid Id { get; set; }
    public Guid? EdgeAgentId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class DeviceWorkerAssignmentEntity
{
    public Guid Id { get; set; }
    public Guid WorkerId { get; set; }
    public Guid RecorderId { get; set; }
    public Guid DefaultRegionId { get; set; }
}

public sealed class DeviceWorkerOperationStatusEntity
{
    public Guid Id { get; set; }
    public Guid WorkerId { get; set; }
    public Guid RecorderId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public string? FailureKind { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
}

public sealed class CameraEntity
{
    public Guid Id { get; set; }
    public Guid RecorderId { get; set; }
    public Guid RegionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public int InputChannelNumber { get; set; }
    public string StreamingChannelMap { get; set; } = "{}";
    public string SourceType { get; set; } = CameraSourceTypes.RecorderChannel;
    public string ProvisioningMode { get; set; } = CameraProvisioningModes.Manual;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool SupportsPtz { get; set; }
    public CameraConnectivity Connectivity { get; set; } = CameraConnectivity.Unknown;
    public int ConsecutiveFailures { get; set; }
    public int ConsecutiveSuccesses { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset? SuspectedAt { get; set; }
    public DateTimeOffset? LastStateChangedAt { get; set; }
}

public sealed class RecorderCapabilityEntity
{
    public Guid Id { get; set; }
    public Guid RecorderId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string CapabilityJson { get; set; } = "{}";
    public DateTimeOffset VerifiedAt { get; set; }
}

public enum ClockSynchronization
{
    Unknown,
    Synchronized,
    Drifted
}

public sealed class RecorderClockObservationEntity
{
    public Guid Id { get; set; }
    public Guid RecorderId { get; set; }
    public DateTimeOffset DeviceTime { get; set; }
    public DateTimeOffset RequestStartedAt { get; set; }
    public DateTimeOffset ResponseReceivedAt { get; set; }
    public int OffsetMilliseconds { get; set; }
    public ClockSynchronization ClockSynchronization { get; set; }
}

public sealed class HealthStateEventEntity
{
    public Guid Id { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public CameraConnectivity PreviousConnectivity { get; set; }
    public CameraConnectivity CurrentConnectivity { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class StreamAssignmentEntity
{
    public Guid Id { get; set; }
    public string StreamKey { get; set; } = string.Empty;
    public string GatewayName { get; set; } = string.Empty;
    public int ReferenceCount { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
}

public sealed class StreamSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? UserSessionId { get; set; }
    public Guid? ClientRequestId { get; set; }
    public int? SlotNumber { get; set; }
    public Guid CameraId { get; set; }
    public CameraPermission Operation { get; set; }
    public string Profile { get; set; } = string.Empty;
    public string StreamKey { get; set; } = string.Empty;
    public string GatewayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastRenewedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public DateTimeOffset? PlaybackStartedAt { get; set; }
    public DateTimeOffset? PlaybackEndedAt { get; set; }
}

public sealed class StreamConnectionTicketEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string GatewayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? ConsumedByGateway { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PtzControlLeaseEntity
{
    public Guid Id { get; set; }
    public Guid CameraId { get; set; }
    public Guid UserId { get; set; }
    public Guid UserSessionId { get; set; }
    public string LeaseTokenHash { get; set; } = string.Empty;
    public long LastSequence { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset LastRenewedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
}

public enum PlaybackExportStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
    Expired
}

public sealed class PlaybackExportEntity
{
    public Guid Id { get; set; }
    public Guid CameraId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public string Container { get; set; } = "mp4";
    public PlaybackExportStatus Status { get; set; } = PlaybackExportStatus.Queued;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDetail { get; set; }
}

public sealed class ExportArtifactEntity
{
    public Guid Id { get; set; }
    public Guid PlaybackExportId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "video/mp4";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum ExportDownloadResult
{
    Started,
    Completed,
    Cancelled,
    Failed
}

public sealed class ExportDownloadAuditEntity
{
    public Guid Id { get; set; }
    public Guid ExportArtifactId { get; set; }
    public Guid UserId { get; set; }
    public Guid? UserSessionId { get; set; }
    public string ClientAddressHash { get; set; } = string.Empty;
    public long BytesServed { get; set; }
    public ExportDownloadResult Result { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ReleaseCatalogEntity
{
    public Guid Id { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public string Status { get; set; } = "available";
    public string DescriptorJson { get; set; } = "{}";
    public string SignatureBase64 { get; set; } = string.Empty;
    public string SigningPublicKeyId { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UpgradePlanEntity
{
    public Guid Id { get; set; }
    public Guid ReleaseCatalogId { get; set; }
    public string TargetScope { get; set; } = "edge";
    public string Status { get; set; } = "draft";
    public string RequestedBy { get; set; } = string.Empty;
    public string? FailureSummary { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class UpgradeTargetEntity
{
    public Guid Id { get; set; }
    public Guid UpgradePlanId { get; set; }
    public Guid? EdgeAgentId { get; set; }
    public Guid? PlatformOperationId { get; set; }
    public string TargetType { get; set; } = "edge";
    public string Component { get; set; } = string.Empty;
    public int Batch { get; set; }
    public string Status { get; set; } = "pending";
    public string ExpectedVersion { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public string? PreviousArtifactJson { get; set; }
    public string? FailureSummary { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StableSince { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class PlatformBackupEntity
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = "available";
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetainUntil { get; set; }
    public DateTimeOffset? LastRestoredAt { get; set; }
    public string? FailureDetail { get; set; }
}

public sealed class EdgeCommandEntity
{
    public Guid Id { get; set; }
    public Guid WorkerId { get; set; }
    public Guid RecorderId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
    public string? LastError { get; set; }
}

public enum RecordingSearchStatus
{
    Queued,
    Completed,
    Failed,
    Expired
}

public sealed class RecordingSearchEntity
{
    public Guid Id { get; set; }
    public Guid CameraId { get; set; }
    public Guid UserId { get; set; }
    public Guid UserSessionId { get; set; }
    public Guid ClientRequestId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int MaxResults { get; set; }
    public RecordingSearchStatus Status { get; set; } = RecordingSearchStatus.Queued;
    public string? ResultJson { get; set; }
    public string? FailureKind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class OutboxEventEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
}

public enum NotificationChannelType
{
    Email,
    WeComWebhook
}

public enum NotificationChannelWebhookProtectionMode
{
    AesGcmRuntimeKey
}

public sealed class NotificationChannelEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public NotificationChannelType Type { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string SecretReference { get; set; } = string.Empty;
    public byte[]? WebhookCiphertext { get; set; }
    public NotificationChannelWebhookProtectionMode? WebhookProtectionMode { get; set; }
    public string? WebhookKeyVersion { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AlertRuleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ResourceType { get; set; } = "*";
    public Guid? RegionId { get; set; }
    public bool NotifyOnRecovery { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AlertRuleChannelEntity
{
    public Guid AlertRuleId { get; set; }
    public Guid NotificationChannelId { get; set; }
}

public sealed class AlertIncidentEntity
{
    public Guid Id { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public Guid? RegionId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string IncidentType { get; set; } = "offline";
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum NotificationEventType
{
    Opened,
    Recovered
}

public enum NotificationDeliveryStatus
{
    Pending,
    Sent,
    Failed,
    DeadLettered
}

public sealed class NotificationDeliveryEntity
{
    public Guid Id { get; set; }
    public Guid AlertIncidentId { get; set; }
    public Guid NotificationChannelId { get; set; }
    public NotificationEventType EventType { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public sealed class AuditLogEntity
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}
