using Controleo.Domain.Entities; using Controleo.Domain.Interfaces;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosDashboardRepository(ICatalogRepository catalogRepo, IBudgetRepository budgetRepo, IExpenseRepository expenseRepo) : IDashboardRepository
{
    public async Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string mk, CancellationToken ct)
    {
        var catalog = await catalogRepo.GetCatalogsAsync(userId, ct); var budgets = await budgetRepo.GetBudgetsAsync(userId, ct); var expenses = await expenseRepo.GetExpensesAsync(userId, mk, ct);
        var bMap = budgets.ToDictionary(b => b.MovementType, b => b.Amount, StringComparer.OrdinalIgnoreCase);
        var eMap = expenses.GroupBy(e => e.MovementType, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount), StringComparer.OrdinalIgnoreCase);
        var all = catalog.MovementTypes.Concat(bMap.Keys).Concat(eMap.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return all.Select(c => { var exp = eMap.TryGetValue(c, out var ev) ? ev : 0m; var bud = bMap.TryGetValue(c, out var bv) ? bv : 0m; return new DashboardCategoryItem(c, exp, bud, bud - exp); }).OrderByDescending(d => d.ExpenseTotal).ToArray();
    }

    public async Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string userId, string mk, CancellationToken ct)
    {
        var expenses = await expenseRepo.GetExpensesAsync(userId, mk, ct);
        return expenses
            .Where(e => !string.IsNullOrWhiteSpace(e.PaymentMethod))
            .GroupBy(e => e.PaymentMethod.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new DashboardPaymentMethodItem(g.Key, g.Sum(e => e.Amount)))
            .OrderByDescending(item => item.ExpenseTotal)
            .ToArray();
    }
}
