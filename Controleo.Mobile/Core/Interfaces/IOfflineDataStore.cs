using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Interfaces;

public interface IOfflineDataStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveCatalogAsync(string userId, ExpenseCatalog catalog, CancellationToken cancellationToken = default);
    Task<ExpenseCatalog?> GetCatalogAsync(string userId, CancellationToken cancellationToken = default);

    Task SaveAvailableMonthsAsync(string userId, IReadOnlyList<string> monthKeys, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableMonthsAsync(string userId, CancellationToken cancellationToken = default);
    Task AddAvailableMonthAsync(string userId, string monthKey, CancellationToken cancellationToken = default);

    Task SaveExpensesAsync(string userId, string monthKey, IReadOnlyList<ExpenseItem> expenses, CancellationToken cancellationToken = default);
    Task MergeExpensesAsync(string userId, string monthKey, IReadOnlyList<ExpenseItem> expenses, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string monthKey, CancellationToken cancellationToken = default);
    Task UpsertExpenseAsync(string userId, string monthKey, ExpenseItem expense, CancellationToken cancellationToken = default);
    Task RemoveExpenseAsync(string userId, string monthKey, string expenseId, CancellationToken cancellationToken = default);
    Task<bool> RemoveExpenseAcrossMonthsAsync(string userId, string expenseId, CancellationToken cancellationToken = default);

    Task SaveDashboardByCategoryAsync(string userId, string monthKey, IReadOnlyList<DashboardCategoryItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string monthKey, CancellationToken cancellationToken = default);
    Task SaveDashboardByPaymentMethodAsync(string userId, string monthKey, IReadOnlyList<DashboardPaymentMethodItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string userId, string monthKey, CancellationToken cancellationToken = default);

    Task<long> EnqueueMutationAsync(string userId, string mutationType, string scopeKey, string payloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfflineMutation>> GetPendingMutationsAsync(string userId, int take, CancellationToken cancellationToken = default);
    Task MarkMutationSucceededAsync(long mutationId, CancellationToken cancellationToken = default);
    Task IncrementMutationRetryAsync(long mutationId, CancellationToken cancellationToken = default);
    Task<bool> TryDropPendingCreateMutationAsync(string userId, string monthKey, string localExpenseId, CancellationToken cancellationToken = default);
    Task RemapExpenseIdAsync(string userId, string monthKey, string localExpenseId, string serverExpenseId, CancellationToken cancellationToken = default);
    Task AddDeadLetterMutationAsync(string userId, OfflineMutation mutation, string reason, CancellationToken cancellationToken = default);
}
