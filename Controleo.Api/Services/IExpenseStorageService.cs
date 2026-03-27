using Controleo.Api.Models;

namespace Controleo.Api.Services;

public interface IExpenseStorageService
{
    Task<SaveExpenseResult> SaveAsync(string userId, ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string monthKey, CancellationToken cancellationToken);
    Task<PagedExpenseResult> GetExpensesPageAsync(string userId, string monthKey, int pageNumber, int pageSize, string? movementType, string? searchTerm, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAvailableMonthKeysAsync(string userId, CancellationToken cancellationToken);
    Task<OperationResult> UpdateExpenseAsync(string userId, string expenseId, ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken);

    Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken cancellationToken);
    Task<OperationResult> UpdateCatalogsAsync(string userId, UpdateCatalogsRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string userId, CancellationToken cancellationToken);
    Task<OperationResult> UpsertBudgetAsync(string userId, string movementType, BudgetUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteBudgetAsync(string userId, string movementType, CancellationToken cancellationToken);

    Task<int> CountExpensesByMovementTypeAsync(string userId, string movementType, CancellationToken cancellationToken);
    Task<OperationResult> DeleteAllByMovementTypeAsync(string userId, string movementType, CancellationToken cancellationToken);

    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string monthKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken cancellationToken);
    Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? recurringExpenseId, RecurringExpenseUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string recurringExpenseId, CancellationToken cancellationToken);

    Task<OperationResult> PurgeAllDataAsync(string userId, CancellationToken cancellationToken);
}
