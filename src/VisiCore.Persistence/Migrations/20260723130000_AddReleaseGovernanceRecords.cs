using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisiCore.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20260723130000_AddReleaseGovernanceRecords")]
public partial class AddReleaseGovernanceRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "release_governance_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReleaseCatalogId = table.Column<Guid>(type: "uuid", nullable: false),
                ChangeIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                SourceCommit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                DossierUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                ReleaseUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                WorkflowRunUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                ReleaseEvidenceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                StagingEvidenceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                SbomUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                ProvenanceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                VerificationUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_release_governance_records", x => x.Id);
                table.ForeignKey("FK_release_governance_records_release_catalog_ReleaseCatalogId", x => x.ReleaseCatalogId, "release_catalog", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_release_governance_records_RecordedAt", "release_governance_records", "RecordedAt");
        migrationBuilder.CreateIndex("IX_release_governance_records_ReleaseCatalogId", "release_governance_records", "ReleaseCatalogId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "release_governance_records");
    }
}
