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
        if (r.Installments is not null && (r.Installments < 1 || r.Installments > 120))
        {
            e["installments"] = ["Las cuotas deben estar entre 1 y 120."];
        }
        if (!string.IsNullOrWhiteSpace(r.ClientMutationId) && r.ClientMutationId.Length > 128)
        {
            e["clientMutationId"] = ["El identificador de mutación no puede superar 128 caracteres."];
        }
        return e;
    }
}
