using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class LinkPlayerAchievementToSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the FK column nullable so existing rows can be backfilled.
            migrationBuilder.AddColumn<Guid>(
                name: "GameAchievementSchemaId",
                table: "steam_player_achievement",
                type: "uuid",
                nullable: true);

            // 2. Backfill by matching the (AppId, ApiName) natural key onto the
            //    catalog row's Id. ApiName (e.g. ACH_ENTA) can repeat across games,
            //    so both columns must match.
            migrationBuilder.Sql(@"
                UPDATE steam_player_achievement spa
                SET ""GameAchievementSchemaId"" = gas.""Id""
                FROM game_achievement_schema gas
                WHERE gas.""AppId"" = spa.""AppId"" AND gas.""ApiName"" = spa.""ApiName"";");

            // 3. Drop orphans with no matching catalog row (accepted data loss).
            migrationBuilder.Sql(@"
                DELETE FROM steam_player_achievement
                WHERE ""GameAchievementSchemaId"" IS NULL;");

            // 4. Now that every row is linked, enforce NOT NULL.
            migrationBuilder.AlterColumn<Guid>(
                name: "GameAchievementSchemaId",
                table: "steam_player_achievement",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 5. Drop the old natural-key indexes and the duplicated columns.
            migrationBuilder.DropIndex(
                name: "idx_steam_player_achievement_steam_app",
                table: "steam_player_achievement");

            migrationBuilder.DropIndex(
                name: "unique_steam_player_achievement_triplet",
                table: "steam_player_achievement");

            migrationBuilder.DropColumn(
                name: "ApiName",
                table: "steam_player_achievement");

            migrationBuilder.DropColumn(
                name: "AppId",
                table: "steam_player_achievement");

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_achievement_schema",
                table: "steam_player_achievement",
                column: "GameAchievementSchemaId");

            migrationBuilder.CreateIndex(
                name: "unique_steam_player_achievement_player_schema",
                table: "steam_player_achievement",
                columns: new[] { "SteamId64", "GameAchievementSchemaId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_steam_player_achievement_game_achievement_schema_GameAchiev~",
                table: "steam_player_achievement",
                column: "GameAchievementSchemaId",
                principalTable: "game_achievement_schema",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_steam_player_achievement_game_achievement_schema_GameAchiev~",
                table: "steam_player_achievement");

            migrationBuilder.DropIndex(
                name: "idx_steam_player_achievement_schema",
                table: "steam_player_achievement");

            migrationBuilder.DropIndex(
                name: "unique_steam_player_achievement_player_schema",
                table: "steam_player_achievement");

            migrationBuilder.DropColumn(
                name: "GameAchievementSchemaId",
                table: "steam_player_achievement");

            migrationBuilder.AddColumn<string>(
                name: "ApiName",
                table: "steam_player_achievement",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AppId",
                table: "steam_player_achievement",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_achievement_steam_app",
                table: "steam_player_achievement",
                columns: new[] { "SteamId64", "AppId" });

            migrationBuilder.CreateIndex(
                name: "unique_steam_player_achievement_triplet",
                table: "steam_player_achievement",
                columns: new[] { "SteamId64", "AppId", "ApiName" },
                unique: true);
        }
    }
}
