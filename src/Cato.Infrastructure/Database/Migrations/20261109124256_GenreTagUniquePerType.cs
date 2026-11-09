using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class GenreTagUniquePerType : Migration
    {
        /// <summary>
        /// A user tag and a store category may share a name ("Co-op"), so the key gains TagType.
        /// The new index is built first and concurrently (no write lock on genre_tag); the rows
        /// already satisfy it, as they are unique on (GameId, TagName). The old index then goes
        /// concurrently too. If the build fails, Postgres leaves an INVALID index: drop it by hand
        /// before the migration runs again.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_genre_tag_GameId_TagType_TagName",
                table: "genre_tag",
                columns: new[] { "GameId", "TagType", "TagName" },
                unique: true)
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_genre_tag_GameId_TagName\";",
                suppressTransaction: true);
        }

        /// <summary>
        /// The old key holds one row per name, so a user tag that shares its exact name with a
        /// category is deleted first (the category won before this migration too).
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM genre_tag u
                USING genre_tag m
                WHERE u."GameId" = m."GameId" AND u."TagName" = m."TagName"
                  AND u."TagType" = 'UserTag' AND m."TagType" <> 'UserTag';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_genre_tag_GameId_TagName",
                table: "genre_tag",
                columns: new[] { "GameId", "TagName" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_genre_tag_GameId_TagType_TagName",
                table: "genre_tag");
        }
    }
}
