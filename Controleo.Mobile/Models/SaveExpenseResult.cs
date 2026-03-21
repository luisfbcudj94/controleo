namespace Controleo.Mobile.Models;

public sealed record SaveExpenseResult(bool IsSuccess, string Message, int RowNumber);
