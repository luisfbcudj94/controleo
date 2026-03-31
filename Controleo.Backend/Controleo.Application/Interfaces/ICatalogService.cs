using Controleo.Application.DTOs;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Application.Interfaces;
public interface ICatalogService
{
    Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpdateCatalogsAsync(string userId, UpdateCatalogsRequest request, CancellationToken ct);
}
