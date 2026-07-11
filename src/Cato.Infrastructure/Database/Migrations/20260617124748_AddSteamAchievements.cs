using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AchievementsComputedAt",
                table: "steam_review",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthorAchievementsAtReview",
                table: "steam_review",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GameAchievementCountAtFetch",
                table: "steam_review",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "game_achievement_schema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    ApiName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    IconUrl = table.Column<string>(type: "text", nullable: true),
                    IconGrayUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_achievement_schema", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_achievement_schema_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steam_player_achievement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId64 = table.Column<long>(type: "bigint", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    ApiName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UnlockTime = table.Column<long>(type: "bigint", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_player_achievement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "steam_player_achievement_fetch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId64 = table.Column<long>(type: "bigint", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AchievedCount = table.Column<int>(type: "integer", nullable: false),
                    SchemaCount = table.Column<int>(type: "integer", nullable: true),
                    LastFetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    QuarantinedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_player_achievement_fetch", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_game_achievement_schema_appid",
                table: "game_achievement_schema",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "unique_game_achievement_schema_game_api",
                table: "game_achievement_schema",
                columns: new[] { "GameId", "ApiName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_achievement_steam_app",
                table: "steam_player_achievement",
                columns: new[] { "SteamId64", "AppId" });

            migrationBuilder.CreateIndex(
                name: "unique_steam_player_achievement_triplet",
                table: "steam_player_achievement",
                columns: new[] { "SteamId64", "AppId", "ApiName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_achievement_fetch_last",
                table: "steam_player_achievement_fetch",
                column: "LastFetchedAt");

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_achievement_fetch_quarantine",
                table: "steam_player_achievement_fetch",
                column: "QuarantinedUntil");

            migrationBuilder.CreateIndex(
                name: "unique_steam_player_achievement_fetch_pair",
                table: "steam_player_achievement_fetch",
                columns: new[] { "SteamId64", "AppId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_achievement_schema");

            migrationBuilder.DropTable(
                name: "steam_player_achievement");

            migrationBuilder.DropTable(
                name: "steam_player_achievement_fetch");

            migrationBuilder.DropColumn(
                name: "AchievementsComputedAt",
                table: "steam_review");

            migrationBuilder.DropColumn(
                name: "AuthorAchievementsAtReview",
                table: "steam_review");

            migrationBuilder.DropColumn(
                name: "GameAchievementCountAtFetch",
                table: "steam_review");
        }
    }
}
