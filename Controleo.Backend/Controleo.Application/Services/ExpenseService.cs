using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class ExpenseService(IExpenseRepository expenseRepo, ICatalogRepository catalogRepo) : IExpenseService
{
    public async Task<SaveExpenseResult> SaveExpenseAsync(string userId, ExpenseEntryRequest r, CancellationToken ct)
    {
        var c = await catalogRepo.GetCatalogsAsync(userId, ct);
        if (!c.MovementTypes.Any(m => string.Equals(m.Trim(), r.MovementType.Trim(), StringComparison.OrdinalIgnoreCase))) return new SaveExpenseResult(false, "Tipo de movimiento no permitido.", 0);
        if (!c.PaymentMethods.Any(m => string.Equals(m.Trim(), r.PaymentMethod.Trim(), StringComparison.OrdinalIgnoreCase))) return new SaveExpenseResult(false, "Medio de pago no permitido.", 0);
        var (isCredit, installments) = ResolveCreditSettings(c, r);
        return await expenseRepo.SaveAsync(userId, r.Description, r.Amount, r.Date, r.MovementType, r.PaymentMethod, isCredit, installments, r.ClientMutationId, ct);
    }
    public Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string mk, CancellationToken ct) => expenseRepo.GetExpensesAsync(userId, mk, ct);
    public Task<PagedExpenseResult> GetExpensesPageAsync(string userId, string mk, int pn, int ps, string? mt, string? pm, string? st, CancellationToken ct) => expenseRepo.GetExpensesPageAsync(userId, mk, pn, ps, mt, pm, st, ct);
    public Task<IReadOnlyList<string>> GetAvailableMonthsAsync(string userId, CancellationToken ct) => expenseRepo.GetAvailableMonthKeysAsync(userId, ct);
    public async Task<OperationResult> UpdateExpenseAsync(string userId, string id, ExpenseEntryRequest r, CancellationToken ct)
    {
        var c = await catalogRepo.GetCatalogsAsync(userId, ct);
        if (!c.MovementTypes.Any(m => string.Equals(m.Trim(), r.MovementType.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "Tipo de movimiento no permitido.");
        if (!c.PaymentMethods.Any(m => string.Equals(m.Trim(), r.PaymentMethod.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "Medio de pago no permitido.");
        var (isCredit, installments) = ResolveCreditSettings(c, r);
        return await expenseRepo.UpdateExpenseAsync(userId, id, r.Description, r.Amount, r.Date, r.MovementType, r.PaymentMethod, isCredit, installments, ct);
    }
    public Task<OperationResult> DeleteExpenseAsync(string userId, string id, CancellationToken ct) => expenseRepo.DeleteExpenseAsync(userId, id, ct);
    public Task<int> CountByMovementTypeAsync(string userId, string mt, CancellationToken ct) => expenseRepo.CountExpensesByMovementTypeAsync(userId, mt, ct);
    public Task<OperationResult> DeleteAllByMovementTypeAsync(string userId, string mt, CancellationToken ct) => expenseRepo.DeleteAllByMovementTypeAsync(userId, mt, ct);

    private static (bool IsCredit, int? Installments) ResolveCreditSettings(ExpenseCatalog catalog, ExpenseEntryRequest request)
    {
        var methodConfig = catalog.PaymentMethodConfigs?
            .FirstOrDefault(cfg => string.Equals(cfg.Name, request.PaymentMethod, StringComparison.OrdinalIgnoreCase));

        var isCredit = request.IsCredit ?? methodConfig?.IsCredit ?? false;
        var installments = NormalizeInstallments(request.Installments);

        if (installments is > 1 && !isCredit)
        {
            isCredit = true;
        }

        if (!isCredit)
        {
            return (false, null);
        }

        installments ??= NormalizeInstallments(methodConfig?.DefaultInstallments);
        return (true, installments);
    }

    private static int? NormalizeInstallments(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return Math.Clamp(value.Value, 1, 120);
    }
}
