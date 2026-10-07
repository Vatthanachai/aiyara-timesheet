using Aiyara.Timesheet.Databases;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Timesheet.Databases.Migrations;

public partial class AddOutboxTimezoneForReports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "TimeZoneId", table: "timesheet_outbox",
            type: "character varying(100)", maxLength: 100,
            nullable: false, defaultValue: "Asia/Bangkok");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "TimeZoneId", table: "timesheet_outbox");
}
