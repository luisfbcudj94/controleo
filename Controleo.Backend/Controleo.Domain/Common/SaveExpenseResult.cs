namespace Controleo.Domain.Common;
public sealed record SaveExpenseResult(bool IsSuccess, string Message, int RowNumber, string? ExpenseId = null);
