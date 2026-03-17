using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamDbSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamdb_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Rating = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Release = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Follows = table.Column<int>(type: "integer", nullable: false),
                    SevenDayGain = table.Column<int>(type: "integer", nullable: false),
                    ScrapedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamdb_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamdb_snapshot_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_steamdb_snapshot_date",
                table: "steamdb_snapshot",
                column: "SnapshotDate");

            migrationBuilder.CreateIndex(
                name: "idx_steamdb_snapshot_source",
                table: "steamdb_snapshot",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "unique_steamdb_snapshot",
                table: "steamdb_snapshot",
                columns: new[] { "GameId", "SnapshotDate", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamdb_snapshot");
        }
    }
}
