using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RebuildSteamSaleFinancialKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_sale_record",
                table: "steam_sale_financial");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxUsd",
                table: "steam_sale_financial",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SteamCommissionUsd",
                table: "steam_sale_financial",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "NetRevenueUsd",
                table: "steam_sale_financial",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "GrossRevenueUsd",
                table: "steam_sale_financial",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "GrossReturnsUsd",
                table: "steam_sale_financial",
                type: "numeric(18,4)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)");

            migrationBuilder.AddColumn<int>(
                name: "AppId",
                table: "steam_sale_financial",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BundleId",
                table: "steam_sale_financial",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeyRequestId",
                table: "steam_sale_financial",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineItemType",
                table: "steam_sale_financial",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartnerId",
                table: "steam_sale_financial",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryAppId",
                table: "steam_sale_financial",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-creating unique_sale_record fails once Steam lines that share
            // (GameId, SaleDate, CountryCode, PackageId, Platform) are stored.
            migrationBuilder.DropColumn(
                name: "AppId",
                table: "steam_sale_financial");

            migrationBuilder.DropColumn(
                name: "BundleId",
                table: "steam_sale_financial");

            migrationBuilder.DropColumn(
                name: "KeyRequestId",
                table: "steam_sale_financial");

            migrationBuilder.DropColumn(
                name: "LineItemType",
                table: "steam_sale_financial");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "steam_sale_financial");

            migrationBuilder.DropColumn(
                name: "PrimaryAppId",
                table: "steam_sale_financial");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxUsd",
                table: "steam_sale_financial",
                type: "numeric(15,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SteamCommissionUsd",
                table: "steam_sale_financial",
                type: "numeric(15,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "NetRevenueUsd",
                table: "steam_sale_financial",
                type: "numeric(15,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "GrossRevenueUsd",
                table: "steam_sale_financial",
                type: "numeric(15,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.AlterColumn<decimal>(
                name: "GrossReturnsUsd",
                table: "steam_sale_financial",
                type: "numeric(15,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)");

            migrationBuilder.CreateIndex(
                name: "unique_sale_record",
                table: "steam_sale_financial",
                columns: new[] { "GameId", "SaleDate", "CountryCode", "PackageId", "Platform" },
                unique: true);
        }
    }
}
