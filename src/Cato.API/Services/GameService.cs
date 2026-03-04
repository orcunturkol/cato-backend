using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace Cato.API.Services;

public class GameService : IGameService
{
    private readonly CatoDbContext _db;
    private readonly ISteamApiService _steam;

    public GameService(CatoDbContext db, ISteamApiService steam)
    {
        _db = db;
        _steam = steam;
    }

    public async Task<Result<GameDto>> CreateGameAsync(CreateGameCommand request, CancellationToken ct = default)
    {
        // Check uniqueness
        var exists = await _db.Games.AnyAsync(g => g.AppId == request.AppId, ct);
        if (exists)
            return Result<GameDto>.Failure($"A game with AppId {request.AppId} already exists.");

        // Upsert developer
        LegalEntity? developer = null;
        if (!string.IsNullOrWhiteSpace(request.DeveloperName))
        {
            developer = await _db.LegalEntities
                .FirstOrDefaultAsync(e => e.Name == request.DeveloperName && e.EntityType == "Developer", ct);
            if (developer is null)
            {
                developer = new LegalEntity { Id = Guid.NewGuid(), Name = request.DeveloperName, EntityType = "Developer" };
                _db.LegalEntities.Add(developer);
            }
        }

        // Upsert publisher
        LegalEntity? publisher = null;
        if (!string.IsNullOrWhiteSpace(request.PublisherName))
        {
            publisher = await _db.LegalEntities
                .FirstOrDefaultAsync(e => e.Name == request.PublisherName && e.EntityType == "Publisher", ct);
            if (publisher is null)
            {
                publisher = new LegalEntity { Id = Guid.NewGuid(), Name = request.PublisherName, EntityType = "Publisher" };
                _db.LegalEntities.Add(publisher);
            }
        }

        var game = new Game
        {
            Id = Guid.NewGuid(),
            AppId = request.AppId,
            Name = request.Name ?? $"App {request.AppId}",
            GameType = request.GameType ?? "Owned",
            DeveloperId = developer?.Id,
            PublisherId = publisher?.Id
        };

        _db.Games.Add(game);
        await _db.SaveChangesAsync(ct);

        // Reload with navigation properties
        game = await _db.Games
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstAsync(g => g.Id == game.Id, ct);

        return Result<GameDto>.Success(game.ToDto());
    }

    public async Task<PagedResult<GameDto>> ListGamesAsync(ListGamesQuery request, CancellationToken ct = default)
    {
        var query = _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.GameType))
            query = query.Where(g => g.GameType == request.GameType);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(g =>
                EF.Functions.ILike(g.Name, $"%{request.Search}%") ||
                g.Genres.Any(genre => EF.Functions.ILike(genre.GenreName, $"%{request.Search}%")) ||
                g.Tags.Any(tag => EF.Functions.ILike(tag.TagName, $"%{request.Search}%")));

        var totalCount = await query.CountAsync(ct);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderBy(g => g.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<GameDto>
        {
            Items = items.Select(g => g.ToDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<Result<GameDto>> GetGameDetailsAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        return game is null
            ? Result<GameDto>.Failure($"Game with Id {id} not found.")
            : Result<GameDto>.Success(game.ToDto());
    }

    public async Task<Result<GameDto>> UpdateGameAsync(UpdateGameCommand request, CancellationToken ct = default)
    {
        var game = await _db.Games
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == request.Id, ct);

        if (game is null)
            return Result<GameDto>.Failure($"Game with Id {request.Id} not found.");

        // Partial update — only set fields that were provided
        if (request.Name is not null) game.Name = request.Name;
        if (request.GameType is not null) game.GameType = request.GameType;
        if (request.PriceUsd.HasValue) game.PriceUsd = request.PriceUsd.Value;
        if (request.IsEarlyAccess.HasValue) game.IsEarlyAccess = request.IsEarlyAccess.Value;
        if (request.IsReleased.HasValue) game.IsReleased = request.IsReleased.Value;
        if (request.ShortDescription is not null) game.ShortDescription = request.ShortDescription;
        if (request.Website is not null) game.Website = request.Website;
        if (request.SteamReviewScore is not null) game.SteamReviewScore = request.SteamReviewScore;
        if (request.ReviewCount.HasValue) game.ReviewCount = request.ReviewCount.Value;
        if (request.FollowersCount.HasValue) game.FollowersCount = request.FollowersCount.Value;

        await _db.SaveChangesAsync(ct);

        return Result<GameDto>.Success(game.ToDto());
    }

    public async Task<Result<bool>> DeleteGameAsync(Guid id, CancellationToken ct = default)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (game is null)
            return Result<bool>.Failure($"Game with Id {id} not found.");

        _db.Games.Remove(game); // Cascade deletes genres & tags
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    public async Task<Result<GameDto>> EnrichGameFromSteamAsync(Guid id, CancellationToken ct = default)
    {
        // 1. Load game with related data
        var game = await _db.Games
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (game is null)
            return Result<GameDto>.Failure($"Game with Id {id} not found.");

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

        if (steamData.PriceOverview is not null)
        {
            game.PriceUsd = steamData.PriceOverview.FinalUsd;
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

        if (steamData.Categories is not null)
        {
            var addedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        // 8. Final save
        await _db.SaveChangesAsync(ct);

        // 9. Reload with navigation properties
        var enrichedGame = await _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstAsync(g => g.Id == id, ct);

        return Result<GameDto>.Success(enrichedGame.ToDto());
    }
}
