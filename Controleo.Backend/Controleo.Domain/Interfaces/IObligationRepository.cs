using Controleo.Domain.Common;
using Controleo.Domain.Entities;

namespace Controleo.Domain.Interfaces;

public interface IObligationRepository
{
    Task<IReadOnlyList<ObligationItem>> GetObligationsAsync(string userId, CancellationToken ct);
    Task<PagedObligationResult> GetObligationsPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct);
    Task<OperationResult> UpsertObligationAsync(string userId, string? id, ObligationUpsertData data, CancellationToken ct);
    Task<OperationResult> DeleteObligationAsync(string userId, string id, CancellationToken ct);
}