using Controleo.Api.Models;

namespace Controleo.Api.Services;

public interface IExpenseStorageService
{
    Task<SaveExpenseResult> SaveAsync(ExpenseEntryRequest request, CancellationToken cancellationToken);
}
