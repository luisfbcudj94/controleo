using System.Net.Http.Json;
using System.Collections.Concurrent;
using Controleo.Mobile.Models;

namespace Controleo.Mobile.Services;

public sealed class ExpenseApiClient(HttpClient httpClient)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<ExpenseItem>>> _expensesCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<DashboardCategoryItem>>> _dashboardCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<BudgetItem>>> _budgetsCache = new();

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

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string monthKey, CancellationToken cancellationToken)
    {
        if (TryGetCache(_expensesCache, monthKey, out var cachedData))
        {
            return cachedData;
        }

        try
        {
            var data = await httpClient.GetFromJsonAsync<List<ExpenseItem>>($"api/expenses?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<ExpenseItem>)(data ?? []);
            _expensesCache[monthKey] = new CacheEntry<IReadOnlyList<ExpenseItem>>(DateTimeOffset.UtcNow, result);
            return result;
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
                _expensesCache.TryRemove(request.Date.ToString("yyyy-MM"), out _);
                _dashboardCache.TryRemove(request.Date.ToString("yyyy-MM"), out _);
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
                _expensesCache.Clear();
                _dashboardCache.Clear();
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
                _expensesCache.Clear();
                _dashboardCache.Clear();
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

    public async Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string monthKey, CancellationToken cancellationToken)
    {
        if (TryGetCache(_dashboardCache, monthKey, out var cachedData))
        {
            return cachedData;
        }

        try
        {
            var data = await httpClient.GetFromJsonAsync<List<DashboardCategoryItem>>($"api/dashboard/by-category?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<DashboardCategoryItem>)(data ?? []);
            _dashboardCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardCategoryItem>>(DateTimeOffset.UtcNow, result);
            return result;
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string monthKey, CancellationToken cancellationToken)
    {
        if (TryGetCache(_budgetsCache, monthKey, out var cachedData))
        {
            return cachedData;
        }

        try
        {
            var data = await httpClient.GetFromJsonAsync<List<BudgetItem>>($"api/budgets?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<BudgetItem>)(data ?? []);
            _budgetsCache[monthKey] = new CacheEntry<IReadOnlyList<BudgetItem>>(DateTimeOffset.UtcNow, result);
            return result;
        }
        catch
        {
            return [];
        }
    }

    public async Task<OperationResult> UpsertBudgetAsync(string movementType, decimal amount, string monthKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.PutAsJsonAsync($"api/budgets/{Uri.EscapeDataString(movementType)}?month={Uri.EscapeDataString(monthKey)}", new BudgetUpsertRequest(amount), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                _budgetsCache.TryRemove(monthKey, out _);
                _dashboardCache.TryRemove(monthKey, out _);
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

    public async Task<OperationResult> DeleteBudgetAsync(string movementType, string monthKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/budgets/{Uri.EscapeDataString(movementType)}?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                _budgetsCache.TryRemove(monthKey, out _);
                _dashboardCache.TryRemove(monthKey, out _);
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

    private static bool TryGetCache<T>(ConcurrentDictionary<string, CacheEntry<T>> cache, string key, out T data)
    {
        if (cache.TryGetValue(key, out var entry) && DateTimeOffset.UtcNow - entry.CreatedAt <= CacheTtl)
        {
            data = entry.Data;
            return true;
        }

        data = default!;
        return false;
    }

    private sealed record CacheEntry<T>(DateTimeOffset CreatedAt, T Data);
}
