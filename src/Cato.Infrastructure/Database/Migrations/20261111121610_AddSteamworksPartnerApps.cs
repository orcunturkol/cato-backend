using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksPartnerApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamworks_partner_app",
                columns: table => new
                {
                    PartnerId = table.Column<int>(type: "integer", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_partner_app", x => new { x.PartnerId, x.AppId });
                    table.ForeignKey(
                        name: "FK_steamworks_partner_app_steamworks_partner_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "steamworks_partner",
                        principalColumn: "PartnerId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_partner_app");
        }
    }
}
