namespace Controleo.Application.DTOs;

public sealed record FinancialScoreResponse(
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
    IReadOnlyList<FinancialScoreComponentResponse> Components,
    IReadOnlyList<string> Drivers);

public sealed record FinancialScoreComponentResponse(
    string Key,
    string Name,
    int Score,
    decimal Weight,
    string Detail);

public sealed record FinancialScoreHistoryItem(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Score,
    string Trend,
    decimal Confidence,
    DateTimeOffset GeneratedAtUtc);
