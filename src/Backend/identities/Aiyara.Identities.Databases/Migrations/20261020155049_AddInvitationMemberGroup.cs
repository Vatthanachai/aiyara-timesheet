using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationMemberGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MemberGroupId",
                table: "invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invitations_MemberGroupId",
                table: "invitations",
                column: "MemberGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_invitations_member_groups_MemberGroupId",
                table: "invitations",
                column: "MemberGroupId",
                principalTable: "member_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_invitations_member_groups_MemberGroupId",
                table: "invitations");

            migrationBuilder.DropIndex(
                name: "IX_invitations_MemberGroupId",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "MemberGroupId",
                table: "invitations");
        }
    }
}
