using Controleo.Api.Models;

namespace Controleo.Api.Services;

public interface IExpenseStorageService
{
    Task<SaveExpenseResult> SaveAsync(ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpdateExpenseAsync(string expenseId, ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteExpenseAsync(string expenseId, CancellationToken cancellationToken);

    Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpdateCatalogsAsync(UpdateCatalogsRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpsertBudgetAsync(string movementType, BudgetUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteBudgetAsync(string movementType, CancellationToken cancellationToken);

    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(CancellationToken cancellationToken);
}
