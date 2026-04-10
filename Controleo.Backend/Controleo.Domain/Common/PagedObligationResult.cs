using Controleo.Domain.Entities;

namespace Controleo.Domain.Common;

public sealed record PagedObligationResult(
    IReadOnlyList<ObligationItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);