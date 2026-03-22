namespace Controleo.Api.Models;

public sealed record BudgetItem(
    string MovementType,
    decimal Amount,
    DateTimeOffset UpdatedAt
);
