using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoDownloadPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_demo_download",
                table: "demo_download");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PeriodEnd",
                table: "demo_download",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "demo_download",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Rows of the manual upload are one-day periods of the upload day.
            migrationBuilder.Sql("UPDATE demo_download SET \"PeriodEnd\" = \"SnapshotDate\", \"UpdatedAt\" = \"CreatedAt\";");

            migrationBuilder.CreateIndex(
                name: "unique_demo_download",
                table: "demo_download",
                columns: new[] { "GameId", "SnapshotDate", "PeriodEnd", "GeoType", "GeoName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_demo_download",
                table: "demo_download");

            migrationBuilder.DropColumn(
                name: "PeriodEnd",
                table: "demo_download");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "demo_download");

            migrationBuilder.CreateIndex(
                name: "unique_demo_download",
                table: "demo_download",
                columns: new[] { "GameId", "SnapshotDate", "GeoType", "GeoName" },
                unique: true);
        }
    }
}
