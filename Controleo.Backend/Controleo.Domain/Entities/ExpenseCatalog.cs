namespace Controleo.Domain.Entities;
public sealed record ExpenseCatalog(
	IReadOnlyList<string> MovementTypes,
	IReadOnlyList<string> PaymentMethods,
	IReadOnlyList<MovementTypeConfig>? MovementTypeConfigs = null,
	IReadOnlyList<PaymentMethodConfig>? PaymentMethodConfigs = null
);
