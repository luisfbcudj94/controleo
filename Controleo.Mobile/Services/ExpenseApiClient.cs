using System.Net.Http.Json;
using Controleo.Mobile.Models;

namespace Controleo.Mobile.Services;

public sealed class ExpenseApiClient(HttpClient httpClient)
{
    private static readonly ExpenseCatalog FallbackCatalog = new(
    [
        "Basicos para vivir",
        "Hogar",
        "Salidas",
        "Imprevistos",
        "Suscripciones",
        "Deudas",
        "-",
        "Prestamo",
        "Bienestar",
        "Viajes",
        "Sogamoso obra"
    ],
    [
        "TC Black",
        "TC Rappi",
        "TD Bancolombia",
        "TC Nu",
        "Efectivo",
        "Transferencia",
        "-",
        "Nequi"
    ]);

    public async Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await httpClient.GetFromJsonAsync<ExpenseCatalog>("api/catalogs", cancellationToken);
            return catalog ?? FallbackCatalog;
        }
        catch
        {
            return FallbackCatalog;
        }
    }

    public async Task<SaveExpenseResult> SaveExpenseAsync(ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/expenses", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SaveExpenseResult>(cancellationToken: cancellationToken);
                return result ?? new SaveExpenseResult(false, "Respuesta inválida del servidor.", 0);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new SaveExpenseResult(false, $"Error API {response.StatusCode}: {body}", 0);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"No fue posible conectar con API: {ex.Message}", 0);
        }
    }
}
