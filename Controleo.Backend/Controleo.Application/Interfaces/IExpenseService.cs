using Controleo.Application.DTOs;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Application.Interfaces;
public interface IExpenseService
{
    Task<SaveExpenseResult> SaveExpenseAsync(string userId, ExpenseEntryRequest request, CancellationToken ct);
    Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string monthKey, CancellationToken ct);
    Task<PagedExpenseResult> GetExpensesPageAsync(string userId, string monthKey, int pageNumber, int pageSize, string? movementType, string? searchTerm, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAvailableMonthsAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpdateExpenseAsync(string userId, string expenseId, ExpenseEntryRequest request, CancellationToken ct);
    Task<OperationResult> DeleteExpenseAsync(string userId, string expenseId, CancellationToken ct);
    Task<int> CountByMovementTypeAsync(string userId, string movementType, CancellationToken ct);
    Task<OperationResult> DeleteAllByMovementTypeAsync(string userId, string movementType, CancellationToken ct);
}
