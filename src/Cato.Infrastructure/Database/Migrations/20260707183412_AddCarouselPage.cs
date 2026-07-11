using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCarouselPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CarouselPage",
                table: "steam_special_event",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarouselPage",
                table: "steam_special_event");
        }
    }
}
