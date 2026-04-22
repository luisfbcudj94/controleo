namespace Controleo.Application.DTOs;
public sealed record GoalContributionRequest(decimal Amount, DateOnly Date, string? Note);
