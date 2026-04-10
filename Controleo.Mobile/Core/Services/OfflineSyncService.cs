using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;

namespace Controleo.Mobile.Core.Services;

public sealed class OfflineSyncService : IOfflineSyncService, IDisposable
{
    private const int MaxBatchSize = 50;
    private const int MaxRetryDelaySeconds = 60;
    private const int MaxRetryCount = 8;

    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly IConnectivityService _connectivityService;
    private readonly IOfflineDataStore _offlineDataStore;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private bool _disposed;

    public OfflineSyncService(
        HttpClient httpClient,
        IAuthService authService,
        IConnectivityService connectivityService,
        IOfflineDataStore offlineDataStore)
    {
        _httpClient = httpClient;
        _authService = authService;
        _connectivityService = connectivityService;
        _offlineDataStore = offlineDataStore;

        _connectivityService.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsSyncInProgress => _syncLock.CurrentCount == 0;

    public async Task TriggerSyncAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_connectivityService.IsOnline)
        {
            return;
        }

        var acquired = await _syncLock.WaitAsync(0, cancellationToken);
        if (!acquired)
        {
            return;
        }

        try
        {
            var userId = (_authService.CurrentUserId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            await _offlineDataStore.InitializeAsync(cancellationToken);
            if (!await EnsureAuthenticatedAsync(cancellationToken))
            {
                return;
            }

            var pendingMutations = await _offlineDataStore.GetPendingMutationsAsync(userId, MaxBatchSize, cancellationToken);
            foreach (var mutation in pendingMutations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var shouldRemove = await ExecuteMutationAsync(userId, mutation, cancellationToken);
                if (shouldRemove)
                {
                    await _offlineDataStore.MarkMutationSucceededAsync(mutation.Id, cancellationToken);
                }
                else
                {
                    var nextRetryCount = mutation.RetryCount + 1;
                    if (nextRetryCount >= MaxRetryCount)
                    {
                        await _offlineDataStore.AddDeadLetterMutationAsync(
                            userId,
                            mutation,
                            $"Mutation retry limit reached ({nextRetryCount}/{MaxRetryCount}).",
                            cancellationToken);
                        await _offlineDataStore.MarkMutationSucceededAsync(mutation.Id, cancellationToken);
                        continue;
                    }

                    await _offlineDataStore.IncrementMutationRetryAsync(mutation.Id, cancellationToken);

                    // Keep operation order stable: stop the current pass when one mutation fails.
                    var delay = GetRetryDelay(nextRetryCount);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                    }

                    break;
                }
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private static TimeSpan GetRetryDelay(int retryCount)
    {
        var safeRetries = Math.Clamp(retryCount, 0, 10);
        var seconds = Math.Min(MaxRetryDelaySeconds, (int)Math.Pow(2, safeRetries));
        return TimeSpan.FromSeconds(seconds);
    }

    private async void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (!isOnline || _disposed)
        {
            return;
        }

        try
        {
            await TriggerSyncAsync(CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task<bool> ExecuteMutationAsync(string userId, OfflineMutation mutation, CancellationToken cancellationToken)
    {
        try
        {
            return mutation.MutationType switch
            {
                OfflineMutationTypes.ExpenseCreate => await ExecuteExpenseCreateAsync(userId, mutation.PayloadJson, cancellationToken),
                OfflineMutationTypes.ExpenseUpdate => await ExecuteExpenseUpdateAsync(mutation.PayloadJson, cancellationToken),
                OfflineMutationTypes.ExpenseDelete => await ExecuteExpenseDeleteAsync(mutation.PayloadJson, cancellationToken),
                _ => true
            };
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ExecuteExpenseCreateAsync(string userId, string payloadJson, CancellationToken cancellationToken)
    {
        var payload = TryDeserialize<OfflineExpenseCreateMutationPayload>(payloadJson);
        if (payload?.Request is null)
        {
            return true;
        }

        var createRequest = payload.Request with { ClientMutationId = ResolveClientMutationId(payload.Request.ClientMutationId, payload.LocalExpenseId) };
        using var request = CreateExpenseJsonRequest(HttpMethod.Post, "api/expenses", createRequest);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return !ShouldRetry(response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<SaveExpenseResult>(cancellationToken: cancellationToken);
        var serverExpenseId = ResolveExpenseId(result);
        if (!string.IsNullOrWhiteSpace(serverExpenseId)
            && !string.IsNullOrWhiteSpace(payload.LocalExpenseId)
            && !string.Equals(serverExpenseId, payload.LocalExpenseId, StringComparison.Ordinal))
        {
            await _offlineDataStore.RemapExpenseIdAsync(userId, payload.MonthKey, payload.LocalExpenseId, serverExpenseId, cancellationToken);
        }

        return true;
    }

    private async Task<bool> ExecuteExpenseUpdateAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = TryDeserialize<OfflineExpenseUpdateMutationPayload>(payloadJson);
        if (payload?.Request is null || string.IsNullOrWhiteSpace(payload.ExpenseId))
        {
            return true;
        }

        if (IsOfflineLocalId(payload.ExpenseId))
        {
            return false;
        }

        using var request = CreateExpenseJsonRequest(HttpMethod.Put, $"api/expenses/{Uri.EscapeDataString(payload.ExpenseId)}", payload.Request);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode || !ShouldRetry(response.StatusCode);
    }

    private async Task<bool> ExecuteExpenseDeleteAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = TryDeserialize<OfflineExpenseDeleteMutationPayload>(payloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ExpenseId))
        {
            return true;
        }

        if (IsOfflineLocalId(payload.ExpenseId))
        {
            return false;
        }

        var response = await _httpClient.DeleteAsync($"api/expenses/{Uri.EscapeDataString(payload.ExpenseId)}", cancellationToken);
        return response.IsSuccessStatusCode || !ShouldRetry(response.StatusCode);
    }

    private static bool ShouldRetry(System.Net.HttpStatusCode statusCode)
    {
        var numericCode = (int)statusCode;
        return numericCode == 408 || numericCode == 429 || numericCode >= 500;
    }

    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        var accessToken = await _authService.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        if (_httpClient.DefaultRequestHeaders.Authorization?.Parameter != accessToken)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return true;
    }

    private T? TryDeserialize<T>(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payloadJson, _jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string ResolveClientMutationId(string? requestClientMutationId, string? fallbackLocalExpenseId)
    {
        if (!string.IsNullOrWhiteSpace(requestClientMutationId))
        {
            return requestClientMutationId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackLocalExpenseId))
        {
            return fallbackLocalExpenseId.Trim();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string? ResolveExpenseId(SaveExpenseResult? result)
    {
        if (!string.IsNullOrWhiteSpace(result?.ExpenseId))
        {
            return result.ExpenseId!.Trim();
        }

        var message = result?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = Regex.Match(message, @"id\s*:\s*([a-zA-Z0-9\-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var id = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static bool IsOfflineLocalId(string? expenseId)
    {
        return !string.IsNullOrWhiteSpace(expenseId)
            && expenseId.StartsWith("offline-", StringComparison.OrdinalIgnoreCase);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectivityService.ConnectivityChanged -= OnConnectivityChanged;
    }
}
