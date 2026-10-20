using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Report.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubjectGroupMemberIdsJson",
                table: "report_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectGroupName",
                table: "report_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubjectGroupMemberIdsJson",
                table: "report_runs");

            migrationBuilder.DropColumn(
                name: "SubjectGroupName",
                table: "report_runs");
        }
    }
}
