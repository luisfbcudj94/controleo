using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class BudgetService(IBudgetRepository budgetRepo, ICatalogRepository catalogRepo) : IBudgetService
{
    public Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string userId, CancellationToken ct) => budgetRepo.GetBudgetsAsync(userId, ct);
    public async Task<OperationResult> UpsertBudgetAsync(string userId, string mt, decimal amount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mt)) return new OperationResult(false, "La sección es requerida.");
        if (amount < 0) return new OperationResult(false, "El presupuesto no puede ser negativo.");
        var c = await catalogRepo.GetCatalogsAsync(userId, ct);
        if (!c.MovementTypes.Any(m => string.Equals(m.Trim(), mt.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "La sección no existe.");
        return await budgetRepo.UpsertBudgetAsync(userId, mt, amount, ct);
    }
    public Task<OperationResult> DeleteBudgetAsync(string userId, string mt, CancellationToken ct) => budgetRepo.DeleteBudgetAsync(userId, mt, ct);
}
