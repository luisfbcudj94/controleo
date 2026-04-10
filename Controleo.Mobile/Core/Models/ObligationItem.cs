namespace Controleo.Mobile.Core.Models;

public sealed record ObligationItem(
    string Id,
    string Description,
    string MovementType,
    string PaymentMethod,
    int DueDayOfMonth,
    decimal MonthlyPayment,
    int ReminderDaysBefore,
    string StartMonth,
    bool IsActive
);