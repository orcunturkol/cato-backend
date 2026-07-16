using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamSpecialEventSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "steam_special_event",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "steam_special_events");

            migrationBuilder.CreateIndex(
                name: "idx_steam_special_event_source",
                table: "steam_special_event",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_steam_special_event_source",
                table: "steam_special_event");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "steam_special_event");
        }
    }
}
