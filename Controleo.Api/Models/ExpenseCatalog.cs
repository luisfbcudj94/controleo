namespace Controleo.Api.Models;

public sealed record ExpenseCatalog(IReadOnlyList<string> MovementTypes, IReadOnlyList<string> PaymentMethods);
