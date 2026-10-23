using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshSessionDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SignedInAtUtc",
                table: "refresh_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "refresh_sessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignedInAtUtc",
                table: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "refresh_sessions");
        }
    }
}
