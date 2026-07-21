using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VisiCore.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20260721103000_AddEdgeAgentEnrollmentUsage")]
public partial class AddEdgeAgentEnrollmentUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "UsedByAgentId",
            table: "edge_agent_enrollments",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_edge_agent_enrollments_UsedByAgentId",
            table: "edge_agent_enrollments",
            column: "UsedByAgentId");

        migrationBuilder.AddForeignKey(
            name: "FK_edge_agent_enrollments_edge_agents_UsedByAgentId",
            table: "edge_agent_enrollments",
            column: "UsedByAgentId",
            principalTable: "edge_agents",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_edge_agent_enrollments_edge_agents_UsedByAgentId",
            table: "edge_agent_enrollments");

        migrationBuilder.DropIndex(
            name: "IX_edge_agent_enrollments_UsedByAgentId",
            table: "edge_agent_enrollments");

        migrationBuilder.DropColumn(
            name: "UsedByAgentId",
            table: "edge_agent_enrollments");
    }
}
