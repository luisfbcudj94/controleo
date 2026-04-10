using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Domain.Interfaces;
public interface IExpenseRepository
{
    Task<SaveExpenseResult> SaveAsync(string userId, string description, decimal amount, DateOnly date, string movementType, string paymentMethod, bool isCredit, int? installments, string? clientMutationId, CancellationToken ct);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string monthKey, CancellationToken ct);
    Task<PagedExpenseResult> GetExpensesPageAsync(string userId, string monthKey, int pageNumber, int pageSize, string? movementType, string? paymentMethod, string? searchTerm, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAvailableMonthKeysAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpdateExpenseAsync(string userId, string expenseId, string description, decimal amount, DateOnly date, string movementType, string paymentMethod, bool isCredit, int? installments, CancellationToken ct);
    Task<OperationResult> DeleteExpenseAsync(string userId, string expenseId, CancellationToken ct);
    Task<int> CountExpensesByMovementTypeAsync(string userId, string movementType, CancellationToken ct);
    Task<OperationResult> DeleteAllByMovementTypeAsync(string userId, string movementType, CancellationToken ct);
}
