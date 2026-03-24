using Cato.API.DTOs;
using Cato.API.Models.AppHistory;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.AppHistory;

public class GetRecentAppEventsHandler : IRequestHandler<GetRecentAppEventsQuery, PagedResult<RecentAppEventDto>>
{
    private readonly CatoDbContext _db;

    public GetRecentAppEventsHandler(CatoDbContext db) => _db = db;

    public async Task<PagedResult<RecentAppEventDto>> Handle(GetRecentAppEventsQuery request, CancellationToken ct)
    {
        // Group change records by (ChangeNumber, AppId) to produce one event per group.
        var baseQuery = _db.AppChangeRecords.AsNoTracking()
            .Join(
                _db.Games.AsNoTracking(),
                r => r.AppId,
                g => g.AppId,
                (r, g) => new { Record = r, Game = g }
            );

        if (!string.IsNullOrEmpty(request.GameType))
            baseQuery = baseQuery.Where(x => x.Game.GameType == request.GameType);

        // Group by (ChangeNumber, AppId) — one event per changelist per app
        var grouped = baseQuery
            .GroupBy(x => new
            {
                x.Record.ChangeNumber,
                x.Record.AppId,
                x.Game.Id,
                x.Game.Name,
                x.Game.HeaderImageUrl,
                x.Game.GameType
            });

        var totalCount = await grouped.CountAsync(ct);

        // Materialize the grouped summaries — pull raw data, derive event type in-memory.
        var rawGroups = await grouped
            .Select(g => new
            {
                g.Key.ChangeNumber,
                g.Key.AppId,
                GameId = g.Key.Id,
                GameName = g.Key.Name,
                g.Key.HeaderImageUrl,
                g.Key.GameType,
                DetectedAt = g.Max(x => x.Record.DetectedAt),
                ChangeCount = g.Count(),
                // Pull the name-change record if it exists (common section, key ending with "name")
                NameAction = g.Where(x => x.Record.Section == "common"
                    && (x.Record.KeyPath == "name" || x.Record.KeyPath == "common/name" || x.Record.KeyPath.EndsWith("/name")))
                    .Select(x => x.Record.Action)
                    .FirstOrDefault(),
                NameOldValue = g.Where(x => x.Record.Section == "common"
                    && (x.Record.KeyPath == "name" || x.Record.KeyPath == "common/name" || x.Record.KeyPath.EndsWith("/name")))
                    .Select(x => x.Record.OldValue)
                    .FirstOrDefault(),
                NameNewValue = g.Where(x => x.Record.Section == "common"
                    && (x.Record.KeyPath == "name" || x.Record.KeyPath == "common/name" || x.Record.KeyPath.EndsWith("/name")))
                    .Select(x => x.Record.NewValue)
                    .FirstOrDefault(),
                // Check for store-related changes
                HasStoreChanges = g.Any(x => x.Record.KeyPath.Contains("store")),
                // Get distinct sections affected
                Sections = g.Select(x => x.Record.Section).Distinct().ToList()
            })
            .OrderByDescending(g => g.DetectedAt)
            .ThenByDescending(g => g.ChangeNumber)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var items = rawGroups.Select(g =>
        {
            var (eventType, eventDetail, oldValue, newValue) = DeriveEvent(
                g.NameAction, g.NameOldValue, g.NameNewValue,
                g.HasStoreChanges, g.Sections, g.GameName, g.ChangeCount);

            return new RecentAppEventDto
            {
                ChangeNumber = g.ChangeNumber,
                AppId = g.AppId,
                GameId = g.GameId,
                GameName = g.GameName,
                HeaderImageUrl = g.HeaderImageUrl,
                GameType = g.GameType,
                EventType = eventType,
                EventDetail = eventDetail,
                OldValue = oldValue,
                NewValue = newValue,
                ChangeCount = g.ChangeCount,
                DetectedAt = g.DetectedAt
            };
        }).ToList();

        return new PagedResult<RecentAppEventDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    private static (string EventType, string EventDetail, string? OldValue, string? NewValue) DeriveEvent(
        string? nameAction, string? nameOldValue, string? nameNewValue,
        bool hasStoreChanges, List<string> sections, string gameName, int changeCount)
    {
        // Name was added → new app
        if (nameAction == "Added" && !string.IsNullOrEmpty(nameNewValue))
        {
            if (hasStoreChanges)
                return ("NewOnStore", nameNewValue, null, nameNewValue);

            return ("New", nameNewValue, null, nameNewValue);
        }

        // Name was modified → rename
        if (nameAction == "Modified" && !string.IsNullOrEmpty(nameNewValue))
        {
            return ("Renamed", $"{nameOldValue} → {nameNewValue}", nameOldValue, nameNewValue);
        }

        // Name was removed → removed from store
        if (nameAction == "Removed")
        {
            return ("RemovedFromStore", nameOldValue ?? gameName, nameOldValue, null);
        }

        // No name change — generic change event
        var sectionSummary = string.Join(", ", sections);
        var detail = $"{changeCount} change{(changeCount != 1 ? "s" : "")} in {sectionSummary}";
        return ("Changed", detail, null, null);
    }
}
