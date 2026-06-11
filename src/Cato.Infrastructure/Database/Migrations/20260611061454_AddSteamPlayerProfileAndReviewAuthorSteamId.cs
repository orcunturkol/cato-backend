using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamPlayerProfileAndReviewAuthorSteamId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AuthorSteamId",
                table: "steam_review",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "steam_player_profile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SteamId64 = table.Column<long>(type: "bigint", nullable: false),
                    CommunityVisibilityState = table.Column<int>(type: "integer", nullable: false),
                    ProfileState = table.Column<int>(type: "integer", nullable: true),
                    PersonaName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfileUrl = table.Column<string>(type: "text", nullable: true),
                    AvatarHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AvatarFullUrl = table.Column<string>(type: "text", nullable: true),
                    PersonaState = table.Column<int>(type: "integer", nullable: true),
                    PersonaStateFlags = table.Column<int>(type: "integer", nullable: true),
                    RealName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PrimaryClanId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    TimeCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLogoff = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LocCountryCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    LocStateCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    LocCityId = table.Column<int>(type: "integer", nullable: true),
                    LastFetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_player_profile", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_steam_review_author_steamid",
                table: "steam_review",
                column: "AuthorSteamId");

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_profile_last_fetched",
                table: "steam_player_profile",
                column: "LastFetchedAt");

            migrationBuilder.CreateIndex(
                name: "idx_steam_player_profile_visibility",
                table: "steam_player_profile",
                column: "CommunityVisibilityState");

            migrationBuilder.CreateIndex(
                name: "unique_steam_player_profile_steamid",
                table: "steam_player_profile",
                column: "SteamId64",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steam_player_profile");

            migrationBuilder.DropIndex(
                name: "idx_steam_review_author_steamid",
                table: "steam_review");

            migrationBuilder.DropColumn(
                name: "AuthorSteamId",
                table: "steam_review");
        }
    }
}
