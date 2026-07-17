using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoPlatform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpenSourceInitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProtectionMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    KeyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerificationError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_plugins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProtocolType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuntimeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AdapterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    PackageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_plugins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_workers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_workers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "edge_agent_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_agent_enrollments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "health_state_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousConnectivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentConnectivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_state_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    SecretReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WebhookCiphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    WebhookProtectionMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    WebhookKeyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_channels", x => x.Id);
                    table.CheckConstraint("CK_notification_channels_webhook_secret_complete", "(\"WebhookCiphertext\" IS NULL AND \"WebhookProtectionMode\" IS NULL AND \"WebhookKeyVersion\" IS NULL) OR (\"WebhookCiphertext\" IS NOT NULL AND \"WebhookProtectionMode\" IS NOT NULL AND \"WebhookKeyVersion\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_regions_regions_ParentId",
                        column: x => x.ParentId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SystemPermissions = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stream_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    GatewayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferenceCount = table.Column<int>(type: "integer", nullable: false),
                    LastAccessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_assignments", x => x.Id);
                    table.CheckConstraint("CK_stream_assignments_ReferenceCount", "\"ReferenceCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequiresPasswordChange = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystemAdministrator = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "device_credential_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_credential_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_credential_versions_device_credentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "device_credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recorders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AdapterType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DevicePluginId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "recorder"),
                    SerialNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FirmwareVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Connectivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveSuccesses = table.Column<int>(type: "integer", nullable: false),
                    ClockSynchronization = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Unknown"),
                    ClockConsecutiveDrifts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ClockConsecutiveSynchronizations = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ClockDriftSinceAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastClockOffsetMilliseconds = table.Column<int>(type: "integer", nullable: true),
                    LastClockObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuspectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStateChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recorders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recorders_device_plugins_DevicePluginId",
                        column: x => x.DevicePluginId,
                        principalTable: "device_plugins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "edge_agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceWorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AgentVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectPublicKeyInfoBase64 = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ServiceStatusJson = table.Column<string>(type: "jsonb", nullable: false),
                    ConfigurationVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDiagnosticAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDiagnosticSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastDiagnosticMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_agents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edge_agents_device_workers_DeviceWorkerId",
                        column: x => x.DeviceWorkerId,
                        principalTable: "device_workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alert_incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IncidentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_incidents_regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotifyOnRecovery = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alert_rules_regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cameras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputChannelNumber = table.Column<int>(type: "integer", nullable: false),
                    StreamingChannelMap = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "recorder-channel"),
                    ProvisioningMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "manual"),
                    Manufacturer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupportsPtz = table.Column<bool>(type: "boolean", nullable: false),
                    Connectivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveSuccesses = table.Column<int>(type: "integer", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuspectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStateChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cameras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cameras_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cameras_regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_worker_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultRegionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_worker_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_worker_assignments_device_workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "device_workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_worker_assignments_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_worker_assignments_regions_DefaultRegionId",
                        column: x => x.DefaultRegionId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_worker_operation_statuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false),
                    FailureKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_worker_operation_statuses", x => x.Id);
                    table.CheckConstraint("CK_device_worker_operation_statuses_ready_failure", "(\"IsReady\" AND \"FailureKind\" IS NULL) OR (NOT \"IsReady\" AND \"FailureKind\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_device_worker_operation_statuses_device_workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "device_workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_worker_operation_statuses_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edge_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_commands", x => x.Id);
                    table.CheckConstraint("CK_edge_commands_attempts", "\"Attempts\" >= 0");
                    table.ForeignKey(
                        name: "FK_edge_commands_device_workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "device_workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_edge_commands_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recorder_capabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CapabilityJson = table.Column<string>(type: "jsonb", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recorder_capabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recorder_capabilities_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recorder_clock_observations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResponseReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OffsetMilliseconds = table.Column<int>(type: "integer", nullable: false),
                    ClockSynchronization = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recorder_clock_observations", x => x.Id);
                    table.CheckConstraint("CK_recorder_clock_observations_time_window", "\"ResponseReceivedAt\" >= \"RequestStartedAt\" AND \"ResponseReceivedAt\" <= \"RequestStartedAt\" + INTERVAL '2 minutes'");
                    table.ForeignKey(
                        name: "FK_recorder_clock_observations_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recorder_endpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecorderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    UseTls = table.Column<bool>(type: "boolean", nullable: false),
                    CertificateThumbprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CredentialReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recorder_endpoints", x => x.Id);
                    table.CheckConstraint("CK_recorder_endpoints_valid_port", "\"Port\" BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "FK_recorder_endpoints_device_credentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "device_credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recorder_endpoints_recorders_RecorderId",
                        column: x => x.RecorderId,
                        principalTable: "recorders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_credential_envelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EdgeAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    KeyEncryptionAlgorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentEncryptionAlgorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    InitializationVector = table.Column<byte[]>(type: "bytea", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    AuthenticationTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerificationError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_credential_envelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_credential_envelopes_device_credential_versions_Cred~",
                        column: x => x.CredentialVersionId,
                        principalTable: "device_credential_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_device_credential_envelopes_edge_agents_EdgeAgentId",
                        column: x => x.EdgeAgentId,
                        principalTable: "edge_agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "edge_agent_configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EdgeAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_edge_agent_configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_edge_agent_configurations_edge_agents_EdgeAgentId",
                        column: x => x.EdgeAgentId,
                        principalTable: "edge_agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EdgeAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_platform_operations_edge_agents_EdgeAgentId",
                        column: x => x.EdgeAgentId,
                        principalTable: "edge_agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertIncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_deliveries_alert_incidents_AlertIncidentId",
                        column: x => x.AlertIncidentId,
                        principalTable: "alert_incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notification_deliveries_notification_channels_NotificationC~",
                        column: x => x.NotificationChannelId,
                        principalTable: "notification_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_rule_channels",
                columns: table => new
                {
                    AlertRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationChannelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_rule_channels", x => new { x.AlertRuleId, x.NotificationChannelId });
                    table.ForeignKey(
                        name: "FK_alert_rule_channels_alert_rules_AlertRuleId",
                        column: x => x.AlertRuleId,
                        principalTable: "alert_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_alert_rule_channels_notification_channels_NotificationChann~",
                        column: x => x.NotificationChannelId,
                        principalTable: "notification_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playback_exports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Container = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playback_exports", x => x.Id);
                    table.CheckConstraint("CK_playback_exports_valid_range", "\"EndedAt\" > \"StartedAt\" AND \"EndedAt\" <= \"StartedAt\" + INTERVAL '31 days' AND \"Attempts\" >= 0");
                    table.ForeignKey(
                        name: "FK_playback_exports_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_playback_exports_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ptz_control_leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRenewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ptz_control_leases", x => x.Id);
                    table.CheckConstraint("CK_ptz_control_leases_valid_window", "\"ExpiresAt\" > \"AcquiredAt\" AND \"LastSequence\" >= 0");
                    table.ForeignKey(
                        name: "FK_ptz_control_leases_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ptz_control_leases_user_sessions_UserSessionId",
                        column: x => x.UserSessionId,
                        principalTable: "user_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ptz_control_leases_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recording_searches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaxResults = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    FailureKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recording_searches", x => x.Id);
                    table.CheckConstraint("CK_recording_searches_valid_range", "\"EndedAt\" > \"StartedAt\" AND \"EndedAt\" <= \"StartedAt\" + INTERVAL '31 days' AND \"MaxResults\" BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_recording_searches_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recording_searches_user_sessions_UserSessionId",
                        column: x => x.UserSessionId,
                        principalTable: "user_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recording_searches_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_camera_scopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: true),
                    Permissions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_camera_scopes", x => x.Id);
                    table.CheckConstraint("CK_role_camera_scopes_exactly_one_target", "(\"RegionId\" IS NOT NULL AND \"CameraId\" IS NULL) OR (\"RegionId\" IS NULL AND \"CameraId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_role_camera_scopes_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_camera_scopes_regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_camera_scopes_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stream_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SlotNumber = table.Column<int>(type: "integer", nullable: true),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Profile = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StreamKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    GatewayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRenewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PlaybackStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlaybackEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_sessions", x => x.Id);
                    table.CheckConstraint("CK_stream_sessions_ActiveClientIdentity", "\"RevokedAt\" IS NOT NULL OR (\"UserSessionId\" IS NOT NULL AND \"ClientRequestId\" IS NOT NULL AND \"SlotNumber\" BETWEEN 0 AND 63)");
                    table.CheckConstraint("CK_stream_sessions_OperationRange", "(\"Operation\" = 'LiveView' AND \"PlaybackStartedAt\" IS NULL AND \"PlaybackEndedAt\" IS NULL) OR (\"Operation\" = 'Playback' AND \"PlaybackStartedAt\" IS NOT NULL AND \"PlaybackEndedAt\" > \"PlaybackStartedAt\" AND \"PlaybackEndedAt\" <= \"PlaybackStartedAt\" + INTERVAL '31 days')");
                    table.ForeignKey(
                        name: "FK_stream_sessions_cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stream_sessions_user_sessions_UserSessionId",
                        column: x => x.UserSessionId,
                        principalTable: "user_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stream_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaybackExportId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_artifacts", x => x.Id);
                    table.CheckConstraint("CK_export_artifacts_size", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_export_artifacts_playback_exports_PlaybackExportId",
                        column: x => x.PlaybackExportId,
                        principalTable: "playback_exports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stream_connection_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GatewayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedByGateway = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stream_connection_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stream_connection_tickets_stream_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "stream_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "export_download_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExportArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientAddressHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BytesServed = table.Column<long>(type: "bigint", nullable: false),
                    Result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_download_audits", x => x.Id);
                    table.CheckConstraint("CK_export_download_audits_bytes", "\"BytesServed\" >= 0");
                    table.ForeignKey(
                        name: "FK_export_download_audits_export_artifacts_ExportArtifactId",
                        column: x => x.ExportArtifactId,
                        principalTable: "export_artifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_download_audits_user_sessions_UserSessionId",
                        column: x => x.UserSessionId,
                        principalTable: "user_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_export_download_audits_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_incidents_RegionId",
                table: "alert_incidents",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_incidents_ResourceType_ResourceId_IncidentType",
                table: "alert_incidents",
                columns: new[] { "ResourceType", "ResourceId", "IncidentType" },
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_alert_rule_channels_NotificationChannelId",
                table: "alert_rule_channels",
                column: "NotificationChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_Name",
                table: "alert_rules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_RegionId",
                table: "alert_rules",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OccurredAt_ActorUserId",
                table: "audit_logs",
                columns: new[] { "OccurredAt", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_cameras_Code",
                table: "cameras",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cameras_RecorderId_InputChannelNumber",
                table: "cameras",
                columns: new[] { "RecorderId", "InputChannelNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cameras_RegionId",
                table: "cameras",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_device_credential_envelopes_CredentialVersionId_EdgeAgentId",
                table: "device_credential_envelopes",
                columns: new[] { "CredentialVersionId", "EdgeAgentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_credential_envelopes_EdgeAgentId",
                table: "device_credential_envelopes",
                column: "EdgeAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_device_credential_versions_CredentialId_Version",
                table: "device_credential_versions",
                columns: new[] { "CredentialId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_credentials_Name",
                table: "device_credentials",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_plugins_AdapterType",
                table: "device_plugins",
                column: "AdapterType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_plugins_Key",
                table: "device_plugins",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_worker_assignments_DefaultRegionId",
                table: "device_worker_assignments",
                column: "DefaultRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_device_worker_assignments_RecorderId",
                table: "device_worker_assignments",
                column: "RecorderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_worker_assignments_WorkerId_RecorderId",
                table: "device_worker_assignments",
                columns: new[] { "WorkerId", "RecorderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_worker_operation_statuses_RecorderId_OperationType_R~",
                table: "device_worker_operation_statuses",
                columns: new[] { "RecorderId", "OperationType", "ReportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_worker_operation_statuses_WorkerId_RecorderId_Operat~",
                table: "device_worker_operation_statuses",
                columns: new[] { "WorkerId", "RecorderId", "OperationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_workers_Name",
                table: "device_workers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_workers_TokenHash",
                table: "device_workers",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_agent_configurations_EdgeAgentId_Version",
                table: "edge_agent_configurations",
                columns: new[] { "EdgeAgentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_agent_enrollments_CodeHash",
                table: "edge_agent_enrollments",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_agents_DeviceWorkerId",
                table: "edge_agents",
                column: "DeviceWorkerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_agents_Name",
                table: "edge_agents",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_edge_commands_AggregateType_AggregateId",
                table: "edge_commands",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_edge_commands_RecorderId",
                table: "edge_commands",
                column: "RecorderId");

            migrationBuilder.CreateIndex(
                name: "IX_edge_commands_WorkerId_CompletedAt_DeadLetteredAt_NextAttem~",
                table: "edge_commands",
                columns: new[] { "WorkerId", "CompletedAt", "DeadLetteredAt", "NextAttemptAt", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_export_artifacts_ExpiresAt_DeletedAt",
                table: "export_artifacts",
                columns: new[] { "ExpiresAt", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_export_artifacts_PlaybackExportId",
                table: "export_artifacts",
                column: "PlaybackExportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_export_download_audits_ExportArtifactId_StartedAt",
                table: "export_download_audits",
                columns: new[] { "ExportArtifactId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_export_download_audits_UserId_StartedAt",
                table: "export_download_audits",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_export_download_audits_UserSessionId",
                table: "export_download_audits",
                column: "UserSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_health_state_events_ResourceType_ResourceId_OccurredAt",
                table: "health_state_events",
                columns: new[] { "ResourceType", "ResourceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_channels_Name",
                table: "notification_channels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_AlertIncidentId_NotificationChannel~",
                table: "notification_deliveries",
                columns: new[] { "AlertIncidentId", "NotificationChannelId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_NotificationChannelId",
                table: "notification_deliveries",
                column: "NotificationChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_Status_NextAttemptAt_LockedUntil",
                table: "notification_deliveries",
                columns: new[] { "Status", "NextAttemptAt", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_ProcessedAt_DeadLetteredAt_NextAttemptAt_Lock~",
                table: "outbox_events",
                columns: new[] { "ProcessedAt", "DeadLetteredAt", "NextAttemptAt", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_operations_EdgeAgentId",
                table: "platform_operations",
                column: "EdgeAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_platform_operations_Status_RequestedAt",
                table: "platform_operations",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_playback_exports_CameraId_RequestedAt",
                table: "playback_exports",
                columns: new[] { "CameraId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_playback_exports_RequestedByUserId",
                table: "playback_exports",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_playback_exports_Status_NextAttemptAt_LockedUntil",
                table: "playback_exports",
                columns: new[] { "Status", "NextAttemptAt", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ptz_control_leases_CameraId",
                table: "ptz_control_leases",
                column: "CameraId",
                unique: true,
                filter: "\"ReleasedAt\" IS NULL AND \"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ptz_control_leases_CameraId_ExpiresAt_ReleasedAt_RevokedAt",
                table: "ptz_control_leases",
                columns: new[] { "CameraId", "ExpiresAt", "ReleasedAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ptz_control_leases_LeaseTokenHash",
                table: "ptz_control_leases",
                column: "LeaseTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ptz_control_leases_UserId",
                table: "ptz_control_leases",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ptz_control_leases_UserSessionId",
                table: "ptz_control_leases",
                column: "UserSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_recorder_capabilities_RecorderId_Version",
                table: "recorder_capabilities",
                columns: new[] { "RecorderId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recorder_clock_observations_RecorderId_ResponseReceivedAt",
                table: "recorder_clock_observations",
                columns: new[] { "RecorderId", "ResponseReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recorder_clock_observations_ResponseReceivedAt",
                table: "recorder_clock_observations",
                column: "ResponseReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_recorder_endpoints_CredentialId",
                table: "recorder_endpoints",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_recorder_endpoints_RecorderId_Protocol",
                table: "recorder_endpoints",
                columns: new[] { "RecorderId", "Protocol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recorders_Code",
                table: "recorders",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recorders_DevicePluginId",
                table: "recorders",
                column: "DevicePluginId");

            migrationBuilder.CreateIndex(
                name: "IX_recording_searches_CameraId",
                table: "recording_searches",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_recording_searches_Status_ExpiresAt",
                table: "recording_searches",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recording_searches_UserId_CreatedAt",
                table: "recording_searches",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recording_searches_UserSessionId_ClientRequestId",
                table: "recording_searches",
                columns: new[] { "UserSessionId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regions_Code",
                table: "regions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regions_ParentId",
                table: "regions",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_role_camera_scopes_CameraId",
                table: "role_camera_scopes",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_role_camera_scopes_RegionId",
                table: "role_camera_scopes",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_role_camera_scopes_RoleId_CameraId",
                table: "role_camera_scopes",
                columns: new[] { "RoleId", "CameraId" });

            migrationBuilder.CreateIndex(
                name: "IX_role_camera_scopes_RoleId_RegionId",
                table: "role_camera_scopes",
                columns: new[] { "RoleId", "RegionId" });

            migrationBuilder.CreateIndex(
                name: "IX_roles_Code",
                table: "roles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stream_assignments_StreamKey",
                table: "stream_assignments",
                column: "StreamKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stream_connection_tickets_SessionId_ExpiresAt_ConsumedAt",
                table: "stream_connection_tickets",
                columns: new[] { "SessionId", "ExpiresAt", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stream_connection_tickets_TokenHash",
                table: "stream_connection_tickets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stream_sessions_CameraId_ExpiresAt_RevokedAt",
                table: "stream_sessions",
                columns: new[] { "CameraId", "ExpiresAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stream_sessions_UserId_ExpiresAt_RevokedAt",
                table: "stream_sessions",
                columns: new[] { "UserId", "ExpiresAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stream_sessions_UserSessionId_ClientRequestId",
                table: "stream_sessions",
                columns: new[] { "UserSessionId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stream_sessions_UserSessionId_ExpiresAt_RevokedAt",
                table: "stream_sessions",
                columns: new[] { "UserSessionId", "ExpiresAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stream_sessions_UserSessionId_SlotNumber",
                table: "stream_sessions",
                columns: new[] { "UserSessionId", "SlotNumber" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL AND \"UserSessionId\" IS NOT NULL AND \"SlotNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_TokenHash",
                table: "user_sessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_UserId_ExpiresAt",
                table: "user_sessions",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_rule_channels");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "device_credential_envelopes");

            migrationBuilder.DropTable(
                name: "device_worker_assignments");

            migrationBuilder.DropTable(
                name: "device_worker_operation_statuses");

            migrationBuilder.DropTable(
                name: "edge_agent_configurations");

            migrationBuilder.DropTable(
                name: "edge_agent_enrollments");

            migrationBuilder.DropTable(
                name: "edge_commands");

            migrationBuilder.DropTable(
                name: "export_download_audits");

            migrationBuilder.DropTable(
                name: "health_state_events");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "platform_operations");

            migrationBuilder.DropTable(
                name: "ptz_control_leases");

            migrationBuilder.DropTable(
                name: "recorder_capabilities");

            migrationBuilder.DropTable(
                name: "recorder_clock_observations");

            migrationBuilder.DropTable(
                name: "recorder_endpoints");

            migrationBuilder.DropTable(
                name: "recording_searches");

            migrationBuilder.DropTable(
                name: "role_camera_scopes");

            migrationBuilder.DropTable(
                name: "stream_assignments");

            migrationBuilder.DropTable(
                name: "stream_connection_tickets");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "alert_rules");

            migrationBuilder.DropTable(
                name: "device_credential_versions");

            migrationBuilder.DropTable(
                name: "export_artifacts");

            migrationBuilder.DropTable(
                name: "alert_incidents");

            migrationBuilder.DropTable(
                name: "notification_channels");

            migrationBuilder.DropTable(
                name: "edge_agents");

            migrationBuilder.DropTable(
                name: "stream_sessions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "device_credentials");

            migrationBuilder.DropTable(
                name: "playback_exports");

            migrationBuilder.DropTable(
                name: "device_workers");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "cameras");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "recorders");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropTable(
                name: "device_plugins");
        }
    }
}
