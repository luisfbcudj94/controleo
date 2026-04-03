namespace Controleo.Mobile.Core.Models;

public sealed record ExpenseCatalog(
    IReadOnlyList<string> MovementTypes,
    IReadOnlyList<string> PaymentMethods,
    IReadOnlyList<MovementTypeConfig>? MovementTypeConfigs = null,
    IReadOnlyList<PaymentMethodConfig>? PaymentMethodConfigs = null
);

public sealed record MovementTypeConfig(
    string Name,
    string Icon,
    string Color
);

public sealed record PaymentMethodConfig(
    string Name,
    string Icon
);
