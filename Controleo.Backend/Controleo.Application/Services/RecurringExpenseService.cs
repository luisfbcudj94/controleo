using System.Globalization;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class RecurringExpenseService(IRecurringExpenseRepository repo, ICatalogRepository catalogRepo) : IRecurringExpenseService
{
    public Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken ct) => repo.GetRecurringExpensesAsync(userId, ct);
    public async Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? id, RecurringExpenseUpsertRequest r, CancellationToken ct)
    {
        if (r.Amount < 0) return new OperationResult(false, "El valor no puede ser negativo.");
        if (r.DayOfMonth is < 1 or > 31) return new OperationResult(false, "El día debe estar entre 1 y 31.");
        var sm = ResolveMonth(r.StartMonth, r.StartDate);
        if (sm is null) return new OperationResult(false, "El mes de inicio es inválido.");
        var c = await catalogRepo.GetCatalogsAsync(userId, ct);
        if (!c.MovementTypes.Any(m => string.Equals(m.Trim(), r.MovementType.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "Tipo de movimiento no permitido.");
        if (!c.PaymentMethods.Any(m => string.Equals(m.Trim(), r.PaymentMethod.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "Medio de pago no permitido.");
        return await repo.UpsertRecurringExpenseAsync(userId, id, r.Description, r.Amount, r.MovementType, r.PaymentMethod, r.DayOfMonth, sm, r.IsActive, ct);
    }
    public Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string id, CancellationToken ct) => repo.DeleteRecurringExpenseAsync(userId, id, ct);
    private static string? ResolveMonth(string? mk, string? dt)
    {
        if (!string.IsNullOrWhiteSpace(mk) && DateOnly.TryParseExact($"{mk}-01", "yyyy-MM-dd", null, DateTimeStyles.None, out _)) return mk.Trim();
        if (DateOnly.TryParse(dt, out var d)) return d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return null;
    }
}
