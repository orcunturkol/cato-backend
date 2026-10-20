using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksNextFest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamworks_nextfest_stat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    DemoAppId = table.Column<int>(type: "integer", nullable: true),
                    FestKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EventEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SnapshotEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DemoPlayerCount = table.Column<int>(type: "integer", nullable: false),
                    WishlistCount = table.Column<int>(type: "integer", nullable: false),
                    PlayerWishlistCount = table.Column<int>(type: "integer", nullable: false),
                    FestPageViews = table.Column<long>(type: "bigint", nullable: true),
                    FestPeriodPageViews = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_nextfest_stat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamworks_nextfest_stat_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_steamworks_nextfest_stat_GameId",
                table: "steamworks_nextfest_stat",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_nextfest_stat",
                table: "steamworks_nextfest_stat",
                columns: new[] { "AppId", "FestKey", "SnapshotEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_nextfest_stat");
        }
    }
}
