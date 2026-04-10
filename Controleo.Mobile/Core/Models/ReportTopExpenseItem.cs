namespace Controleo.Mobile.Core.Models;

public sealed record ReportTopExpenseItem(
    DateOnly Date,
    string Description,
    string MovementType,
    string PaymentMethod,
    decimal Amount);
