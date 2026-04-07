using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNewIngestionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_price_snapshot",
                table: "price_snapshot");

            migrationBuilder.CreateTable(
                name: "steam_traffic_breakdown",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PageCategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PageFeature = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Visits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_traffic_breakdown", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steam_traffic_breakdown_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_insight",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RelatedAppId = table.Column<int>(type: "integer", nullable: false),
                    RelatedName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LinkScore = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Genres = table.Column<string>(type: "jsonb", nullable: false),
                    CopiesSold = table.Column<long>(type: "bigint", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_insight", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wishlist_insight_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "unique_price_snapshot",
                table: "price_snapshot",
                columns: new[] { "GameId", "CapturedAt", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "unique_traffic_breakdown",
                table: "steam_traffic_breakdown",
                columns: new[] { "GameId", "SnapshotDate", "PageCategory", "PageFeature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "unique_wishlist_insight",
                table: "wishlist_insight",
                columns: new[] { "GameId", "SnapshotDate", "RelatedAppId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steam_traffic_breakdown");

            migrationBuilder.DropTable(
                name: "wishlist_insight");

            migrationBuilder.DropIndex(
                name: "unique_price_snapshot",
                table: "price_snapshot");

            migrationBuilder.CreateIndex(
                name: "unique_price_snapshot",
                table: "price_snapshot",
                columns: new[] { "GameId", "CapturedAt" },
                unique: true);
        }
    }
}
