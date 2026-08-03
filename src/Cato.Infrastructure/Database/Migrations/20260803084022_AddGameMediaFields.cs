using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGameMediaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ScreenshotUrls",
                table: "main_game",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "TrailerThumbnailUrl",
                table: "main_game",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerUrl",
                table: "main_game",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreenshotUrls",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "TrailerThumbnailUrl",
                table: "main_game");

            migrationBuilder.DropColumn(
                name: "TrailerUrl",
                table: "main_game");
        }
    }
}
