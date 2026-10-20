using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LogoUpdatedAtUtc",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "LogoUpdatedAtUtc",
                table: "tenants");
        }
    }
}
