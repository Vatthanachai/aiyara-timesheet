using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformOrganizationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AccessRevocationVersion",
                table: "tenants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "tenants",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateTable(
                name: "platform_organization_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_organization_audit", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_platform_organization_audit_TenantId_CreatedAtUtc",
                table: "platform_organization_audit",
                columns: new[] { "TenantId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_organization_audit");

            migrationBuilder.DropColumn(
                name: "AccessRevocationVersion",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "tenants");
        }
    }
}
