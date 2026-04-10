using System.Globalization;
using System.Text.Json;
using Controleo.Mobile.Core.Interfaces;
using Controleo.Mobile.Core.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace Controleo.Mobile.Core.Services;

public sealed class OfflineDataStore : IOfflineDataStore
{
    private const string DatabaseFileName = "controleo.offline.db3";
    private const string GlobalScope = "global";

    private const string CatalogKind = "catalog";
    private const string MonthsKind = "months";
    private const string ExpensesKind = "expenses";
    private const string DashboardCategoryKind = "dashboard-category";
    private const string DashboardPaymentKind = "dashboard-payment";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private SQLiteAsyncConnection? _connection;
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);
            _connection = new SQLiteAsyncConnection(
                dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

            await _connection.CreateTableAsync<SnapshotEntity>();
            await _connection.CreateTableAsync<MutationEntity>();
            await _connection.CreateTableAsync<DeadLetterEntity>();
            await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_offline_snapshots_user_kind_scope ON offline_snapshots(userId, kind, scopeKey);");
            await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_offline_mutations_user_id ON offline_mutations(userId, id);");
            await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_offline_dead_letters_user_id ON offline_dead_letters(userId, id);");
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task SaveCatalogAsync(string userId, ExpenseCatalog catalog, CancellationToken cancellationToken = default)
    {
        return SaveSnapshotAsync(userId, CatalogKind, GlobalScope, catalog, cancellationToken);
    }

    public Task<ExpenseCatalog?> GetCatalogAsync(string userId, CancellationToken cancellationToken = default)
    {
        return GetSnapshotAsync<ExpenseCatalog>(userId, CatalogKind, GlobalScope, cancellationToken);
    }

    public Task SaveAvailableMonthsAsync(string userId, IReadOnlyList<string> monthKeys, CancellationToken cancellationToken = default)
    {
        var normalized = (monthKeys ?? [])
            .Select(NormalizeMonthKey)
            .Where(month => !string.IsNullOrWhiteSpace(month))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(month => month, StringComparer.Ordinal)
            .ToArray();

        return SaveSnapshotAsync(userId, MonthsKind, GlobalScope, normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAvailableMonthsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var months = await GetSnapshotAsync<string[]>(userId, MonthsKind, GlobalScope, cancellationToken);
        return months?.ToArray() ?? [];
    }

    public async Task AddAvailableMonthAsync(string userId, string monthKey, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var current = await GetAvailableMonthsAsync(userId, cancellationToken);
        var next = current
            .Append(normalized)
            .Where(month => !string.IsNullOrWhiteSpace(month))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(month => month, StringComparer.Ordinal)
            .ToArray();

        await SaveAvailableMonthsAsync(userId, next, cancellationToken);
    }

    public Task SaveExpensesAsync(string userId, string monthKey, IReadOnlyList<ExpenseItem> expenses, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return Task.CompletedTask;
        }

        var normalizedExpenses = NormalizeExpenses(expenses);
        return SaveSnapshotAsync(userId, ExpensesKind, normalizedMonth, normalizedExpenses, cancellationToken);
    }

    public async Task MergeExpensesAsync(string userId, string monthKey, IReadOnlyList<ExpenseItem> expenses, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return;
        }

        var existing = await GetExpensesAsync(userId, normalizedMonth, cancellationToken);
        var pendingState = await GetPendingExpenseMutationStateForMonthAsync(userId, normalizedMonth, cancellationToken);
        var serverItems = (expenses ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToArray();

        var byId = serverItems
            .ToDictionary(item => item.Id, item => item, StringComparer.Ordinal);

        foreach (var localItem in existing)
        {
            if (string.IsNullOrWhiteSpace(localItem.Id))
            {
                continue;
            }

            if (byId.ContainsKey(localItem.Id))
            {
                continue;
            }

            var shouldKeepLocalPending = pendingState.CreateIds.Contains(localItem.Id)
                || pendingState.UpdateIds.Contains(localItem.Id);

            if (!shouldKeepLocalPending)
            {
                continue;
            }

            if (IsOfflineLocalId(localItem.Id)
                && pendingState.CreateIds.Contains(localItem.Id)
                && serverItems.Any(serverItem => IsSameExpenseSnapshot(localItem, serverItem)))
            {
                continue;
            }

            byId[localItem.Id] = localItem;
        }

        await SaveExpensesAsync(userId, normalizedMonth, byId.Values.ToArray(), cancellationToken);
    }

    private async Task<PendingExpenseMutationState> GetPendingExpenseMutationStateForMonthAsync(string userId, string monthKey, CancellationToken cancellationToken)
    {
        var pending = await GetPendingMutationsAsync(userId, 200, cancellationToken);
        var createIds = new HashSet<string>(StringComparer.Ordinal);
        var updateIds = new HashSet<string>(StringComparer.Ordinal);
        var deleteIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mutation in pending)
        {
            if (!string.Equals(NormalizeMonthKey(mutation.ScopeKey), monthKey, StringComparison.Ordinal))
            {
                continue;
            }

            switch (mutation.MutationType)
            {
                case OfflineMutationTypes.ExpenseCreate:
                {
                    var payload = TryDeserialize<OfflineExpenseCreateMutationPayload>(mutation.PayloadJson);
                    if (!string.IsNullOrWhiteSpace(payload?.LocalExpenseId))
                    {
                        createIds.Add(payload.LocalExpenseId);
                    }

                    break;
                }
                case OfflineMutationTypes.ExpenseUpdate:
                {
                    var payload = TryDeserialize<OfflineExpenseUpdateMutationPayload>(mutation.PayloadJson);
                    if (!string.IsNullOrWhiteSpace(payload?.ExpenseId))
                    {
                        updateIds.Add(payload.ExpenseId);
                    }

                    break;
                }
                case OfflineMutationTypes.ExpenseDelete:
                {
                    var payload = TryDeserialize<OfflineExpenseDeleteMutationPayload>(mutation.PayloadJson);
                    if (!string.IsNullOrWhiteSpace(payload?.ExpenseId))
                    {
                        deleteIds.Add(payload.ExpenseId);
                    }

                    break;
                }
            }
        }

        return new PendingExpenseMutationState(createIds, updateIds, deleteIds);
    }

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string monthKey, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return [];
        }

        var items = await GetSnapshotAsync<ExpenseItem[]>(userId, ExpensesKind, normalizedMonth, cancellationToken);
        return NormalizeExpenses(items ?? []);
    }

    public async Task UpsertExpenseAsync(string userId, string monthKey, ExpenseItem expense, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth) || string.IsNullOrWhiteSpace(expense.Id))
        {
            return;
        }

        var existing = await GetExpensesAsync(userId, normalizedMonth, cancellationToken);
        var byId = existing
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, item => item, StringComparer.Ordinal);

        byId[expense.Id] = expense;
        await SaveExpensesAsync(userId, normalizedMonth, byId.Values.ToArray(), cancellationToken);
        await AddAvailableMonthAsync(userId, normalizedMonth, cancellationToken);
    }

    public async Task RemoveExpenseAsync(string userId, string monthKey, string expenseId, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth) || string.IsNullOrWhiteSpace(expenseId))
        {
            return;
        }

        var existing = await GetExpensesAsync(userId, normalizedMonth, cancellationToken);
        var next = existing
            .Where(item => !string.Equals(item.Id, expenseId, StringComparison.Ordinal))
            .ToArray();

        await SaveExpensesAsync(userId, normalizedMonth, next, cancellationToken);
    }

    public async Task<bool> RemoveExpenseAcrossMonthsAsync(string userId, string expenseId, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId) || string.IsNullOrWhiteSpace(expenseId))
        {
            return false;
        }

        var months = await GetAvailableMonthsAsync(normalizedUserId, cancellationToken);
        var removedAny = false;

        foreach (var month in months)
        {
            var expenses = await GetExpensesAsync(normalizedUserId, month, cancellationToken);
            if (!expenses.Any(item => string.Equals(item.Id, expenseId, StringComparison.Ordinal)))
            {
                continue;
            }

            var next = expenses
                .Where(item => !string.Equals(item.Id, expenseId, StringComparison.Ordinal))
                .ToArray();

            await SaveExpensesAsync(normalizedUserId, month, next, cancellationToken);
            removedAny = true;
        }

        return removedAny;
    }

    public Task SaveDashboardByCategoryAsync(string userId, string monthKey, IReadOnlyList<DashboardCategoryItem> items, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return Task.CompletedTask;
        }

        return SaveSnapshotAsync(userId, DashboardCategoryKind, normalizedMonth, (items ?? []).ToArray(), cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string monthKey, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return [];
        }

        var items = await GetSnapshotAsync<DashboardCategoryItem[]>(userId, DashboardCategoryKind, normalizedMonth, cancellationToken);
        return items?.ToArray() ?? [];
    }

    public Task SaveDashboardByPaymentMethodAsync(string userId, string monthKey, IReadOnlyList<DashboardPaymentMethodItem> items, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return Task.CompletedTask;
        }

        return SaveSnapshotAsync(userId, DashboardPaymentKind, normalizedMonth, (items ?? []).ToArray(), cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardPaymentMethodItem>> GetDashboardByPaymentMethodAsync(string userId, string monthKey, CancellationToken cancellationToken = default)
    {
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedMonth))
        {
            return [];
        }

        var items = await GetSnapshotAsync<DashboardPaymentMethodItem[]>(userId, DashboardPaymentKind, normalizedMonth, cancellationToken);
        return items?.ToArray() ?? [];
    }

    public async Task<long> EnqueueMutationAsync(string userId, string mutationType, string scopeKey, string payloadJson, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId) || string.IsNullOrWhiteSpace(mutationType))
        {
            return 0;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var mutation = new MutationEntity
        {
            UserId = normalizedUserId,
            MutationType = mutationType.Trim(),
            ScopeKey = NormalizeScopeKey(scopeKey),
            PayloadJson = payloadJson ?? string.Empty,
            RetryCount = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await connection.InsertAsync(mutation);
        return mutation.Id;
    }

    public async Task<IReadOnlyList<OfflineMutation>> GetPendingMutationsAsync(string userId, int take, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return [];
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var safeTake = Math.Clamp(take, 1, 200);

        var rows = await connection.Table<MutationEntity>()
            .Where(item => item.UserId == normalizedUserId)
            .OrderBy(item => item.Id)
            .Take(safeTake)
            .ToListAsync();

        return rows
            .Select(item => new OfflineMutation(item.Id, item.MutationType, item.PayloadJson, item.ScopeKey, item.RetryCount))
            .ToArray();
    }

    public async Task MarkMutationSucceededAsync(long mutationId, CancellationToken cancellationToken = default)
    {
        if (mutationId <= 0)
        {
            return;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        await connection.DeleteAsync<MutationEntity>(mutationId);
    }

    public async Task IncrementMutationRetryAsync(long mutationId, CancellationToken cancellationToken = default)
    {
        if (mutationId <= 0)
        {
            return;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var row = await connection.FindAsync<MutationEntity>(mutationId);
        if (row is null)
        {
            return;
        }

        row.RetryCount = Math.Max(0, row.RetryCount) + 1;
        await connection.UpdateAsync(row);
    }

    public async Task<bool> TryDropPendingCreateMutationAsync(string userId, string monthKey, string localExpenseId, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUserId(userId);
        var normalizedMonth = NormalizeMonthKey(monthKey);
        if (string.IsNullOrWhiteSpace(normalizedUserId)
            || string.IsNullOrWhiteSpace(normalizedMonth)
            || string.IsNullOrWhiteSpace(localExpenseId))
        {
            return false;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var rows = await connection.Table<MutationEntity>()
            .Where(item => item.UserId == normalizedUserId)
            .OrderBy(item => item.Id)
            .ToListAsync();

        var matchingCreateIds = rows
            .Where(item => string.Equals(item.MutationType, OfflineMutationTypes.ExpenseCreate, StringComparison.Ordinal))
            .Where(item => string.Equals(NormalizeMonthKey(item.ScopeKey), normalizedMonth, StringComparison.Ordinal))
            .Where(item =>
            {
                var payload = TryDeserialize<OfflineExpenseCreateMutationPayload>(item.PayloadJson);
                return string.Equals(payload?.LocalExpenseId, localExpenseId, StringComparison.Ordinal);
            })
            .Select(item => item.Id)
            .ToHashSet();

        if (matchingCreateIds.Count == 0)
        {
            return false;
        }

        var idsToDelete = new HashSet<long>(matchingCreateIds);
        foreach (var row in rows)
        {
            switch (row.MutationType)
            {
                case OfflineMutationTypes.ExpenseUpdate:
                {
                    var payload = TryDeserialize<OfflineExpenseUpdateMutationPayload>(row.PayloadJson);
                    if (string.Equals(payload?.ExpenseId, localExpenseId, StringComparison.Ordinal))
                    {
                        idsToDelete.Add(row.Id);
                    }

                    break;
                }
                case OfflineMutationTypes.ExpenseDelete:
                {
                    var payload = TryDeserialize<OfflineExpenseDeleteMutationPayload>(row.PayloadJson);
                    if (string.Equals(payload?.ExpenseId, localExpenseId, StringComparison.Ordinal))
                    {
                        idsToDelete.Add(row.Id);
                    }

                    break;
                }
            }
        }

        foreach (var id in idsToDelete)
        {
            await connection.DeleteAsync<MutationEntity>(id);
        }

        return true;
    }

    public async Task RemapExpenseIdAsync(string userId, string monthKey, string localExpenseId, string serverExpenseId, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUserId(userId);
        var normalizedMonth = NormalizeMonthKey(monthKey);
        var normalizedLocalId = (localExpenseId ?? string.Empty).Trim();
        var normalizedServerId = (serverExpenseId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedUserId)
            || string.IsNullOrWhiteSpace(normalizedMonth)
            || string.IsNullOrWhiteSpace(normalizedLocalId)
            || string.IsNullOrWhiteSpace(normalizedServerId)
            || string.Equals(normalizedLocalId, normalizedServerId, StringComparison.Ordinal))
        {
            return;
        }

        var monthExpenses = await GetExpensesAsync(normalizedUserId, normalizedMonth, cancellationToken);
        if (monthExpenses.Count > 0)
        {
            var remappedExpenses = monthExpenses
                .Select(item => string.Equals(item.Id, normalizedLocalId, StringComparison.Ordinal)
                    ? item with { Id = normalizedServerId, UpdatedAt = DateTimeOffset.UtcNow }
                    : item)
                .ToArray();

            await SaveExpensesAsync(normalizedUserId, normalizedMonth, remappedExpenses, cancellationToken);
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var rows = await connection.Table<MutationEntity>()
            .Where(item => item.UserId == normalizedUserId)
            .OrderBy(item => item.Id)
            .ToListAsync();

        foreach (var row in rows)
        {
            switch (row.MutationType)
            {
                case OfflineMutationTypes.ExpenseUpdate:
                {
                    var payload = TryDeserialize<OfflineExpenseUpdateMutationPayload>(row.PayloadJson);
                    if (payload is null || !string.Equals(payload.ExpenseId, normalizedLocalId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var updatedPayload = payload with { ExpenseId = normalizedServerId };
                    await UpdateMutationPayloadAsync(connection, row, JsonSerializer.Serialize(updatedPayload, _jsonOptions));
                    break;
                }
                case OfflineMutationTypes.ExpenseDelete:
                {
                    var payload = TryDeserialize<OfflineExpenseDeleteMutationPayload>(row.PayloadJson);
                    if (payload is null || !string.Equals(payload.ExpenseId, normalizedLocalId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var updatedPayload = payload with { ExpenseId = normalizedServerId };
                    await UpdateMutationPayloadAsync(connection, row, JsonSerializer.Serialize(updatedPayload, _jsonOptions));
                    break;
                }
            }
        }
    }

    public async Task AddDeadLetterMutationAsync(string userId, OfflineMutation mutation, string reason, CancellationToken cancellationToken = default)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId) || mutation is null)
        {
            return;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var deadLetter = new DeadLetterEntity
        {
            UserId = normalizedUserId,
            MutationType = mutation.MutationType,
            ScopeKey = NormalizeScopeKey(mutation.ScopeKey),
            PayloadJson = mutation.PayloadJson,
            RetryCount = mutation.RetryCount,
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim(),
            FailedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await connection.InsertAsync(deadLetter);
    }

    private static async Task UpdateMutationPayloadAsync(SQLiteAsyncConnection connection, MutationEntity row, string payloadJson)
    {
        row.PayloadJson = payloadJson;
        await connection.UpdateAsync(row);
    }

    private async Task SaveSnapshotAsync<T>(string userId, string kind, string scopeKey, T payload, CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var entity = new SnapshotEntity
        {
            Id = BuildSnapshotId(normalizedUserId, kind, scopeKey),
            UserId = normalizedUserId,
            Kind = kind,
            ScopeKey = NormalizeScopeKey(scopeKey),
            PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await connection.InsertOrReplaceAsync(entity);
    }

    private async Task<T?> GetSnapshotAsync<T>(string userId, string kind, string scopeKey, CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return default;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        var entity = await connection.FindAsync<SnapshotEntity>(BuildSnapshotId(normalizedUserId, kind, scopeKey));
        if (entity is null || string.IsNullOrWhiteSpace(entity.PayloadJson))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(entity.PayloadJson, _jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return _connection ?? throw new InvalidOperationException("Offline database is not initialized.");
    }

    private static IReadOnlyList<ExpenseItem> NormalizeExpenses(IEnumerable<ExpenseItem> expenses)
    {
        return (expenses ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.UpdatedAt)
            .ToArray();
    }

    private static bool IsOfflineLocalId(string? expenseId)
    {
        return !string.IsNullOrWhiteSpace(expenseId)
            && expenseId.StartsWith("offline-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameExpenseSnapshot(ExpenseItem left, ExpenseItem right)
    {
        return left.Date == right.Date
            && left.Amount == right.Amount
            && string.Equals(left.Description?.Trim(), right.Description?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.MovementType?.Trim(), right.MovementType?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.PaymentMethod?.Trim(), right.PaymentMethod?.Trim(), StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeMonthKey(string? monthKey)
    {
        if (string.IsNullOrWhiteSpace(monthKey))
        {
            return string.Empty;
        }

        var candidate = monthKey.Trim();
        if (!DateOnly.TryParseExact(
                $"{candidate}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            return string.Empty;
        }

        return candidate;
    }

    private static string NormalizeUserId(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }

    private static string NormalizeScopeKey(string? scopeKey)
    {
        return string.IsNullOrWhiteSpace(scopeKey) ? GlobalScope : scopeKey.Trim();
    }

    private static string BuildSnapshotId(string userId, string kind, string scopeKey)
    {
        return $"{NormalizeUserId(userId)}|{kind}|{NormalizeScopeKey(scopeKey)}";
    }

    [Table("offline_snapshots")]
    private sealed class SnapshotEntity
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [Indexed]
        public string UserId { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string ScopeKey { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        public string UpdatedAt { get; set; } = string.Empty;
    }

    [Table("offline_mutations")]
    private sealed class MutationEntity
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }

        [Indexed]
        public string UserId { get; set; } = string.Empty;

        public string MutationType { get; set; } = string.Empty;

        public string ScopeKey { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        public int RetryCount { get; set; }

        public string CreatedAt { get; set; } = string.Empty;
    }

    [Table("offline_dead_letters")]
    private sealed class DeadLetterEntity
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }

        [Indexed]
        public string UserId { get; set; } = string.Empty;

        public string MutationType { get; set; } = string.Empty;

        public string ScopeKey { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        public int RetryCount { get; set; }

        public string Reason { get; set; } = string.Empty;

        public string FailedAt { get; set; } = string.Empty;
    }

    private sealed record PendingExpenseMutationState(
        HashSet<string> CreateIds,
        HashSet<string> UpdateIds,
        HashSet<string> DeleteIds);
}
