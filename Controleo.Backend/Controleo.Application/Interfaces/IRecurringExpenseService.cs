using Controleo.Application.DTOs;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Application.Interfaces;
public interface IRecurringExpenseService
{
    Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken ct);
    Task<PagedRecurringExpenseResult> GetRecurringExpensesPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct);
    Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? id, RecurringExpenseUpsertRequest request, CancellationToken ct);
    Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string id, CancellationToken ct);
}
