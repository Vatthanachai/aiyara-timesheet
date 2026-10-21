using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Timesheet.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "BreakEnd",
                table: "work_weeks",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "BreakStart",
                table: "work_weeks",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesBreak",
                table: "time_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakEnd",
                table: "work_weeks");

            migrationBuilder.DropColumn(
                name: "BreakStart",
                table: "work_weeks");

            migrationBuilder.DropColumn(
                name: "IncludesBreak",
                table: "time_entries");
        }
    }
}
