using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_entity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_entity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "main_game",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GameType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PriceUsd = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    DiscountPercent = table.Column<int>(type: "integer", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublisherId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsEarlyAccess = table.Column<bool>(type: "boolean", nullable: false),
                    IsReleased = table.Column<bool>(type: "boolean", nullable: false),
                    HeaderImageUrl = table.Column<string>(type: "text", nullable: true),
                    CapsuleImageUrl = table.Column<string>(type: "text", nullable: true),
                    ShortDescription = table.Column<string>(type: "text", nullable: true),
                    DetailedDescription = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    Platforms = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    SupportedLanguages = table.Column<string>(type: "text", nullable: true),
                    MetacriticScore = table.Column<int>(type: "integer", nullable: true),
                    SteamReviewScore = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    FollowersCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_main_game", x => x.Id);
                    table.ForeignKey(
                        name: "FK_main_game_legal_entity_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "legal_entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_main_game_legal_entity_PublisherId",
                        column: x => x.PublisherId,
                        principalTable: "legal_entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "game_genre",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenreName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GenreType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_genre", x => x.Id);
                    table.ForeignKey(
                        name: "FK_game_genre_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "genre_tag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TagType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_genre_tag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_genre_tag_main_game_GameId",
                        column: x => x.GameId,
                        principalTable: "main_game",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_genre_GameId",
                table: "game_genre",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_game_genre_GameId_GenreName_Source",
                table: "game_genre",
                columns: new[] { "GameId", "GenreName", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_genre_GenreName",
                table: "game_genre",
                column: "GenreName");

            migrationBuilder.CreateIndex(
                name: "IX_genre_tag_GameId",
                table: "genre_tag",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_genre_tag_GameId_TagName",
                table: "genre_tag",
                columns: new[] { "GameId", "TagName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_genre_tag_TagName",
                table: "genre_tag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_genre_tag_TagType",
                table: "genre_tag",
                column: "TagType");

            migrationBuilder.CreateIndex(
                name: "IX_legal_entity_EntityType",
                table: "legal_entity",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_legal_entity_Name",
                table: "legal_entity",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_main_game_AppId",
                table: "main_game",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_main_game_DeveloperId",
                table: "main_game",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_main_game_GameType",
                table: "main_game",
                column: "GameType");

            migrationBuilder.CreateIndex(
                name: "IX_main_game_PublisherId",
                table: "main_game",
                column: "PublisherId");

            migrationBuilder.CreateIndex(
                name: "IX_main_game_ReleaseDate",
                table: "main_game",
                column: "ReleaseDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_genre");

            migrationBuilder.DropTable(
                name: "genre_tag");

            migrationBuilder.DropTable(
                name: "main_game");

            migrationBuilder.DropTable(
                name: "legal_entity");
        }
    }
}
