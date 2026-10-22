using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Timesheet.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminder_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reminder_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthEndEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SendAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DailyRanOn = table.Column<DateOnly>(type: "date", nullable: true),
                    MonthEndRanOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reminder_deliveries_TenantId",
                table: "reminder_deliveries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_reminder_deliveries_TenantId_OwnerId_Kind_Date",
                table: "reminder_deliveries",
                columns: new[] { "TenantId", "OwnerId", "Kind", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reminder_settings_TenantId",
                table: "reminder_settings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_deliveries");

            migrationBuilder.DropTable(
                name: "reminder_settings");
        }
    }
}
