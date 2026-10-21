using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;

namespace Cato.API.Services.DbTables;

/// <summary>Where the rows come from. A fixed list, so the frontend can colour and filter by it.</summary>
public static class DataSite
{
    public const string SteamStore = "Steam Store";
    public const string SteamWebApi = "Steam Web API";
    public const string SteamKit = "Steam (SteamKit2)";
    public const string SteamCommunity = "Steam Community";
    public const string Steamworks = "Steamworks";
    public const string SteamDb = "SteamDB";
    public const string Gamalytic = "Gamalytic";
    public const string RedditMetrics = "reddit_metrics";
    public const string Manual = "Manual entry";
    public const string Cato = "CATO";
}

/// <summary>How a table gets its rows today.</summary>
public static class DataCollection
{
    /// <summary>A scheduled job writes it.</summary>
    public const string Automatic = "Automatic";
    /// <summary>Only an upload or an API call by a person writes it.</summary>
    public const string Manual = "Manual";
    /// <summary>CATO writes it about itself (logs, schedules).</summary>
    public const string Internal = "Internal";
    /// <summary>Mapped, but no code writes it.</summary>
    public const string NoWriter = "No writer";
}

/// <summary>
/// One place rows come from. <c>Site</c> is one of <see cref="DataSite"/>; <c>Detail</c> is what is
/// read there (page, API method or file); <c>Url</c> is the URL as the code calls it, with
/// {placeholders}, or null for non-HTTP sources; <c>Via</c> is the job, queue or endpoint that
/// carries it into the table; <c>JobName</c> is that job's job_run.JobName, when one exists.
/// </summary>
public sealed record DbTableSource(
    string Site,
    string Detail,
    string? Url,
    string Via,
    string? JobName = null,
    string? Producer = null);

public sealed record DbColumnDoc(string? Source = null, string? Validation = null, string? Fallback = null);

/// <summary>
/// <c>Validation</c>: checks on the way in (unique keys come from the EF model and are added at
/// read time). <c>Fallback</c>: what happens when the source fails or a value is missing.
/// <c>Columns</c>: notes for columns whose origin differs from the table's sources, by property name.
/// </summary>
public sealed record DbTableDoc(
    string Table,
    string Collection,
    string Summary,
    IReadOnlyList<DbTableSource> Sources,
    IReadOnlyList<string> Validation,
    IReadOnlyList<string> Fallback,
    IReadOnlyDictionary<string, DbColumnDoc>? Columns = null);

/// <summary>
/// Data lineage for every table in <c>CatoDbContext</c>: where its rows come from, what
/// is checked on the way in, and what happens when the source fails. Written from the
/// code (backend ingestion, watchers and the collector's orchestrators), not from older
/// docs. A test fails when a mapped table has no entry here.
/// </summary>
public static class DbTableCatalog
{
    private const string Backend = JobRunProducer.CatoBackend;
    private const string Collector = JobRunProducer.ExternalCollector;
    private const string AppDetailsUrl = "https://store.steampowered.com/api/appdetails?appids={appId}&cc=us&l=english";

    private static readonly DbTableSource StoreEnrichment = new(
        DataSite.SteamStore, "App details API", AppDetailsUrl,
        "Backend store enrichment: GameEnrichmentWatcher (hourly sweep), PICS watcher (new games), POST /api/games/{id}/enrich, owned-sync",
        BackendJobNames.GameEnrichmentWatcher, Backend);

    private static readonly DbTableSource StoreUserTags = new(
        DataSite.SteamStore, "Store page user tags (HTML)", "https://store.steampowered.com/app/{appId}?l=english",
        "Backend store enrichment, same run as the app details", BackendJobNames.GameEnrichmentWatcher, Backend);

    private static readonly DbTableSource PicsDiscovery = new(
        DataSite.SteamKit, "SteamKit2 PICSGetChangesSince + PICSGetProductInfo (common section)", null,
        "Backend SteamPicsWatcher, every 10 min", BackendJobNames.SteamPicsWatcher, Backend);

    private static readonly DbTableSource PicsHistory = new(
        DataSite.SteamKit, "SteamKit2 PICSGetProductInfo, full KeyValue tree", null,
        "Backend SteamPicsChangeHistory, every 10 min", BackendJobNames.SteamPicsChangeHistory, Backend);

    private static readonly DbTableSource StorePrice = new(
        DataSite.SteamStore, "App details API, price_overview", AppDetailsUrl,
        "Backend SteamPriceWatcher, daily", BackendJobNames.SteamPriceWatcher, Backend);

    private static readonly DbTableSource StoreReviews = new(
        DataSite.SteamStore, "App reviews API",
        "https://store.steampowered.com/appreviews/{appId}?json=1&filter=recent&language=all&num_per_page=100",
        "Backend SteamReviewWatcher, daily, Sourcing and Owned games", BackendJobNames.SteamReviewWatcher, Backend);

    private static readonly DbTableSource PlayerSummaries = new(
        DataSite.SteamWebApi, "ISteamUser/GetPlayerSummaries v2",
        "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={key}&steamids={ids}",
        "Backend SteamPlayerProfileWatcher, every 30 min, review authors from Redis", BackendJobNames.SteamPlayerProfileWatcher, Backend);

    private static readonly DbTableSource AchievementSchema = new(
        DataSite.SteamWebApi, "ISteamUserStats/GetSchemaForGame v2",
        "https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={key}&appid={appId}&l=english",
        "Backend GameAchievementSchemaWatcher, every 6 h, Sourcing and Owned games", BackendJobNames.GameAchievementSchemaWatcher, Backend);

    private static readonly DbTableSource PlayerAchievements = new(
        DataSite.SteamWebApi, "ISteamUserStats/GetPlayerAchievements v1",
        "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={key}&steamid={id}&appid={appId}&l=english",
        "Backend PlayerAchievementWatcher, every 10 min, reviewers with public profiles", BackendJobNames.PlayerAchievementWatcher, Backend);

    private static readonly DbTableSource Ccu = new(
        DataSite.SteamWebApi, "ISteamUserStats/GetNumberOfCurrentPlayers v1",
        "https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={appId}",
        "Collector run_ccu, every 4 h → batch steam_current_players", "ccu", Collector);

    private static readonly DbTableSource GroupMembers = new(
        DataSite.SteamCommunity, "Community hub member list XML (memberCount)",
        "https://steamcommunity.com/games/{appId}/memberslistxml/?xml=1",
        "Collector run_daily_db, daily, through rotating proxies → batch group_member_count", "group_member_count", Collector);

    private static readonly DbTableSource SteamDbFollowers = new(
        DataSite.SteamDb, "Follower history graph API (logged-in browser session)",
        "https://steamdb.info/api/GetGraphFollowersLoggedIn/?appid={appId}",
        "Collector run_steamdb_followers: hourly 40-game runs plus a per-minute request pass; requested games first, then Owned games (daily refresh), then SteamDB list games by rank → batch steamdb_follower_history",
        "steamdb_followers", Collector);

    private static readonly DbTableSource SteamDbMostWished = new(
        DataSite.SteamDb, "Most wished ranking page", "https://steamdb.info/stats/mostwished/",
        "Collector run_daily_steamdb, daily, browser + proxies → batch steamdb_most_wished", "steamdb", Collector);

    private static readonly DbTableSource SteamDbWishlistActivity = new(
        DataSite.SteamDb, "Wishlist activity ranking page", "https://steamdb.info/stats/wishlistactivity/",
        "Collector run_daily_steamdb, daily, browser + proxies → batch steamdb_wishlist_activity", "steamdb", Collector);

    private static readonly DbTableSource Financials = new(
        DataSite.Steamworks, "IPartnerFinancialsService GetChangedDatesForPartner + GetDetailedSales",
        "https://partner.steam-api.com/IPartnerFinancialsService/GetDetailedSales/v001/?date={date}",
        "Collector run_daily_financial, daily → batch steam_financial", "financial", Collector);

    private static readonly DbTableSource WishlistReports = new(
        DataSite.Steamworks, "Wishlist CSV reports: actions, cohorts, countries",
        "https://partner.steampowered.com/report_csv.php?file=...&params=query=QueryWishlistActionsForCSV^appID={appId}",
        "Collector run_steamworks_wishlist, daily, every game the partner login sees → batch steamworks_wishlist_report",
        "steamworks_wishlist", Collector);

    private static readonly DbTableSource StoreTraffic = new(
        DataSite.Steamworks, "Store traffic CSV (navtrafficstats)",
        "https://partner.steamgames.com/apps/navtrafficstats/{appId}?attribution_filter=all&preset_date_range=custom&format=csv",
        "Collector run_steamworks_traffic, daily, Owned games → batch steamworks_store_traffic", "steamworks_traffic", Collector);

    private static readonly DbTableSource NavStats = new(
        DataSite.Steamworks, "Publisher Nav Stats CSV (one row per product)",
        "https://partner.steamgames.com/pub/navstats/{publisherId}",
        "Collector run_steamworks_navstats, daily 11:30 → batch steamworks_nav_stats", "steamworks_navstats", Collector);

    private static readonly DbTableSource SteamworksPlayers = new(
        DataSite.Steamworks, "Players page + app details page (HTML)",
        "https://partner.steampowered.com/app/players/{appId}/",
        "Collector run_steamworks_players, daily 12:00, Owned games → batch steamworks_players", "steamworks_players", Collector);

    private static readonly DbTableSource DemoDownloads = new(
        DataSite.Steamworks, "Demo downloads by region CSV",
        "https://partner.steampowered.com/nav_regions.php?csv=1&downloads=1&appID={demoAppId}",
        "Collector run_steamworks_demo_downloads, daily 13:00, demos from steamworks_app_details → batch steamworks_demo_downloads",
        "steamworks_demo_downloads", Collector);

    private static readonly DbTableSource BundleSales = new(
        DataSite.Steamworks, "Bundle sales CSV reports: QueryBundleSalesForCSV, QueryBundlePackageSalesForCSV",
        "https://partner.steampowered.com/bundles/",
        "Collector run_steamworks_bundles, daily 13:30, bundles sold in the last 35 days → batch steamworks_bundle_sales",
        "steamworks_bundles", Collector);

    private static readonly DbTableSource NextFest = new(
        DataSite.Steamworks, "Next Fest recap pages (embedded demo_stats)",
        "https://partner.steamgames.com/recap/nextfest/{slug}/{publisherId}",
        "Collector run_steamworks_nextfest, daily 11:45 → batch steamworks_nextfest", "steamworks_nextfest", Collector);

    private static readonly DbTableSource AchievementPercents = new(
        DataSite.SteamWebApi, "ISteamUserStats/GetGlobalAchievementPercentagesForApp v2 (no key)",
        "https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}",
        "Backend GameAchievementSchemaWatcher, every 6 h, Owned games only", BackendJobNames.GameAchievementSchemaWatcher, Backend);

    private static readonly DbTableSource SpecialEvents = new(
        DataSite.SteamStore, "Specials hub carousel + sale event pages (data-partnereventstore)",
        "https://store.steampowered.com/specials?l=english&cc=us",
        "Collector run_daily_special_events, daily → legacy queue source steam_special_events", "special_events", Collector);

    private static readonly DbTableSource SaleCollection = new(
        DataSite.SteamStore, "Sales news collection + event pages (data-partnereventstore)",
        "https://store.steampowered.com/news/collection/sales/?l=english&cc=us",
        "Collector run_daily_sale_collection, daily → legacy queue source steam_sale_collection", "sale_collection", Collector);

    private static readonly DbTableSource RedditAnalysis = new(
        DataSite.RedditMetrics, "Gemini game extraction (extracted_at, game name)", null,
        "Queue cato-game-analyzed, routing key game.analyzed.#");

    private static readonly IReadOnlyList<string> CrudOnly =
        ["Request validators on the API endpoints (allowed values, ranges, required fields)."];

    private static readonly IReadOnlyList<string> NoAutomaticSource =
        ["No automatic source. Rows exist only when someone calls the API."];

    public static IReadOnlyList<DbTableDoc> Tables { get; } =
    [
        new("main_game", DataCollection.Automatic,
            "One row per Steam app CATO tracks. Store data comes from Steam; collectors and reddit_metrics only create the row.",
            [StoreEnrichment, StoreUserTags, PicsDiscovery, StorePrice, StoreReviews, RedditAnalysis,
             new(DataSite.Steamworks, "Apps the Steamworks partner login can see", null,
                 "POST /api/games/owned-sync from collector run_steamworks_wishlist (sets GameType Owned)", "steamworks_wishlist", Collector),
             new(DataSite.Manual, "Game create, update, bulk import", null, "POST /api/games, PATCH /api/games/{id}, POST /api/games/bulk-import")],
            [
                "PICS discovery keeps only type game or demo, release state released, released on or after 2023-01-01, not free, name with Latin letters.",
                "After discovery the quality filter deletes the row for blocked content descriptors (1, 3, 4), blocked tags, blocked developers or publishers, or a non-Latin name. The AppId allowlist overrides it.",
                "Create: AppId > 0. Update: PriceUsd >= 0. Bulk import: GameType is Owned, Competitor, Sourcing or Other.",
            ],
            [
                "A collector item for an unknown app creates a stub row (GameType Other, name \"App {id}\"). The hourly enrichment sweep fills it later.",
                "App details failure (success=false, or HTTP 429 after 3 retries) adds 1 to EnrichmentFailures. At 5 failures the game leaves the never-enriched queue.",
                "Store records older than 30 days are enriched again (GameEnrichment:RefreshAfterDays).",
                "If the user-tag scrape fails, genre_tag keeps only the store categories.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["AppId"] = new("Steam app id from the creator: PICS change feed, collector batch item, reddit_metrics, owned-sync, POST /api/games", "AppId > 0 on create"),
                ["Name"] = new("Steam Store app details: name. First value from PICS common.name, a collector payload name or \"App {id}\"", null, "Kept when app details has no name. reddit_metrics only replaces names that start with \"App \"."),
                ["GameType"] = new("CATO: Other on discovery or stub, Owned from Steamworks owned-sync, Sourcing from bulk import, or PATCH /api/games/{id}", "Owned, Competitor, Sourcing or Other"),
                ["ReleaseDate"] = new("Steam Store app details: release_date.date. First value from PICS steam_release_date", "Parsed with fixed date formats", "Kept when the text is not a date (\"Q4 2026\", \"To be announced\"); see ReleaseDateRaw"),
                ["ReleaseDateRaw"] = new("Steam Store app details: release_date.date, literal text"),
                ["IsReleased"] = new("Steam Store app details: not coming_soon. True on PICS discovery"),
                ["PriceUsd"] = new("Steam Store app details: price_overview.initial / 100. Daily refresh by SteamPriceWatcher", "PATCH: >= 0", "Kept when price_overview is missing (free or delisted games)"),
                ["DiscountPercent"] = new("Steam Store app details: price_overview.discount_percent. Daily refresh by SteamPriceWatcher", null, "Kept when price_overview is missing"),
                ["DeveloperId"] = new("Steam Store app details: developers[0] → legal_entity (find or create)", null, "Never cleared"),
                ["PublisherId"] = new("Steam Store app details: publishers[0] → legal_entity (find or create)", null, "Never cleared"),
                ["IsEarlyAccess"] = new("Steam Store app details: genres contains \"Early Access\"", null, "Enrichment never sets it back to false"),
                ["HeaderImageUrl"] = new("Steam Store app details: header_image", null, "Overwritten on every enrichment, also with null"),
                ["CapsuleImageUrl"] = new("Steam Store app details: capsule_image", null, "Overwritten on every enrichment, also with null"),
                ["ScreenshotUrls"] = new("Steam Store app details: screenshots[].path_full", null, "Overwritten on every enrichment, also with null"),
                ["TrailerUrl"] = new("Steam Store app details: highlighted movie, else the first; mp4.max → mp4.480 → webm.max → webm.480 → hls_h264", null, "Overwritten on every enrichment, also with null"),
                ["TrailerThumbnailUrl"] = new("Steam Store app details: movie thumbnail", null, "Overwritten on every enrichment, also with null"),
                ["ShortDescription"] = new("Steam Store app details: short_description (also PATCH)", null, "Overwritten on every enrichment, also with null"),
                ["DetailedDescription"] = new("Steam Store app details: detailed_description", null, "Overwritten on every enrichment, also with null"),
                ["Website"] = new("Steam Store app details: website (also PATCH)", null, "Overwritten on every enrichment, also with null"),
                ["SupportedLanguages"] = new("Steam Store app details: supported_languages", null, "Overwritten on every enrichment, also with null"),
                ["ContentDescriptorIds"] = new("Steam Store app details: content_descriptors.ids", null, "Null when the list is empty"),
                ["Platforms"] = new("Steam Store app details: platforms {windows, mac, linux}", null, "Kept when absent"),
                ["MetacriticScore"] = new("Steam Store app details: metacritic.score", null, "Kept when absent"),
                ["SteamReviewScore"] = new("Steam Store app reviews: query_summary.review_score_desc (latest review_summary_snapshot), SteamReviewWatcher"),
                ["ReviewCount"] = new("Steam Store app reviews: query_summary.total_reviews (latest review_summary_snapshot), SteamReviewWatcher"),
                ["FollowersCount"] = new("Manual only: PATCH /api/games/{id}. Not filled from group_member_count_snapshot or SteamDB"),
                ["IsFiltered"] = new("CATO quality filter: POST /admin/filter/backfill"),
                ["FilterReason"] = new("CATO quality filter: POST /admin/filter/backfill"),
                ["FilteredAt"] = new("CATO quality filter: POST /admin/filter/backfill"),
                ["LastEnrichedAt"] = new("CATO: set when store enrichment succeeds"),
                ["EnrichmentFailures"] = new("CATO: +1 when app details fails", null, "At 5 the game leaves the never-enriched queue"),
                ["AnalyzedAt"] = new("reddit_metrics: game.analyzed extracted_at"),
            }),

        new("legal_entity", DataCollection.Automatic,
            "Developers and publishers.",
            [StoreEnrichment, new(DataSite.Manual, "DeveloperName / PublisherName", null, "POST /api/games")],
            ["Found by (Name, EntityType) in code before insert; no unique index."],
            ["Never deleted. A game's link is set to null if the entity goes."],
            new Dictionary<string, DbColumnDoc>
            {
                ["Name"] = new("Steam Store app details: developers[0] or publishers[0]"),
                ["EntityType"] = new("CATO: Developer or Publisher, from the field it came from"),
                ["ContactEmail"] = new("No writer"),
                ["Website"] = new("No writer"),
                ["Notes"] = new("No writer"),
            }),

        new("game_genre", DataCollection.Automatic,
            "Steam store genres of a game.",
            [StoreEnrichment],
            ["Deleted and written again on every enrichment.", "Blank names and case-insensitive duplicates are skipped."],
            ["If app details has no genres, the old rows are deleted and none are added."],
            new Dictionary<string, DbColumnDoc>
            {
                ["GenreName"] = new("Steam Store app details: genres[].description"),
                ["GenreType"] = new("CATO: Primary for the first genre, Secondary for the rest"),
                ["Source"] = new("CATO: always \"Steam\""),
            }),

        new("genre_tag", DataCollection.Automatic,
            "Store categories and user tags of a game.",
            [StoreEnrichment, StoreUserTags],
            ["Deleted and written again on every enrichment.", "Case-insensitive duplicates are skipped; a category wins over a user tag."],
            ["If the store page scrape fails, only the categories are stored."],
            new Dictionary<string, DbColumnDoc>
            {
                ["TagName"] = new("Steam Store app details: categories[].description (Mechanic), or store page popular tags (UserTag)"),
                ["TagType"] = new("CATO: Mechanic for categories, UserTag for store page tags"),
                ["Weight"] = new("CATO: rank of the user tag on the store page (1 = top); 0 for categories"),
                ["Source"] = new("CATO: always \"Steam\""),
            }),

        new("steam_sale_financial", DataCollection.Automatic,
            "Steam sales lines per game, date, country, package and platform.",
            [Financials, new(DataSite.Manual, "Sales JSON file", null, "POST /api/ingestion/financial")],
            [
                "No business key: Steam sends several lines per (date, country, package, platform). The game's rows for each date are deleted and the new set is inserted.",
                "Returns are stored as positive numbers; key activations go to ActivatedUnits, not SalesUnits.",
                "A line without a date that parses is skipped and counted as failed. Text longer than the column is cut.",
            ],
            [
                "Any error rolls back the whole batch; the message goes to the dead-letter queue. The collector's highwatermark may move on anyway; recover with run_daily_financial --since.",
                "Unknown game: a stub main_game row is created.",
                "A page of GetDetailedSales that fails mid-way ends that date with the lines read so far.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["SaleDate"] = new("GetDetailedSales: date"),
                ["CountryCode"] = new("GetDetailedSales: country_code", null, "\"XX\" when missing"),
                ["Platform"] = new("GetDetailedSales: platform", null, "\"Steam\" when missing"),
                ["SalesUnits"] = new("GetDetailedSales: gross_units_sold", null, "0 when missing"),
                ["ReturnsUnits"] = new("GetDetailedSales: gross_units_returned, stored positive"),
                ["ActivatedUnits"] = new("GetDetailedSales: gross_units_activated", null, "0 when missing"),
                ["NetUnits"] = new("Database: SalesUnits - ReturnsUnits (stored computed column)"),
                ["GrossRevenueUsd"] = new("GetDetailedSales: gross_sales_usd", null, "0 when not a number"),
                ["GrossReturnsUsd"] = new("GetDetailedSales: gross_returns_usd, stored positive", null, "0 when not a number"),
                ["TaxUsd"] = new("GetDetailedSales: net_tax_usd", null, "0 when not a number"),
                ["NetRevenueUsd"] = new("GetDetailedSales: net_sales_usd (after refunds and VAT, before Steam's share)", null, "0 when not a number"),
                ["SteamCommissionUsd"] = new("No writer"),
                ["DiscountId"] = new("No writer"),
                ["SaleType"] = new("GetDetailedSales: package_sale_type"),
                ["CombinedDiscountId"] = new("GetDetailedSales: combined_discount_id"),
                ["RevenueShareTier"] = new("GetDetailedSales: additional_revenue_share_tier"),
                ["PrimaryAppId"] = new("GetDetailedSales: primary_appid"),
            }),

        new("steam_traffic", DataCollection.NoWriter,
            "Old daily traffic table. Store traffic now goes to steam_traffic_breakdown.",
            [],
            ["No code writes this table. GET /api/games/{appId}/traffic still reads it."],
            ["Not applicable."],
            new Dictionary<string, DbColumnDoc>
            {
                ["NetWishlistChange"] = new("Database: WishlistAdditions - WishlistDeletions (stored computed column)"),
            }),

        new("ccu_history", DataCollection.Automatic,
            "Concurrent player counts over time.",
            [Ccu,
             new(DataSite.SteamKit, "SteamKit2 GetNumberOfCurrentPlayers", null, "POST /api/steamkit/ccu/{appId}/save (on request)"),
             new(DataSite.Gamalytic, "Peak CCU history file", null, "POST /api/ingestion/peak-ccu (manual upload)")],
            [
                "Insert only: an existing (GameId, Timestamp, Source) row is skipped.",
                "Collector keeps a value only when Steam answers result = 1 with a player_count.",
                "The SteamKit save skips when a SteamKit2 row exists from the last minute.",
            ],
            [
                "Collector: HTTP 429 is retried 3 times. An app that fails 5 runs in a row goes to the Redis quarantine set.",
                "Unknown game: the collector batch creates a stub; the uploads reject it.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["CcuCount"] = new("GetNumberOfCurrentPlayers: response.player_count", null, "0 when missing"),
                ["Timestamp"] = new("Collector: response timestamp", null, "The batch item's scraped_at when missing"),
                ["Source"] = new("CATO: \"Steam API\" (collector), \"SteamKit2\" or \"Gamalytic\""),
                ["PeakCcuToday"] = new("No writer"),
            }),

        new("owned_game_data", DataCollection.Manual,
            "Old wishlist summary per owned game and day. The daily Steamworks reports now go to steamworks_wishlist_daily.",
            [new(DataSite.Steamworks, "Wishlist summary JSON file", null, "POST /api/ingestion/owned-game, or legacy queue source steamworks_owned_game (no scheduled producer)")],
            ["Insert only: a second file on the same day is ignored.", "game_id is required; the game must exist."],
            ["A field that is missing or not a number counts as failed; the log still reads Completed."],
            new Dictionary<string, DbColumnDoc>
            {
                ["SnapshotDate"] = new("CATO: upload day (UTC)"),
            }),

        new("group_member_count_snapshot", DataCollection.Automatic,
            "Community hub followers. Two series in one table, told apart by Source.",
            [GroupMembers, SteamDbFollowers],
            [
                "Every query and upsert filters on Source, so the daily scrape never overwrites the SteamDB history.",
                "Follower history rows that do not parse are skipped; a date sent twice in one payload is written once.",
                "SteamDB graph data must have a one-day step; other steps are rejected.",
            ],
            [
                "A failed community scrape is still stored: MemberCount 0 and the Error text on insert; on update the old count is kept.",
                "Community scrape: 5 tries per app with proxy rotation; an app with no community group ends in the Redis quarantine set after 5 runs.",
                "SteamDB: HTTP 429 is retried 5 times; a rate limit or lost session stops the run and it resumes at the same game next time.",
                "SteamDB: a queued game is always fetched fresh; a seeded game that has a cached file on the collector is sent again from that file.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["Source"] = new("CATO: steam_community_group (daily scrape) or steamdb_follower_history (SteamDB backfill)"),
                ["MemberCount"] = new("Steam Community memberCount, or SteamDB follower_count"),
                ["Error"] = new("Collector: scrape error text of the community scrape"),
                ["ScrapedAt"] = new("Collector: scraped_at of the batch item"),
            }),

        new("steamdb_snapshot", DataCollection.Automatic,
            "Daily SteamDB ranking rows: most wished and wishlist activity.",
            [SteamDbMostWished, SteamDbWishlistActivity],
            [
                "Upsert by (GameId, SnapshotDate, Source); SnapshotDate is the date of scraped_at.",
                "Collector: Cloudflare and IP-ban pages are detected; rows without an app id are skipped; a row count different from the page's total only logs a warning.",
            ],
            [
                "Collector: up to 5 tries, each on a fresh proxy and browser. If today's CSV exists, it is published again without a scrape.",
                "A payload without source or rank sends the whole batch to the dead-letter queue.",
                "Unknown game: a stub main_game row is created with the SteamDB name.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["Source"] = new("Payload data.source: steamdb_most_wished or steamdb_wishlist_activity"),
                ["Rank"] = new("SteamDB table: rank", "Required"),
                ["Follows"] = new("SteamDB table: follows", null, "0 when missing"),
                ["SevenDayGain"] = new("SteamDB table: 7-day gain", null, "0 when missing"),
                ["Price"] = new("SteamDB table: price", null, "Null when missing"),
                ["Rating"] = new("SteamDB table: rating", null, "Null when missing"),
                ["Release"] = new("SteamDB table: release", null, "Null when missing"),
            }),

        new("steam_special_event", DataCollection.Automatic,
            "Steam sale events (seasonal sales, fests, publisher sales).",
            [SpecialEvents, SaleCollection],
            ["Upsert by AnnouncementGid; an event without a gid is skipped and counted as failed.", "Collector removes duplicate events by gid."],
            [
                "EventUrl, ClanAccountId and EventType keep the old value when absent; TabNames changes only when sent.",
                "An error in one event is logged and the other events continue.",
                "Rows are never deleted; LastSeenAt shows when an event was last seen.",
                "A failed publish stops the job; the sale-collection state file does not move on.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["Source"] = new("CATO: the job that saw the event last (steam_special_events or steam_sale_collection)"),
                ["TabNames"] = new("Specials hub tab labels joined to the event's hub placements"),
                ["FirstSeenAt"] = new("CATO: first time a job saw the event"),
                ["LastSeenAt"] = new("CATO: last time a job saw the event"),
            }),

        new("steam_special_event_game", DataCollection.Automatic,
            "Games tagged in each Steam sale event.",
            [SpecialEvents, SaleCollection],
            ["Only item_type game or demo with an app id; duplicates per event are removed.", "Upsert by (SteamSpecialEventId, GameId)."],
            ["Unknown game: a stub main_game row is created.", "Displayed start and end and discount keep the old value when absent."]),

        new("price_snapshot", DataCollection.Automatic,
            "Price history of released games.",
            [StorePrice, new(DataSite.Gamalytic, "Regional price history file", null, "POST /api/ingestion/regional-prices (manual upload)")],
            [
                "The watcher inserts a row only when the price differs from the last snapshot.",
                "The watcher covers games released since 2023-01-01 with a price above 0.",
                "Upload: an entry without fullPrice or finalPrice is counted as failed; a snapshot without timestamp is skipped.",
            ],
            ["No price_overview in app details: the game is skipped. An error counts the game as failed and the job reads PartialSuccess."],
            new Dictionary<string, DbColumnDoc>
            {
                ["BasePriceUsd"] = new("App details: price_overview.initial / 100. Upload: fullPrice in that currency"),
                ["FinalPriceUsd"] = new("App details: price_overview.final / 100. Upload: finalPrice in that currency"),
                ["DiscountPercent"] = new("App details: discount_percent. Upload: computed from full and final price"),
                ["Currency"] = new("App details: price_overview.currency. Upload: currency key", null, "\"USD\" when missing"),
            }),

        new("wishlist_insight", DataCollection.Manual,
            "Games that are also wishlisted with a game.",
            [new(DataSite.Gamalytic, "Wishlist insights file (alsoWishlisted)", null, "POST /api/ingestion/wishlist-insights (manual upload)")],
            ["alsoWishlisted is required; the game must exist; a steamId that is not a number is counted as failed."],
            ["Missing numbers become 0; a missing name becomes empty text."],
            new Dictionary<string, DbColumnDoc>
            {
                ["SnapshotDate"] = new("CATO: upload day (UTC)"),
            }),

        new("steamworks_wishlist_daily", DataCollection.Automatic,
            "Wishlist adds, deletes, purchases and gifts per owned game and Pacific-time day.",
            [WishlistReports, new(DataSite.Steamworks, "Wishlist CSV file", null, "POST /api/ingestion/wishlists (manual upload)")],
            [
                "The whole history is sent every day; only new or changed rows are written.",
                "Dates must be yyyy-MM-dd and counts must be numbers; bad rows are skipped.",
                "Collector: a logged-out session or a response that is not a CSV stops that report.",
            ],
            [
                "Rows missing from a later export are kept. The export is sparse: no row means zero.",
                "If the actions report fails, it is sent as null and the table is not touched.",
                "A login or rate-limit error stops the whole run.",
            ]),

        new("steamworks_wishlist_cohort", DataCollection.Automatic,
            "Wishlist conversions by the month the wishlist was added.",
            [WishlistReports],
            ["The whole history is sent every day; only new or changed rows are written."],
            ["Rows missing from a later export are kept.", "If the cohorts report fails, it is sent as null and the table is not touched."]),

        new("steamworks_wishlist_country", DataCollection.Automatic,
            "Wishlist actions by country, last ~35 days per run.",
            [WishlistReports],
            ["Only the payload's date range is compared.", "country is required, at most 100 characters; a blank country row is skipped."],
            ["Rows outside the payload's date range are kept.", "If the countries report fails, it is sent as null and the table is not touched."],
            new Dictionary<string, DbColumnDoc>
            {
                ["Country"] = new("Steamworks CSV: country name (the CSV column is called CountryCode)"),
                ["Region"] = new("Steamworks CSV: Steam region of the country", null, "Empty text when blank"),
            }),

        new("steam_country", DataCollection.Automatic,
            "Steam country codes, names and regions, used to group sales by region.",
            [Financials],
            ["Upsert by CountryCode from reference_data.country_info of each sales batch."],
            ["Name falls back to the code; region falls back to empty text. Never deleted."]),

        new("steamworks_bundle", DataCollection.Automatic,
            "Steam bundle names.",
            [Financials, BundleSales],
            ["Upsert by BundleId from reference_data.bundle_info of each sales batch and from each bundle sales item."],
            ["Name falls back to \"Bundle {id}\". Never deleted."]),

        new("steamworks_nav_stat", DataCollection.Automatic,
            "Store impressions and visits per product of the publisher (demos and playtests too), per period and filter.",
            [NavStats],
            [
                "Upsert by (PublisherId, period, Attribution, Region, AppId).",
                "Attribution is all, store, steamworks or external. A row needs app_id > 0, impressions and visits.",
                "An invalid period or filter counts all rows as failed.",
            ],
            [
                "Keyed by app id, not game: no game row is stubbed.",
                "Daily one-day periods, history as whole months, never overlapping. Rows missing from a later export are kept.",
                "A login or rate-limit error stops the run.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["PeriodStart"] = new("Collector: period start (GMT)"),
                ["PeriodEnd"] = new("Collector: period end (GMT, inclusive)"),
                ["AppName"] = new("Steamworks CSV product name", null, "Kept when a later row has no name"),
            }),

        new("steamworks_player_country", DataCollection.Automatic,
            "Yesterday's players by country of each Owned game (shares, not counts).",
            [SteamworksPlayers],
            [
                "Unique by (GameId, SnapshotDate, CountryCode).",
                "A country needs a code, a name and a share from 0 to 100.",
            ],
            [
                "The set for a date is replaced: a country that dropped out of a re-fetch is removed.",
                "Steam shows only yesterday, so there is no history before the job started.",
                "A part sent as null leaves the table alone.",
            ]),

        new("steamworks_app_details", DataCollection.Automatic,
            "Daily snapshot of the Steamworks app details page of each Owned game and its demo.",
            [SteamworksPlayers],
            ["Upsert by (AppId, SnapshotDate); an item without app_id > 0 counts as failed."],
            [
                "A value missing from the page is stored as null.",
                "A part sent as null leaves the table alone.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["GameId"] = new("CATO: the main game; a demo row carries the main game's id"),
                ["IsDemo"] = new("Collector: true for the demo_details part"),
            }),

        new("steamworks_bundle_sales_daily", DataCollection.Automatic,
            "Daily bundle sales per partner.",
            [BundleSales],
            ["Upsert by (BundleId, PartnerId, DateLocal). Money is decimal(18,4)."],
            [
                "Steam sends returns negative; they are stored positive.",
                "A day missing from a later export keeps its row.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["DateLocal"] = new("Steamworks CSV DateLocal (Pacific time date)"),
            }),

        new("steamworks_bundle_package_sales_daily", DataCollection.Automatic,
            "Daily sales of each package inside a bundle.",
            [BundleSales],
            ["Upsert by (BundleId, PackageId, DateLocal). Money is decimal(18,4)."],
            [
                "Steam sends returns negative; they are stored positive.",
                "A day missing from a later export keeps its row. A package's game comes from steam_sale_financial at read time.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["DateLocal"] = new("Steamworks CSV DateLocal (Pacific time date)"),
            }),

        new("steamworks_nextfest_stat", DataCollection.Automatic,
            "Cumulative Next Fest snapshots per game: demo players, wishlists, wishlists from demo players.",
            [NextFest],
            ["Upsert by (AppId, FestKey, SnapshotEnd)."],
            [
                "Re-reading a past fest changes nothing.",
                "A login or rate-limit error stops the run; a failed recap page skips that fest.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["FestKey"] = new("Steam sale page id, e.g. sale_nextfest_june_2025"),
            }),

        new("steam_traffic_breakdown", DataCollection.Automatic,
            "Store page impressions and visits per page category and feature, per period.",
            [StoreTraffic, new(DataSite.Steamworks, "Store traffic CSV file", null, "POST /api/ingestion/store-traffic (manual upload, filed under the upload day)")],
            [
                "Rows with the same category and feature are added together before the upsert.",
                "Collector: the CSV must start with \"Page / Category\"; an HTML error page is rejected.",
            ],
            [
                "An invalid period counts all rows as failed. Rows missing from a later fetch are kept.",
                "A report error skips the rest of that game's periods; a login or rate-limit error stops the run.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["SnapshotDate"] = new("Collector: period start (GMT)"),
                ["PeriodEnd"] = new("Collector: period end (GMT, inclusive)"),
                ["PageCategory"] = new("Steamworks CSV: Page / Category"),
                ["PageFeature"] = new("Steamworks CSV: Page / Feature", null, "\"(none)\" when blank"),
                ["OwnerImpressions"] = new("Steamworks CSV: owner impressions", null, "0 when missing"),
                ["OwnerVisits"] = new("Steamworks CSV: owner visits", null, "0 when missing"),
            }),

        new("app_kv_snapshot", DataCollection.Automatic,
            "Full PICS KeyValue tree of a tracked app at each change number.",
            [PicsHistory],
            ["Skipped when the change number equals the last snapshot's.", "Tracked apps: released since 2023-01-01 with a price above 0."],
            ["The change number is saved only after the rows are saved. An error in one app is logged and skipped. Rows stay when the game is deleted."]),

        new("app_change_record", DataCollection.Automatic,
            "One row per changed PICS key of a tracked app.",
            [PicsHistory],
            ["Diff of the flattened KeyValue tree against the last snapshot. On an app's first snapshot every key is written as Added."],
            ["Same as app_kv_snapshot."]),

        new("ingestion_log", DataCollection.Internal,
            "One row per ingestion run (batch, legacy message or upload).",
            [new(DataSite.Cato, "Ingestion runs", null, "Every ingestion method and the batch dispatcher (Source = batch:{source})")],
            ["Status is Running, Completed or Failed."],
            ["The Running row is saved before the work starts, so it survives a rollback and then reads Failed."]),

        new("job_run", DataCollection.Internal,
            "One row per run of a backend watcher or collector job.",
            [new(DataSite.Cato, "Backend watcher runs", null, "JobRunTracker in each backend watcher"),
             new(DataSite.Cato, "Collector job runs", null, "POST /api/job-runs from each collector orchestrator")],
            ["Status must be reportable; EndTime is required unless Running and must not be before StartTime; metrics must be a JSON object."],
            ["Reporting is best effort: the collector never fails a job because the report failed. A killed job leaves a Running row that reads Lost."]),

        new("job_schedule", DataCollection.Internal,
            "The collector's published cron schedule.",
            [new(DataSite.Cato, "Collector crontab", null, "PUT /api/job-schedules/{producer} on every collector run")],
            ["Producer is not cato-backend; at most 200 entries; cron has 5 fields; enabled entries need a cron."],
            ["Each publish replaces the whole list."]),

        new("scheduled_job", DataCollection.Internal,
            "One row per job: a collector job (cron, time limit, agent group; the crontab or the dispatcher starts it) or a backend watcher (interval, switch), with its alert threshold.",
            [new(DataSite.Cato, "Seed from the 2026-10-20 crontab", null, "AddScheduledJobs migration"),
             new(DataSite.Cato, "Watcher settings", null, "Each backend watcher writes its row from its settings on its first cycle (WatcherSchedule)"),
             new(DataSite.Cato, "Jobs page edits", null, "PUT /api/scheduled-jobs/{name}, POST /api/scheduled-jobs/groups/{group}/managed-by")],
            ["Cron is checked with Cronos; the interval between runs must be longer than the time limit; overlaps in one agent group give warnings.",
             "Only dispatcher-managed jobs and watchers can be edited; a watcher's interval is 1 minute to 7 days."],
            ["No automatic source: rows change only through the API, and every change is logged in scheduled_job_change."]),

        new("scheduled_job_change", DataCollection.Internal,
            "Audit log of scheduled_job: who changed what, when and why, with the values before and after.",
            [new(DataSite.Cato, "Schedule edits", null, "Written with each scheduled_job change")],
            ["Old and new values are JSON objects."],
            ["Append only."]),

        new("job_dispatch", DataCollection.Internal,
            "One row per run the dispatcher hands to an agent group: a cron slot or a run-now request.",
            [new(DataSite.Cato, "Job dispatcher", null, "JobDispatcherService every 20 s (worker), POST /api/scheduled-jobs/{name}/run, the follower-history button")],
            ["(JobName, ScheduledAt) is unique, so a restart never dispatches a slot twice; one claimed run per agent group."],
            ["A pending run nobody claims expires after 6 h; a claimed run with no finish is marked lost after its time limit + 10 min, or when its agent claims again."]),

        new("job_agent", DataCollection.Internal,
            "One row per job agent process (cato-agent@<group>): when it was last heard and what it runs now.",
            [new(DataSite.Cato, "Agent claims and heartbeats", null, "POST /api/job-agents/{group}/claim and /heartbeat")],
            ["The agent id is <group>@<host>; the group must be light, events, steamdb or steamworks."],
            ["A silent agent stays in the table; the agent_offline alert reports it after 10 min."]),

        new("worker_state", DataCollection.Internal,
            "Small values a background service needs after a restart: the PICS change numbers of the two PICS watchers.",
            [new(DataSite.Cato, "PICS watchers", null, "SteamPicsWatcherService and SteamPicsChangeHistoryService after each poll", BackendJobNames.SteamPicsWatcher, Backend)],
            ["One row per key; a change number is written only when it is not 0."],
            ["A missing row falls back to the container file of the old build once, then to change 0 (all changes again)."]),

        new("alert_incident", DataCollection.Internal,
            "One row per alert: a rule firing for a job, an agent or a queue, from open to resolved, with the channels that sent each message.",
            [new(DataSite.Cato, "Alert monitor", null, "AlertMonitorService every 60 s (worker)")],
            ["OpenKey (job|rule) is unique while the incident is open, so one open incident per job and rule."],
            ["A channel that fails is retried alone next cycle; an incident resolved before any message went out sends nothing."]),

        new("marketing_target", DataCollection.Manual,
            "Influencers, media, events and mailing lists.",
            [new(DataSite.Manual, "Marketing targets", null, "POST, PATCH, DELETE /api/marketing-targets")],
            CrudOnly, ["Invalid JSON in PreferredGenres or PreferredTags is stored as null.", .. NoAutomaticSource]),

        new("action", DataCollection.Manual,
            "Marketing actions (campaigns, discounts, posts).",
            [new(DataSite.Manual, "Marketing actions", null, "POST, PATCH, DELETE /api/actions")],
            CrudOnly, ["DecisionSource defaults to Manual and Status to Planned.", .. NoAutomaticSource]),

        new("game_action", DataCollection.Manual,
            "Links a marketing action to its games.",
            [new(DataSite.Manual, "Action games", null, "POST /api/actions, POST and DELETE /api/actions/{id}/games")],
            ["A game is linked to an action at most once."],
            ["GameRole defaults to Primary.", .. NoAutomaticSource]),

        new("action_target", DataCollection.Manual,
            "Links a marketing action to its targets.",
            [new(DataSite.Manual, "Action targets", null, "POST /api/actions, POST, PATCH and DELETE /api/actions/{id}/targets")],
            CrudOnly, ["Status defaults to Planned.", .. NoAutomaticSource]),

        new("target_match", DataCollection.Manual,
            "Fit score of a marketing target for a game.",
            [new(DataSite.Manual, "Target match scores", null, "PUT /api/target-matches")],
            ["Scores 0-100, sent by the client; CalculatedAt is set by the server."],
            NoAutomaticSource),

        new("action_impact", DataCollection.Manual,
            "Measured result of a marketing action.",
            [new(DataSite.Manual, "Action impact", null, "PUT /api/actions/{id}/impact")],
            ["No validator. Every metric, ROI included, is sent by the client; nothing is computed."],
            NoAutomaticSource),

        new("game_news", DataCollection.Automatic,
            "Steam news posts and patch notes.",
            [new(DataSite.SteamWebApi, "ISteamNews/GetNewsForApp v2 (no key), newest News:Count items",
                 "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}",
                 "Backend GameNewsWatcher, every 6 h, Owned games", BackendJobNames.GameNewsWatcher, Backend),
             new(DataSite.SteamWebApi, "ISteamNews/GetNewsForApp JSON file", "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}", "POST /api/ingestion/news (manual upload)"),
             new(DataSite.SteamCommunity, "Game RSS feed file", "https://steamcommunity.com/games/{appId}/rss", "POST /api/ingestion/patch-notes (manual upload)")],
            ["Upsert by (GameId, ExternalId); an empty id is counted as failed; the game must exist.",
             "The watcher uses the same mapping as the manual upload (Source = news)."],
            ["A failed watcher call leaves the rows as they are. A missing date becomes the upload time. A patch-note date that does not parse is stored as 0001-01-01."],
            new Dictionary<string, DbColumnDoc>
            {
                ["Source"] = new("CATO: news (watcher and news upload) or patch_notes (RSS upload)"),
            }),

        new("active_users_history", DataCollection.Automatic,
            "Daily active users and daily peak concurrent users of Owned games.",
            [SteamworksPlayers,
             new(DataSite.Gamalytic, "Active users history file", null, "POST /api/ingestion/active-users (manual upload)")],
            [
                "Upsert by (GameId, RecordedAt); RecordedAt is the day at 00:00 UTC.",
                "Collector: a day needs a date, DAU >= 0 and PCU >= 0, else it counts as failed.",
                "Upload: a row without timestamp is counted as failed.",
            ],
            [
                "The collector re-sends the whole history every day; days missing from a later export keep their rows.",
                "Upload: missing DAU or MAU becomes 0. The collector never writes Mau (stays 0).",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["PeakConcurrentUsers"] = new("Steamworks players page: daily peak concurrent users"),
                ["Mau"] = new("Gamalytic upload only", null, "0 for collector rows"),
            }),

        new("demo_download", DataCollection.Automatic,
            "Demo downloads by region and country, per period, filed under the main game.",
            [DemoDownloads,
             new(DataSite.Steamworks, "Demo downloads by region CSV file", null, "POST /api/ingestion/demo-downloads (manual upload, filed under the upload day)")],
            [
                "Upsert by period and (geo type, name); geo type is Region or Country.",
                "Rows are Region until the separator row \"Country,0\", then Country.",
                "An invalid period counts all rows as failed.",
            ],
            [
                "Daily one-day periods, history as whole months, never overlapping. Rows missing from a later export are kept.",
                "A login or rate-limit error stops the run; a download error skips that demo.",
                "A share that does not parse becomes null; DemoAppId is kept when not sent.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["SnapshotDate"] = new("Collector: period start (GMT). Upload: upload day (UTC)"),
                ["PeriodEnd"] = new("Collector: period end (GMT, inclusive). Upload: same as SnapshotDate"),
            }),

        new("review_summary_snapshot", DataCollection.Automatic,
            "Review score summary per game, one row per watcher run.",
            [StoreReviews],
            ["Append only: every run adds a row, also on the same day."],
            ["No row when the API returns nothing or no reviews on the first page."],
            new Dictionary<string, DbColumnDoc>
            {
                ["ReviewScoreDesc"] = new("App reviews: query_summary.review_score_desc", null, "Empty text when missing"),
            }),

        new("steam_review", DataCollection.Automatic,
            "Steam user reviews of Sourcing and Owned games.",
            [StoreReviews, PlayerAchievements],
            ["Paging stops at the newest review already stored; known ids are skipped.", "At most 2000 reviews per game per run."],
            [
                "HTTP errors are retried 3 times; then the game counts as failed and the job reads PartialSuccess.",
                "Language and text become empty text when missing.",
            ],
            new Dictionary<string, DbColumnDoc>
            {
                ["AuthorAchievementsAtReview"] = new("CATO: computed by PlayerAchievementWatcher from steam_player_achievement"),
                ["GameAchievementCountAtFetch"] = new("CATO: computed by PlayerAchievementWatcher from game_achievement_schema"),
                ["AchievementsComputedAt"] = new("CATO: set by PlayerAchievementWatcher"),
            }),

        new("steam_player_profile", DataCollection.Automatic,
            "Public Steam profiles of review authors.",
            [PlayerSummaries],
            ["Upsert by SteamId64: only the latest profile is kept."],
            [
                "No Steam Web API key: the watcher does nothing.",
                "A failed call skips the chunk; an id missing from the answer adds a Redis failure and is quarantined after 5.",
            ]),

        new("game_achievement_schema", DataCollection.Automatic,
            "Achievement definitions of Sourcing and Owned games.",
            [AchievementSchema],
            ["Upsert by (GameId, ApiName); an entry with an empty name is skipped."],
            ["A failed call leaves the rows as they are. Achievements that Steam removes are never deleted."]),

        new("game_achievement_percent", DataCollection.Automatic,
            "Global unlock percentage of each achievement, one snapshot per day, Owned games.",
            [AchievementPercents],
            [
                "Upsert by (GameId, ApiName, SnapshotDate); a value changes in place within the day.",
                "An entry needs a name of 1-200 characters and a percent from 0 to 100; it is rounded to 3 decimals.",
            ],
            [
                "HTTP 403 or 404 means the app has no stats: nothing is written.",
                "A failed call leaves the day without rows; the next run (6 h) tries again.",
                "Sourcing games are left out to keep the table small.",
            ]),

        new("steam_player_achievement", DataCollection.Automatic,
            "Achievements each review author unlocked.",
            [PlayerAchievements],
            ["Only unlocked achievements with a schema row are kept.", "Rows no longer unlocked are deleted."],
            ["See steam_player_achievement_fetch for retry and quarantine."]),

        new("steam_player_achievement_fetch", DataCollection.Automatic,
            "Fetch state per player and game for the achievement watcher.",
            [PlayerAchievements],
            ["Status is ok, private, no_stats, not_owned or error."],
            [
                "A failed call (no answer) changes nothing.",
                "private, no_stats or not_owned: wait 168 h. Other errors: wait 60 min, 168 h after 5 failures.",
                "A successful fetch is refreshed after 30 days.",
            ]),
    ];

    private static readonly Dictionary<string, DbTableDoc> ByTable =
        Tables.ToDictionary(t => t.Table, StringComparer.Ordinal);

    public static DbTableDoc? Find(string table) => ByTable.GetValueOrDefault(table);
}
