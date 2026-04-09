using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPriority4Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_users_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Dau = table.Column<int>(type: "integer", nullable: false),
                    Mau = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_active_users_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_active_users_history_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "demo_download",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    DemoAppId = table.Column<int>(type: "integer", nullable: true),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    GeoType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GeoName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalDownloads = table.Column<long>(type: "bigint", nullable: false),
                    SharePercent = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demo_download", x => x.Id);
                    table.ForeignKey(
                        name: "FK_demo_download_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_news",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Contents = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FeedLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_news", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_news_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_active_users_history_recorded_at",
                table: "active_users_history",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "unique_active_users_history",
                table: "active_users_history",
                columns: new[] { "GameId", "RecordedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_demo_download_snapshot_date",
                table: "demo_download",
                column: "SnapshotDate");

            migrationBuilder.CreateIndex(
                name: "unique_demo_download",
                table: "demo_download",
                columns: new[] { "GameId", "SnapshotDate", "GeoType", "GeoName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_game_news_published_at",
                table: "game_news",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "unique_game_news",
                table: "game_news",
                columns: new[] { "GameId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_users_history");

            migrationBuilder.DropTable(
                name: "demo_download");

            migrationBuilder.DropTable(
                name: "game_news");
        }
    }
}
