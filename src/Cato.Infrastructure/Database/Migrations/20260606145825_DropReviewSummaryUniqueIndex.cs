using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class DropReviewSummaryUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_review_summary_game_date",
                table: "review_summary_snapshot");

            migrationBuilder.CreateIndex(
                name: "idx_review_summary_game_date",
                table: "review_summary_snapshot",
                columns: new[] { "GameId", "SnapshotDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_review_summary_game_date",
                table: "review_summary_snapshot");

            migrationBuilder.CreateIndex(
                name: "unique_review_summary_game_date",
                table: "review_summary_snapshot",
                columns: new[] { "GameId", "SnapshotDate" },
                unique: true);
        }
    }
}
