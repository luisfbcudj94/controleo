using System.Globalization;
using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;

namespace Controleo.Application.Services;

public sealed class ObligationService(IObligationRepository repo, ICatalogRepository catalogRepo) : IObligationService
{
    public Task<IReadOnlyList<ObligationItem>> GetObligationsAsync(string userId, CancellationToken ct)
        => repo.GetObligationsAsync(userId, ct);

    public Task<PagedObligationResult> GetObligationsPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct)
        => repo.GetObligationsPageAsync(userId, pageNumber, pageSize, ct);

    public async Task<OperationResult> UpsertObligationAsync(string userId, string? id, ObligationUpsertRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return new OperationResult(false, "La descripción es requerida.");
        if (request.MonthlyPayment <= 0)
            return new OperationResult(false, "La cuota mensual debe ser mayor a cero.");
        if (request.DueDayOfMonth is < 1 or > 31)
            return new OperationResult(false, "El día de cobro debe estar entre 1 y 31.");
        if (request.ReminderDaysBefore is < 0 or > 30)
            return new OperationResult(false, "El recordatorio debe estar entre 0 y 30 días.");

        var startMonth = ResolveMonth(request.StartMonth);
        if (startMonth is null)
            return new OperationResult(false, "El mes de inicio es inválido.");

        var movementType = request.MovementType?.Trim() ?? string.Empty;
        var paymentMethod = request.PaymentMethod?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(movementType))
            return new OperationResult(false, "El tipo de movimiento es requerido.");
        if (string.IsNullOrWhiteSpace(paymentMethod))
            return new OperationResult(false, "El medio de pago es requerido.");

        var catalogs = await catalogRepo.GetCatalogsAsync(userId, ct);
        if (!catalogs.MovementTypes.Any(m => string.Equals(m.Trim(), movementType, StringComparison.OrdinalIgnoreCase)))
            return new OperationResult(false, "Tipo de movimiento no permitido.");
        if (!catalogs.PaymentMethods.Any(m => string.Equals(m.Trim(), paymentMethod, StringComparison.OrdinalIgnoreCase)))
            return new OperationResult(false, "Medio de pago no permitido.");

        var data = new ObligationUpsertData(
            request.Description.Trim(),
            movementType,
            paymentMethod,
            request.DueDayOfMonth,
            request.MonthlyPayment,
            request.ReminderDaysBefore,
            startMonth,
            request.IsActive);

        return await repo.UpsertObligationAsync(userId, id, data, ct);
    }

    public Task<OperationResult> DeleteObligationAsync(string userId, string id, CancellationToken ct)
        => repo.DeleteObligationAsync(userId, id, ct);

    private static string? ResolveMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month))
            return DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var token = month.Trim();
        if (DateOnly.TryParseExact($"{token}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        if (DateOnly.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        return null;
    }
}