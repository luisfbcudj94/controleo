namespace Controleo.Mobile.Core.Models;

public sealed record SavingsGoalItem(
    string Id,
    string Name,
    string Icon,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateOnly TargetDate,
    string Status,
    string Priority,
    decimal ProgressPercent,
    decimal SuggestedMonthlyContribution,
    DateOnly? ProjectedCompletionDate,
    int DaysRemaining,
    bool IsOnTrack,
    decimal? MonthlyAvailableSavings,
    IReadOnlyList<GoalContributionItem> Contributions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GoalContributionItem(
    string Id,
    string GoalId,
    decimal Amount,
    DateOnly Date,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record GoalSimulationResult(
    DateOnly? ProjectedDate,
    decimal RequiredMonthlyAmount,
    string Feasibility,
    string Description);

public sealed record GoalAlertItem(
    string GoalId,
    string GoalName,
    string Message,
    string Severity);

public sealed record GoalUpsertRequest(
    string Name,
    string Icon,
    decimal TargetAmount,
    DateOnly TargetDate,
    string Priority,
    string? Status = null);

public sealed record GoalContributionRequest(
    decimal Amount,
    DateOnly Date,
    string? Note);

public sealed record GoalSimulationRequest(
    string ScenarioType,
    decimal NewValue);
