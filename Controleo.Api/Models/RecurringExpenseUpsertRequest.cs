namespace Controleo.Api.Models;

public sealed record RecurringExpenseUpsertRequest(
    string Description,
    decimal Amount,
    string MovementType,
    string PaymentMethod,
    int DayOfMonth,
    string? StartMonth,
    string? EndMonth,
    string? StartDate,
    string? EndDate,
    bool IsActive
);
