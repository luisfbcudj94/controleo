using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Application.Interfaces;
public interface IBudgetService
{
    Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpsertBudgetAsync(string userId, string movementType, decimal amount, CancellationToken ct);
    Task<OperationResult> DeleteBudgetAsync(string userId, string movementType, CancellationToken ct);
}
