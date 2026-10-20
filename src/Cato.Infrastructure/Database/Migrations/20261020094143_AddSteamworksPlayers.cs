using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PeakConcurrentUsers",
                table: "active_users_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "active_users_history",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "steamworks_app_details",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MedianPlaytimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    MedianPlaytimeText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LifetimeUniqueUsers = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_app_details", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamworks_app_details_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steamworks_player_country",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SharePercent = table.Column<decimal>(type: "numeric(6,3)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_player_country", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamworks_player_country_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_steamworks_app_details_game",
                table: "steamworks_app_details",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_app_details",
                table: "steamworks_app_details",
                columns: new[] { "AppId", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_player_country",
                table: "steamworks_player_country",
                columns: new[] { "GameId", "SnapshotDate", "CountryCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_app_details");

            migrationBuilder.DropTable(
                name: "steamworks_player_country");

            migrationBuilder.DropColumn(
                name: "PeakConcurrentUsers",
                table: "active_users_history");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "active_users_history");
        }
    }
}
