using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupMemberCountSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "unique_group_member_count_snapshot",
                table: "group_member_count_snapshot");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "group_member_count_snapshot",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "steam_community_group");

            migrationBuilder.CreateIndex(
                name: "idx_group_member_count_source",
                table: "group_member_count_snapshot",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "unique_group_member_count_snapshot",
                table: "group_member_count_snapshot",
                columns: new[] { "GameId", "SnapshotDate", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_group_member_count_source",
                table: "group_member_count_snapshot");

            migrationBuilder.DropIndex(
                name: "unique_group_member_count_snapshot",
                table: "group_member_count_snapshot");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "group_member_count_snapshot");

            migrationBuilder.CreateIndex(
                name: "unique_group_member_count_snapshot",
                table: "group_member_count_snapshot",
                columns: new[] { "GameId", "SnapshotDate" },
                unique: true);
        }
    }
}
