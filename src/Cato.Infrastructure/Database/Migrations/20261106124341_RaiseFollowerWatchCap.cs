using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RaiseFollowerWatchCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The first run (2026-11-06) read 15,000 of the daily set's ~28,700 games in 55 min and
            // none of the weekly set. 37,500 covers the daily set and a seventh of the weekly set
            // (~2.7 h at ~230 games a minute, inside the 4 h limit). The Jobs page cannot edit Args.
            migrationBuilder.UpdateData(
                table: "scheduled_job",
                keyColumn: "Name",
                keyValue: "follower_watch",
                columns: new[] { "Args", "UpdatedAt", "UpdatedBy" },
                values: new object[] { "--max-games 37500", new DateTime(2026, 11, 6, 13, 0, 0, DateTimeKind.Utc), "migration: raise the follower watch cap" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "scheduled_job",
                keyColumn: "Name",
                keyValue: "follower_watch",
                columns: new[] { "Args", "UpdatedAt", "UpdatedBy" },
                values: new object[] { "--max-games 15000", new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc), "seed: follower watch" });
        }
    }
}
