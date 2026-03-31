namespace Controleo.Domain.Entities;
public sealed record BudgetItem(string MovementType, decimal Amount, DateTimeOffset UpdatedAt);
