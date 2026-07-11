using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamSpecialEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steam_special_event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnnouncementGid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SaleVanityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EventUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ClanAccountId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Subtitle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    HeaderImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LogoImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CapsuleImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BackgroundColor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TabNames = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_special_event", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "steam_special_event_game",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamSpecialEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SteamDisplayedStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SteamDisplayedEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DiscountPercent = table.Column<int>(type: "integer", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_special_event_game", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steam_special_event_game_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_steam_special_event_game_steam_special_event_SteamSpecialEv~",
                        column: x => x.SteamSpecialEventId,
                        principalTable: "steam_special_event",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_steam_special_event_last_seen",
                table: "steam_special_event",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "idx_steam_special_event_start",
                table: "steam_special_event",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "unique_steam_special_event_gid",
                table: "steam_special_event",
                column: "AnnouncementGid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_special_event_game_last_seen",
                table: "steam_special_event_game",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_steam_special_event_game_GameId",
                table: "steam_special_event_game",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "unique_steam_special_event_game",
                table: "steam_special_event_game",
                columns: new[] { "SteamSpecialEventId", "GameId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steam_special_event_game");

            migrationBuilder.DropTable(
                name: "steam_special_event");
        }
    }
}
