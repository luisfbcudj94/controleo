using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Services;

public sealed class ExpenseApiClient : IExpenseApiClient
{
    private readonly HttpClient httpClient;
    private readonly IAuthService authService;
    private readonly IConnectivityService connectivityService;
    private readonly IOfflineDataStore offlineDataStore;
    private readonly IOfflineSyncService offlineSyncService;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim offlineInitLock = new(1, 1);
    private bool offlineInitialized;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);
    private const string PremiumOfflineMessage = "El modo offline esta disponible solo para usuarios premium.";

    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<ExpenseItem>>> _expensesCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<DashboardCategoryItem>>> _dashboardCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<DashboardPaymentMethodItem>>> _dashboardByPaymentMethodCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<BudgetItem>>> _budgetsCache = new();
    private CacheEntry<IReadOnlyList<string>>? _monthsCache;

    public ExpenseApiClient(
        HttpClient httpClient,
        IAuthService authService,
        IConnectivityService connectivityService,
        IOfflineDataStore offlineDataStore,
        IOfflineSyncService offlineSyncService)
    {
        this.httpClient = httpClient;
        this.authService = authService;
        this.connectivityService = connectivityService;
        this.offlineDataStore = offlineDataStore;
        this.offlineSyncService = offlineSyncService;
        this.authService.SessionCleared += ClearAllCaches;
    }

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

    public void ClearAllCaches()
    {
        InvalidateExpenseAndDashboardCaches();
        _budgetsCache.Clear();
    }

    public async Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken)
    {
        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();
        var canUseOffline = CanUseOfflineData(userId);

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return await GetOfflineCatalogOrFallbackAsync(userId, canUseOffline, cancellationToken);
            }

            var catalog = await httpClient.GetFromJsonAsync<ExpenseCatalog>("api/catalogs", cancellationToken);
            var result = catalog ?? FallbackCatalog;

            if (canUseOffline)
            {
                await offlineDataStore.SaveCatalogAsync(userId!, result, cancellationToken);
            }

            return result;
        }
        catch
        {
            return await GetOfflineCatalogOrFallbackAsync(userId, canUseOffline, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string monthKey, CancellationToken cancellationToken)
    {
        if (TryGetCache(_expensesCache, monthKey, out var cachedData))
        {
            return cachedData;
        }

        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();
        var canUseOffline = CanUseOfflineData(userId);

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return await GetOfflineExpensesOrEmptyAsync(userId, monthKey, canUseOffline, cancellationToken);
            }

            var data = await httpClient.GetFromJsonAsync<List<ExpenseItem>>($"api/expenses?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<ExpenseItem>)(data ?? []);
            _expensesCache[monthKey] = new CacheEntry<IReadOnlyList<ExpenseItem>>(DateTimeOffset.UtcNow, result);

            if (canUseOffline)
            {
                await offlineDataStore.MergeExpensesAsync(userId!, monthKey, result, cancellationToken);
                await offlineDataStore.AddAvailableMonthAsync(userId!, monthKey, cancellationToken);
            }

            return result;
        }
        catch
        {
            return await GetOfflineExpensesOrEmptyAsync(userId, monthKey, canUseOffline, cancellationToken);
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

        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();
        var canUseOffline = CanUseOfflineData(userId);

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return await BuildOfflineExpensePageAsync(
                    userId,
                    monthKey,
                    resolvedPageNumber,
                    resolvedPageSize,
                    movementType,
                    paymentMethod,
                    searchTerm,
                    canUseOffline,
                    cancellationToken);
            }

            var data = await httpClient.GetFromJsonAsync<PagedExpenseResult>(
                $"api/expenses/paged?month={Uri.EscapeDataString(monthKey)}&pageNumber={resolvedPageNumber}&pageSize={resolvedPageSize}{movementSegment}{paymentMethodSegment}{searchSegment}",
                cancellationToken);

            var result = data ?? new PagedExpenseResult([], 1, resolvedPageSize, 0, 0m, 1, false, false);
            if (canUseOffline)
            {
                await offlineDataStore.MergeExpensesAsync(userId!, monthKey, result.Items, cancellationToken);
                await offlineDataStore.AddAvailableMonthAsync(userId!, monthKey, cancellationToken);
            }

            return result;
        }
        catch
        {
            return await BuildOfflineExpensePageAsync(
                userId,
                monthKey,
                resolvedPageNumber,
                resolvedPageSize,
                movementType,
                paymentMethod,
                searchTerm,
                canUseOffline,
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableMonthsAsync(CancellationToken cancellationToken)
    {
        if (_monthsCache is not null && DateTimeOffset.UtcNow - _monthsCache.CreatedAt <= CacheTtl)
        {
            return _monthsCache.Data;
        }

        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();
        var canUseOffline = CanUseOfflineData(userId);

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return await GetOfflineMonthsOrEmptyAsync(userId, canUseOffline, cancellationToken);
            }

            var data = await httpClient.GetFromJsonAsync<List<string>>("api/expenses/months", cancellationToken);
            var result = (IReadOnlyList<string>)(data ?? []);
            _monthsCache = new CacheEntry<IReadOnlyList<string>>(DateTimeOffset.UtcNow, result);

            if (canUseOffline)
            {
                await offlineDataStore.SaveAvailableMonthsAsync(userId!, result, cancellationToken);
            }

            return result;
        }
        catch
        {
            return await GetOfflineMonthsOrEmptyAsync(userId, canUseOffline, cancellationToken);
        }
    }

    public async Task<SaveExpenseResult> SaveExpenseAsync(ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new SaveExpenseResult(false, "Debes iniciar sesión antes de guardar gastos.", 0);
        }

        if (!connectivityService.IsOnline)
        {
            if (!CanUseOfflineData(userId))
            {
                return new SaveExpenseResult(false, PremiumOfflineMessage, 0);
            }

            return await SaveExpenseOfflineAsync(userId, request, cancellationToken);
        }

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

                // Reconnection might have happened recently; proactively flush pending queue.
                await offlineSyncService.TriggerSyncAsync(cancellationToken);

                return result ?? new SaveExpenseResult(false, "Respuesta inválida del servidor.", 0);
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible guardar el gasto.", cancellationToken);
            return new SaveExpenseResult(false, message, 0);
        }
        catch (HttpRequestException)
        {
            if (!CanUseOfflineData(userId))
            {
                return new SaveExpenseResult(false, "No fue posible conectar con API.", 0);
            }

            return await SaveExpenseOfflineAsync(userId, request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!CanUseOfflineData(userId))
            {
                return new SaveExpenseResult(false, "No fue posible conectar con API.", 0);
            }

            return await SaveExpenseOfflineAsync(userId, request, cancellationToken);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"No fue posible conectar con API: {ex.Message}", 0);
        }
    }

    public async Task<OperationResult> UpdateExpenseAsync(string id, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new OperationResult(false, "Debes iniciar sesión antes de actualizar gastos.");
        }

        if (!connectivityService.IsOnline)
        {
            if (!CanUseOfflineData(userId))
            {
                return new OperationResult(false, PremiumOfflineMessage);
            }

            return await UpdateExpenseOfflineAsync(userId, id, request, cancellationToken);
        }

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

                await offlineSyncService.TriggerSyncAsync(cancellationToken);

                return result ?? new OperationResult(false, "Respuesta inválida del servidor.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible actualizar el gasto.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (HttpRequestException)
        {
            if (!CanUseOfflineData(userId))
            {
                return new OperationResult(false, "No fue posible conectar con API.");
            }

            return await UpdateExpenseOfflineAsync(userId, id, request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!CanUseOfflineData(userId))
            {
                return new OperationResult(false, "No fue posible conectar con API.");
            }

            return await UpdateExpenseOfflineAsync(userId, id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteExpenseAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new OperationResult(false, "Debes iniciar sesión antes de eliminar gastos.");
        }

        if (!connectivityService.IsOnline)
        {
            if (!CanUseOfflineData(userId))
            {
                return new OperationResult(false, PremiumOfflineMessage);
            }

            return await DeleteExpenseOfflineAsync(userId, id, cancellationToken);
        }

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

                await offlineDataStore.RemoveExpenseAcrossMonthsAsync(userId, id, cancellationToken);

                var monthKey = await ResolveExpenseMonthKeyAsync(userId, id, cancellationToken);
                if (CanUseOfflineData(userId) && !string.IsNullOrWhiteSpace(monthKey))
                {
                    await offlineDataStore.RemoveExpenseAsync(userId, monthKey, id, cancellationToken);
                }

                await offlineSyncService.TriggerSyncAsync(cancellationToken);

                return result ?? new OperationResult(true, "Gasto eliminado correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar el gasto.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (HttpRequestException)
        {
            if (!CanUseOfflineData(userId))
            {
                return new OperationResult(false, "No fue posible conectar con API.");
            }

            return await DeleteExpenseOfflineAsync(userId, id, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!CanUseOfflineData(userId))
            {
                return new OperationResult(false, "No fue posible conectar con API.");
            }

            return await DeleteExpenseOfflineAsync(userId, id, cancellationToken);
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

        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();
        var canUseOffline = CanUseOfflineData(userId);

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return await GetOfflineDashboardByCategoryOrEmptyAsync(userId, monthKey, canUseOffline, cancellationToken);
            }

            var data = await httpClient.GetFromJsonAsync<List<DashboardCategoryItem>>($"api/dashboard/by-category?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<DashboardCategoryItem>)(data ?? []);
            _dashboardCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardCategoryItem>>(DateTimeOffset.UtcNow, result);

            if (canUseOffline)
            {
                await offlineDataStore.SaveDashboardByCategoryAsync(userId!, monthKey, result, cancellationToken);
            }

            return result;
        }
        catch
        {
            return await GetOfflineDashboardByCategoryOrEmptyAsync(userId, monthKey, canUseOffline, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string monthKey, CancellationToken cancellationToken)
    {
        if (TryGetCache(_dashboardByPaymentMethodCache, monthKey, out var cachedData))
        {
            return cachedData;
        }

        await EnsureOfflineStoreInitializedAsync(cancellationToken);
        var userId = GetCurrentUserId();
        var canUseOffline = CanUseOfflineData(userId);

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return await GetOfflineDashboardByPaymentMethodOrEmptyAsync(userId, monthKey, canUseOffline, cancellationToken);
            }

            var data = await httpClient.GetFromJsonAsync<List<DashboardPaymentMethodItem>>($"api/dashboard/by-payment-method?month={Uri.EscapeDataString(monthKey)}", cancellationToken);
            var result = (IReadOnlyList<DashboardPaymentMethodItem>)(data ?? []);
            _dashboardByPaymentMethodCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardPaymentMethodItem>>(DateTimeOffset.UtcNow, result);

            if (canUseOffline)
            {
                await offlineDataStore.SaveDashboardByPaymentMethodAsync(userId!, monthKey, result, cancellationToken);
            }

            return result;
        }
        catch
        {
            return await GetOfflineDashboardByPaymentMethodOrEmptyAsync(userId, monthKey, canUseOffline, cancellationToken);
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

            var data = await httpClient.GetFromJsonAsync<List<RecurringExpenseResponse>>("api/recurring-expenses", cancellationToken);
            if (data is null)
            {
                return [];
            }

            return data
                .Select(item => new RecurringExpenseItem(
                    item.Id ?? string.Empty,
                    item.Description ?? string.Empty,
                    item.Amount,
                    item.MovementType ?? string.Empty,
                    item.PaymentMethod ?? string.Empty,
                    item.DayOfMonth,
                    ResolveRecurringStartDate(item.StartDate, item.StartMonth, item.DayOfMonth),
                    item.IsActive))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public async Task<PagedRecurringResult> GetRecurringExpensesPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var resolvedPageNumber = Math.Max(1, pageNumber);
        var resolvedPageSize = pageSize is 10 or 20 ? pageSize : 5;

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new PagedRecurringResult([], 1, resolvedPageSize, 0, 1, false, false);
            }

            var data = await httpClient.GetFromJsonAsync<PagedRecurringResponse>(
                $"api/recurring-expenses/paged?pageNumber={resolvedPageNumber}&pageSize={resolvedPageSize}",
                cancellationToken);

            if (data is null)
            {
                return new PagedRecurringResult([], 1, resolvedPageSize, 0, 1, false, false);
            }

            var mappedItems = (data.Items ?? [])
                .Select(item => new RecurringExpenseItem(
                    item.Id ?? string.Empty,
                    item.Description ?? string.Empty,
                    item.Amount,
                    item.MovementType ?? string.Empty,
                    item.PaymentMethod ?? string.Empty,
                    item.DayOfMonth,
                    ResolveRecurringStartDate(item.StartDate, item.StartMonth, item.DayOfMonth),
                    item.IsActive))
                .ToArray();

            var totalPages = data.TotalPages <= 0 ? 1 : data.TotalPages;
            var currentPage = Math.Min(Math.Max(data.PageNumber, 1), totalPages);

            return new PagedRecurringResult(
                mappedItems,
                currentPage,
                data.PageSize <= 0 ? resolvedPageSize : data.PageSize,
                Math.Max(data.TotalCount, 0),
                totalPages,
                data.HasPreviousPage,
                data.HasNextPage);
        }
        catch
        {
            return new PagedRecurringResult([], 1, resolvedPageSize, 0, 1, false, false);
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

            using var recurringRequest = CreateRecurringJsonRequest(
                string.IsNullOrWhiteSpace(id) ? HttpMethod.Post : HttpMethod.Put,
                string.IsNullOrWhiteSpace(id) ? "api/recurring-expenses" : $"api/recurring-expenses/{id}",
                request);
            var response = await httpClient.SendAsync(recurringRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                InvalidateExpenseAndDashboardCaches();
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
                InvalidateExpenseAndDashboardCaches();
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

    public async Task<IReadOnlyList<ObligationItem>> GetObligationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

            var data = await httpClient.GetFromJsonAsync<List<ObligationResponse>>("api/obligations", cancellationToken);
            if (data is null)
            {
                return [];
            }

            return data
                .Select(item => new ObligationItem(
                    item.Id ?? string.Empty,
                    item.Description ?? string.Empty,
                    item.MovementType ?? string.Empty,
                    item.PaymentMethod ?? string.Empty,
                    item.DueDayOfMonth,
                    item.MonthlyPayment,
                    item.ReminderDaysBefore,
                    string.IsNullOrWhiteSpace(item.StartMonth)
                        ? DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                        : item.StartMonth,
                    item.IsActive))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public async Task<PagedObligationResult> GetObligationsPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var resolvedPageNumber = Math.Max(1, pageNumber);
        var resolvedPageSize = pageSize is 10 or 20 ? pageSize : 5;

        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new PagedObligationResult([], 1, resolvedPageSize, 0, 1, false, false);
            }

            var data = await httpClient.GetFromJsonAsync<PagedObligationResponse>(
                $"api/obligations/paged?pageNumber={resolvedPageNumber}&pageSize={resolvedPageSize}",
                cancellationToken);

            if (data is null)
            {
                return new PagedObligationResult([], 1, resolvedPageSize, 0, 1, false, false);
            }

            var mappedItems = (data.Items ?? [])
                .Select(item => new ObligationItem(
                    item.Id ?? string.Empty,
                    item.Description ?? string.Empty,
                    item.MovementType ?? string.Empty,
                    item.PaymentMethod ?? string.Empty,
                    item.DueDayOfMonth,
                    item.MonthlyPayment,
                    item.ReminderDaysBefore,
                    string.IsNullOrWhiteSpace(item.StartMonth)
                        ? DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                        : item.StartMonth,
                    item.IsActive))
                .ToArray();

            var totalPages = data.TotalPages <= 0 ? 1 : data.TotalPages;
            var currentPage = Math.Min(Math.Max(data.PageNumber, 1), totalPages);

            return new PagedObligationResult(
                mappedItems,
                currentPage,
                data.PageSize <= 0 ? resolvedPageSize : data.PageSize,
                Math.Max(data.TotalCount, 0),
                totalPages,
                data.HasPreviousPage,
                data.HasNextPage);
        }
        catch
        {
            return new PagedObligationResult([], 1, resolvedPageSize, 0, 1, false, false);
        }
    }

    public async Task<OperationResult> SaveObligationAsync(string? id, ObligationUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de guardar obligaciones.");
            }

            using var obligationRequest = CreateObligationJsonRequest(
                string.IsNullOrWhiteSpace(id) ? HttpMethod.Post : HttpMethod.Put,
                string.IsNullOrWhiteSpace(id) ? "api/obligations" : $"api/obligations/{id}",
                request);
            var response = await httpClient.SendAsync(obligationRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Obligación guardada correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible guardar la obligación.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteObligationAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión antes de eliminar obligaciones.");
            }

            var response = await httpClient.DeleteAsync($"api/obligations/{id}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Obligación eliminada correctamente.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar la obligación.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<UserProfileResult> GetUserProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return UserProfileResult.Failure("Debes iniciar sesión para consultar tu perfil.");
            }

            var response = await httpClient.GetAsync("api/profile", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadFriendlyApiErrorAsync(response, "No fue posible consultar el perfil.", cancellationToken);
                return UserProfileResult.Failure(message);
            }

            var data = await response.Content.ReadFromJsonAsync<UserProfileResponse>(cancellationToken: cancellationToken);
            if (data is null)
            {
                return UserProfileResult.Failure("No se recibió información del perfil.");
            }

            return new UserProfileResult(
                true,
                string.Empty,
                data.UserId ?? string.Empty,
                data.Name ?? string.Empty,
                data.Email ?? string.Empty,
                data.IsPremium,
                data.IsAdmin,
                data.MonthlyIncome,
                data.UpdatedAt);
        }
        catch (Exception ex)
        {
            return UserProfileResult.Failure($"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<OperationResult> UpdateMonthlyIncomeAsync(decimal? monthlyIncome, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new OperationResult(false, "Debes iniciar sesión para actualizar tu ingreso.");
            }

            var payload = new Dictionary<string, object?>
            {
                ["monthlyIncome"] = monthlyIncome
            };

            using var request = new HttpRequestMessage(HttpMethod.Put, "api/profile/monthly-income")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Ingreso actualizado.");
            }

            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible actualizar el ingreso.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<ReportPreviewResult> GetReportPreviewAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return ReportPreviewResult.Failure(startDate, endDate, "Debes iniciar sesión para consultar analitica avanzada.");
            }

            var response = await httpClient.GetAsync(
                $"api/reports/preview?from={startDate:yyyy-MM-dd}&to={endDate:yyyy-MM-dd}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadFriendlyApiErrorAsync(response, "No fue posible obtener la vista previa del reporte.", cancellationToken);
                return ReportPreviewResult.Failure(startDate, endDate, message);
            }

            var data = await response.Content.ReadFromJsonAsync<ReportPreviewResponse>(cancellationToken: cancellationToken);
            if (data is null)
            {
                return ReportPreviewResult.Failure(startDate, endDate, "No se recibió información del reporte.");
            }

            return new ReportPreviewResult(
                true,
                string.Empty,
                data.StartDate,
                data.EndDate,
                data.TotalTransactions,
                data.TotalAmount,
                data.AverageDailyAmount,
                data.PreviousPeriodTransactions,
                data.PreviousPeriodAmount,
                data.AmountChangePercentage,
                data.TransactionsChangePercentage,
                (data.CategoryBreakdown ?? [])
                    .Select(item => new ReportBreakdownItem(
                        item.Name ?? string.Empty,
                        item.Amount,
                        item.Count,
                        item.Percentage))
                    .ToArray(),
                (data.PaymentMethodBreakdown ?? [])
                    .Select(item => new ReportBreakdownItem(
                        item.Name ?? string.Empty,
                        item.Amount,
                        item.Count,
                        item.Percentage))
                    .ToArray(),
                (data.TopExpenses ?? [])
                    .Select(item => new ReportTopExpenseItem(
                        item.Date,
                        item.Description ?? string.Empty,
                        item.MovementType ?? string.Empty,
                        item.PaymentMethod ?? string.Empty,
                        item.Amount))
                    .ToArray(),
                (data.DailyTrend ?? [])
                    .Select(item => new ReportDailyTrendPoint(
                        item.Date,
                        item.Amount,
                        item.Transactions))
                    .ToArray(),
                (data.Insights ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToArray());
        }
        catch (Exception ex)
        {
            return ReportPreviewResult.Failure(startDate, endDate, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<FinancialScoreResult> GetFinancialScoreAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return FinancialScoreResult.Failure(startDate, endDate, "Debes iniciar sesión para consultar el score financiero.");
            }

            var response = await httpClient.GetAsync(
                $"api/reports/financial-score?from={startDate:yyyy-MM-dd}&to={endDate:yyyy-MM-dd}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadFriendlyApiErrorAsync(response, "No fue posible consultar el score financiero.", cancellationToken);
                return FinancialScoreResult.Failure(startDate, endDate, message);
            }

            var data = await response.Content.ReadFromJsonAsync<FinancialScoreResponse>(cancellationToken: cancellationToken);
            if (data is null)
            {
                return FinancialScoreResult.Failure(startDate, endDate, "No se recibió información del score financiero.");
            }

            return new FinancialScoreResult(
                true,
                string.Empty,
                data.PeriodStart,
                data.PeriodEnd,
                data.Score,
                data.Trend ?? "stable",
                data.Confidence,
                data.AlgorithmVersion ?? string.Empty,
                data.TotalSpent,
                data.MonthlyIncome,
                data.SpendingToIncomeRatio,
                data.ObligationsToIncomeRatio,
                data.BudgetUtilizationRatio,
                data.AmountChangePercentage,
                (data.Components ?? [])
                    .Select(item => new FinancialScoreComponent(
                        item.Key ?? string.Empty,
                        item.Name ?? string.Empty,
                        item.Score,
                        item.Weight,
                        item.Detail ?? string.Empty))
                    .ToArray(),
                (data.Drivers ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToArray());
        }
        catch (Exception ex)
        {
            return FinancialScoreResult.Failure(startDate, endDate, $"No fue posible conectar con API: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<FinancialScoreHistoryItem>> GetFinancialScoreHistoryAsync(int limit, CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 24);
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return [];
            }

            var data = await httpClient.GetFromJsonAsync<List<FinancialScoreHistoryResponse>>(
                $"api/reports/financial-score/history?limit={safeLimit}",
                cancellationToken);

            return (data ?? [])
                .Select(item => new FinancialScoreHistoryItem(
                    item.PeriodStart,
                    item.PeriodEnd,
                    item.Score,
                    item.Trend ?? "stable",
                    item.Confidence,
                    item.GeneratedAtUtc))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public async Task<FinancialRecommendationsResult> GetFinancialRecommendationsAsync(DateOnly startDate, DateOnly endDate, bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return FinancialRecommendationsResult.Failure("Debes iniciar sesión para consultar recomendaciones.");
            }

            var response = await httpClient.GetAsync(
                $"api/reports/recommendations?from={startDate:yyyy-MM-dd}&to={endDate:yyyy-MM-dd}&forceRefresh={forceRefresh.ToString().ToLowerInvariant()}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadFriendlyApiErrorAsync(response, "No fue posible consultar recomendaciones financieras.", cancellationToken);
                return FinancialRecommendationsResult.Failure(message);
            }

            var data = await response.Content.ReadFromJsonAsync<FinancialRecommendationsResponse>(cancellationToken: cancellationToken);
            if (data is null)
            {
                return FinancialRecommendationsResult.Failure("No se recibieron recomendaciones del servidor.");
            }

            return new FinancialRecommendationsResult(
                true,
                string.Empty,
                data.SourceLabel ?? string.Empty,
                data.Priority ?? string.Empty,
                (data.Recommendations ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToArray(),
                data.ScoreReference,
                data.Trend ?? "stable",
                data.Confidence,
                (data.Drivers ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToArray(),
                data.GeneratedAtUtc);
        }
        catch (Exception ex)
        {
            return FinancialRecommendationsResult.Failure($"No fue posible conectar con API: {ex.Message}");
        }
    }

    public Task<ReportFileDownloadResult> DownloadReportCsvAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        return DownloadReportFileAsync("csv", "text/csv; charset=utf-8", startDate, endDate, cancellationToken);
    }

    public Task<ReportFileDownloadResult> DownloadReportPdfAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        return DownloadReportFileAsync("pdf", "application/pdf", startDate, endDate, cancellationToken);
    }

    private async Task<ReportFileDownloadResult> DownloadReportFileAsync(
        string format,
        string fallbackContentType,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return new ReportFileDownloadResult(false, "Debes iniciar sesión para descargar reportes de analitica avanzada.", null);
            }

            var response = await httpClient.GetAsync(
                $"api/reports/export/{format}?from={startDate:yyyy-MM-dd}&to={endDate:yyyy-MM-dd}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadFriendlyApiErrorAsync(response, "No fue posible descargar el reporte.", cancellationToken);
                return new ReportFileDownloadResult(false, message, null);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0)
            {
                return new ReportFileDownloadResult(false, "El reporte llegó vacío.", null);
            }

            var contentType = response.Content.Headers.ContentType?.ToString();
            var fileName = ResolveReportFileName(response, format, startDate, endDate);

            return new ReportFileDownloadResult(
                true,
                "Reporte generado correctamente.",
                new ReportFilePayload(bytes, string.IsNullOrWhiteSpace(contentType) ? fallbackContentType : contentType, fileName));
        }
        catch (Exception ex)
        {
            return new ReportFileDownloadResult(false, $"No fue posible conectar con API: {ex.Message}", null);
        }
    }

    private async Task EnsureOfflineStoreInitializedAsync(CancellationToken cancellationToken)
    {
        if (offlineInitialized)
        {
            return;
        }

        await offlineInitLock.WaitAsync(cancellationToken);
        try
        {
            if (offlineInitialized)
            {
                return;
            }

            await offlineDataStore.InitializeAsync(cancellationToken);
            offlineInitialized = true;
        }
        finally
        {
            offlineInitLock.Release();
        }
    }

    private string? GetCurrentUserId()
    {
        var userId = (authService.CurrentUserId ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(userId) ? null : userId;
    }

    private bool CanUseOfflineData(string? userId)
    {
        return !string.IsNullOrWhiteSpace(userId) && authService.IsCurrentUserPremium;
    }

    private async Task<ExpenseCatalog> GetOfflineCatalogOrFallbackAsync(string? userId, bool canUseOffline, CancellationToken cancellationToken)
    {
        if (!canUseOffline)
        {
            return FallbackCatalog;
        }

        var localCatalog = await offlineDataStore.GetCatalogAsync(userId!, cancellationToken);
        return localCatalog ?? FallbackCatalog;
    }

    private async Task<IReadOnlyList<ExpenseItem>> GetOfflineExpensesOrEmptyAsync(string? userId, string monthKey, bool canUseOffline, CancellationToken cancellationToken)
    {
        if (!canUseOffline)
        {
            return [];
        }

        var localExpenses = await offlineDataStore.GetExpensesAsync(userId!, monthKey, cancellationToken);
        _expensesCache[monthKey] = new CacheEntry<IReadOnlyList<ExpenseItem>>(DateTimeOffset.UtcNow, localExpenses);
        return localExpenses;
    }

    private async Task<IReadOnlyList<string>> GetOfflineMonthsOrEmptyAsync(string? userId, bool canUseOffline, CancellationToken cancellationToken)
    {
        if (!canUseOffline)
        {
            return [];
        }

        var monthKeys = await offlineDataStore.GetAvailableMonthsAsync(userId!, cancellationToken);
        _monthsCache = new CacheEntry<IReadOnlyList<string>>(DateTimeOffset.UtcNow, monthKeys);
        return monthKeys;
    }

    private async Task<PagedExpenseResult> BuildOfflineExpensePageAsync(
        string? userId,
        string monthKey,
        int pageNumber,
        int pageSize,
        string? movementType,
        string? paymentMethod,
        string? searchTerm,
        bool canUseOffline,
        CancellationToken cancellationToken)
    {
        if (!canUseOffline)
        {
            return new PagedExpenseResult([], 1, pageSize, 0, 0m, 1, false, false);
        }

        var localExpenses = await offlineDataStore.GetExpensesAsync(userId!, monthKey, cancellationToken);
        var filtered = localExpenses
            .Where(item => string.IsNullOrWhiteSpace(movementType) || string.Equals(item.MovementType, movementType, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(paymentMethod) || string.Equals(item.PaymentMethod, paymentMethod, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(searchTerm) || item.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();

        var totalCount = filtered.Count;
        var totalAmount = filtered.Sum(item => item.Amount);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)Math.Max(1, pageSize)));
        var currentPage = Math.Min(Math.Max(pageNumber, 1), totalPages);
        var skip = (currentPage - 1) * pageSize;
        var items = filtered.Skip(skip).Take(pageSize).ToArray();

        return new PagedExpenseResult(
            items,
            currentPage,
            pageSize,
            totalCount,
            totalAmount,
            totalPages,
            currentPage > 1,
            currentPage < totalPages);
    }

    private async Task<SaveExpenseResult> SaveExpenseOfflineAsync(string userId, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        var monthKey = request.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow;
        var localId = $"offline-{Guid.NewGuid():N}";
        var clientMutationId = string.IsNullOrWhiteSpace(request.ClientMutationId) ? localId : request.ClientMutationId.Trim();
        var requestForMutation = request with { ClientMutationId = clientMutationId };
        var (isCredit, installments) = ResolveOfflineCreditSettings(request);

        var localExpense = new ExpenseItem(
            localId,
            request.Date,
            request.Description?.Trim() ?? string.Empty,
            request.Amount,
            request.MovementType?.Trim() ?? string.Empty,
            request.PaymentMethod?.Trim() ?? string.Empty,
            now,
            now,
            isCredit,
            installments);

        await offlineDataStore.UpsertExpenseAsync(userId, monthKey, localExpense, cancellationToken);

        var mutation = new OfflineExpenseCreateMutationPayload(localId, monthKey, requestForMutation);
        await offlineDataStore.EnqueueMutationAsync(
            userId,
            OfflineMutationTypes.ExpenseCreate,
            monthKey,
            JsonSerializer.Serialize(mutation, jsonOptions),
            cancellationToken);

        _expensesCache.TryRemove(monthKey, out _);
        _dashboardCache.TryRemove(monthKey, out _);
        _dashboardByPaymentMethodCache.TryRemove(monthKey, out _);
        _monthsCache = null;

        await offlineSyncService.TriggerSyncAsync(cancellationToken);
        return new SaveExpenseResult(true, "Guardado offline. Se sincronizara automaticamente al reconectar.", 0);
    }

    private async Task<OperationResult> UpdateExpenseOfflineAsync(string userId, string id, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        var monthKey = request.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow;
        var (isCredit, installments) = ResolveOfflineCreditSettings(request);

        var localExpense = new ExpenseItem(
            id,
            request.Date,
            request.Description?.Trim() ?? string.Empty,
            request.Amount,
            request.MovementType?.Trim() ?? string.Empty,
            request.PaymentMethod?.Trim() ?? string.Empty,
            now,
            now,
            isCredit,
            installments);

        await offlineDataStore.UpsertExpenseAsync(userId, monthKey, localExpense, cancellationToken);

        var mutation = new OfflineExpenseUpdateMutationPayload(id, monthKey, request);
        await offlineDataStore.EnqueueMutationAsync(
            userId,
            OfflineMutationTypes.ExpenseUpdate,
            monthKey,
            JsonSerializer.Serialize(mutation, jsonOptions),
            cancellationToken);

        _expensesCache.TryRemove(monthKey, out _);
        _dashboardCache.TryRemove(monthKey, out _);
        _dashboardByPaymentMethodCache.TryRemove(monthKey, out _);

        await offlineSyncService.TriggerSyncAsync(cancellationToken);
        return new OperationResult(true, "Actualizacion guardada offline. Se sincronizara automaticamente al reconectar.");
    }

    private async Task<OperationResult> DeleteExpenseOfflineAsync(string userId, string expenseId, CancellationToken cancellationToken)
    {
        var monthKey = await ResolveExpenseMonthKeyAsync(userId, expenseId, cancellationToken);
        if (string.IsNullOrWhiteSpace(monthKey))
        {
            monthKey = DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        await offlineDataStore.RemoveExpenseAsync(userId, monthKey, expenseId, cancellationToken);

        if (IsOfflineLocalId(expenseId))
        {
            var droppedPendingCreate = await offlineDataStore.TryDropPendingCreateMutationAsync(userId, monthKey, expenseId, cancellationToken);
            if (droppedPendingCreate)
            {
                _expensesCache.TryRemove(monthKey, out _);
                _dashboardCache.TryRemove(monthKey, out _);
                _dashboardByPaymentMethodCache.TryRemove(monthKey, out _);
                _monthsCache = null;
                await offlineSyncService.TriggerSyncAsync(cancellationToken);
                return new OperationResult(true, "Eliminacion local aplicada sin pendientes de sincronizacion.");
            }
        }

        var mutation = new OfflineExpenseDeleteMutationPayload(expenseId, monthKey);
        await offlineDataStore.EnqueueMutationAsync(
            userId,
            OfflineMutationTypes.ExpenseDelete,
            monthKey,
            JsonSerializer.Serialize(mutation, jsonOptions),
            cancellationToken);

        _expensesCache.TryRemove(monthKey, out _);
        _dashboardCache.TryRemove(monthKey, out _);
        _dashboardByPaymentMethodCache.TryRemove(monthKey, out _);
        _monthsCache = null;

        await offlineSyncService.TriggerSyncAsync(cancellationToken);
        return new OperationResult(true, "Eliminacion guardada offline. Se sincronizara automaticamente al reconectar.");
    }

    private async Task<string> ResolveExpenseMonthKeyAsync(string userId, string expenseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expenseId))
        {
            return string.Empty;
        }

        var months = await offlineDataStore.GetAvailableMonthsAsync(userId, cancellationToken);
        foreach (var month in months.OrderByDescending(item => item, StringComparer.Ordinal))
        {
            var expenses = await offlineDataStore.GetExpensesAsync(userId, month, cancellationToken);
            if (expenses.Any(item => string.Equals(item.Id, expenseId, StringComparison.Ordinal)))
            {
                return month;
            }
        }

        return string.Empty;
    }

    private async Task<IReadOnlyList<DashboardCategoryItem>> GetOfflineDashboardByCategoryOrEmptyAsync(string? userId, string monthKey, bool canUseOffline, CancellationToken cancellationToken)
    {
        if (!canUseOffline)
        {
            return [];
        }

        var stored = await offlineDataStore.GetDashboardByCategoryAsync(userId!, monthKey, cancellationToken);
        if (stored.Count > 0)
        {
            _dashboardCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardCategoryItem>>(DateTimeOffset.UtcNow, stored);
            return stored;
        }

        var generated = await BuildOfflineDashboardByCategoryAsync(userId!, monthKey, cancellationToken);
        _dashboardCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardCategoryItem>>(DateTimeOffset.UtcNow, generated);
        return generated;
    }

    private async Task<IReadOnlyList<DashboardCategoryItem>> BuildOfflineDashboardByCategoryAsync(string userId, string monthKey, CancellationToken cancellationToken)
    {
        var expenses = await offlineDataStore.GetExpensesAsync(userId, monthKey, cancellationToken);
        if (expenses.Count == 0)
        {
            return [];
        }

        var previousSnapshot = await offlineDataStore.GetDashboardByCategoryAsync(userId, monthKey, cancellationToken);
        var budgets = previousSnapshot
            .GroupBy(item => item.MovementType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().BudgetTotal, StringComparer.OrdinalIgnoreCase);

        var groupedExpenses = expenses
            .GroupBy(item => item.MovementType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount), StringComparer.OrdinalIgnoreCase);

        var movementTypes = budgets.Keys.Union(groupedExpenses.Keys, StringComparer.OrdinalIgnoreCase);

        return movementTypes
            .Select(movementType =>
            {
                groupedExpenses.TryGetValue(movementType, out var expenseTotal);
                budgets.TryGetValue(movementType, out var budgetTotal);
                return new DashboardCategoryItem(movementType, expenseTotal, budgetTotal, budgetTotal - expenseTotal);
            })
            .OrderByDescending(item => item.ExpenseTotal)
            .ToArray();
    }

    private async Task<IReadOnlyList<DashboardPaymentMethodItem>> GetOfflineDashboardByPaymentMethodOrEmptyAsync(string? userId, string monthKey, bool canUseOffline, CancellationToken cancellationToken)
    {
        if (!canUseOffline)
        {
            return [];
        }

        var stored = await offlineDataStore.GetDashboardByPaymentMethodAsync(userId!, monthKey, cancellationToken);
        if (stored.Count > 0)
        {
            _dashboardByPaymentMethodCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardPaymentMethodItem>>(DateTimeOffset.UtcNow, stored);
            return stored;
        }

        var generated = await BuildOfflineDashboardByPaymentMethodAsync(userId!, monthKey, cancellationToken);
        _dashboardByPaymentMethodCache[monthKey] = new CacheEntry<IReadOnlyList<DashboardPaymentMethodItem>>(DateTimeOffset.UtcNow, generated);
        return generated;
    }

    private async Task<IReadOnlyList<DashboardPaymentMethodItem>> BuildOfflineDashboardByPaymentMethodAsync(string userId, string monthKey, CancellationToken cancellationToken)
    {
        var expenses = await offlineDataStore.GetExpensesAsync(userId, monthKey, cancellationToken);
        if (expenses.Count == 0)
        {
            return [];
        }

        return expenses
            .GroupBy(item => item.PaymentMethod, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DashboardPaymentMethodItem(group.Key, group.Sum(item => item.Amount)))
            .OrderByDescending(item => item.ExpenseTotal)
            .ToArray();
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

    private sealed record CacheEntry<T>(DateTimeOffset CreatedAt, T Data);

    private sealed record RecurringExpenseResponse(
        string? Id,
        string? Description,
        decimal Amount,
        string? MovementType,
        string? PaymentMethod,
        int DayOfMonth,
        DateOnly? StartDate,
        string? StartMonth,
        bool IsActive);

    private sealed record PagedRecurringResponse(
        List<RecurringExpenseResponse>? Items,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage);

    private sealed record ObligationResponse(
        string? Id,
        string? Description,
        string? MovementType,
        string? PaymentMethod,
        int DueDayOfMonth,
        decimal MonthlyPayment,
        int ReminderDaysBefore,
        string? StartMonth,
        bool IsActive);

    private sealed record PagedObligationResponse(
        List<ObligationResponse>? Items,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage);

    private sealed record UserProfileResponse(
        string? UserId,
        string? Name,
        string? Email,
        bool IsPremium,
        bool IsAdmin,
        decimal? MonthlyIncome,
        DateTimeOffset UpdatedAt);

    private sealed record FinancialScoreResponse(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int Score,
        string? Trend,
        decimal Confidence,
        string? AlgorithmVersion,
        decimal TotalSpent,
        decimal? MonthlyIncome,
        decimal? SpendingToIncomeRatio,
        decimal? ObligationsToIncomeRatio,
        decimal BudgetUtilizationRatio,
        decimal AmountChangePercentage,
        List<FinancialScoreComponentResponse>? Components,
        List<string>? Drivers);

    private sealed record FinancialScoreComponentResponse(
        string? Key,
        string? Name,
        int Score,
        decimal Weight,
        string? Detail);

    private sealed record FinancialScoreHistoryResponse(
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int Score,
        string? Trend,
        decimal Confidence,
        DateTimeOffset GeneratedAtUtc);

    private sealed record FinancialRecommendationsResponse(
        string? SourceLabel,
        string? Priority,
        List<string>? Recommendations,
        int ScoreReference,
        string? Trend,
        decimal Confidence,
        List<string>? Drivers,
        DateTimeOffset GeneratedAtUtc);

    private sealed record ReportPreviewResponse(
        DateOnly StartDate,
        DateOnly EndDate,
        int TotalTransactions,
        decimal TotalAmount,
        decimal AverageDailyAmount,
        int PreviousPeriodTransactions,
        decimal PreviousPeriodAmount,
        decimal AmountChangePercentage,
        decimal TransactionsChangePercentage,
        List<ReportBreakdownResponse>? CategoryBreakdown,
        List<ReportBreakdownResponse>? PaymentMethodBreakdown,
        List<ReportTopExpenseResponse>? TopExpenses,
        List<ReportDailyTrendResponse>? DailyTrend,
        List<string>? Insights);

    private sealed record ReportBreakdownResponse(
        string? Name,
        decimal Amount,
        int Count,
        decimal Percentage);

    private sealed record ReportTopExpenseResponse(
        DateOnly Date,
        string? Description,
        string? MovementType,
        string? PaymentMethod,
        decimal Amount);

    private sealed record ReportDailyTrendResponse(
        DateOnly Date,
        decimal Amount,
        int Transactions);

    private static DateOnly ResolveRecurringStartDate(DateOnly? startDate, string? startMonth, int dayOfMonth)
    {
        if (startDate is { } explicitStartDate && explicitStartDate != DateOnly.MinValue)
        {
            return explicitStartDate;
        }

        if (!string.IsNullOrWhiteSpace(startMonth) && DateOnly.TryParseExact($"{startMonth.Trim()}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthStart))
        {
            var safeDay = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
            return new DateOnly(monthStart.Year, monthStart.Month, safeDay);
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var fallbackDay = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(today.Year, today.Month));
        return new DateOnly(today.Year, today.Month, fallbackDay);
    }

    private void InvalidateExpenseAndDashboardCaches()
    {
        _expensesCache.Clear();
        _dashboardCache.Clear();
        _dashboardByPaymentMethodCache.Clear();
        _monthsCache = null;
    }

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
            ["paymentMethod"] = request.PaymentMethod,
            ["isCredit"] = request.IsCredit,
            ["installments"] = request.Installments,
            ["clientMutationId"] = request.ClientMutationId
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
                .ToArray(),
            ["paymentMethodConfigs"] = (request.PaymentMethodConfigs ?? [])
                .Select(c => new Dictionary<string, object?>
                {
                    ["name"] = c.Name,
                    ["icon"] = c.Icon,
                    ["isCredit"] = c.IsCredit,
                    ["defaultInstallments"] = c.DefaultInstallments,
                    ["dueDayOfMonth"] = c.DueDayOfMonth,
                    ["reminderDaysBefore"] = c.ReminderDaysBefore
                })
                .ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(HttpMethod.Put, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static (bool IsCredit, int? Installments) ResolveOfflineCreditSettings(ExpenseEntryRequest request)
    {
        var installments = NormalizeInstallments(request.Installments);
        var isCredit = request.IsCredit ?? (installments is > 1);

        if (!isCredit)
        {
            return (false, null);
        }

        return (true, installments);
    }

    private static int? NormalizeInstallments(int? value)
    {
        if (value is null)
        {
            return null;
        }

        return Math.Clamp(value.Value, 1, 120);
    }

    private static bool IsOfflineLocalId(string? expenseId)
    {
        return !string.IsNullOrWhiteSpace(expenseId)
            && expenseId.StartsWith("offline-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveReportFileName(HttpResponseMessage response, string format, DateOnly startDate, DateOnly endDate)
    {
        var fromContentHeader = response.Content.Headers.ContentDisposition;
        var candidate = fromContentHeader?.FileNameStar ?? fromContentHeader?.FileName;

        if (string.IsNullOrWhiteSpace(candidate)
            && response.Headers.TryGetValues("Content-Disposition", out var values))
        {
            var raw = values.FirstOrDefault();
            candidate = ParseFilenameFromRawContentDisposition(raw);
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return $"controleo-reporte-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.{format}";
        }

        var normalized = candidate.Trim().Trim('"');
        if (normalized.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }

        try
        {
            normalized = Uri.UnescapeDataString(normalized);
        }
        catch
        {
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? $"controleo-reporte-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.{format}"
            : normalized;
    }

    private static string? ParseFilenameFromRawContentDisposition(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var filenameStarMarker = "filename*=";
        var filenameMarker = "filename=";
        var token = raw;

        var filenameStarIndex = token.IndexOf(filenameStarMarker, StringComparison.OrdinalIgnoreCase);
        if (filenameStarIndex >= 0)
        {
            var segment = token[(filenameStarIndex + filenameStarMarker.Length)..];
            var semicolon = segment.IndexOf(';');
            return semicolon >= 0 ? segment[..semicolon].Trim() : segment.Trim();
        }

        var filenameIndex = token.IndexOf(filenameMarker, StringComparison.OrdinalIgnoreCase);
        if (filenameIndex < 0)
        {
            return null;
        }

        var filenameSegment = token[(filenameIndex + filenameMarker.Length)..];
        var filenameSemicolon = filenameSegment.IndexOf(';');
        return filenameSemicolon >= 0 ? filenameSegment[..filenameSemicolon].Trim() : filenameSegment.Trim();
    }

    private static HttpRequestMessage CreateRecurringJsonRequest(HttpMethod method, string relativeUrl, RecurringExpenseUpsertRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["description"] = request.Description,
            ["amount"] = request.Amount,
            ["movementType"] = request.MovementType,
            ["paymentMethod"] = request.PaymentMethod,
            ["dayOfMonth"] = request.DayOfMonth,
            ["startMonth"] = request.StartDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ["endMonth"] = null,
            ["startDate"] = request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = null,
            ["isActive"] = request.IsActive
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpRequestMessage CreateObligationJsonRequest(HttpMethod method, string relativeUrl, ObligationUpsertRequest request)
    {
        var payload = new Dictionary<string, object?>
        {
            ["description"] = request.Description,
            ["movementType"] = request.MovementType,
            ["paymentMethod"] = request.PaymentMethod,
            ["dueDayOfMonth"] = request.DueDayOfMonth,
            ["monthlyPayment"] = request.MonthlyPayment,
            ["reminderDaysBefore"] = request.ReminderDaysBefore,
            ["startMonth"] = request.StartMonth,
            ["isActive"] = request.IsActive
        };

        var json = JsonSerializer.Serialize(payload);
        return new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    // ──── Goals ────

    public async Task<IReadOnlyList<SavingsGoalItem>> GetGoalsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return [];
            var data = await httpClient.GetFromJsonAsync<List<SavingsGoalItem>>("api/goals", cancellationToken);
            return data ?? [];
        }
        catch { return []; }
    }

    public async Task<SavingsGoalItem?> GetGoalDetailAsync(string goalId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return null;
            return await httpClient.GetFromJsonAsync<SavingsGoalItem>($"api/goals/{Uri.EscapeDataString(goalId)}", cancellationToken);
        }
        catch { return null; }
    }

    public async Task<OperationResult> CreateGoalAsync(GoalUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var payload = new Dictionary<string, object?>
            {
                ["name"] = request.Name,
                ["icon"] = request.Icon,
                ["targetAmount"] = request.TargetAmount,
                ["targetDate"] = request.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["priority"] = request.Priority
            };
            using var msg = new HttpRequestMessage(HttpMethod.Post, "api/goals")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            var response = await httpClient.SendAsync(msg, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Meta creada.");
            }
            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible crear la meta.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex) { return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}"); }
    }

    public async Task<OperationResult> UpdateGoalAsync(string goalId, GoalUpsertRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var payload = new Dictionary<string, object?>
            {
                ["name"] = request.Name,
                ["icon"] = request.Icon,
                ["targetAmount"] = request.TargetAmount,
                ["targetDate"] = request.TargetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["priority"] = request.Priority,
                ["status"] = request.Status
            };
            using var msg = new HttpRequestMessage(HttpMethod.Put, $"api/goals/{Uri.EscapeDataString(goalId)}")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            var response = await httpClient.SendAsync(msg, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Meta actualizada.");
            }
            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible actualizar la meta.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex) { return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}"); }
    }

    public async Task<OperationResult> DeleteGoalAsync(string goalId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var response = await httpClient.DeleteAsync($"api/goals/{Uri.EscapeDataString(goalId)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Meta eliminada.");
            }
            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar la meta.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex) { return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}"); }
    }

    public async Task<OperationResult> AddGoalContributionAsync(string goalId, GoalContributionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var payload = new Dictionary<string, object?>
            {
                ["amount"] = request.Amount,
                ["date"] = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["note"] = request.Note
            };
            using var msg = new HttpRequestMessage(HttpMethod.Post, $"api/goals/{Uri.EscapeDataString(goalId)}/contributions")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            var response = await httpClient.SendAsync(msg, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Aporte registrado.");
            }
            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible registrar el aporte.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex) { return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}"); }
    }

    public async Task<OperationResult> DeleteGoalContributionAsync(string goalId, string contributionId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var response = await httpClient.DeleteAsync($"api/goals/{Uri.EscapeDataString(goalId)}/contributions/{Uri.EscapeDataString(contributionId)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Aporte eliminado.");
            }
            var message = await ReadFriendlyApiErrorAsync(response, "No fue posible eliminar el aporte.", cancellationToken);
            return new OperationResult(false, message);
        }
        catch (Exception ex) { return new OperationResult(false, $"No fue posible conectar con API: {ex.Message}"); }
    }

    public async Task<GoalSimulationResult?> SimulateGoalAsync(string goalId, GoalSimulationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return null;
            var payload = new Dictionary<string, object?>
            {
                ["scenarioType"] = request.ScenarioType,
                ["newValue"] = request.NewValue
            };
            using var msg = new HttpRequestMessage(HttpMethod.Post, $"api/goals/{Uri.EscapeDataString(goalId)}/simulate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            var response = await httpClient.SendAsync(msg, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<GoalSimulationResult>(cancellationToken: cancellationToken);
        }
        catch { return null; }
    }

    public async Task<IReadOnlyList<GoalAlertItem>> GetGoalAlertsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return [];
            var data = await httpClient.GetFromJsonAsync<List<GoalAlertItem>>("api/goals/alerts", cancellationToken);
            return data ?? [];
        }
        catch { return []; }
    }

    // ──── Coach IA ────

    public async Task<ChatMessageItem?> SendCoachMessageAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return null;
            var payload = new Dictionary<string, object?> { ["content"] = content };
            using var msg = new HttpRequestMessage(HttpMethod.Post, "api/coach/message")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            var response = await httpClient.SendAsync(msg, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ChatMessageItem>(cancellationToken: cancellationToken);
        }
        catch { return null; }
    }

    public async Task<ChatHistoryResult> GetCoachHistoryAsync(int limit, CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return new ChatHistoryResult([]);
            var data = await httpClient.GetFromJsonAsync<ChatHistoryResult>($"api/coach/history?limit={limit}", cancellationToken);
            return data ?? new ChatHistoryResult([]);
        }
        catch { return new ChatHistoryResult([]); }
    }

    public async Task<OperationResult> ClearCoachHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken))
                return new OperationResult(false, "Debes iniciar sesión.");
            var response = await httpClient.DeleteAsync("api/coach/history", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OperationResult>(cancellationToken: cancellationToken);
                return result ?? new OperationResult(true, "Historial eliminado.");
            }
            return new OperationResult(false, "No fue posible limpiar el historial.");
        }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    public async Task<CoachSuggestionsResult> GetCoachSuggestionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await EnsureAuthenticatedAsync(cancellationToken)) return new CoachSuggestionsResult([]);
            var data = await httpClient.GetFromJsonAsync<CoachSuggestionsResult>("api/coach/suggestions", cancellationToken);
            return data ?? new CoachSuggestionsResult([]);
        }
        catch { return new CoachSuggestionsResult([]); }
    }
}
