namespace Controleo.Mobile.Core.Models;

public sealed record SaveExpenseResult(bool IsSuccess, string Message, int RowNumber, string? ExpenseId = null);
