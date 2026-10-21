using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Timesheet.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Organizations that already saved their working days keep the hours new entries used
            // to start with (WorkWeek.DefaultStartTime / DefaultEndTime).
            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "work_weeks",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(17, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "work_weeks",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "work_weeks");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "work_weeks");
        }
    }
}
