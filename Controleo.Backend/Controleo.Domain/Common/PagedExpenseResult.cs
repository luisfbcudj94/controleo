using Controleo.Domain.Entities;
namespace Controleo.Domain.Common;
public sealed record PagedExpenseResult(IReadOnlyList<ExpenseItem> Items, int PageNumber, int PageSize, int TotalCount, decimal TotalAmount, int TotalPages, bool HasPreviousPage, bool HasNextPage);
