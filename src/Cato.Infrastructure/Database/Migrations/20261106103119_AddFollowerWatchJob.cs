using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowerWatchJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The collector job that reads the follower-watch sets (FollowerWatchQueue). Off until
            // its collector image is out; switch it on, and set its --max-games, on the Jobs page.
            // 05:00 UTC: the light agent runs one job at a time, after group_member_count (03:00)
            // and financial (04:00), and before ccu (12:00).
            migrationBuilder.InsertData(
                table: "scheduled_job",
                columns: new[]
                {
                    "Name", "Label", "AgentGroup", "Module", "Args", "Cron", "Enabled", "TimeoutSeconds",
                    "AlertFailedRatio", "AlertMinItems", "ManagedBy", "UpdatedAt", "UpdatedBy",
                },
                values: new object[]
                {
                    "follower_watch", "Steam follower watch (untracked games)", "light", "orchestrators.run_follower_watch",
                    "--max-games 15000", "0 5 * * *", false, 14400, 0.10m, 50, "dispatcher",
                    new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc), "seed: follower watch",
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "scheduled_job", keyColumn: "Name", keyValue: "follower_watch");
        }
    }
}
