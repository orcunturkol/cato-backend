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
    }
}
