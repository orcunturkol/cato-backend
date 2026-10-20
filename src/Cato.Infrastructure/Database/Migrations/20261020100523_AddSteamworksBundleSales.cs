using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksBundleSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamworks_bundle_package_sales_daily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<int>(type: "integer", nullable: false),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    DateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitsSold = table.Column<int>(type: "integer", nullable: false),
                    UnitsReturned = table.Column<int>(type: "integer", nullable: false),
                    GrossSalesUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    GrossReturnsUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountReturnsUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_bundle_package_sales_daily", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "steamworks_bundle_sales_daily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<int>(type: "integer", nullable: false),
                    PartnerId = table.Column<int>(type: "integer", nullable: false),
                    DateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitsSold = table.Column<int>(type: "integer", nullable: false),
                    UnitsReturned = table.Column<int>(type: "integer", nullable: false),
                    GrossSalesUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    GrossReturnsUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    DiscountReturnsUsd = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_bundle_sales_daily", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_bundle_package_sales_daily",
                table: "steamworks_bundle_package_sales_daily",
                columns: new[] { "BundleId", "PackageId", "DateLocal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steamworks_bundle_sales_daily_date",
                table: "steamworks_bundle_sales_daily",
                column: "DateLocal");

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_bundle_sales_daily",
                table: "steamworks_bundle_sales_daily",
                columns: new[] { "BundleId", "PartnerId", "DateLocal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_bundle_package_sales_daily");

            migrationBuilder.DropTable(
                name: "steamworks_bundle_sales_daily");
        }
    }
}
