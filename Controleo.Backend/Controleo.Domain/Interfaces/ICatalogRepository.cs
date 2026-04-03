using Controleo.Domain.Common;
using Controleo.Domain.Entities;
namespace Controleo.Domain.Interfaces;
public interface ICatalogRepository
{
    Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken ct);
    Task<OperationResult> UpdateCatalogsAsync(
        string userId,
        IReadOnlyList<string> movementTypes,
        IReadOnlyList<string> paymentMethods,
        IReadOnlyList<MovementTypeConfig>? movementTypeConfigs,
        IReadOnlyList<PaymentMethodConfig>? paymentMethodConfigs,
        CancellationToken ct
    );
}
