using Controleo.Api.Models;

namespace Controleo.Api.Services;

public interface IExpenseStorageService
{
    Task<SaveExpenseResult> SaveAsync(ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string monthKey, CancellationToken cancellationToken);
    Task<OperationResult> UpdateExpenseAsync(string expenseId, ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteExpenseAsync(string expenseId, CancellationToken cancellationToken);

    Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpdateCatalogsAsync(UpdateCatalogsRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string monthKey, CancellationToken cancellationToken);
    Task<OperationResult> UpsertBudgetAsync(string movementType, string monthKey, BudgetUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteBudgetAsync(string movementType, string monthKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string monthKey, CancellationToken cancellationToken);

    Task<OperationResult> PurgeAllDataAsync(CancellationToken cancellationToken);
}
