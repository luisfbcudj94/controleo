namespace Controleo.Api.Models;

public sealed record DashboardCategoryItem(
    string MovementType,
    decimal ExpenseTotal,
    decimal BudgetTotal,
    decimal Balance
);
