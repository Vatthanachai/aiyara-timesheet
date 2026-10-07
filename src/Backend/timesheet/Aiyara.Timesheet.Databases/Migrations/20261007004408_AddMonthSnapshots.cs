using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Timesheet.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddMonthSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "month_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_month_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_month_snapshots_TenantId",
                table: "month_snapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_month_snapshots_TenantId_OwnerId_Year_Month",
                table: "month_snapshots",
                columns: new[] { "TenantId", "OwnerId", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "month_snapshots");
        }
    }
}
