using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Timesheet.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddRunningTimers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "running_timers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TaskName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PersonalTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncludesBreak = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_running_timers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_running_timers_TenantId",
                table: "running_timers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_running_timers_TenantId_OwnerId",
                table: "running_timers",
                columns: new[] { "TenantId", "OwnerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "running_timers");
        }
    }
}
