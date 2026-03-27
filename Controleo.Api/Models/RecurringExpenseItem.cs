namespace Controleo.Api.Models;

public sealed record RecurringExpenseItem(
    string Id,
    string Description,
    decimal Amount,
    string MovementType,
    string PaymentMethod,
    int DayOfMonth,
    string StartMonth,
    string? EndMonth,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
