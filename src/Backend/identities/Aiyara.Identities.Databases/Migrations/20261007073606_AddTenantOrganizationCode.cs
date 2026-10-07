using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Identities.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantOrganizationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizationCode",
                table: "tenants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.Sql("""
                WITH tenant_prefixes AS (
                    SELECT "Id",
                        CASE WHEN "Id" = '00000000-0000-0000-0000-000000000001'::uuid THEN 'AIYA'
                             ELSE left(rpad(coalesce(nullif(regexp_replace(upper("Name"), '[^A-Z0-9]', '', 'g'), ''),
                                                     nullif(regexp_replace(upper("Slug"), '[^A-Z0-9]', '', 'g'), ''), 'XXXX'), 4, 'X'), 4)
                        END AS prefix
                    FROM tenants
                ), numbered AS (
                    SELECT "Id", prefix, row_number() OVER (PARTITION BY prefix ORDER BY "Id") AS sequence
                    FROM tenant_prefixes
                    WHERE "Id" <> '00000000-0000-0000-0000-000000000001'::uuid
                )
                UPDATE tenants AS tenant
                SET "OrganizationCode" = CASE
                    WHEN tenant."Id" = '00000000-0000-0000-0000-000000000001'::uuid THEN 'AIYA001'
                    ELSE numbered.prefix || lpad((numbered.sequence +
                        CASE WHEN numbered.prefix = 'AIYA' AND numbered.sequence >= 1 THEN 1 ELSE 0 END)::text, 3, '0')
                    END
                FROM numbered
                WHERE tenant."Id" = numbered."Id";

                UPDATE tenants SET "OrganizationCode" = 'AIYA001'
                WHERE "Id" = '00000000-0000-0000-0000-000000000001'::uuid;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "OrganizationCode",
                table: "tenants",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_OrganizationCode",
                table: "tenants",
                column: "OrganizationCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenants_OrganizationCode",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "OrganizationCode",
                table: "tenants");
        }
    }
}
