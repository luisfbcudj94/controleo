namespace Controleo.Domain.Entities;
public sealed record SavingsGoal(string Id, string Name, string Icon, decimal TargetAmount, decimal CurrentAmount, DateOnly TargetDate, string Status, string Priority, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
