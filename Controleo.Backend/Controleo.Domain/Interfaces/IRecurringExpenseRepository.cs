using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Domain.Interfaces;
public interface IRecurringExpenseRepository
{
    Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? id, string description, decimal amount, string movementType, string paymentMethod, int dayOfMonth, string startMonth, bool isActive, CancellationToken ct);
    Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string id, CancellationToken ct);
}
