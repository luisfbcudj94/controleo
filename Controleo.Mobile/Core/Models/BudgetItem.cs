namespace Controleo.Mobile.Core.Models;

public sealed record BudgetItem(
    string MovementType,
    decimal Amount,
    DateTimeOffset UpdatedAt
);
