namespace Controleo.Mobile.Core.Models;

public sealed record RecurringExpenseUpsertRequest(
    string Description,
    decimal Amount,
    string MovementType,
    string PaymentMethod,
    int DayOfMonth,
    DateOnly StartDate,
    bool IsActive
);
