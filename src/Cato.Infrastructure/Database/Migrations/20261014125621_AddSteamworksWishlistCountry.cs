using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamworksWishlistCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "steamworks_wishlist_country",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    DateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Adds = table.Column<int>(type: "integer", nullable: false),
                    Deletes = table.Column<int>(type: "integer", nullable: false),
                    PurchasesAndActivations = table.Column<int>(type: "integer", nullable: false),
                    Gifts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steamworks_wishlist_country", x => x.Id);
                    table.ForeignKey(
                        name: "FK_steamworks_wishlist_country_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "unique_steamworks_wishlist_country",
                table: "steamworks_wishlist_country",
                columns: new[] { "GameId", "DateLocal", "Country" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steamworks_wishlist_country");
        }
    }
}
