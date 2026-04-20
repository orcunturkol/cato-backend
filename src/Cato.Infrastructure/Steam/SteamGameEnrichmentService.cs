using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Cato.Infrastructure.Steam;

public class SteamGameEnrichmentService : ISteamGameEnrichmentService
{
    private readonly CatoDbContext _db;
    private readonly ISteamApiService _steam;
    private readonly IRedisAppIdSyncService _redisSync;
    private readonly ILogger<SteamGameEnrichmentService> _logger;

    public SteamGameEnrichmentService(
        CatoDbContext db,
        ISteamApiService steam,
        IRedisAppIdSyncService redisSync,
        ILogger<SteamGameEnrichmentService> logger)
    {
        _db = db;
        _steam = steam;
        _redisSync = redisSync;
        _logger = logger;
    }

    public async Task<bool> EnrichGameAsync(Guid gameId, CancellationToken ct = default)
    {
        // 1. Load game with related data
        var game = await _db.Games
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);

        if (game is null)
        {
            _logger.LogWarning("Enrichment failed: Game with Id {GameId} not found", gameId);
            return false;
        }

        // 2. Call Steam API
        var steamData = await _steam.GetAppDetailsAsync(game.AppId, ct);
        if (steamData is null)
        {
            _logger.LogWarning("Enrichment failed: Could not fetch Steam data for AppId {AppId}", game.AppId);
            return false;
        }

        // 2b. Fetch user-defined tags from store page
        var userTags = await _steam.GetUserTagsAsync(game.AppId, ct);

        // 3. Map core fields
        if (!string.IsNullOrWhiteSpace(steamData.Name))
            game.Name = steamData.Name;

        game.ShortDescription = steamData.ShortDescription;
        game.DetailedDescription = steamData.DetailedDescription;
        game.HeaderImageUrl = steamData.HeaderImage;
        game.CapsuleImageUrl = steamData.CapsuleImage;
        game.Website = steamData.Website;
        game.SupportedLanguages = steamData.SupportedLanguages;

        if (steamData.PriceOverview is not null)
        {
            game.PriceUsd = steamData.PriceOverview.InitialUsd;
            game.DiscountPercent = steamData.PriceOverview.DiscountPercent ?? 0;
        }

        if (steamData.Platforms is not null)
        {
            game.Platforms = JsonSerializer.SerializeToDocument(new
            {
                windows = steamData.Platforms.Windows,
                mac = steamData.Platforms.Mac,
                linux = steamData.Platforms.Linux
            });
        }

        if (steamData.ReleaseDate is not null)
        {
            game.IsReleased = !(steamData.ReleaseDate.ComingSoon ?? true);
            if (!string.IsNullOrWhiteSpace(steamData.ReleaseDate.Date))
            {
                if (DateOnly.TryParseExact(steamData.ReleaseDate.Date, "MMM d, yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                    game.ReleaseDate = d1;
                else if (DateOnly.TryParseExact(steamData.ReleaseDate.Date, "d MMM, yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                    game.ReleaseDate = d2;
                else if (DateOnly.TryParseExact(steamData.ReleaseDate.Date, "MMM yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d3))
                    game.ReleaseDate = d3;
            }
        }

        if (steamData.Genres?.Any(g =>
                g.Description?.Equals("Early Access", StringComparison.OrdinalIgnoreCase) == true) == true)
            game.IsEarlyAccess = true;

        if (steamData.Metacritic?.Score is not null)
            game.MetacriticScore = steamData.Metacritic.Score;

        game.ContentDescriptorIds = steamData.ContentDescriptors?.Ids is { Count: > 0 } ids
            ? JsonSerializer.SerializeToDocument(ids)
            : null;

        // 4. Upsert Developer
        var devName = steamData.Developers?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(devName))
        {
            var developer = await _db.LegalEntities
                .FirstOrDefaultAsync(e => e.Name == devName && e.EntityType == "Developer", ct);
            if (developer is null)
            {
                developer = new LegalEntity { Id = Guid.NewGuid(), Name = devName, EntityType = "Developer" };
                _db.LegalEntities.Add(developer);
                await _db.SaveChangesAsync(ct);
            }
            game.DeveloperId = developer.Id;
        }

        // 5. Upsert Publisher
        var pubName = steamData.Publishers?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(pubName))
        {
            var publisher = await _db.LegalEntities
                .FirstOrDefaultAsync(e => e.Name == pubName && e.EntityType == "Publisher", ct);
            if (publisher is null)
            {
                publisher = new LegalEntity { Id = Guid.NewGuid(), Name = pubName, EntityType = "Publisher" };
                _db.LegalEntities.Add(publisher);
                await _db.SaveChangesAsync(ct);
            }
            game.PublisherId = publisher.Id;
        }

        // 6. Replace genres
        if (game.Genres.Count > 0)
        {
            _db.GameGenres.RemoveRange(game.Genres);
            await _db.SaveChangesAsync(ct);
        }

        if (steamData.Genres is not null)
        {
            var isFirst = true;
            foreach (var genre in steamData.Genres)
            {
                if (string.IsNullOrWhiteSpace(genre.Description)) continue;
                _db.GameGenres.Add(new GameGenre
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    GenreName = genre.Description,
                    GenreType = isFirst ? "Primary" : "Secondary",
                    Source = "Steam"
                });
                isFirst = false;
            }
        }

        // 7. Replace tags
        if (game.Tags.Count > 0)
        {
            _db.GenreTags.RemoveRange(game.Tags);
            await _db.SaveChangesAsync(ct);
        }

        {
            var addedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (steamData.Categories is not null)
            {
                foreach (var cat in steamData.Categories)
                {
                    if (string.IsNullOrWhiteSpace(cat.Description)) continue;
                    if (!addedTags.Add(cat.Description)) continue;

                    _db.GenreTags.Add(new GenreTag
                    {
                        Id = Guid.NewGuid(),
                        GameId = game.Id,
                        TagName = cat.Description,
                        TagType = "Mechanic",
                        Weight = 0,
                        Source = "Steam"
                    });
                }
            }

            foreach (var tag in userTags)
            {
                if (string.IsNullOrWhiteSpace(tag.Name)) continue;
                if (!addedTags.Add(tag.Name)) continue;

                _db.GenreTags.Add(new GenreTag
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    TagName = tag.Name,
                    TagType = "UserTag",
                    Weight = tag.Rank,
                    Source = "Steam"
                });
            }
        }

        // 8. Final save
        await _db.SaveChangesAsync(ct);

        // 9. Sync AppId + name to Redis so orchestrators can pick it up
        await _redisSync.SyncAsync(game.AppId, game.GameType, game.Name, ct);

        _logger.LogInformation("Enriched game AppId={AppId} Name='{Name}'", game.AppId, game.Name);
        return true;
    }
}
