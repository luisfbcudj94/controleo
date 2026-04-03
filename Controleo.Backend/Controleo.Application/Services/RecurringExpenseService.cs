using System.Globalization;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class RecurringExpenseService(IRecurringExpenseRepository repo, ICatalogRepository catalogRepo, IExpenseRepository expenseRepo) : IRecurringExpenseService
{
    public Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken ct) => repo.GetRecurringExpensesAsync(userId, ct);
    public Task<PagedRecurringExpenseResult> GetRecurringExpensesPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct) => repo.GetRecurringExpensesPageAsync(userId, pageNumber, pageSize, ct);
    public async Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? id, RecurringExpenseUpsertRequest r, CancellationToken ct)
    {
        if (r.Amount < 0) return new OperationResult(false, "El valor no puede ser negativo.");
        if (r.DayOfMonth is < 1 or > 31) return new OperationResult(false, "El día debe estar entre 1 y 31.");
        var sm = ResolveMonth(r.StartMonth, r.StartDate);
        if (sm is null) return new OperationResult(false, "El mes de inicio es inválido.");
        var c = await catalogRepo.GetCatalogsAsync(userId, ct);
        if (!c.MovementTypes.Any(m => string.Equals(m.Trim(), r.MovementType.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "Tipo de movimiento no permitido.");
        if (!c.PaymentMethods.Any(m => string.Equals(m.Trim(), r.PaymentMethod.Trim(), StringComparison.OrdinalIgnoreCase))) return new OperationResult(false, "Medio de pago no permitido.");
        var result = await repo.UpsertRecurringExpenseAsync(userId, id, r.Description, r.Amount, r.MovementType, r.PaymentMethod, r.DayOfMonth, sm, r.IsActive, ct);
        if (!result.IsSuccess) return result;

        try
        {
            var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            await expenseRepo.GetExpensesAsync(userId, currentMonth, ct);
        }
        catch
        {
        }

        return result;
    }
    public Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string id, CancellationToken ct) => repo.DeleteRecurringExpenseAsync(userId, id, ct);
    private static string? ResolveMonth(string? mk, string? dt)
    {
        if (!string.IsNullOrWhiteSpace(mk))
        {
            var monthToken = mk.Trim();
            if (DateOnly.TryParseExact($"{monthToken}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return monthToken;
            }

            if (monthToken.Length >= 7)
            {
                var shortToken = monthToken[..7];
                if (DateOnly.TryParseExact($"{shortToken}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    return shortToken;
                }
            }

            if (DateOnly.TryParse(monthToken, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth))
            {
                return parsedMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            }
        }

        if (!string.IsNullOrWhiteSpace(dt))
        {
            var dateToken = dt.Trim();
            if (DateOnly.TryParseExact(dateToken, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
            {
                return exactDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            }

            if (DateOnly.TryParse(dateToken, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return parsedDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(dateToken, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDateTime))
            {
                return parsedDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }
}
