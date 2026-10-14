using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksWishlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamworks_wishlist_cohort",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthCohort = table.Column<DateOnly>(type: "date", nullable: false),
                    PurchasesAndActivations = table.Column<int>(type: "integer", nullable: false),
                    Gifts = table.Column<int>(type: "integer", nullable: false),
                    TotalConversions = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_wishlist_cohort", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamworks_wishlist_cohort_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steamworks_wishlist_daily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    Adds = table.Column<int>(type: "integer", nullable: false),
                    Deletes = table.Column<int>(type: "integer", nullable: false),
                    PurchasesAndActivations = table.Column<int>(type: "integer", nullable: false),
                    Gifts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_wishlist_daily", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamworks_wishlist_daily_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_steamworks_wishlist_cohort_game_month",
                table: "steamworks_wishlist_cohort",
                columns: new[] { "GameId", "MonthCohort" });

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_wishlist_cohort",
                table: "steamworks_wishlist_cohort",
                columns: new[] { "GameId", "DateLocal", "MonthCohort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_wishlist_daily",
                table: "steamworks_wishlist_daily",
                columns: new[] { "GameId", "DateLocal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_wishlist_cohort");

            migrationBuilder.DropTable(
                name: "steamworks_wishlist_daily");
        }
    }
}
