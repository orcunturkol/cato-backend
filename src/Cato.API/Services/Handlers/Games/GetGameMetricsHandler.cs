using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Models.Ingestion;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class GetGameMetricsHandler : IRequestHandler<GetGameMetricsQuery, List<GameMetricsDto>>
{
    private const string MostWished = "steamdb_most_wished";
    private const string WishlistActivity = "steamdb_wishlist_activity";

    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public GetGameMetricsHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<List<GameMetricsDto>> Handle(GetGameMetricsQuery request, CancellationToken ct)
    {
        var appIds = request.AppIds.Distinct().ToList();
        var games = await _db.Games.AsNoTracking()
            .Where(g => appIds.Contains(g.AppId))
            .Select(g => new { g.Id, g.AppId })
            .ToListAsync(ct);
        var gameIds = games.Select(g => g.Id).ToList();

        var now = _time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var from = today.AddDays(-request.Days);
        var rankFrom = today.AddDays(-Math.Max(request.Days, 30));
        var ccuFrom = now.AddDays(-7);

        // A set of queries for all games together -- never one round trip per game.
        var graphSpans = await _db.GroupMemberCountSnapshots.AsNoTracking()
            .Where(s => gameIds.Contains(s.GameId) && s.Error == null
                        && s.Source == GroupMemberCountSnapshot.SteamDbFollowerSource)
            .GroupBy(s => s.GameId)
            .Select(g => new { GameId = g.Key, Start = g.Min(s => s.SnapshotDate), End = g.Max(s => s.SnapshotDate) })
            .ToDictionaryAsync(x => x.GameId, ct);
        var members = (await _db.GroupMemberCountSnapshots.AsNoTracking()
                .Where(s => gameIds.Contains(s.GameId) && s.Error == null && s.SnapshotDate >= from)
                .Select(s => new { s.GameId, s.SnapshotDate, s.MemberCount, s.Source })
                .ToListAsync(ct))
            .ToLookup(s => s.GameId);
        var snapshots = (await _db.SteamDbSnapshots.AsNoTracking()
                .Where(s => gameIds.Contains(s.GameId) && s.SnapshotDate >= rankFrom)
                .Select(s => new { s.GameId, s.Source, s.SnapshotDate, s.Rank, s.Follows, s.SevenDayGain })
                .ToListAsync(ct))
            .ToLookup(s => s.GameId);
        var ccu = (await _db.CcuHistories.AsNoTracking()
                .Where(c => gameIds.Contains(c.GameId) && c.Timestamp >= ccuFrom)
                .Select(c => new { c.GameId, c.Timestamp, c.CcuCount, c.PeakCcuToday })
                .ToListAsync(ct))
            .ToLookup(c => c.GameId);
        var summaries = (await _db.ReviewSummarySnapshots.AsNoTracking()
                .Where(s => gameIds.Contains(s.GameId)
                            && s.SnapshotDate == _db.ReviewSummarySnapshots
                                .Where(x => x.GameId == s.GameId).Max(x => x.SnapshotDate))
                .Select(s => new { s.GameId, s.SnapshotDate, s.CreatedAt, s.TotalReviews, s.TotalPositive, s.TotalNegative, s.ReviewScoreDesc })
                .ToListAsync(ct))
            .GroupBy(s => s.GameId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.CreatedAt)!); // a day can hold several runs
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var reviews = (await _db.SteamReviews.AsNoTracking()
                .Where(r => gameIds.Contains(r.GameId) && r.TimestampCreated >= fromUtc)
                .Select(r => new { r.GameId, r.TimestampCreated, r.VotedUp })
                .ToListAsync(ct))
            .ToLookup(r => r.GameId);
        var unlocks = (await _db.GameAchievementPercents.AsNoTracking()
                .Where(a => gameIds.Contains(a.GameId)
                            && a.SnapshotDate == _db.GameAchievementPercents
                                .Where(x => x.GameId == a.GameId).Max(x => x.SnapshotDate))
                .Select(a => new { a.GameId, a.SnapshotDate, a.Percent })
                .ToListAsync(ct))
            .ToLookup(a => a.GameId);

        AchievementSummaryDto? Unlocks(Guid gameId)
        {
            var day = unlocks[gameId].ToList();
            if (day.Count == 0)
                return null;
            var percents = day.Select(a => (double)a.Percent).Order().ToList();
            var mid = percents.Count / 2;
            var median = percents.Count % 2 == 1 ? percents[mid] : (percents[mid - 1] + percents[mid]) / 2;
            return new AchievementSummaryDto(day[0].SnapshotDate, percents.Count, percents[^1], median);
        }

        ListRankDto? Latest(Guid gameId, string source) => snapshots[gameId]
            .Where(s => s.Source == source)
            .OrderByDescending(s => s.SnapshotDate)
            .Select(s => new ListRankDto(s.SnapshotDate, s.Rank, s.Follows, s.SevenDayGain))
            .FirstOrDefault();

        return games.Select(g =>
        {
            var span = graphSpans.GetValueOrDefault(g.Id);
            var graphEnd = span?.End ?? DateOnly.MinValue;
            var listed = snapshots[g.Id]
                .Where(s => s.Follows > 0 && s.SnapshotDate >= from)
                .GroupBy(s => s.SnapshotDate)
                .Select(d => (Date: d.Key, Follows: d.Max(s => s.Follows)));
            var followers = Merge(
                graphEnd,
                members[g.Id].Select(m => (m.SnapshotDate, m.MemberCount, m.Source)),
                listed);

            var recent = ccu[g.Id].ToList();
            var latest = recent.MaxBy(c => c.Timestamp);
            var ccuSummary = latest is null
                ? null
                : new CcuSummaryDto(
                    latest.Timestamp,
                    latest.CcuCount,
                    recent.Max(c => Math.Max(c.CcuCount, c.PeakCcuToday ?? 0)));

            var summary = summaries.GetValueOrDefault(g.Id);
            var daily = reviews[g.Id]
                .GroupBy(r => DateOnly.FromDateTime(r.TimestampCreated))
                .OrderBy(d => d.Key)
                .Select(d => new ReviewDayDto(d.Key, d.Count(), d.Count(r => r.VotedUp)))
                .ToList();

            return new GameMetricsDto(
                g.AppId, followers, Latest(g.Id, MostWished), Latest(g.Id, WishlistActivity), ccuSummary,
                span?.Start,
                summary is null
                    ? null
                    : new ReviewSummaryDto(summary.SnapshotDate, summary.TotalReviews, summary.TotalPositive,
                        summary.TotalNegative, summary.ReviewScoreDesc),
                daily,
                Unlocks(g.Id));
        }).ToList();
    }

    // Same merge as GameDataService.GetFollowerSeriesAsync, on rows already cut to the
    // window (graphEnd comes from the whole graph). GameMetricsHandlerTests compares the
    // two, so keep them in step.
    private static List<FollowerPointDto> Merge(
        DateOnly graphEnd,
        IEnumerable<(DateOnly Date, int Count, string Source)> members,
        IEnumerable<(DateOnly Date, int Follows)> listed)
    {
        var all = members.ToList();
        var graph = all.Where(m => m.Source == GroupMemberCountSnapshot.SteamDbFollowerSource).ToList();

        var after = new Dictionary<DateOnly, FollowerPointDto>();
        foreach (var l in listed.Where(l => l.Date > graphEnd))
            after[l.Date] = new FollowerPointDto(l.Date, l.Follows, "steamdb_list");
        foreach (var m in all.Where(m => m.Source == GroupMemberCountSnapshot.SteamCommunitySource && m.Date > graphEnd))
            after.TryAdd(m.Date, new FollowerPointDto(m.Date, m.Count, m.Source));

        return graph
            .Select(m => new FollowerPointDto(m.Date, m.Count, m.Source))
            .Concat(after.Values)
            .OrderBy(p => p.Date)
            .ToList();
    }
}
