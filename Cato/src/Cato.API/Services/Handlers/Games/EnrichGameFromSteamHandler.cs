using System.Globalization;
using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class EnrichGameFromSteamHandler : IRequestHandler<EnrichGameFromSteamCommand, Result<GameDto>>
{
    private readonly CatoDbContext _db;
    private readonly ISteamApiService _steam;

    public EnrichGameFromSteamHandler(CatoDbContext db, ISteamApiService steam)
    {
        _db = db;
        _steam = steam;
    }

    public async Task<Result<GameDto>> Handle(EnrichGameFromSteamCommand request, CancellationToken ct)
    {
        // 1. Load game with related data
        var game = await _db.Games
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == request.Id, ct);

        if (game is null)
            return Result<GameDto>.Failure($"Game with Id {request.Id} not found.");

        // 2. Call Steam API
        var steamData = await _steam.GetAppDetailsAsync(game.AppId, ct);
        if (steamData is null)
            return Result<GameDto>.Failure($"Could not fetch Steam data for AppId {game.AppId}.");

        // 3. Map core fields
        if (!string.IsNullOrWhiteSpace(steamData.Name))
            game.Name = steamData.Name;

        game.ShortDescription = steamData.ShortDescription;
        game.DetailedDescription = steamData.DetailedDescription;
        game.HeaderImageUrl = steamData.HeaderImage;
        game.CapsuleImageUrl = steamData.CapsuleImage;
        game.Website = steamData.Website;
        game.SupportedLanguages = steamData.SupportedLanguages;

        // Price
        if (steamData.PriceOverview is not null)
        {
            game.PriceUsd = steamData.PriceOverview.FinalUsd;
            game.DiscountPercent = steamData.PriceOverview.DiscountPercent ?? 0;
        }

        // Platforms -> JSONB
        if (steamData.Platforms is not null)
        {
            game.Platforms = JsonSerializer.SerializeToDocument(new
            {
                windows = steamData.Platforms.Windows,
                mac = steamData.Platforms.Mac,
                linux = steamData.Platforms.Linux
            });
        }

        // Release date
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

        // Early Access detection
        if (steamData.Genres?.Any(g =>
                g.Description?.Equals("Early Access", StringComparison.OrdinalIgnoreCase) == true) == true)
            game.IsEarlyAccess = true;

        // Metacritic
        if (steamData.Metacritic?.Score is not null)
            game.MetacriticScore = steamData.Metacritic.Score;

        // 4. Upsert Developer — save separately to avoid FK tracking issues
        var devName = steamData.Developers?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(devName))
        {
            var developer = await _db.LegalEntities
                .FirstOrDefaultAsync(e => e.Name == devName && e.EntityType == "Developer", ct);
            if (developer is null)
            {
                developer = new LegalEntity
                {
                    Id = Guid.NewGuid(),
                    Name = devName,
                    EntityType = "Developer"
                };
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
                publisher = new LegalEntity
                {
                    Id = Guid.NewGuid(),
                    Name = pubName,
                    EntityType = "Publisher"
                };
                _db.LegalEntities.Add(publisher);
                await _db.SaveChangesAsync(ct);
            }
            game.PublisherId = publisher.Id;
        }

        // 6. Replace genres — remove old, add new
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

        // 7. Replace tags from Steam categories
        if (game.Tags.Count > 0)
        {
            _db.GenreTags.RemoveRange(game.Tags);
            await _db.SaveChangesAsync(ct);
        }

        if (steamData.Categories is not null)
        {
            var addedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in steamData.Categories)
            {
                if (string.IsNullOrWhiteSpace(cat.Description)) continue;
                if (!addedTags.Add(cat.Description)) continue; // skip duplicates

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

        // 8. Final save (game updates + new genres + new tags)
        await _db.SaveChangesAsync(ct);

        // 9. Reload with navigation properties for the response
        var enrichedGame = await _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstAsync(g => g.Id == request.Id, ct);

        return Result<GameDto>.Success(enrichedGame.ToDto());
    }
}
