using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_change_record",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeNumber = table.Column<long>(type: "bigint", nullable: false),
                    Section = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    KeyPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_change_record", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_change_record_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "app_kv_snapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeNumber = table.Column<long>(type: "bigint", nullable: false),
                    RawKeyValuesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_kv_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_kv_snapshot_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_app_change_record_app_change",
                table: "app_change_record",
                columns: new[] { "AppId", "ChangeNumber" });

            migrationBuilder.CreateIndex(
                name: "idx_app_change_record_app_detected",
                table: "app_change_record",
                columns: new[] { "AppId", "DetectedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_app_change_record_game_detected",
                table: "app_change_record",
                columns: new[] { "GameId", "DetectedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_app_kv_snapshot_appid",
                table: "app_kv_snapshot",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_app_kv_snapshot_GameId",
                table: "app_kv_snapshot",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "unique_app_kv_snapshot",
                table: "app_kv_snapshot",
                columns: new[] { "AppId", "ChangeNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_change_record");

            migrationBuilder.DropTable(
                name: "app_kv_snapshot");
        }
    }
}
