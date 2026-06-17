using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_run",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Producer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "cato-backend"),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MetricsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_run", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_job_run_job_name",
                table: "job_run",
                column: "JobName");

            migrationBuilder.CreateIndex(
                name: "idx_job_run_job_started",
                table: "job_run",
                columns: new[] { "JobName", "StartTime" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_job_run_start_time",
                table: "job_run",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "idx_job_run_status",
                table: "job_run",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_run");
        }
    }
}
