using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisiCore.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20260721100000_AddReleaseCatalogAndUpgradePlans")]
public partial class AddReleaseCatalogAndUpgradePlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "release_catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DescriptorJson = table.Column<string>(type: "jsonb", nullable: false),
                SignatureBase64 = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: false),
                SigningPublicKeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_release_catalog", x => x.Id));

        migrationBuilder.CreateTable(
            name: "upgrade_plans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReleaseCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                TargetScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FailureSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_upgrade_plans", x => x.Id);
                table.ForeignKey("FK_upgrade_plans_release_catalog_ReleaseCatalogId", x => x.ReleaseCatalogId, "release_catalog", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "upgrade_targets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UpgradePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                EdgeAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                PlatformOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Component = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Batch = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExpectedVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PreviousVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                PreviousArtifactJson = table.Column<string>(type: "jsonb", nullable: true),
                FailureSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                StableSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_upgrade_targets", x => x.Id);
                table.ForeignKey("FK_upgrade_targets_edge_agents_EdgeAgentId", x => x.EdgeAgentId, "edge_agents", "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_upgrade_targets_upgrade_plans_UpgradePlanId", x => x.UpgradePlanId, "upgrade_plans", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_release_catalog_ProductVersion_Channel", "release_catalog", new[] { "ProductVersion", "Channel" }, unique: true);
        migrationBuilder.CreateIndex("IX_release_catalog_Status_PublishedAt", "release_catalog", new[] { "Status", "PublishedAt" });
        migrationBuilder.CreateIndex("IX_upgrade_plans_ReleaseCatalogId", "upgrade_plans", "ReleaseCatalogId");
        migrationBuilder.CreateIndex("IX_upgrade_plans_Status_RequestedAt", "upgrade_plans", new[] { "Status", "RequestedAt" });
        migrationBuilder.CreateIndex("IX_upgrade_targets_EdgeAgentId", "upgrade_targets", "EdgeAgentId");
        migrationBuilder.CreateIndex("IX_upgrade_targets_UpgradePlanId_Batch_Status", "upgrade_targets", new[] { "UpgradePlanId", "Batch", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "upgrade_targets");
        migrationBuilder.DropTable(name: "upgrade_plans");
        migrationBuilder.DropTable(name: "release_catalog");
    }
}
