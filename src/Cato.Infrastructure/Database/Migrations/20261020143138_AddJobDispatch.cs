using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddJobDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_agent",
                columns: table => new
                {
                    AgentId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AgentGroup = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentDispatchId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_agent", x => x.AgentId);
                });

            migrationBuilder.CreateTable(
                name: "job_dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AgentGroup = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClaimedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_dispatch", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_job_dispatch_group_status",
                table: "job_dispatch",
                columns: new[] { "AgentGroup", "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "ux_job_dispatch_job_slot",
                table: "job_dispatch",
                columns: new[] { "JobName", "ScheduledAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_agent");

            migrationBuilder.DropTable(
                name: "job_dispatch");
        }
    }
}
