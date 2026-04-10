namespace Controleo.Mobile.Core.Models;

public sealed record ExpenseItem(
    string Id,
    DateOnly Date,
    string Description,
    decimal Amount,
    string MovementType,
    string PaymentMethod,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsCredit = false,
    int? Installments = null
);
