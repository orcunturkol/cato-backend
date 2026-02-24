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

        foreach (var entry in ChangeTracker.Entries<IngestionLog>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }
    }
}
