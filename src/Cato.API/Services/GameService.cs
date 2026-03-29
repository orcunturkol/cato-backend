using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services;

public class GameService : IGameService
{
    private readonly CatoDbContext _db;
    private readonly ISteamApiService _steam;
    private readonly ISteamGameEnrichmentService _enrichment;

    public GameService(CatoDbContext db, ISteamApiService steam, ISteamGameEnrichmentService enrichment)
    {
        _db = db;
        _steam = steam;
        _enrichment = enrichment;
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
        var success = await _enrichment.EnrichGameAsync(id, ct);
        if (!success)
            return Result<GameDto>.Failure($"Enrichment failed for game {id}.");

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
