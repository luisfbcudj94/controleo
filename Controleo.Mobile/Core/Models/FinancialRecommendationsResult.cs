namespace Controleo.Mobile.Core.Models;

public sealed record FinancialRecommendationsResult(
    bool IsSuccess,
    string Message,
    string SourceLabel,
    string Priority,
    IReadOnlyList<string> Recommendations,
    int ScoreReference,
    string Trend,
    decimal Confidence,
    IReadOnlyList<string> Drivers,
    DateTimeOffset GeneratedAtUtc)
{
    public static FinancialRecommendationsResult Failure(string message)
    {
        return new FinancialRecommendationsResult(
            false,
            message,
            string.Empty,
            string.Empty,
            [],
            0,
            "stable",
            0,
            [],
            DateTimeOffset.UtcNow);
    }
}
