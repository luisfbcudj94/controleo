namespace Controleo.Domain.Entities;
public sealed record GoalContribution(string Id, string GoalId, decimal Amount, DateOnly Date, string? Note, DateTimeOffset CreatedAt);
