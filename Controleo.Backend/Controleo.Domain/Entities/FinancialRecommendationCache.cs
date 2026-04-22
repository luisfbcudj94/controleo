namespace Controleo.Domain.Entities;

public sealed record FinancialRecommendationCache(
    string CacheId,
    string UserId,
    string SourceLabel,
    string Priority,
    IReadOnlyList<string> Recommendations,
    int ScoreAtGeneration,
    DateTimeOffset GeneratedAtUtc);

public sealed record AiUsageTotals(
    int DailyRequestCount,
    int UserMonthlyRequestCount,
    int GlobalMonthlyRequestCount,
    decimal UserMonthlyEstimatedUsd,
    decimal GlobalMonthlyEstimatedUsd);

public sealed record AiRecommendationResult(
    bool IsSuccess,
    string SourceLabel,
    string Priority,
    IReadOnlyList<string> Recommendations,
    string? ErrorMessage = null);
