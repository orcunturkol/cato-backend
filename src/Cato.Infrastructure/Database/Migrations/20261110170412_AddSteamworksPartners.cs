using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every stored sale line counts as reported by 216530 (the only Financial API key so
            // far). A line of another partner would then be filed under the wrong partner: stop.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM steam_sale_financial WHERE ""PartnerId"" IS NOT NULL AND ""PartnerId"" <> 216530) THEN
        RAISE EXCEPTION 'steam_sale_financial has lines of a partner other than 216530; set ReportPartnerId by hand before this migration';
    END IF;
END $$;");

            migrationBuilder.DropIndex(
                name: "idx_steam_sale_game_date",
                table: "steam_sale_financial");

            migrationBuilder.AddColumn<int>(
                name: "ReportPartnerId",
                table: "steam_sale_financial",
                type: "integer",
                nullable: false,
                defaultValue: 216530);

            migrationBuilder.CreateTable(
                name: "steamworks_partner",
                columns: table => new
                {
                    PartnerId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_partner", x => x.PartnerId);
                });

            migrationBuilder.CreateIndex(
                name: "idx_steam_sale_game_date",
                table: "steam_sale_financial",
                columns: new[] { "GameId", "SaleDate", "ReportPartnerId" });

            // The first partner. The collector's discovery adds the others.
            migrationBuilder.InsertData(
                table: "steamworks_partner",
                columns: new[] { "PartnerId", "Name", "Enabled", "DiscoveredAt" },
                values: new object[]
                {
                    216530, "PLAN B YAZILIM OYUN ANONIM SIRKETI", true,
                    new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_partner");

            migrationBuilder.DropIndex(
                name: "idx_steam_sale_game_date",
                table: "steam_sale_financial");

            migrationBuilder.DropColumn(
                name: "ReportPartnerId",
                table: "steam_sale_financial");

            migrationBuilder.CreateIndex(
                name: "idx_steam_sale_game_date",
                table: "steam_sale_financial",
                columns: new[] { "GameId", "SaleDate" });
        }
    }
}
