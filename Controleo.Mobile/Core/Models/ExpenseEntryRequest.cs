namespace Controleo.Mobile.Core.Models;

public sealed record ExpenseEntryRequest(
    DateOnly Date,
    string Description,
    decimal Amount,
    string MovementType,
    string PaymentMethod,
    bool? IsCredit = null,
    int? Installments = null,
    string? ClientMutationId = null
);
