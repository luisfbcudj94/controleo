namespace Controleo.Mobile.Core.Models;

public sealed record PagedObligationResult(
    IReadOnlyList<ObligationItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage
);