using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameAchievementPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_achievement_percent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    ApiName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_achievement_percent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_achievement_percent_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_game_achievement_percent_game_date",
                table: "game_achievement_percent",
                columns: new[] { "GameId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "unique_game_achievement_percent",
                table: "game_achievement_percent",
                columns: new[] { "GameId", "ApiName", "SnapshotDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_achievement_percent");
        }
    }
}
