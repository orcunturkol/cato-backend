using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ccu_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CcuCount = table.Column<int>(type: "integer", nullable: false),
                    PeakCcuToday = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Steam API"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ccu_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ccu_history_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordsProcessed = table.Column<int>(type: "integer", nullable: false),
                    RecordsInserted = table.Column<int>(type: "integer", nullable: false),
                    RecordsUpdated = table.Column<int>(type: "integer", nullable: false),
                    RecordsFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "owned_game_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WishlistAdditions = table.Column<int>(type: "integer", nullable: false),
                    WishlistDeletions = table.Column<int>(type: "integer", nullable: false),
                    PurchasesAndActivations = table.Column<int>(type: "integer", nullable: false),
                    Gifts = table.Column<int>(type: "integer", nullable: false),
                    PeriodWishlistBalance = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owned_game_data", x => x.Id);
                    table.ForeignKey(
                        name: "FK_owned_game_data_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steam_sale_financial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Steam"),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    SalesUnits = table.Column<int>(type: "integer", nullable: false),
                    ReturnsUnits = table.Column<int>(type: "integer", nullable: false),
                    NetUnits = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "\"SalesUnits\" - \"ReturnsUnits\"", stored: true),
                    GrossRevenueUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    GrossReturnsUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    SteamCommissionUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    TaxUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    NetRevenueUsd = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BasePrice = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SalePrice = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DiscountId = table.Column<int>(type: "integer", nullable: true),
                    SaleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CombinedDiscountId = table.Column<int>(type: "integer", nullable: true),
                    RevenueShareTier = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_sale_financial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steam_sale_financial_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steam_traffic",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrafficDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StorePageVisits = table.Column<int>(type: "integer", nullable: false),
                    UniqueVisitors = table.Column<int>(type: "integer", nullable: false),
                    Impressions = table.Column<int>(type: "integer", nullable: false),
                    ClickThroughRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    WishlistAdditions = table.Column<int>(type: "integer", nullable: false),
                    WishlistDeletions = table.Column<int>(type: "integer", nullable: false),
                    NetWishlistChange = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "\"WishlistAdditions\" - \"WishlistDeletions\"", stored: true),
                    Purchases = table.Column<int>(type: "integer", nullable: false),
                    PurchaseConversionRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    TrafficSource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_traffic", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steam_traffic_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_ccu_history_game_time",
                table: "ccu_history",
                columns: new[] { "GameId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_ccu_history_timestamp",
                table: "ccu_history",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_ccu_history_unique",
                table: "ccu_history",
                columns: new[] { "GameId", "Timestamp", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_ingestion_log_source",
                table: "ingestion_log",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "idx_ingestion_log_start_time",
                table: "ingestion_log",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "idx_ingestion_log_status",
                table: "ingestion_log",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_owned_game_data_GameId_SnapshotDate",
                table: "owned_game_data",
                columns: new[] { "GameId", "SnapshotDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_sale_country",
                table: "steam_sale_financial",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "idx_steam_sale_date",
                table: "steam_sale_financial",
                column: "SaleDate");

            migrationBuilder.CreateIndex(
                name: "idx_steam_sale_game_date",
                table: "steam_sale_financial",
                columns: new[] { "GameId", "SaleDate" });

            migrationBuilder.CreateIndex(
                name: "unique_sale_record",
                table: "steam_sale_financial",
                columns: new[] { "GameId", "SaleDate", "CountryCode", "PackageId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_steam_traffic_game_date",
                table: "steam_traffic",
                columns: new[] { "GameId", "TrafficDate" });

            migrationBuilder.CreateIndex(
                name: "idx_steam_traffic_source",
                table: "steam_traffic",
                column: "TrafficSource");

            migrationBuilder.CreateIndex(
                name: "unique_traffic_record",
                table: "steam_traffic",
                columns: new[] { "GameId", "TrafficDate", "TrafficSource" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ccu_history");

            migrationBuilder.DropTable(
                name: "ingestion_log");

            migrationBuilder.DropTable(
                name: "owned_game_data");

            migrationBuilder.DropTable(
                name: "steam_sale_financial");

            migrationBuilder.DropTable(
                name: "steam_traffic");
        }
    }
}
