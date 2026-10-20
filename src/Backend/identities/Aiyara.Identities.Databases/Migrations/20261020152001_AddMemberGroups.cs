using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MemberGroupId",
                table: "memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "member_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_member_groups_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_MemberGroupId",
                table: "memberships",
                column: "MemberGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_member_groups_TenantId_Name",
                table: "member_groups",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_memberships_member_groups_MemberGroupId",
                table: "memberships",
                column: "MemberGroupId",
                principalTable: "member_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_memberships_member_groups_MemberGroupId",
                table: "memberships");

            migrationBuilder.DropTable(
                name: "member_groups");

            migrationBuilder.DropIndex(
                name: "IX_memberships_MemberGroupId",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "MemberGroupId",
                table: "memberships");
        }
    }
}
