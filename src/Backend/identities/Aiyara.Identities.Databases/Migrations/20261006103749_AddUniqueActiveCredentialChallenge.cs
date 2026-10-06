using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveCredentialChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credential_challenges_TenantId",
                table: "credential_challenges");

            migrationBuilder.CreateIndex(
                name: "IX_credential_challenges_TenantId_AccountId_Purpose",
                table: "credential_challenges",
                columns: new[] { "TenantId", "AccountId", "Purpose" },
                unique: true,
                filter: "\"UsedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credential_challenges_TenantId_AccountId_Purpose",
                table: "credential_challenges");

            migrationBuilder.CreateIndex(
                name: "IX_credential_challenges_TenantId",
                table: "credential_challenges",
                column: "TenantId");
        }
    }
}
