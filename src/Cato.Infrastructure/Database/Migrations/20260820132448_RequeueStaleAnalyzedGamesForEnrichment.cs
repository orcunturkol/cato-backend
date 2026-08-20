using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cato.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RequeueStaleAnalyzedGamesForEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddGameEnrichmentState seeded LastEnrichedAt from UpdatedAt, which was
            // the best proxy available but means "last modified by anything" — the
            // price watcher touches these rows daily. So games carrying store data
            // years out of date look freshly enriched, and no staleness window can
            // find them: measured, 30 days caught 426 of 721.
            //
            // A reddit-analysed game with LastEnrichedAt set but no ReleaseDateRaw was
            // enriched before that column existed, i.e. by code that also never stored
            // screenshots or trailers (9.3% coverage on old rows against 97.7% on new).
            // Clearing the marker puts them back in the watcher's first band.
            //
            // Games where Steam genuinely returns no release_date — 0.8% of them — get
            // re-enriched once for nothing. That is cheaper than leaving the rest wrong.
            migrationBuilder.Sql("""
                UPDATE main_game
                SET "LastEnrichedAt" = NULL
                WHERE "AnalyzedAt" IS NOT NULL
                  AND "LastEnrichedAt" IS NOT NULL
                  AND "ReleaseDateRaw" IS NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to restore: the timestamps this cleared were themselves a proxy,
            // and the watcher will have written real ones by the time anyone rolls back.
        }
    }
}
