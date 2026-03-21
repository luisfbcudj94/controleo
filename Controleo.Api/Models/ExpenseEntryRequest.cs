namespace Controleo.Api.Models;

public sealed record ExpenseEntryRequest(
    DateOnly Date,
    string Description,
    decimal Amount,
    string MovementType,
    string PaymentMethod
);
