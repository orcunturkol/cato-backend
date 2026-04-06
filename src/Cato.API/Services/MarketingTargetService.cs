using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Actions;
using Cato.API.Models.MarketingTargets;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services;

public class MarketingTargetService : IMarketingTargetService
{
    private readonly CatoDbContext _db;

    public MarketingTargetService(CatoDbContext db)
    {
        _db = db;
    }

    public async Task<Result<MarketingTargetDto>> CreateAsync(CreateMarketingTargetCommand request, CancellationToken ct = default)
    {
        var target = new MarketingTarget
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            TargetType = request.TargetType,
            ContactEmail = request.ContactEmail,
            ContactTwitter = request.ContactTwitter,
            ContactDiscord = request.ContactDiscord,
            PreferredGenres = ParseJson(request.PreferredGenres),
            PreferredTags = ParseJson(request.PreferredTags),
            AudienceSize = request.AudienceSize,
            AudienceRegion = request.AudienceRegion,
            Platform = request.Platform,
            EngagementRate = request.EngagementRate,
            CostEstimateUsd = request.CostEstimateUsd,
            LastContacted = request.LastContacted,
            ResponseRate = request.ResponseRate,
            Notes = request.Notes
        };

        _db.MarketingTargets.Add(target);
        await _db.SaveChangesAsync(ct);

        return Result<MarketingTargetDto>.Success(target.ToDto());
    }

    public async Task<Result<MarketingTargetDto>> UpdateAsync(UpdateMarketingTargetCommand request, CancellationToken ct = default)
    {
        var target = await _db.MarketingTargets.FirstOrDefaultAsync(t => t.Id == request.Id, ct);
        if (target is null)
            return Result<MarketingTargetDto>.Failure($"Marketing target with Id {request.Id} not found.");

        if (request.Name is not null) target.Name = request.Name;
        if (request.TargetType is not null) target.TargetType = request.TargetType;
        if (request.ContactEmail is not null) target.ContactEmail = request.ContactEmail;
        if (request.ContactTwitter is not null) target.ContactTwitter = request.ContactTwitter;
        if (request.ContactDiscord is not null) target.ContactDiscord = request.ContactDiscord;
        if (request.PreferredGenres is not null) target.PreferredGenres = ParseJson(request.PreferredGenres);
        if (request.PreferredTags is not null) target.PreferredTags = ParseJson(request.PreferredTags);
        if (request.AudienceSize.HasValue) target.AudienceSize = request.AudienceSize.Value;
        if (request.AudienceRegion is not null) target.AudienceRegion = request.AudienceRegion;
        if (request.Platform is not null) target.Platform = request.Platform;
        if (request.EngagementRate.HasValue) target.EngagementRate = request.EngagementRate.Value;
        if (request.CostEstimateUsd.HasValue) target.CostEstimateUsd = request.CostEstimateUsd.Value;
        if (request.LastContacted.HasValue) target.LastContacted = request.LastContacted.Value;
        if (request.ResponseRate.HasValue) target.ResponseRate = request.ResponseRate.Value;
        if (request.Notes is not null) target.Notes = request.Notes;

        await _db.SaveChangesAsync(ct);
        return Result<MarketingTargetDto>.Success(target.ToDto());
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var target = await _db.MarketingTargets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (target is null)
            return Result<bool>.Failure($"Marketing target with Id {id} not found.");

        _db.MarketingTargets.Remove(target);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<MarketingTargetDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var target = await _db.MarketingTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (target is null)
            return Result<MarketingTargetDto>.Failure($"Marketing target with Id {id} not found.");

        return Result<MarketingTargetDto>.Success(target.ToDto());
    }

    public async Task<PagedResult<MarketingTargetSummaryDto>> ListAsync(ListMarketingTargetsQuery request, CancellationToken ct = default)
    {
        var query = _db.MarketingTargets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.TargetType))
            query = query.Where(t => t.TargetType == request.TargetType);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, $"%{request.Search}%") ||
                (t.Platform != null && EF.Functions.ILike(t.Platform, $"%{request.Search}%")));

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<MarketingTargetSummaryDto>
        {
            Items = items.Select(t => t.ToSummaryDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<TargetMatchDto>> ListMatchesAsync(ListTargetMatchesQuery request, CancellationToken ct = default)
    {
        var query = _db.TargetMatches
            .AsNoTracking()
            .Include(tm => tm.Game)
            .Include(tm => tm.Target)
            .AsQueryable();

        if (request.GameId.HasValue)
            query = query.Where(tm => tm.GameId == request.GameId.Value);

        if (request.TargetId.HasValue)
            query = query.Where(tm => tm.TargetId == request.TargetId.Value);

        if (!string.IsNullOrWhiteSpace(request.LifecycleStage))
            query = query.Where(tm => tm.LifecycleStage == request.LifecycleStage);

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(tm => tm.RelevanceScore)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<TargetMatchDto>
        {
            Items = items.Select(tm => tm.ToDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<Result<TargetMatchDto>> UpsertMatchAsync(UpsertTargetMatchCommand request, CancellationToken ct = default)
    {
        var existing = await _db.TargetMatches
            .Include(tm => tm.Game)
            .Include(tm => tm.Target)
            .FirstOrDefaultAsync(tm =>
                tm.GameId == request.GameId &&
                tm.TargetId == request.TargetId &&
                tm.LifecycleStage == request.LifecycleStage, ct);

        if (existing is null)
        {
            existing = new TargetMatch
            {
                Id = Guid.NewGuid(),
                GameId = request.GameId,
                TargetId = request.TargetId
            };
            _db.TargetMatches.Add(existing);
        }

        existing.LifecycleStage = request.LifecycleStage;
        existing.RelevanceScore = request.RelevanceScore;
        existing.GenreMatchScore = request.GenreMatchScore;
        existing.TagMatchScore = request.TagMatchScore;
        existing.HistoricalPerformanceScore = request.HistoricalPerformanceScore;
        if (request.SampleSize.HasValue) existing.SampleSize = request.SampleSize.Value;
        existing.MatchingGenres = ParseJson(request.MatchingGenres);
        existing.MatchingTags = ParseJson(request.MatchingTags);
        existing.CalculatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Reload with navigation
        existing = await _db.TargetMatches
            .AsNoTracking()
            .Include(tm => tm.Game)
            .Include(tm => tm.Target)
            .FirstAsync(tm => tm.Id == existing.Id, ct);

        return Result<TargetMatchDto>.Success(existing.ToDto());
    }

    private static JsonDocument? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }
}
