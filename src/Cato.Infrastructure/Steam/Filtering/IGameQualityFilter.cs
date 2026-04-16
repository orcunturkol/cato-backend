using Cato.Domain.Entities;

namespace Cato.Infrastructure.Steam.Filtering;

public enum FilterStage
{
    PreEnrichment,
    PostEnrichment,
    Backfill
}

public record FilterDecision(bool Rejected, string? Reason)
{
    public static FilterDecision Accept() => new(false, null);
    public static FilterDecision Reject(string reason) => new(true, reason);
}

public interface IGameQualityFilter
{
    bool ShouldRejectByName(string name);
    FilterDecision Evaluate(Game game, FilterStage stage);
}
