using Controleo.Application.DTOs;
using Controleo.Application.Interfaces;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
namespace Controleo.Application.Services;
public sealed class CatalogService(ICatalogRepository repo) : ICatalogService
{
    public Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken ct) => repo.GetCatalogsAsync(userId, ct);
    public Task<OperationResult> UpdateCatalogsAsync(string userId, UpdateCatalogsRequest r, CancellationToken ct)
    {
        if (r.MovementTypes is null or { Count: 0 }) return Task.FromResult(new OperationResult(false, "Debe existir al menos una sección de gasto."));
        if (r.PaymentMethods is null or { Count: 0 }) return Task.FromResult(new OperationResult(false, "Debe existir al menos un medio de pago."));
        return repo.UpdateCatalogsAsync(userId, r.MovementTypes, r.PaymentMethods, r.MovementTypeConfigs, ct);
    }
}
