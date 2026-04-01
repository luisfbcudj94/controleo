namespace Controleo.Mobile.Core.Models;

public sealed record UpdateCatalogsRequest(
    IReadOnlyList<string> MovementTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<MovementTypeConfig>? MovementTypeConfigs = null
);
