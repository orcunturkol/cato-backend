using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFreshnessDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_steamworks_wishlist_daily_date",
                table: "steamworks_wishlist_daily",
                column: "DateLocal")
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "idx_steamworks_nav_stat_period_end",
                table: "steamworks_nav_stat",
                column: "PeriodEnd")
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_traffic_breakdown_period_end",
                table: "steam_traffic_breakdown",
                column: "PeriodEnd")
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "idx_demo_download_period_end",
                table: "demo_download",
                column: "PeriodEnd")
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_steamworks_wishlist_daily_date",
                table: "steamworks_wishlist_daily");

            migrationBuilder.DropIndex(
                name: "idx_steamworks_nav_stat_period_end",
                table: "steamworks_nav_stat");

            migrationBuilder.DropIndex(
                name: "idx_steam_traffic_breakdown_period_end",
                table: "steam_traffic_breakdown");

            migrationBuilder.DropIndex(
                name: "idx_demo_download_period_end",
                table: "demo_download");
        }
    }
}
