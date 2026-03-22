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
    private CacheEntry<IReadOnlyList<string>>? _monthsCache;

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

    public async Task<PagedExpenseResult> GetExpensesPageAsync(
        string monthKey,
        int pageNumber,
        int pageSize,
        string? movementType,
        CancellationToken cancellationToken)
    {
        var resolvedPageNumber = Math.Max(1, pageNumber);
        var resolvedPageSize = pageSize is 10 or 20 ? pageSize : 5;
        var movementSegment = string.IsNullOrWhiteSpace(movementType)
            ? string.Empty
            : $"&movementType={Uri.EscapeDataString(movementType)}";

        try
        {
            var data = await httpClient.GetFromJsonAsync<PagedExpenseResult>(
                $"api/expenses/paged?month={Uri.EscapeDataString(monthKey)}&pageNumber={resolvedPageNumber}&pageSize={resolvedPageSize}{movementSegment}",
                cancellationToken);

            return data ?? new PagedExpenseResult([], 1, resolvedPageSize, 0, 0m, 1, false, false);
        }
        catch
        {
            return new PagedExpenseResult([], 1, resolvedPageSize, 0, 0m, 1, false, false);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableMonthsAsync(CancellationToken cancellationToken)
    {
        if (_monthsCache is not null && DateTimeOffset.UtcNow - _monthsCache.CreatedAt <= CacheTtl)
        {
            return _monthsCache.Data;
        }

        try
        {
            var data = await httpClient.GetFromJsonAsync<List<string>>("api/expenses/months", cancellationToken);
            var result = (IReadOnlyList<string>)(data ?? []);
            _monthsCache = new CacheEntry<IReadOnlyList<string>>(DateTimeOffset.UtcNow, result);
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
                _monthsCache = null;
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
                _monthsCache = null;
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
                _monthsCache = null;
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

    public async Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "global";
        if (TryGetCache(_budgetsCache, cacheKey, out var cachedData))
        {
            return cachedData;
        }

        try
        {
            var data = await httpClient.GetFromJsonAsync<List<BudgetItem>>("api/budgets", cancellationToken);
            var result = (IReadOnlyList<BudgetItem>)(data ?? []);
            _budgetsCache[cacheKey] = new CacheEntry<IReadOnlyList<BudgetItem>>(DateTimeOffset.UtcNow, result);
            return result;
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
                _budgetsCache.Clear();
                _dashboardCache.Clear();
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
                _budgetsCache.Clear();
                _dashboardCache.Clear();
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
