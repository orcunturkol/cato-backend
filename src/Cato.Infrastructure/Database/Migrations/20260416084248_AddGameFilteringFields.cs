using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameFilteringFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "ContentDescriptorIds",
                table: "main_game",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilterReason",
                table: "main_game",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FilteredAt",
                table: "main_game",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFiltered",
                table: "main_game",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_main_game_IsFiltered_FilterReason",
                table: "main_game",
                columns: new[] { "IsFiltered", "FilterReason" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_game_IsFiltered_FilterReason",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "ContentDescriptorIds",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "FilterReason",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "FilteredAt",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "IsFiltered",
                table: "main_game");
        }
    }
}
