namespace Controleo.Api.Models;

public sealed record SaveExpenseResult(bool IsSuccess, string Message, int RowNumber);
