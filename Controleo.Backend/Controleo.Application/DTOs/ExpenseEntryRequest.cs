namespace Controleo.Application.DTOs;
public sealed record ExpenseEntryRequest(DateOnly Date, string Description, decimal Amount, string MovementType, string PaymentMethod);
