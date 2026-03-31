using Controleo.Application.DTOs;
namespace Controleo.Application.Validation;
public static class ExpenseValidator
{
    public static Dictionary<string, string[]> Validate(ExpenseEntryRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (r.Date == default) e["date"] = ["La fecha es requerida."];
        if (string.IsNullOrWhiteSpace(r.Description)) e["description"] = ["La descripción es requerida."];
        if (r.Amount == 0) e["amount"] = ["El valor debe ser diferente de cero."];
        if (string.IsNullOrWhiteSpace(r.MovementType)) e["movementType"] = ["El tipo de movimiento es requerido."];
        if (string.IsNullOrWhiteSpace(r.PaymentMethod)) e["paymentMethod"] = ["El medio de pago es requerido."];
        return e;
    }
}
