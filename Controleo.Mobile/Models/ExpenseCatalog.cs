namespace Controleo.Mobile.Models;

public sealed record ExpenseCatalog(
    IReadOnlyList<string> MovementTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<MovementTypeConfig>? MovementTypeConfigs = null
);

public sealed record MovementTypeConfig(
    string Name,
    string Icon,
    string Color
);
