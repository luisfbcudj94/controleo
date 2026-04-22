namespace Controleo.Application.DTOs;

public sealed record FinancialRecommendationResponse(
    string SourceLabel,
    string Priority,
    IReadOnlyList<string> Recommendations,
    int ScoreReference,
    string Trend,
    decimal Confidence,
    IReadOnlyList<string> Drivers,
    DateTimeOffset GeneratedAtUtc);
