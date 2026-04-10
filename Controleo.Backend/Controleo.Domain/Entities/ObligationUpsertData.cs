namespace Controleo.Domain.Entities;

public sealed record ObligationUpsertData(
    string Description,
    string MovementType,
    string PaymentMethod,
    int DueDayOfMonth,
    decimal MonthlyPayment,
    int ReminderDaysBefore,
    string StartMonth,
    bool IsActive);