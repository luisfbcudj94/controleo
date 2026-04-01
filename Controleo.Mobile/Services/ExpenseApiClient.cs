using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Controleo.Mobile.Interfaces;
using Controleo.Mobile.Models;

namespace Controleo.Mobile.Services;

public sealed class ExpenseApiClient(HttpClient httpClient, IAuthService authService) : IExpenseApiClient
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<ExpenseItem>>> _expensesCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<DashboardCategoryItem>>> _dashboardCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<DashboardPaymentMethodItem>>> _dashboardByPaymentMethodCache = new();
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return FallbackCatalog;
            }

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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

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
        string? paymentMethod,
        string? searchTerm,
        CancellationToken cancellationToken)
    {
        var resolvedPageNumber = Math.Max(1, pageNumber);
        var resolvedPageSize = pageSize is 10 or 20 ? pageSize : 5;
        var movementSegment = string.IsNullOrWhiteSpace(movementType)
            ? string.Empty
            : $"&movementType={Uri.EscapeDataString(movementType)}";
        var paymentMethodSegment = string.IsNullOrWhiteSpace(paymentMethod)
            ? string.Empty
            : $"&paymentMethod={Uri.EscapeDataString(paymentMethod)}";
        var searchSegment = string.IsNullOrWhiteSpace(searchTerm)
            ? string.Empty
            : $"&searchTerm={Uri.EscapeDataString(searchTerm)}";

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new PagedExpenseResult([], 1, resolvedPageSize, 0, 0m, 1, false, false);
            }

            var data = await httpClient.GetFromJsonAsync<PagedExpenseResult>(
                $"api/expenses/paged?month={Uri.EscapeDataString(monthKey)}&pageNumber={resolvedPageNumber}&pageSize={resolvedPageSize}{movementSegment}{paymentMethodSegment}{searchSegment}",
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new SaveExpenseResult(false, "Debes iniciar sesión antes de guardar gastos.", 0);
            }

            using var saveRequest = CreateExpenseJsonRequest(HttpMethod.Post, "api/expenses", request);
            var response = await httpClient.SendAsync(saveRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SaveExpenseResult>(cancellationToken: cancellationToken);
                _expensesCache.TryRemove(request.Date.ToString("yyyy-MM"), out _);
                _dashboardCache.TryRemove(request.Date.ToString("yyyy-MM"), out _);
                _dashboardByPaymentMethodCache.TryRemove(request.Date.ToString("yyyy-MM"), out _);
                _monthsCache = null;
                return result ?? new SaveExpenseResult(false, "Respuesta inválida del servidor.", 0);
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible guardar el gasto.", cancellationToken);
            return new SaveExpenseResult(false, message, 0);
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de actualizar gastos.");
            }

            using var updateRequest = CreateExpenseJsonRequest(HttpMethod.Put, $"api/expenses/{id}", request);
            var response = await httpClient.SendAsync(updateRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                _expensesCache.Clear();
                _dashboardCache.Clear();
                _dashboardByPaymentMethodCache.Clear();
                _monthsCache = null;
                return result ?? new OperationResult(false, "Respuesta inválida del servidor.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible actualizar el gasto.", cancellationToken);
            return new OperationResult(false, message);
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de eliminar gastos.");
            }

            var response = await httpClient.DeleteAsync($"api/expenses/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                _expensesCache.Clear();
                _dashboardCache.Clear();
                _dashboardByPaymentMethodCache.Clear();
                _monthsCache = null;
                return result ?? new OperationResult(true, "Gasto eliminado correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar el gasto.", cancellationToken);
            return new OperationResult(false, message);
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

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

    public async Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string monthKey, CancellationToken cancellationToken)
    {
        if (TryGetCache(_dashboardByPaymentMethodCache, monthKey, out var cachedData))
        {
            return cachedData;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

            var data = await httpClient.GetFromJsonAsync<List<DashboardPaymentMethodItem>>($"api/dashboard/by-payment-method?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<DashboardPaymentMethodItem>)(data ?? []);
            _dashboardByPaymentMethodCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardPaymentMethodItem>>(DateTimeOffset.UtcNow, result);
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de guardar presupuestos.");
            }

            using var upsertRequest = CreateBudgetJsonRequest($"api/budgets/{Uri.EscapeDataString(movementType)}", amount);
            var response = await httpClient.SendAsync(upsertRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                _budgetsCache.Clear();
                _dashboardCache.Clear();
                _dashboardByPaymentMethodCache.Clear();
                return result ?? new OperationResult(true, "Presupuesto guardado correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible guardar el presupuesto.", cancellationToken);
            return new OperationResult(false, message);
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de eliminar presupuestos.");
            }

            var response = await httpClient.DeleteAsync($"api/budgets/{Uri.EscapeDataString(movementType)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                _budgetsCache.Clear();
                _dashboardCache.Clear();
                _dashboardByPaymentMethodCache.Clear();
                return result ?? new OperationResult(true, "Presupuesto eliminado correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar el presupuesto.", cancellationToken);
            return new OperationResult(false, message);
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
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de actualizar catálogos.");
            }

            using var updateRequest = CreateCatalogsJsonRequest("api/catalogs", request);
            var response = await httpClient.SendAsync(updateRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Catálogos actualizados correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible actualizar catálogos.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<int> CountExpensesByMovementTypeAsync(string movementType, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return 0;
            var encoded = Uri.EscapeDataString(movementType);
            var data = await httpClient.GetFromJsonAsync<CountResult>($"api/expenses/count-by-type/{encoded}", cancellationToken);
            return data?.Count ?? 0;
        }
        catch { return 0; }
    }

    public async Task<OperationResult> DeleteAllByMovementTypeAsync(string movementType, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var encoded = Uri.EscapeDataString(movementType);
            var response = await httpClient.DeleteAsync($"api/movement-types/{encoded}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Tipo eliminado correctamente.");
            }
            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    private sealed record CountResult(int Count);

    public async Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

            var data = await httpClient.GetFromJsonAsync<List<RecurringExpenseItem>>("api/recurring-expenses", cancellationToken);
            return data ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<OperationResult> SaveRecurringExpenseAsync(string? id, RecurringExpenseUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de guardar recurrentes.");
            }

            var response = string.IsNullOrWhiteSpace(id)
                ? await httpClient.PostAsJsonAsync("api/recurring-expenses", request, cancellationToken)
                : await httpClient.PutAsJsonAsync($"api/recurring-expenses/{id}", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Gasto recurrente guardado correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible guardar el gasto recurrente.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteRecurringExpenseAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de eliminar recurrentes.");
            }

            var response = await httpClient.DeleteAsync($"api/recurring-expenses/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Gasto recurrente eliminado correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar el gasto recurrente.", cancellationToken);
            return new OperationResult(false, message);
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

    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
#if DEBUG
        // Local Functions allows anonymous dev-user; avoid stale/mismatched tokens hiding data.
        if (IsLocalDevelopmentApi())
        {
            httpClient.DefaultRequestHeaders.Authorization = null;
            return true;
        }
#endif

        var accessToken = await authService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        if (httpClient.DefaultRequestHeaders.Authorization?.Parameter != accessToken)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return true;
    }

    private bool IsLocalDevelopmentApi()
    {
        var host = httpClient.BaseAddress?.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return string.Equals(host, "10.0.2.2", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CacheEntry<T>(DateTimeOffset CreatedAt, T Data);

    private static async Task<string> ReadFriendlyApiErrorAsync(HttpResponseMessage response, string fallbackMessage, CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return "Tu sesión expiró. Inicia sesión nuevamente.";
        }

        try
        {
            var operationResult = await response.Content.ReadFromJsonAsync<ApiOperationResult>(cancellationToken: cancellationToken);
            if (operationResult is not null && !string.IsNullOrWhiteSpace(operationResult.Message))
            {
                return NormalizeApiMessage(operationResult.Message, statusCode);
            }
        }
        catch
        {
        }

        try
        {
            var validation = await response.Content.ReadFromJsonAsync<ApiValidationProblem>(cancellationToken: cancellationToken);
            var firstError = validation?.Errors?
                .SelectMany(pair => pair.Value ?? [])
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

            if (!string.IsNullOrWhiteSpace(firstError))
            {
                return firstError.Trim();
            }
        }
        catch
        {
        }

        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parsedFromRaw = TryParseMessageFromRaw(raw);
                if (!string.IsNullOrWhiteSpace(parsedFromRaw))
                {
                    return NormalizeApiMessage(parsedFromRaw, statusCode);
                }
            }
        }
        catch
        {
        }

        return $"{fallbackMessage} (código {statusCode}).";
    }

    private static string NormalizeApiMessage(string message, int statusCode)
    {
        var normalized = message.Trim();

        if (normalized.Contains("Request inválido", StringComparison.OrdinalIgnoreCase))
        {
            return "No pudimos procesar la información enviada. Revisa los datos e inténtalo de nuevo.";
        }

        if (normalized.Contains("Debes enviar secciones y medios de pago", StringComparison.OrdinalIgnoreCase))
        {
            return "Debes tener al menos una sección y un medio de pago antes de guardar.";
        }

        if (normalized.Contains("Tipo de movimiento no permitido", StringComparison.OrdinalIgnoreCase))
        {
            return "El tipo de movimiento no está permitido para tu cuenta. Actualiza catálogos e intenta otra vez.";
        }

        if (normalized.Contains("Medio de pago no permitido", StringComparison.OrdinalIgnoreCase))
        {
            return "El medio de pago no está permitido para tu cuenta. Actualiza catálogos e intenta otra vez.";
        }

        return statusCode >= 500
            ? "El servidor tuvo un problema al procesar la solicitud. Inténtalo de nuevo en unos segundos."
            : normalized;
    }

    private static string? TryParseMessageFromRaw(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "message", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }
                }
            }
        }
        catch
        {
        }

        return trimmed;
    }

    private sealed record ApiOperationResult(bool IsSuccess, string Message);

    private sealed class ApiValidationProblem
    {
        public Dictionary<string, string[]>? Errors { get; set; }
    }

    private static HttpRequestMessage CreateExpenseJsonRequest(HttpMethod method, string relativeUrl, ExpenseEntryRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["date"] = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["description"] = request.Description,
            ["amount"] = request.Amount,
            ["movementType"] = request.MovementType,
            ["paymentMethod"] = request.PaymentMethod
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpRequestMessage CreateBudgetJsonRequest(string relativeUrl, decimal amount)
    {
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = amount
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(HttpMethod.Put, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpRequestMessage CreateCatalogsJsonRequest(string relativeUrl, UpdateCatalogsRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["movementTypes"] = request.MovementTypes?.ToArray() ?? [],
            ["paymentMethods"] = request.PaymentMethods?.ToArray() ?? [],
            ["movementTypeConfigs"] = (request.MovementTypeConfigs ?? [])
                .Select(c => new Dictionary<string, string>
                {
                    ["name"] = c.Name,
                    ["icon"] = c.Icon,
                    ["color"] = c.Color
                })
                .ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(HttpMethod.Put, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
