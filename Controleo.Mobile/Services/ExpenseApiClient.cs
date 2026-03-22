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

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var data = await httpClient.GetFromJsonAsync<List<ExpenseItem>>("api/expenses", cancellationToken);
            return data ?? [];
        }
        catch
        {
            return [];
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

    public async Task<OperationResult> UpdateExpenseAsync(string id, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PutAsJsonAsync($"api/expenses/{id}", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(false, "Respuesta inválida del servidor.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new OperationResult(false, $"Error API {response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteExpenseAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/expenses/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Gasto eliminado correctamente.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new OperationResult(false, $"Error API {response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var data = await httpClient.GetFromJsonAsync<List<DashboardCategoryItem>>("api/dashboard/by-category", cancellationToken);
            return data ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var data = await httpClient.GetFromJsonAsync<List<BudgetItem>>("api/budgets", cancellationToken);
            return data ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<OperationResult> UpsertBudgetAsync(string movementType, decimal amount, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PutAsJsonAsync($"api/budgets/{Uri.EscapeDataString(movementType)}", new BudgetUpsertRequest(amount), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Presupuesto guardado correctamente.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new OperationResult(false, $"Error API {response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteBudgetAsync(string movementType, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/budgets/{Uri.EscapeDataString(movementType)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Presupuesto eliminado correctamente.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new OperationResult(false, $"Error API {response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> UpdateCatalogsAsync(UpdateCatalogsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PutAsJsonAsync("api/catalogs", request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Catálogos actualizados correctamente.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new OperationResult(false, $"Error API {response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }
}
