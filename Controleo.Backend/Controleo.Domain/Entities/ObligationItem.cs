namespace Controleo.Domain.Entities;

public sealed record ObligationItem(
    string Id,
    string Description,
    string MovementType,
    string PaymentMethod,
    int DueDayOfMonth,
    decimal MonthlyPayment,
    int ReminderDaysBefore,
    string StartMonth,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);