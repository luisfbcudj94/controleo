namespace Controleo.Mobile.Core.Models;

public sealed record DashboardCategoryItem(
    string MovementType,
    decimal ExpenseTotal,
    decimal BudgetTotal,
    decimal Balance
);
