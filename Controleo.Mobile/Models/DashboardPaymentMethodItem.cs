namespace Controleo.Mobile.Models;

public sealed record DashboardPaymentMethodItem(
    string PaymentMethod,
    decimal ExpenseTotal
);
