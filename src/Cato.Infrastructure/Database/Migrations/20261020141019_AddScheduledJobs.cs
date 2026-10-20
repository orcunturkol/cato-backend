using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_job",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AgentGroup = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Module = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Args = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Cron = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    AlertFailedRatio = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    AlertMinItems = table.Column<int>(type: "integer", nullable: false),
                    ManagedBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "cron"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_job", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_change",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OldJson = table.Column<string>(type: "jsonb", nullable: false),
                    NewJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_job_change", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_scheduled_job_agent_group",
                table: "scheduled_job",
                column: "AgentGroup");

            migrationBuilder.CreateIndex(
                name: "idx_scheduled_job_change_job_changed",
                table: "scheduled_job_change",
                columns: new[] { "JobName", "ChangedAt" },
                descending: new[] { false, true });

            // Seed: the live crontab on 2026-10-20. Every job still runs from cron
            // (ManagedBy "cron"); a group moves to the dispatcher when its agent starts.
            // ccu, group_member_count and financial had no time limit in the crontab.
            var seededAt = new DateTime(2026, 10, 20, 14, 0, 0, DateTimeKind.Utc);
            const string seededBy = "seed: live crontab 2026-10-20";
            migrationBuilder.InsertData(
                table: "scheduled_job",
                columns: new[]
                {
                    "Name", "Label", "AgentGroup", "Module", "Args", "Cron", "Enabled", "TimeoutSeconds",
                    "AlertFailedRatio", "AlertMinItems", "ManagedBy", "UpdatedAt", "UpdatedBy",
                },
                values: new object[,]
                {
                    { "ccu", "Current players (CCU)", "light", "orchestrators.run_ccu", "", "0 */4 * * *", true, 3600, 0.10m, 1, "cron", seededAt, seededBy },
                    { "group_member_count", "Steam group member counts", "light", "orchestrators.run_daily_db", "", "0 3 * * *", true, 7200, 0.10m, 1, "cron", seededAt, seededBy },
                    { "financial", "Partner financials", "light", "orchestrators.run_daily_financial", "", "0 4 * * *", true, 3600, 0.10m, 1, "cron", seededAt, seededBy },
                    { "special_events", "Special events", "events", "orchestrators.run_daily_special_events", "", "30 4 * * *", true, 1800, 0.10m, 1, "cron", seededAt, seededBy },
                    { "sale_collection", "Sale collection", "events", "orchestrators.run_daily_sale_collection", "", "0 5 * * *", true, 3600, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamdb", "SteamDB rankings", "steamdb", "orchestrators.run_daily_steamdb", "", "30 3 * * *", true, 3600, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamdb_followers", "SteamDB follower history", "steamdb", "orchestrators.run_steamdb_followers", "--max-games 40", "45 0,1,6-23 * * *", true, 3000, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamdb_followers_priority", "SteamDB follower history, requests", "steamdb", "orchestrators.run_steamdb_followers", "--priority-only --max-games 20", null, true, 1200, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_wishlist", "Steamworks wishlist reports", "steamworks", "orchestrators.run_steamworks_wishlist", "", "0 10 * * *", true, 2700, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_traffic", "Steamworks store traffic", "steamworks", "orchestrators.run_steamworks_traffic", "", "0 11 * * *", true, 2700, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_navstats", "Steamworks Nav Stats", "steamworks", "orchestrators.run_steamworks_navstats", "", "30 11 * * *", true, 900, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_nextfest", "Steam Next Fest recaps", "steamworks", "orchestrators.run_steamworks_nextfest", "", "45 11 * * *", true, 600, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_players", "Steamworks players and app details", "steamworks", "orchestrators.run_steamworks_players", "", "0 12 * * *", true, 2700, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_demo_downloads", "Steamworks demo downloads", "steamworks", "orchestrators.run_steamworks_demo_downloads", "", "0 13 * * *", true, 1800, 0.10m, 1, "cron", seededAt, seededBy },
                    { "steamworks_bundles", "Steamworks bundle sales", "steamworks", "orchestrators.run_steamworks_bundles", "", "30 13 * * *", true, 1800, 0.10m, 1, "cron", seededAt, seededBy },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_job");

            migrationBuilder.DropTable(
                name: "scheduled_job_change");
        }
    }
}
