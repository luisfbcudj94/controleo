namespace Controleo.Mobile.Core.Models;

public sealed record PagedExpenseResult(
    IReadOnlyList<ExpenseItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    decimal TotalAmount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage
);
