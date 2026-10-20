using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksNavStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamworks_nav_stat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublisherId = table.Column<int>(type: "integer", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    AppName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Attribution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Visits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_nav_stat", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_nav_stat",
                table: "steamworks_nav_stat",
                columns: new[] { "PublisherId", "PeriodStart", "PeriodEnd", "Attribution", "Region", "AppId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_nav_stat");
        }
    }
}
