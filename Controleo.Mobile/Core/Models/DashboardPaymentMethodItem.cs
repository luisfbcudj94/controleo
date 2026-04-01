namespace Controleo.Mobile.Core.Models;

public sealed record DashboardPaymentMethodItem(
    string PaymentMethod,
    decimal ExpenseTotal
);
