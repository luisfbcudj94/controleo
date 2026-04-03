namespace Controleo.Mobile.Core.Models;

public sealed record PagedRecurringResult(
    IReadOnlyList<RecurringExpenseItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage
);
