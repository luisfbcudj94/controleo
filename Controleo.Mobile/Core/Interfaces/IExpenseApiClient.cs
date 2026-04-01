using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Interfaces;

public interface IExpenseApiClient
{
    Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string monthKey, CancellationToken cancellationToken);
    Task<PagedExpenseResult> GetExpensesPageAsync(string monthKey, int pageNumber, int pageSize, string? movementType, string? paymentMethod, string? searchTerm, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAvailableMonthsAsync(CancellationToken cancellationToken);
    Task<SaveExpenseResult> SaveExpenseAsync(ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> UpdateExpenseAsync(string id, ExpenseEntryRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteExpenseAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string monthKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string monthKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(CancellationToken cancellationToken);
    Task<OperationResult> UpsertBudgetAsync(string movementType, decimal amount, CancellationToken cancellationToken);
    Task<OperationResult> DeleteBudgetAsync(string movementType, CancellationToken cancellationToken);
    Task<OperationResult> UpdateCatalogsAsync(UpdateCatalogsRequest request, CancellationToken cancellationToken);
    Task<int> CountExpensesByMovementTypeAsync(string movementType, CancellationToken cancellationToken);
    Task<OperationResult> DeleteAllByMovementTypeAsync(string movementType, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(CancellationToken cancellationToken);
    Task<OperationResult> SaveRecurringExpenseAsync(string? id, RecurringExpenseUpsertRequest request, CancellationToken cancellationToken);
    Task<OperationResult> DeleteRecurringExpenseAsync(string id, CancellationToken cancellationToken);
}
