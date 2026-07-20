using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisiCore.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20260720103000_AddPlatformBackups")]
public partial class AddPlatformBackups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "platform_backups",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastRestoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailureDetail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_platform_backups", x => x.Id);
                table.CheckConstraint("CK_platform_backups_size", "\"SizeBytes\" >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_platform_backups_Kind_CreatedAt",
            table: "platform_backups",
            columns: new[] { "Kind", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_platform_backups_Status_RetainUntil",
            table: "platform_backups",
            columns: new[] { "Status", "RetainUntil" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "platform_backups");
    }
}
