namespace Controleo.Mobile.Core.Models;

public sealed record FinancialScoreComponent(
    string Key,
    string Name,
    int Score,
    decimal Weight,
    string Detail);

public sealed record FinancialScoreResult(
    bool IsSuccess,
    string Message,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Score,
    string Trend,
    decimal Confidence,
    string AlgorithmVersion,
    decimal TotalSpent,
    decimal? MonthlyIncome,
    decimal? SpendingToIncomeRatio,
    decimal? ObligationsToIncomeRatio,
    decimal BudgetUtilizationRatio,
    decimal AmountChangePercentage,
    IReadOnlyList<FinancialScoreComponent> Components,
    IReadOnlyList<string> Drivers)
{
    public static FinancialScoreResult Failure(DateOnly startDate, DateOnly endDate, string message)
    {
        return new FinancialScoreResult(
            false,
            message,
            startDate,
            endDate,
            0,
            "stable",
            0,
            "",
            0,
            null,
            null,
            null,
            0,
            0,
            [],
            []);
    }
}

public sealed record FinancialScoreHistoryItem(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Score,
    string Trend,
    decimal Confidence,
    DateTimeOffset GeneratedAtUtc);
