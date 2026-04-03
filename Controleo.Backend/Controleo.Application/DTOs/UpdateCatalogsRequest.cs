using Controleo.Domain.Entities;
namespace Controleo.Application.DTOs;
public sealed record UpdateCatalogsRequest(
	IReadOnlyList<string> MovementTypes,
	IReadOnlyList<string> PaymentMethods,
	IReadOnlyList<MovementTypeConfig>? MovementTypeConfigs = null,
	IReadOnlyList<PaymentMethodConfig>? PaymentMethodConfigs = null
);
