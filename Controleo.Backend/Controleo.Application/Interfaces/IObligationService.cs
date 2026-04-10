using Controleo.Application.DTOs;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;

namespace Controleo.Application.Interfaces;

public interface IObligationService
{
    Task<IReadOnlyList<ObligationItem>> GetObligationsAsync(string userId, CancellationToken ct);
    Task<PagedObligationResult> GetObligationsPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct);
    Task<OperationResult> UpsertObligationAsync(string userId, string? id, ObligationUpsertRequest request, CancellationToken ct);
    Task<OperationResult> DeleteObligationAsync(string userId, string id, CancellationToken ct);
}