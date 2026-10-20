using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Report.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_report_schedules_TenantId_ReportDefinitionId",
                table: "report_schedules");

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectGroupId",
                table: "report_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectGroupName",
                table: "report_schedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_TenantId_ReportDefinitionId",
                table: "report_schedules",
                columns: new[] { "TenantId", "ReportDefinitionId" },
                unique: true,
                filter: "\"SubjectGroupId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_TenantId_ReportDefinitionId_SubjectGroupId",
                table: "report_schedules",
                columns: new[] { "TenantId", "ReportDefinitionId", "SubjectGroupId" },
                unique: true,
                filter: "\"SubjectGroupId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_report_schedules_TenantId_ReportDefinitionId",
                table: "report_schedules");

            migrationBuilder.DropIndex(
                name: "IX_report_schedules_TenantId_ReportDefinitionId_SubjectGroupId",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "SubjectGroupId",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "SubjectGroupName",
                table: "report_schedules");

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_TenantId_ReportDefinitionId",
                table: "report_schedules",
                columns: new[] { "TenantId", "ReportDefinitionId" },
                unique: true);
        }
    }
}
