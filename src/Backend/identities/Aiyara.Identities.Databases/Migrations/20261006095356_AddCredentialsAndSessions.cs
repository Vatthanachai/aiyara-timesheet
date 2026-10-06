using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialsAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PasswordExpiryDays",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PasswordMinimumLength",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordPolicyUpdatedAtUtc",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "PasswordRequireDigit",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordRequireLowercase",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordRequireSymbol",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordRequireUppercase",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntilUtc",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SessionVersion",
                table: "accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "credential_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credential_challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credential_challenges_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_credential_challenges_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_sessions_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_refresh_sessions_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credential_challenges_AccountId",
                table: "credential_challenges",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_credential_challenges_TenantId",
                table: "credential_challenges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_credential_challenges_TokenHash",
                table: "credential_challenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_AccountId",
                table: "refresh_sessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_TenantId",
                table: "refresh_sessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_TokenHash",
                table: "refresh_sessions",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credential_challenges");

            migrationBuilder.DropTable(
                name: "refresh_sessions");

            migrationBuilder.DropColumn(
                name: "PasswordExpiryDays",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PasswordMinimumLength",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PasswordPolicyUpdatedAtUtc",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PasswordRequireDigit",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PasswordRequireLowercase",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PasswordRequireSymbol",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "PasswordRequireUppercase",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "LockedUntilUtc",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "accounts");
        }
    }
}
