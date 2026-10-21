using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DispatchId",
                table: "job_run",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rule = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OpenKey = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenSentTo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResolveSentTo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_incident", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_alert_incident_opened_at",
                table: "alert_incident",
                column: "OpenedAt");

            migrationBuilder.CreateIndex(
                name: "ux_alert_incident_open_key",
                table: "alert_incident",
                column: "OpenKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_incident");

            migrationBuilder.DropColumn(
                name: "DispatchId",
                table: "job_run");
        }
    }
}
