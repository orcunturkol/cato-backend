using Cato.Domain.Entities;

namespace Cato.API.DTOs;

public record MarketingTargetDto(
    Guid Id,
    string Name,
    string TargetType,
    string? ContactEmail,
    string? ContactTwitter,
    string? ContactDiscord,
    string? PreferredGenres,
    string? PreferredTags,
    int? AudienceSize,
    string? AudienceRegion,
    string? Platform,
    decimal? EngagementRate,
    decimal? CostEstimateUsd,
    DateOnly? LastContacted,
    decimal? ResponseRate,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record MarketingTargetSummaryDto(
    Guid Id,
    string Name,
    string TargetType,
    string? Platform,
    int? AudienceSize,
    decimal? EngagementRate,
    decimal? ResponseRate,
    DateOnly? LastContacted
);

public record MarketingActionDto(
    Guid Id,
    string ActionType,
    string DecisionSource,
    string Status,
    DateOnly? PlannedDate,
    DateOnly? ActionDate,
    DateOnly? CompletionDate,
    string Description,
    decimal? BudgetUsd,
    decimal? ActualCostUsd,
    string? Notes,
    string? CreatedBy,
    List<GameActionDto> Games,
    List<ActionTargetDto> Targets,
    ActionImpactDto? Impact,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record MarketingActionSummaryDto(
    Guid Id,
    string ActionType,
    string DecisionSource,
    string Status,
    DateOnly? PlannedDate,
    DateOnly? ActionDate,
    string Description,
    decimal? BudgetUsd,
    decimal? ActualCostUsd,
    int GamesCount,
    int TargetsCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record GameActionDto(
    Guid Id,
    Guid ActionId,
    Guid GameId,
    string? GameName,
    string GameRole,
    string? Notes,
    DateTime CreatedAt
);

public record ActionTargetDto(
    Guid Id,
    Guid ActionId,
    Guid TargetId,
    string? TargetName,
    string? TargetType,
    string Status,
    DateOnly? OutreachDate,
    DateOnly? ResponseDate,
    string? DeliverableUrl,
    int? Views,
    int? Engagement,
    decimal? CostUsd,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record TargetMatchDto(
    Guid Id,
    Guid GameId,
    string? GameName,
    Guid TargetId,
    string? TargetName,
    string? LifecycleStage,
    decimal? RelevanceScore,
    decimal? GenreMatchScore,
    decimal? TagMatchScore,
    decimal? HistoricalPerformanceScore,
    int SampleSize,
    string? MatchingGenres,
    string? MatchingTags,
    DateTime? CalculatedAt
);

public record ActionImpactDto(
    Guid Id,
    Guid ActionId,
    DateOnly? MeasurementStart,
    DateOnly? MeasurementEnd,
    DateOnly? BaselineStart,
    DateOnly? BaselineEnd,
    int? BaselineWishlistAdds,
    int? ResultWishlistAdds,
    int? WishlistChange,
    decimal? WishlistChangePercent,
    int? BaselineSalesUnits,
    int? ResultSalesUnits,
    int? SalesUnitsChange,
    decimal? SalesChangePercent,
    decimal? BaselineRevenueUsd,
    decimal? ResultRevenueUsd,
    decimal? RevenueChangeUsd,
    decimal? RevenueChangePercent,
    int? BaselineTraffic,
    int? ResultTraffic,
    int? TrafficChange,
    decimal? TrafficChangePercent,
    decimal? BaselineConversionRate,
    decimal? ResultConversionRate,
    decimal? ConversionRateChange,
    decimal? TotalCostUsd,
    decimal? Roi,
    string? Notes,
    DateTime? CalculatedAt,
    string? CalculatedBy
);

public static class MarketingMappings
{
    public static MarketingTargetDto ToDto(this MarketingTarget t) => new(
        Id: t.Id,
        Name: t.Name,
        TargetType: t.TargetType,
        ContactEmail: t.ContactEmail,
        ContactTwitter: t.ContactTwitter,
        ContactDiscord: t.ContactDiscord,
        PreferredGenres: t.PreferredGenres?.RootElement.ToString(),
        PreferredTags: t.PreferredTags?.RootElement.ToString(),
        AudienceSize: t.AudienceSize,
        AudienceRegion: t.AudienceRegion,
        Platform: t.Platform,
        EngagementRate: t.EngagementRate,
        CostEstimateUsd: t.CostEstimateUsd,
        LastContacted: t.LastContacted,
        ResponseRate: t.ResponseRate,
        Notes: t.Notes,
        CreatedAt: t.CreatedAt,
        UpdatedAt: t.UpdatedAt
    );

    public static MarketingTargetSummaryDto ToSummaryDto(this MarketingTarget t) => new(
        Id: t.Id,
        Name: t.Name,
        TargetType: t.TargetType,
        Platform: t.Platform,
        AudienceSize: t.AudienceSize,
        EngagementRate: t.EngagementRate,
        ResponseRate: t.ResponseRate,
        LastContacted: t.LastContacted
    );

    public static MarketingActionDto ToDto(this MarketingAction a) => new(
        Id: a.Id,
        ActionType: a.ActionType,
        DecisionSource: a.DecisionSource,
        Status: a.Status,
        PlannedDate: a.PlannedDate,
        ActionDate: a.ActionDate,
        CompletionDate: a.CompletionDate,
        Description: a.Description,
        BudgetUsd: a.BudgetUsd,
        ActualCostUsd: a.ActualCostUsd,
        Notes: a.Notes,
        CreatedBy: a.CreatedBy,
        Games: a.GameActions.Select(ga => ga.ToDto()).ToList(),
        Targets: a.ActionTargets.Select(at => at.ToDto()).ToList(),
        Impact: a.Impact?.ToDto(),
        CreatedAt: a.CreatedAt,
        UpdatedAt: a.UpdatedAt
    );

    public static MarketingActionSummaryDto ToSummaryDto(this MarketingAction a) => new(
        Id: a.Id,
        ActionType: a.ActionType,
        DecisionSource: a.DecisionSource,
        Status: a.Status,
        PlannedDate: a.PlannedDate,
        ActionDate: a.ActionDate,
        Description: a.Description,
        BudgetUsd: a.BudgetUsd,
        ActualCostUsd: a.ActualCostUsd,
        GamesCount: a.GameActions.Count,
        TargetsCount: a.ActionTargets.Count,
        CreatedAt: a.CreatedAt,
        UpdatedAt: a.UpdatedAt
    );

    public static GameActionDto ToDto(this GameAction ga) => new(
        Id: ga.Id,
        ActionId: ga.ActionId,
        GameId: ga.GameId,
        GameName: ga.Game?.Name,
        GameRole: ga.GameRole,
        Notes: ga.Notes,
        CreatedAt: ga.CreatedAt
    );

    public static ActionTargetDto ToDto(this ActionTarget at) => new(
        Id: at.Id,
        ActionId: at.ActionId,
        TargetId: at.TargetId,
        TargetName: at.Target?.Name,
        TargetType: at.Target?.TargetType,
        Status: at.Status,
        OutreachDate: at.OutreachDate,
        ResponseDate: at.ResponseDate,
        DeliverableUrl: at.DeliverableUrl,
        Views: at.Views,
        Engagement: at.Engagement,
        CostUsd: at.CostUsd,
        Notes: at.Notes,
        CreatedAt: at.CreatedAt,
        UpdatedAt: at.UpdatedAt
    );

    public static TargetMatchDto ToDto(this TargetMatch tm) => new(
        Id: tm.Id,
        GameId: tm.GameId,
        GameName: tm.Game?.Name,
        TargetId: tm.TargetId,
        TargetName: tm.Target?.Name,
        LifecycleStage: tm.LifecycleStage,
        RelevanceScore: tm.RelevanceScore,
        GenreMatchScore: tm.GenreMatchScore,
        TagMatchScore: tm.TagMatchScore,
        HistoricalPerformanceScore: tm.HistoricalPerformanceScore,
        SampleSize: tm.SampleSize,
        MatchingGenres: tm.MatchingGenres?.RootElement.ToString(),
        MatchingTags: tm.MatchingTags?.RootElement.ToString(),
        CalculatedAt: tm.CalculatedAt
    );

    public static ActionImpactDto ToDto(this ActionImpact ai) => new(
        Id: ai.Id,
        ActionId: ai.ActionId,
        MeasurementStart: ai.MeasurementStart,
        MeasurementEnd: ai.MeasurementEnd,
        BaselineStart: ai.BaselineStart,
        BaselineEnd: ai.BaselineEnd,
        BaselineWishlistAdds: ai.BaselineWishlistAdds,
        ResultWishlistAdds: ai.ResultWishlistAdds,
        WishlistChange: ai.WishlistChange,
        WishlistChangePercent: ai.WishlistChangePercent,
        BaselineSalesUnits: ai.BaselineSalesUnits,
        ResultSalesUnits: ai.ResultSalesUnits,
        SalesUnitsChange: ai.SalesUnitsChange,
        SalesChangePercent: ai.SalesChangePercent,
        BaselineRevenueUsd: ai.BaselineRevenueUsd,
        ResultRevenueUsd: ai.ResultRevenueUsd,
        RevenueChangeUsd: ai.RevenueChangeUsd,
        RevenueChangePercent: ai.RevenueChangePercent,
        BaselineTraffic: ai.BaselineTraffic,
        ResultTraffic: ai.ResultTraffic,
        TrafficChange: ai.TrafficChange,
        TrafficChangePercent: ai.TrafficChangePercent,
        BaselineConversionRate: ai.BaselineConversionRate,
        ResultConversionRate: ai.ResultConversionRate,
        ConversionRateChange: ai.ConversionRateChange,
        TotalCostUsd: ai.TotalCostUsd,
        Roi: ai.Roi,
        Notes: ai.Notes,
        CalculatedAt: ai.CalculatedAt,
        CalculatedBy: ai.CalculatedBy
    );
}
