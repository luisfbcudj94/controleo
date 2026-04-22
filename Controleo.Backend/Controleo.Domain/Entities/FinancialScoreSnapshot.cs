namespace Controleo.Domain.Entities;

public sealed record FinancialScoreComponent(
    string Key,
    string Name,
    int Score,
    decimal Weight,
    string Detail);

public sealed record FinancialScoreSnapshot(
    string SnapshotId,
    string UserId,
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
    IReadOnlyList<string> Drivers,
    DateTimeOffset GeneratedAtUtc);
