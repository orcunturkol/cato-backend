using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameEnrichmentState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAt",
                table: "main_game",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnrichmentFailures",
                table: "main_game",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEnrichedAt",
                table: "main_game",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseDateRaw",
                table: "main_game",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Games already carrying store data were enriched at some point; without
            // this the watcher would read every one of them as "never enriched" and
            // re-request ~60k store records it already has. UpdatedAt is the closest
            // record of when that happened.
            migrationBuilder.Sql("""
                UPDATE main_game
                SET "LastEnrichedAt" = "UpdatedAt"
                WHERE "HeaderImageUrl" IS NOT NULL
                """);

            migrationBuilder.CreateIndex(
                name: "IX_main_game_LastEnrichedAt_EnrichmentFailures_AnalyzedAt",
                table: "main_game",
                columns: new[] { "LastEnrichedAt", "EnrichmentFailures", "AnalyzedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_game_LastEnrichedAt_EnrichmentFailures_AnalyzedAt",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "AnalyzedAt",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "EnrichmentFailures",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "LastEnrichedAt",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "ReleaseDateRaw",
                table: "main_game");
        }
    }
}
