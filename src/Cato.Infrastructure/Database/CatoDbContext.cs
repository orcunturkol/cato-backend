using Cato.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Database;

public class CatoDbContext : DbContext
{
    public CatoDbContext(DbContextOptions<CatoDbContext> options) : base(options) { }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<GameGenre> GameGenres => Set<GameGenre>();
    public DbSet<GenreTag> GenreTags => Set<GenreTag>();
    public DbSet<SteamSaleFinancial> SteamSaleFinancials => Set<SteamSaleFinancial>();
    public DbSet<SteamTraffic> SteamTraffic => Set<SteamTraffic>();
    public DbSet<CcuHistory> CcuHistories => Set<CcuHistory>();
    public DbSet<OwnedGameData> OwnedGameData => Set<OwnedGameData>();
    public DbSet<IngestionLog> IngestionLogs => Set<IngestionLog>();
    public DbSet<GroupMemberCountSnapshot> GroupMemberCountSnapshots => Set<GroupMemberCountSnapshot>();
    public DbSet<SteamDbSnapshot> SteamDbSnapshots => Set<SteamDbSnapshot>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<AppKeyValueSnapshot> AppKeyValueSnapshots => Set<AppKeyValueSnapshot>();
    public DbSet<AppChangeRecord> AppChangeRecords => Set<AppChangeRecord>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<MarketingTarget> MarketingTargets => Set<MarketingTarget>();
    public DbSet<MarketingAction> MarketingActions => Set<MarketingAction>();
    public DbSet<GameAction> GameActions => Set<GameAction>();
    public DbSet<ActionTarget> ActionTargets => Set<ActionTarget>();
    public DbSet<TargetMatch> TargetMatches => Set<TargetMatch>();
    public DbSet<ActionImpact> ActionImpacts => Set<ActionImpact>();
    public DbSet<WishlistInsight> WishlistInsights => Set<WishlistInsight>();
    public DbSet<SteamTrafficBreakdown> SteamTrafficBreakdowns => Set<SteamTrafficBreakdown>();
    public DbSet<GameNews> GameNews => Set<GameNews>();
    public DbSet<ActiveUsersHistory> ActiveUsersHistories => Set<ActiveUsersHistory>();
    public DbSet<DemoDownload> DemoDownloads => Set<DemoDownload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── LegalEntity ──
        modelBuilder.Entity<LegalEntity>(entity =>
        {
            entity.ToTable("legal_entity");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContactEmail).HasMaxLength(255);

            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.Name);
        });

        // ── Game (main_game) ──
        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("main_game");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.AppId).IsRequired();
            entity.HasIndex(e => e.AppId).IsUnique();

            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.GameType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PriceUsd).HasColumnType("decimal(10,2)");
            entity.Property(e => e.HeaderImageUrl).HasColumnType("text");
            entity.Property(e => e.CapsuleImageUrl).HasColumnType("text");
            entity.Property(e => e.ShortDescription).HasColumnType("text");
            entity.Property(e => e.DetailedDescription).HasColumnType("text");
            entity.Property(e => e.Website).HasColumnType("text");
            entity.Property(e => e.SupportedLanguages).HasColumnType("text");
            entity.Property(e => e.SteamReviewScore).HasMaxLength(50);
            entity.Property(e => e.Platforms).HasColumnType("jsonb");

            entity.HasIndex(e => e.GameType);
            entity.HasIndex(e => e.ReleaseDate);

            entity.HasOne(e => e.Developer)
                .WithMany(le => le.DeveloperGames)
                .HasForeignKey(e => e.DeveloperId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Publisher)
                .WithMany(le => le.PublisherGames)
                .HasForeignKey(e => e.PublisherId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── GameGenre ──
        modelBuilder.Entity<GameGenre>(entity =>
        {
            entity.ToTable("game_genre");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.GenreName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.GenreType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.GenreName);
            entity.HasIndex(e => new { e.GameId, e.GenreName, e.Source }).IsUnique();

            entity.HasOne(e => e.Game)
                .WithMany(g => g.Genres)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── GenreTag ──
        modelBuilder.Entity<GenreTag>(entity =>
        {
            entity.ToTable("genre_tag");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TagName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TagType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(50);

            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => e.TagName);
            entity.HasIndex(e => e.TagType);
            entity.HasIndex(e => new { e.GameId, e.TagName }).IsUnique();

            entity.HasOne(e => e.Game)
                .WithMany(g => g.Tags)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SteamSaleFinancial ──
        modelBuilder.Entity<SteamSaleFinancial>(entity =>
        {
            entity.ToTable("steam_sale_financial");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CountryCode).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Platform).HasMaxLength(50).HasDefaultValue("Steam");
            entity.Property(e => e.GrossRevenueUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.GrossReturnsUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.SteamCommissionUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.TaxUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.NetRevenueUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.Property(e => e.BasePrice).HasMaxLength(20);
            entity.Property(e => e.SalePrice).HasMaxLength(20);
            entity.Property(e => e.SaleType).HasMaxLength(50);
            entity.Property(e => e.NetUnits)
                .HasComputedColumnSql("\"SalesUnits\" - \"ReturnsUnits\"", stored: true);

            entity.HasIndex(e => new { e.GameId, e.SaleDate }).HasDatabaseName("idx_steam_sale_game_date");
            entity.HasIndex(e => e.CountryCode).HasDatabaseName("idx_steam_sale_country");
            entity.HasIndex(e => e.SaleDate).HasDatabaseName("idx_steam_sale_date");
            entity.HasIndex(e => new { e.GameId, e.SaleDate, e.CountryCode, e.PackageId, e.Platform })
                .IsUnique().HasDatabaseName("unique_sale_record");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.SalesFinancials)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SteamTraffic ──
        modelBuilder.Entity<SteamTraffic>(entity =>
        {
            entity.ToTable("steam_traffic");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ClickThroughRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.PurchaseConversionRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TrafficSource).HasMaxLength(100);
            entity.Property(e => e.NetWishlistChange)
                .HasComputedColumnSql("\"WishlistAdditions\" - \"WishlistDeletions\"", stored: true);

            entity.HasIndex(e => new { e.GameId, e.TrafficDate }).HasDatabaseName("idx_steam_traffic_game_date");
            entity.HasIndex(e => e.TrafficSource).HasDatabaseName("idx_steam_traffic_source");
            entity.HasIndex(e => new { e.GameId, e.TrafficDate, e.TrafficSource })
                .IsUnique().HasDatabaseName("unique_traffic_record");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.TrafficRecords)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CcuHistory ──
        modelBuilder.Entity<CcuHistory>(entity =>
        {
            entity.ToTable("ccu_history");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Source).HasMaxLength(50).HasDefaultValue("Steam API");

            entity.HasIndex(e => new { e.GameId, e.Timestamp }).HasDatabaseName("idx_ccu_history_game_time");
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_ccu_history_timestamp");
            entity.HasIndex(e => new { e.GameId, e.Timestamp, e.Source })
                .IsUnique().HasDatabaseName("idx_ccu_history_unique");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.CcuHistories)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── OwnedGameData ──
        modelBuilder.Entity<OwnedGameData>(entity =>
        {
            entity.ToTable("owned_game_data");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.GameId, e.SnapshotDate }).IsUnique();

            entity.HasOne(e => e.Game)
                .WithMany(g => g.OwnedGameData)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── GroupMemberCountSnapshot ──
        modelBuilder.Entity<GroupMemberCountSnapshot>(entity =>
        {
            entity.ToTable("group_member_count_snapshot");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Error).HasColumnType("text");

            entity.HasIndex(e => new { e.GameId, e.SnapshotDate })
                .IsUnique()
                .HasDatabaseName("unique_group_member_count_snapshot");
            entity.HasIndex(e => e.SnapshotDate).HasDatabaseName("idx_group_member_count_date");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.GroupMemberCountSnapshots)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SteamDbSnapshot ──
        modelBuilder.Entity<SteamDbSnapshot>(entity =>
        {
            entity.ToTable("steamdb_snapshot");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Source).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Price).HasMaxLength(50);
            entity.Property(e => e.Rating).HasMaxLength(50);
            entity.Property(e => e.Release).HasMaxLength(100);

            entity.HasIndex(e => new { e.GameId, e.SnapshotDate, e.Source })
                .IsUnique()
                .HasDatabaseName("unique_steamdb_snapshot");
            entity.HasIndex(e => e.SnapshotDate).HasDatabaseName("idx_steamdb_snapshot_date");
            entity.HasIndex(e => e.Source).HasDatabaseName("idx_steamdb_snapshot_source");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.SteamDbSnapshots)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PriceSnapshot ──
        modelBuilder.Entity<PriceSnapshot>(entity =>
        {
            entity.ToTable("price_snapshot");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.BasePriceUsd).HasColumnType("decimal(10,2)");
            entity.Property(e => e.FinalPriceUsd).HasColumnType("decimal(10,2)");
            entity.Property(e => e.Currency).HasMaxLength(10);

            entity.HasIndex(e => new { e.GameId, e.CapturedAt, e.Currency })
                .IsUnique()
                .HasDatabaseName("unique_price_snapshot");
            entity.HasIndex(e => e.CapturedAt).HasDatabaseName("idx_price_snapshot_captured_at");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.PriceSnapshots)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WishlistInsight ──
        modelBuilder.Entity<WishlistInsight>(entity =>
        {
            entity.ToTable("wishlist_insight");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RelatedName).HasMaxLength(500);
            entity.Property(e => e.LinkScore).HasColumnType("decimal(5,4)");
            entity.Property(e => e.Price).HasColumnType("decimal(10,2)");
            entity.Property(e => e.Revenue).HasColumnType("decimal(14,2)");
            entity.Property(e => e.Genres).HasColumnType("jsonb");

            entity.HasIndex(e => new { e.GameId, e.SnapshotDate, e.RelatedAppId })
                .IsUnique()
                .HasDatabaseName("unique_wishlist_insight");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.WishlistInsights)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SteamTrafficBreakdown ──
        modelBuilder.Entity<SteamTrafficBreakdown>(entity =>
        {
            entity.ToTable("steam_traffic_breakdown");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.PageCategory).HasMaxLength(200);
            entity.Property(e => e.PageFeature).HasMaxLength(300);

            entity.HasIndex(e => new { e.GameId, e.SnapshotDate, e.PageCategory, e.PageFeature })
                .IsUnique()
                .HasDatabaseName("unique_traffic_breakdown");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.TrafficBreakdowns)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AppKeyValueSnapshot ──
        modelBuilder.Entity<AppKeyValueSnapshot>(entity =>
        {
            entity.ToTable("app_kv_snapshot");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RawKeyValuesJson).HasColumnType("jsonb").IsRequired();

            entity.HasIndex(e => e.AppId).HasDatabaseName("idx_app_kv_snapshot_appid");
            entity.HasIndex(e => new { e.AppId, e.ChangeNumber })
                .IsUnique()
                .HasDatabaseName("unique_app_kv_snapshot");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.AppKeyValueSnapshots)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── AppChangeRecord ──
        modelBuilder.Entity<AppChangeRecord>(entity =>
        {
            entity.ToTable("app_change_record");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Section).HasMaxLength(50).IsRequired();
            entity.Property(e => e.KeyPath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OldValue).HasColumnType("text");
            entity.Property(e => e.NewValue).HasColumnType("text");

            entity.HasIndex(e => new { e.AppId, e.ChangeNumber })
                .HasDatabaseName("idx_app_change_record_app_change");
            entity.HasIndex(e => new { e.AppId, e.DetectedAt })
                .IsDescending(false, true)
                .HasDatabaseName("idx_app_change_record_app_detected");
            entity.HasIndex(e => new { e.GameId, e.DetectedAt })
                .IsDescending(false, true)
                .HasDatabaseName("idx_app_change_record_game_detected");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.AppChangeRecords)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── IngestionLog ──
        modelBuilder.Entity<IngestionLog>(entity =>
        {
            entity.ToTable("ingestion_log");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Source).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.Source).HasDatabaseName("idx_ingestion_log_source");
            entity.HasIndex(e => e.StartTime).HasDatabaseName("idx_ingestion_log_start_time");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_ingestion_log_status");
        });

        // ── User ──
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // ── UserProfile ──
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profile");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Bio).HasColumnType("text");
            entity.Property(e => e.AvatarUrl).HasColumnType("text");
            entity.HasOne(e => e.User)
                .WithMany(u => u.Profiles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── MarketingTarget ──
        modelBuilder.Entity<MarketingTarget>(entity =>
        {
            entity.ToTable("marketing_target");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.TargetType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContactEmail).HasMaxLength(255);
            entity.Property(e => e.ContactTwitter).HasMaxLength(255);
            entity.Property(e => e.ContactDiscord).HasMaxLength(255);
            entity.Property(e => e.PreferredGenres).HasColumnType("jsonb");
            entity.Property(e => e.PreferredTags).HasColumnType("jsonb");
            entity.Property(e => e.AudienceRegion).HasMaxLength(100);
            entity.Property(e => e.Platform).HasMaxLength(100);
            entity.Property(e => e.EngagementRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.CostEstimateUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.ResponseRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.Notes).HasColumnType("text");

            entity.HasIndex(e => e.TargetType).HasDatabaseName("idx_marketing_target_type");
            entity.HasIndex(e => e.Name).HasDatabaseName("idx_marketing_target_name");
            entity.HasIndex(e => e.Platform).HasDatabaseName("idx_marketing_target_platform");
        });

        // ── MarketingAction (table: action) ──
        modelBuilder.Entity<MarketingAction>(entity =>
        {
            entity.ToTable("action");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ActionType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DecisionSource).HasMaxLength(50).HasDefaultValue("Manual");
            entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Planned");
            entity.Property(e => e.Description).HasColumnType("text").IsRequired();
            entity.Property(e => e.BudgetUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.ActualCostUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.Notes).HasColumnType("text");
            entity.Property(e => e.CreatedBy).HasMaxLength(255);

            entity.HasIndex(e => e.ActionType).HasDatabaseName("idx_action_type");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_action_status");
            entity.HasIndex(e => e.PlannedDate).HasDatabaseName("idx_action_planned_date");
            entity.HasIndex(e => e.ActionDate).HasDatabaseName("idx_action_date");
        });

        // ── GameAction ──
        modelBuilder.Entity<GameAction>(entity =>
        {
            entity.ToTable("game_action");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.GameRole).HasMaxLength(50).HasDefaultValue("Primary");
            entity.Property(e => e.Notes).HasColumnType("text");

            entity.HasIndex(e => e.ActionId).HasDatabaseName("idx_game_action_action");
            entity.HasIndex(e => e.GameId).HasDatabaseName("idx_game_action_game");
            entity.HasIndex(e => new { e.ActionId, e.GameId }).IsUnique().HasDatabaseName("unique_game_action");

            entity.HasOne(e => e.Action)
                .WithMany(a => a.GameActions)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Game)
                .WithMany(g => g.GameActions)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ActionTarget ──
        modelBuilder.Entity<ActionTarget>(entity =>
        {
            entity.ToTable("action_target");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Status).HasMaxLength(50).HasDefaultValue("Planned");
            entity.Property(e => e.DeliverableUrl).HasColumnType("text");
            entity.Property(e => e.CostUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.Notes).HasColumnType("text");

            entity.HasIndex(e => e.ActionId).HasDatabaseName("idx_action_target_action");
            entity.HasIndex(e => e.TargetId).HasDatabaseName("idx_action_target_target");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_action_target_status");
            entity.HasIndex(e => new { e.ActionId, e.TargetId }).IsUnique().HasDatabaseName("unique_action_target");

            entity.HasOne(e => e.Action)
                .WithMany(a => a.ActionTargets)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Target)
                .WithMany(t => t.ActionTargets)
                .HasForeignKey(e => e.TargetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TargetMatch ──
        modelBuilder.Entity<TargetMatch>(entity =>
        {
            entity.ToTable("target_match");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.LifecycleStage).HasMaxLength(50);
            entity.Property(e => e.RelevanceScore).HasColumnType("decimal(5,2)");
            entity.Property(e => e.GenreMatchScore).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TagMatchScore).HasColumnType("decimal(5,2)");
            entity.Property(e => e.HistoricalPerformanceScore).HasColumnType("decimal(5,2)");
            entity.Property(e => e.MatchingGenres).HasColumnType("jsonb");
            entity.Property(e => e.MatchingTags).HasColumnType("jsonb");

            entity.HasIndex(e => e.GameId).HasDatabaseName("idx_target_match_game");
            entity.HasIndex(e => e.TargetId).HasDatabaseName("idx_target_match_target");
            entity.HasIndex(e => e.RelevanceScore).HasDatabaseName("idx_target_match_score");
            entity.HasIndex(e => new { e.GameId, e.TargetId, e.LifecycleStage })
                .IsUnique().HasDatabaseName("unique_target_match");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.TargetMatches)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Target)
                .WithMany(t => t.TargetMatches)
                .HasForeignKey(e => e.TargetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── GameNews ──
        modelBuilder.Entity<GameNews>(entity =>
        {
            entity.ToTable("game_news");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ExternalId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Url).HasMaxLength(1000);
            entity.Property(e => e.Author).HasMaxLength(200);
            entity.Property(e => e.Contents).HasColumnType("text");
            entity.Property(e => e.FeedLabel).HasMaxLength(200);

            entity.HasIndex(e => new { e.GameId, e.ExternalId })
                .IsUnique()
                .HasDatabaseName("unique_game_news");
            entity.HasIndex(e => e.PublishedAt).HasDatabaseName("idx_game_news_published_at");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.News)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ActiveUsersHistory ──
        modelBuilder.Entity<ActiveUsersHistory>(entity =>
        {
            entity.ToTable("active_users_history");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.GameId, e.RecordedAt })
                .IsUnique()
                .HasDatabaseName("unique_active_users_history");
            entity.HasIndex(e => e.RecordedAt).HasDatabaseName("idx_active_users_history_recorded_at");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.ActiveUsersHistories)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DemoDownload ──
        modelBuilder.Entity<DemoDownload>(entity =>
        {
            entity.ToTable("demo_download");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.GeoType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.GeoName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SharePercent).HasColumnType("decimal(6,3)");

            entity.HasIndex(e => new { e.GameId, e.SnapshotDate, e.GeoType, e.GeoName })
                .IsUnique()
                .HasDatabaseName("unique_demo_download");
            entity.HasIndex(e => e.SnapshotDate).HasDatabaseName("idx_demo_download_snapshot_date");

            entity.HasOne(e => e.Game)
                .WithMany(g => g.DemoDownloads)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ActionImpact ──
        modelBuilder.Entity<ActionImpact>(entity =>
        {
            entity.ToTable("action_impact");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.WishlistChangePercent).HasColumnType("decimal(10,2)");
            entity.Property(e => e.SalesChangePercent).HasColumnType("decimal(10,2)");
            entity.Property(e => e.BaselineRevenueUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.ResultRevenueUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.RevenueChangeUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.RevenueChangePercent).HasColumnType("decimal(10,2)");
            entity.Property(e => e.TrafficChangePercent).HasColumnType("decimal(10,2)");
            entity.Property(e => e.BaselineConversionRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ResultConversionRate).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ConversionRateChange).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TotalCostUsd).HasColumnType("decimal(15,2)");
            entity.Property(e => e.Roi).HasColumnType("decimal(10,2)");
            entity.Property(e => e.Notes).HasColumnType("text");
            entity.Property(e => e.CalculatedBy).HasMaxLength(255);

            entity.HasIndex(e => e.ActionId).IsUnique().HasDatabaseName("unique_action_impact");
            entity.HasIndex(e => e.Roi).HasDatabaseName("idx_action_impact_roi");

            entity.HasOne(e => e.Action)
                .WithOne(a => a.Impact)
                .HasForeignKey<ActionImpact>(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges()
    {
        SetTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Game>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<LegalEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<GameGenre>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<GenreTag>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<SteamSaleFinancial>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SteamTraffic>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<CcuHistory>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<GroupMemberCountSnapshot>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<OwnedGameData>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SteamDbSnapshot>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<PriceSnapshot>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<WishlistInsight>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SteamTrafficBreakdown>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<GameNews>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ActiveUsersHistory>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<DemoDownload>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<AppKeyValueSnapshot>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<AppChangeRecord>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<IngestionLog>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<UserProfile>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<MarketingTarget>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<MarketingAction>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<GameAction>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<ActionTarget>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
