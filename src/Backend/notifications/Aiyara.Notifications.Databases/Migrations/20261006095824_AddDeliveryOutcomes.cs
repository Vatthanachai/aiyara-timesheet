using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiyara.Notifications.Databases.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Template = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_deliveries");
        }
    }
}
