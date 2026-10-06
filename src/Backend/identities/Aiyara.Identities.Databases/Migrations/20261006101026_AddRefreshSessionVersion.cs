using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SessionVersion",
                table: "refresh_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "refresh_sessions");
        }
    }
}
