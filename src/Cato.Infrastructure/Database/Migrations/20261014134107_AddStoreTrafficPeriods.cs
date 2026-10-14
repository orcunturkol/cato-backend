using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreTrafficPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_traffic_breakdown",
                table: "steam_traffic_breakdown");

            migrationBuilder.AddColumn<long>(
                name: "OwnerImpressions",
                table: "steam_traffic_breakdown",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OwnerVisits",
                table: "steam_traffic_breakdown",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodEnd",
                table: "steam_traffic_breakdown",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "steam_traffic_breakdown",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Rows from the manual upload are one-day snapshots.
            migrationBuilder.Sql(
                "UPDATE steam_traffic_breakdown SET \"PeriodEnd\" = \"SnapshotDate\", \"UpdatedAt\" = \"CreatedAt\";");

            migrationBuilder.CreateIndex(
                name: "unique_traffic_breakdown",
                table: "steam_traffic_breakdown",
                columns: new[] { "GameId", "SnapshotDate", "PeriodEnd", "PageCategory", "PageFeature" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_traffic_breakdown",
                table: "steam_traffic_breakdown");

            migrationBuilder.DropColumn(
                name: "OwnerImpressions",
                table: "steam_traffic_breakdown");

            migrationBuilder.DropColumn(
                name: "OwnerVisits",
                table: "steam_traffic_breakdown");

            migrationBuilder.DropColumn(
                name: "PeriodEnd",
                table: "steam_traffic_breakdown");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "steam_traffic_breakdown");

            migrationBuilder.CreateIndex(
                name: "unique_traffic_breakdown",
                table: "steam_traffic_breakdown",
                columns: new[] { "GameId", "SnapshotDate", "PageCategory", "PageFeature" },
                unique: true);
        }
    }
}
