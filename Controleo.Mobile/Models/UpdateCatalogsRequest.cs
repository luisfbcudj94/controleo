namespace Controleo.Mobile.Models;

public sealed record UpdateCatalogsRequest(
    IReadOnlyList<string> MovementTypes,
    IReadOnlyList<string> PaymentMethods
);
