using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "review_summary_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewScore = table.Column<int>(type: "integer", nullable: false),
                    ReviewScoreDesc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TotalPositive = table.Column<int>(type: "integer", nullable: false),
                    TotalNegative = table.Column<int>(type: "integer", nullable: false),
                    TotalReviews = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_summary_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_review_summary_snapshot_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steam_review",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendationId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VotedUp = table.Column<bool>(type: "boolean", nullable: false),
                    Language = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewText = table.Column<string>(type: "text", nullable: false),
                    PlaytimeForeverMinutes = table.Column<int>(type: "integer", nullable: false),
                    PlaytimeAtReviewMinutes = table.Column<int>(type: "integer", nullable: false),
                    VotesUp = table.Column<int>(type: "integer", nullable: false),
                    VotesFunny = table.Column<int>(type: "integer", nullable: false),
                    SteamPurchase = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedForFree = table.Column<bool>(type: "boolean", nullable: false),
                    WrittenDuringEarlyAccess = table.Column<bool>(type: "boolean", nullable: false),
                    TimestampCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimestampUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_review", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steam_review_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_review_summary_snapshot_date",
                table: "review_summary_snapshot",
                column: "SnapshotDate");

            migrationBuilder.CreateIndex(
                name: "unique_review_summary_game_date",
                table: "review_summary_snapshot",
                columns: new[] { "GameId", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_review_game_created",
                table: "steam_review",
                columns: new[] { "GameId", "TimestampCreated" });

            migrationBuilder.CreateIndex(
                name: "idx_steam_review_language",
                table: "steam_review",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "unique_steam_review_game_rec",
                table: "steam_review",
                columns: new[] { "GameId", "RecommendationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_summary_snapshot");

            migrationBuilder.DropTable(
                name: "steam_review");
        }
    }
}
