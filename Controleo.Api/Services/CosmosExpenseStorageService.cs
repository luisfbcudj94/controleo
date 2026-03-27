using System.Globalization;
using System.Net;
using Controleo.Api.Models;
using Controleo.Api.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Services;

public sealed class CosmosExpenseStorageService : IExpenseStorageService
{
    private readonly CosmosStorageOptions _options;
    private readonly Container _expensesContainer;
    private readonly Container _budgetsContainer;
    private readonly Container _recurringContainer;
    private readonly Container _settingsContainer;

    public CosmosExpenseStorageService(IOptions<CosmosStorageOptions> options)
    {
        _options = options.Value;

        var endpoint = ResolveEndpoint(_options.Endpoint);
        var key = ResolveKey(_options.Key);

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("No se pudo resolver el endpoint de Cosmos DB. Configura CosmosStorage:Endpoint o COSMOS_DB_ENDPOINT.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("No se pudo resolver la llave de Cosmos DB. Configura CosmosStorage:Key o COSMOS_DB_KEY.");
        }

        var client = new CosmosClient(endpoint, key, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });

        var database = client.GetDatabase(_options.DatabaseName);
        _expensesContainer = database.GetContainer(_options.ExpensesContainerName);
        _budgetsContainer = database.GetContainer(_options.BudgetsContainerName);
        _recurringContainer = database.GetContainer(_options.RecurringExpensesContainerName);
        _settingsContainer = database.GetContainer(_options.SettingsContainerName);
    }

    public async Task<SaveExpenseResult> SaveAsync(string userId, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogsAsync(userId, cancellationToken);
        if (!IsAllowedValue(request.MovementType, catalog.MovementTypes))
        {
            return new SaveExpenseResult(false, "Tipo de movimiento no permitido.", 0);
        }

        if (!IsAllowedValue(request.PaymentMethod, catalog.PaymentMethods))
        {
            return new SaveExpenseResult(false, "Medio de pago no permitido.", 0);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var document = new ExpenseDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Date = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MonthKey = request.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Description = request.Description.Trim(),
                Amount = request.Amount,
                MovementType = request.MovementType.Trim(),
                PaymentMethod = request.PaymentMethod.Trim(),
                SourceRecurringId = null,
                SourceRecurringMonth = null,
                CreatedAt = now.ToString("O", CultureInfo.InvariantCulture),
                UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
            };

            await _expensesContainer.CreateItemAsync(document, new PartitionKey(document.MonthKey), cancellationToken: cancellationToken);
            return new SaveExpenseResult(true, $"Gasto guardado en Cosmos (id: {document.Id}).", 0);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"No se pudo guardar en Cosmos: {ex.Message}", 0);
        }
    }

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string monthKey, CancellationToken cancellationToken)
    {
        await EnsureRecurringExpensesGeneratedAsync(userId, monthKey, cancellationToken);

        var byMonth = await QueryAsync<ExpenseDocument>(
            _expensesContainer,
            new QueryDefinition("SELECT * FROM c WHERE c.monthKey = @monthKey AND c.userId = @userId")
                .WithParameter("@monthKey", monthKey)
                .WithParameter("@userId", userId),
            cancellationToken);

        var mapped = byMonth
            .Select(MapExpense)
            .Where(item => item is not null)
            .Cast<ExpenseItem>()
            .OrderByDescending(item => item.Date)
            .ThenByDescending(item => item.UpdatedAt)
            .ToArray();

        return mapped;
    }

    public async Task<PagedExpenseResult> GetExpensesPageAsync(string userId, string monthKey, int pageNumber, int pageSize, string? movementType, string? searchTerm, CancellationToken cancellationToken)
    {
        var normalizedPageNumber = pageNumber < 1 ? 1 : pageNumber;
        var normalizedPageSize = pageSize <= 0 ? 5 : pageSize;

        var data = await GetExpensesAsync(userId, monthKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(movementType))
        {
            data = data
                .Where(item => string.Equals(item.MovementType, movementType.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var normalizedSearchTerm = searchTerm.Trim();
                data = data
                .Where(item => item.Description.Contains(normalizedSearchTerm, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            }

        var totalCount = data.Count;
        var totalAmount = data.Sum(item => item.Amount);
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);
        var safePageNumber = Math.Min(normalizedPageNumber, totalPages);
        var skip = (safePageNumber - 1) * normalizedPageSize;
        var items = data
            .Skip(skip)
            .Take(normalizedPageSize)
            .ToArray();

        return new PagedExpenseResult(
            items,
            safePageNumber,
            normalizedPageSize,
            totalCount,
            totalAmount,
            totalPages,
            safePageNumber > 1,
            safePageNumber < totalPages);
    }

    public async Task<IReadOnlyList<string>> GetAvailableMonthKeysAsync(string userId, CancellationToken cancellationToken)
    {
        var snapshot = await QueryAsync<ExpenseMonthProjection>(
            _expensesContainer,
            new QueryDefinition("SELECT c.monthKey, c.date FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            cancellationToken);

        var monthKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot)
        {
            if (IsValidMonthKey(row.MonthKey))
            {
                monthKeys.Add(row.MonthKey!);
                continue;
            }

            if (TryParseDate(row.Date, out var date))
            {
                monthKeys.Add(date.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            }
        }

        return monthKeys
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<OperationResult> UpdateExpenseAsync(string userId, string expenseId, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expenseId))
        {
            return new OperationResult(false, "El id del gasto es requerido.");
        }

        var catalog = await GetCatalogsAsync(userId, cancellationToken);
        if (!IsAllowedValue(request.MovementType, catalog.MovementTypes))
        {
            return new OperationResult(false, "Tipo de movimiento no permitido.");
        }

        if (!IsAllowedValue(request.PaymentMethod, catalog.PaymentMethods))
        {
            return new OperationResult(false, "Medio de pago no permitido.");
        }

        try
        {
            var existing = await GetExpenseDocumentByIdAsync(userId, expenseId, cancellationToken);
            if (existing is null)
            {
                return new OperationResult(false, "No existe el gasto indicado.");
            }

            var targetMonthKey = request.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var updated = new ExpenseDocument
            {
                Id = existing.Id,
                UserId = userId,
                Date = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MonthKey = targetMonthKey,
                Description = request.Description.Trim(),
                Amount = request.Amount,
                MovementType = request.MovementType.Trim(),
                PaymentMethod = request.PaymentMethod.Trim(),
                SourceRecurringId = existing.SourceRecurringId,
                SourceRecurringMonth = existing.SourceRecurringMonth,
                CreatedAt = string.IsNullOrWhiteSpace(existing.CreatedAt)
                    ? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    : existing.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            await _expensesContainer.UpsertItemAsync(updated, new PartitionKey(updated.MonthKey), cancellationToken: cancellationToken);

            if (!string.Equals(existing.MonthKey, updated.MonthKey, StringComparison.Ordinal))
            {
                await DeleteExpenseDocumentAsync(existing.Id, existing.MonthKey, cancellationToken);
            }

            return new OperationResult(true, "Gasto actualizado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo actualizar el gasto: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expenseId))
        {
            return new OperationResult(false, "El id del gasto es requerido.");
        }

        try
        {
            var existing = await GetExpenseDocumentByIdAsync(userId, expenseId, cancellationToken);
            if (existing is null)
            {
                return new OperationResult(false, "No existe el gasto indicado.");
            }

            await DeleteExpenseDocumentAsync(existing.Id, existing.MonthKey, cancellationToken);
            return new OperationResult(true, "Gasto eliminado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo eliminar el gasto: {ex.Message}");
        }
    }

    public async Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken cancellationToken)
    {
        var catalogDocumentId = BuildCatalogDocId(userId);

        try
        {
            var response = await _settingsContainer.ReadItemAsync<CatalogDocument>(
                catalogDocumentId,
                new PartitionKey(catalogDocumentId),
                cancellationToken: cancellationToken);

            var movementTypes = NormalizeCatalog(response.Resource.MovementTypes);
            var paymentMethods = NormalizeCatalog(response.Resource.PaymentMethods);
            var configs = MapConfigs(response.Resource.MovementTypeConfigs);

            if (movementTypes.Length == 0 || paymentMethods.Length == 0)
            {
                return new ExpenseCatalog(
                    movementTypes.Length == 0 ? NormalizeCatalog(_options.MovementTypes) : movementTypes,
                    paymentMethods.Length == 0 ? NormalizeCatalog(_options.PaymentMethods) : paymentMethods,
                    configs);
            }

            return new ExpenseCatalog(movementTypes, paymentMethods, configs);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new ExpenseCatalog(NormalizeCatalog(_options.MovementTypes), NormalizeCatalog(_options.PaymentMethods));
        }
        catch
        {
            return new ExpenseCatalog(NormalizeCatalog(_options.MovementTypes), NormalizeCatalog(_options.PaymentMethods));
        }
    }

    public async Task<OperationResult> UpdateCatalogsAsync(string userId, UpdateCatalogsRequest request, CancellationToken cancellationToken)
    {
        var movementTypes = NormalizeCatalog(request.MovementTypes);
        var paymentMethods = NormalizeCatalog(request.PaymentMethods);

        if (movementTypes.Length == 0)
        {
            return new OperationResult(false, "Debe existir al menos una sección de gasto.");
        }

        if (paymentMethods.Length == 0)
        {
            return new OperationResult(false, "Debe existir al menos un medio de pago.");
        }

        try
        {
            var catalogDocumentId = BuildCatalogDocId(userId);
            var payload = new CatalogDocument
            {
                Id = catalogDocumentId,
                UserId = userId,
                MovementTypes = movementTypes,
                PaymentMethods = paymentMethods,
                MovementTypeConfigs = (request.MovementTypeConfigs ?? [])
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                    .Select(c => new MovementTypeConfigDoc
                    {
                        Name = c.Name.Trim(),
                        Icon = string.IsNullOrWhiteSpace(c.Icon) ? "\ud83d\udccb" : c.Icon.Trim(),
                        Color = string.IsNullOrWhiteSpace(c.Color) ? "#D8F3DC" : c.Color.Trim()
                    })
                    .ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            await _settingsContainer.UpsertItemAsync(payload, new PartitionKey(payload.Id), cancellationToken: cancellationToken);
            return new OperationResult(true, "Catálogos actualizados correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudieron actualizar los catálogos: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string userId, CancellationToken cancellationToken)
    {
        var data = await QueryAsync<BudgetDocument>(
            _budgetsContainer,
            new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            cancellationToken);

        return data
            .Select(MapBudget)
            .Where(item => item is not null)
            .Cast<BudgetItem>()
            .GroupBy(item => item.MovementType, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(item => item.MovementType)
            .ToArray();
    }

    public async Task<OperationResult> UpsertBudgetAsync(string userId, string movementType, BudgetUpsertRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return new OperationResult(false, "La sección es requerida.");
        }

        if (request.Amount < 0)
        {
            return new OperationResult(false, "El presupuesto no puede ser negativo.");
        }

        var normalizedMovementType = movementType.Trim();
        var catalog = await GetCatalogsAsync(userId, cancellationToken);
        if (!IsAllowedValue(normalizedMovementType, catalog.MovementTypes))
        {
            return new OperationResult(false, "La sección no existe en configuración.");
        }

        try
        {
            var payload = new BudgetDocument
            {
                Id = BuildBudgetKey(userId, normalizedMovementType),
                UserId = userId,
                MovementType = normalizedMovementType,
                Amount = request.Amount,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            await _budgetsContainer.UpsertItemAsync(payload, new PartitionKey(payload.MovementType), cancellationToken: cancellationToken);
            return new OperationResult(true, "Presupuesto guardado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo guardar el presupuesto: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteBudgetAsync(string userId, string movementType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return new OperationResult(false, "La sección es requerida.");
        }

        try
        {
            var normalizedMovementType = movementType.Trim();
            var key = BuildBudgetKey(userId, normalizedMovementType);

            try
            {
                await _budgetsContainer.DeleteItemAsync<BudgetDocument>(key, new PartitionKey(normalizedMovementType), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }

            var legacy = await QueryAsync<BudgetIdentity>(
                _budgetsContainer,
                new QueryDefinition("SELECT c.id, c.movementType FROM c WHERE c.movementType = @movementType AND c.userId = @userId")
                    .WithParameter("@movementType", normalizedMovementType)
                    .WithParameter("@userId", userId),
                cancellationToken);

            foreach (var item in legacy)
            {
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.MovementType))
                {
                    continue;
                }

                if (string.Equals(item.Id, key, StringComparison.Ordinal))
                {
                    continue;
                }

                await _budgetsContainer.DeleteItemAsync<BudgetDocument>(item.Id, new PartitionKey(item.MovementType), cancellationToken: cancellationToken);
            }

            return new OperationResult(true, "Presupuesto eliminado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo eliminar el presupuesto: {ex.Message}");
        }
    }

    public async Task<int> CountExpensesByMovementTypeAsync(string userId, string movementType, CancellationToken cancellationToken)
    {
        try
        {
            var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.userId = @userId AND c.movementType = @mt")
                .WithParameter("@userId", userId)
                .WithParameter("@mt", movementType);

            var results = await QueryAsync<int>(_expensesContainer, query, cancellationToken);
            return results.FirstOrDefault();
        }
        catch
        {
            return 0;
        }
    }

    public async Task<OperationResult> DeleteAllByMovementTypeAsync(string userId, string movementType, CancellationToken cancellationToken)
    {
        try
        {
            // Delete all expenses with this movement type
            var expenses = await QueryAsync<ExpenseDocument>(
                _expensesContainer,
                new QueryDefinition("SELECT c.id, c.monthKey FROM c WHERE c.userId = @userId AND c.movementType = @mt")
                    .WithParameter("@userId", userId)
                    .WithParameter("@mt", movementType),
                cancellationToken);

            foreach (var expense in expenses)
            {
                try
                {
                    await _expensesContainer.DeleteItemAsync<ExpenseDocument>(
                        expense.Id, new PartitionKey(expense.MonthKey), cancellationToken: cancellationToken);
                }
                catch { /* continue deleting others */ }
            }

            // Delete budget for this movement type
            await DeleteBudgetAsync(userId, movementType, cancellationToken);

            // Delete recurring expenses with this movement type
            var recurring = await QueryAsync<RecurringExpenseDocument>(
                _recurringContainer,
                new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @userId AND c.movementType = @mt")
                    .WithParameter("@userId", userId)
                    .WithParameter("@mt", movementType),
                cancellationToken);

            foreach (var rec in recurring)
            {
                try
                {
                    await _recurringContainer.DeleteItemAsync<RecurringExpenseDocument>(
                        rec.Id, new PartitionKey(rec.Id), cancellationToken: cancellationToken);
                }
                catch { /* continue */ }
            }

            return new OperationResult(true, $"Se eliminaron {expenses.Count} gastos, presupuesto y recurrentes del tipo '{movementType}'.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error eliminando datos del tipo '{movementType}': {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(string userId, string monthKey, CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogsAsync(userId, cancellationToken);
        var budgets = await GetBudgetsAsync(userId, cancellationToken);
        var expenses = await GetExpensesAsync(userId, monthKey, cancellationToken);

        var budgetMap = budgets.ToDictionary(item => item.MovementType, item => item.Amount, StringComparer.OrdinalIgnoreCase);
        var expenseMap = expenses
            .GroupBy(item => item.MovementType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount), StringComparer.OrdinalIgnoreCase);

        var allCategories = catalog.MovementTypes
            .Concat(budgetMap.Keys)
            .Concat(expenseMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToArray();

        return allCategories
            .Select(category =>
            {
                var expenseTotal = expenseMap.TryGetValue(category, out var expenseValue) ? expenseValue : 0m;
                var budgetTotal = budgetMap.TryGetValue(category, out var budgetValue) ? budgetValue : 0m;
                return new DashboardCategoryItem(category, expenseTotal, budgetTotal, budgetTotal - expenseTotal);
            })
            .OrderByDescending(item => item.ExpenseTotal)
            .ToArray();
    }

    public async Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken cancellationToken)
    {
        var data = await QueryAsync<RecurringExpenseDocument>(
            _recurringContainer,
            new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            cancellationToken);

        return data
            .Select(MapRecurringExpense)
            .Where(item => item is not null)
            .Cast<RecurringExpenseItem>()
            .OrderBy(item => item.DayOfMonth)
            .ThenBy(item => item.Description)
            .ToArray();
    }

    public async Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? recurringExpenseId, RecurringExpenseUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.Amount < 0)
        {
            return new OperationResult(false, "El valor no puede ser negativo.");
        }

        if (request.DayOfMonth is < 1 or > 31)
        {
            return new OperationResult(false, "El día del mes debe estar entre 1 y 31.");
        }

        if (!TryResolveMonthKey(request.StartMonth, request.StartDate, out var startMonth))
        {
            return new OperationResult(false, "El mes de inicio es inválido.");
        }

        var catalog = await GetCatalogsAsync(userId, cancellationToken);
        if (!IsAllowedValue(request.MovementType, catalog.MovementTypes))
        {
            return new OperationResult(false, "Tipo de movimiento no permitido.");
        }

        if (!IsAllowedValue(request.PaymentMethod, catalog.PaymentMethods))
        {
            return new OperationResult(false, "Medio de pago no permitido.");
        }

        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(recurringExpenseId) ? Guid.NewGuid().ToString("N") : recurringExpenseId.Trim();

        try
        {
            var existing = await GetRecurringExpenseDocumentByIdAsync(userId, id, cancellationToken);
            var payload = new RecurringExpenseDocument
            {
                Id = id,
                UserId = userId,
                Description = request.Description.Trim(),
                Amount = request.Amount,
                MovementType = request.MovementType.Trim(),
                PaymentMethod = request.PaymentMethod.Trim(),
                DayOfMonth = request.DayOfMonth,
                StartMonth = startMonth,
                EndMonth = null,
                IsActive = request.IsActive,
                CreatedAt = existing?.CreatedAt ?? now.ToString("O", CultureInfo.InvariantCulture),
                UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
            };

            await _recurringContainer.UpsertItemAsync(payload, new PartitionKey(payload.UserId), cancellationToken: cancellationToken);
            return new OperationResult(true, "Gasto recurrente guardado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo guardar el gasto recurrente: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string recurringExpenseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recurringExpenseId))
        {
            return new OperationResult(false, "El id del gasto recurrente es requerido.");
        }

        try
        {
            await _recurringContainer.DeleteItemAsync<RecurringExpenseDocument>(recurringExpenseId.Trim(), new PartitionKey(userId), cancellationToken: cancellationToken);
            return new OperationResult(true, "Gasto recurrente eliminado correctamente.");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new OperationResult(false, "No existe el gasto recurrente indicado.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo eliminar el gasto recurrente: {ex.Message}");
        }
    }

    public async Task<OperationResult> PurgeAllDataAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            await DeleteUserDocumentsAsync(_expensesContainer, userId, "monthKey", cancellationToken);
            await DeleteUserDocumentsAsync(_budgetsContainer, userId, "movementType", cancellationToken);
            await DeleteUserDocumentsAsync(_recurringContainer, userId, "userId", cancellationToken);
            return new OperationResult(true, "Datos del usuario eliminados correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo limpiar la base de datos: {ex.Message}");
        }
    }

    private async Task<ExpenseDocument?> GetExpenseDocumentByIdAsync(string userId, string expenseId, CancellationToken cancellationToken)
    {
        var data = await QueryAsync<ExpenseDocument>(
            _expensesContainer,
            new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id AND c.userId = @userId")
                .WithParameter("@id", expenseId)
                .WithParameter("@userId", userId),
            cancellationToken);

        return data.FirstOrDefault();
    }

    private async Task<RecurringExpenseDocument?> GetRecurringExpenseDocumentByIdAsync(string userId, string recurringExpenseId, CancellationToken cancellationToken)
    {
        var data = await QueryAsync<RecurringExpenseDocument>(
            _recurringContainer,
            new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id AND c.userId = @userId")
                .WithParameter("@id", recurringExpenseId)
                .WithParameter("@userId", userId),
            cancellationToken);

        return data.FirstOrDefault();
    }

    private async Task DeleteExpenseDocumentAsync(string id, string? monthKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(monthKey))
        {
            return;
        }

        try
        {
            await _expensesContainer.DeleteItemAsync<ExpenseDocument>(id, new PartitionKey(monthKey), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private async Task DeleteUserDocumentsAsync(Container container, string userId, string partitionField, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<IdentityPartitionRow>(
            container,
            new QueryDefinition($"SELECT c.id, c.{partitionField} AS partitionKey FROM c WHERE c.userId = @userId")
                .WithParameter("@userId", userId),
            cancellationToken);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.PartitionKey))
            {
                continue;
            }

            try
            {
                await container.DeleteItemAsync<object>(row.Id, new PartitionKey(row.PartitionKey), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
    }

    private async Task EnsureRecurringExpensesGeneratedAsync(string userId, string monthKey, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact($"{monthKey}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthStart))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonthKey = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        if (string.CompareOrdinal(monthKey, currentMonthKey) > 0)
        {
            return;
        }

        var recurringItems = await QueryAsync<RecurringExpenseDocument>(
            _recurringContainer,
            new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId AND c.isActive = true")
                .WithParameter("@userId", userId),
            cancellationToken);

        if (recurringItems.Count == 0)
        {
            return;
        }

        var catalog = await GetCatalogsAsync(userId, cancellationToken);

        foreach (var recurring in recurringItems)
        {
            if (string.IsNullOrWhiteSpace(recurring.Id))
            {
                continue;
            }

            if (!IsRecurringApplicableToMonth(recurring, monthKey))
            {
                continue;
            }

            var dueDay = Math.Min(Math.Max(recurring.DayOfMonth, 1), DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
            var dueDate = new DateOnly(monthStart.Year, monthStart.Month, dueDay);

            if (monthKey == currentMonthKey && dueDate > today)
            {
                continue;
            }

            if (!IsAllowedValue(recurring.MovementType ?? string.Empty, catalog.MovementTypes)
                || !IsAllowedValue(recurring.PaymentMethod ?? string.Empty, catalog.PaymentMethods))
            {
                continue;
            }

            var existing = await QueryAsync<ExpenseIdentity>(
                _expensesContainer,
                new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.monthKey = @monthKey AND c.userId = @userId AND c.sourceRecurringId = @sourceRecurringId AND c.sourceRecurringMonth = @sourceRecurringMonth")
                    .WithParameter("@monthKey", monthKey)
                    .WithParameter("@userId", userId)
                    .WithParameter("@sourceRecurringId", recurring.Id)
                    .WithParameter("@sourceRecurringMonth", monthKey),
                cancellationToken);

            if (existing.Count > 0)
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var expense = new ExpenseDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Date = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MonthKey = monthKey,
                Description = recurring.Description?.Trim() ?? string.Empty,
                Amount = recurring.Amount,
                MovementType = recurring.MovementType?.Trim() ?? string.Empty,
                PaymentMethod = recurring.PaymentMethod?.Trim() ?? string.Empty,
                SourceRecurringId = recurring.Id,
                SourceRecurringMonth = monthKey,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _expensesContainer.CreateItemAsync(expense, new PartitionKey(monthKey), cancellationToken: cancellationToken);
        }
    }

    private static async Task<List<T>> QueryAsync<T>(Container container, QueryDefinition queryDefinition, CancellationToken cancellationToken)
    {
        var iterator = container.GetItemQueryIterator<T>(queryDefinition);
        var results = new List<T>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }

    private static string ResolveEndpoint(string configuredEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(configuredEndpoint) && !configuredEndpoint.StartsWith("TU_", StringComparison.OrdinalIgnoreCase))
        {
            return configuredEndpoint.Trim();
        }

        return (Environment.GetEnvironmentVariable("COSMOS_DB_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
            ?? string.Empty).Trim();
    }

    private static string ResolveKey(string configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey) && !configuredKey.StartsWith("TU_", StringComparison.OrdinalIgnoreCase))
        {
            return configuredKey.Trim();
        }

        return (Environment.GetEnvironmentVariable("COSMOS_DB_KEY")
            ?? Environment.GetEnvironmentVariable("COSMOS_KEY")
            ?? string.Empty).Trim();
    }

    private static bool IsAllowedValue(string value, IEnumerable<string> allowedValues)
    {
        var normalizedValue = value.Trim();
        return allowedValues.Any(item => string.Equals(item.Trim(), normalizedValue, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] NormalizeCatalog(IEnumerable<string>? source)
    {
        return (source ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildBudgetKey(string userId, string movementType)
    {
        var safeUser = userId.Trim().Replace("/", "_", StringComparison.Ordinal).ToLowerInvariant();
        var safeMovementType = movementType.Trim().Replace("/", "_", StringComparison.Ordinal).ToLowerInvariant();
        return $"{safeUser}-budget-{safeMovementType}";
    }

    private static string BuildCatalogDocId(string userId)
    {
        var safeUser = userId.Trim().Replace("/", "_", StringComparison.Ordinal).ToLowerInvariant();
        return $"{safeUser}-catalogs";
    }

    private static bool IsRecurringApplicableToMonth(RecurringExpenseDocument recurring, string monthKey)
    {
        if (!IsValidMonthKey(monthKey) || !IsValidMonthKey(recurring.StartMonth))
        {
            return false;
        }

        if (string.CompareOrdinal(monthKey, recurring.StartMonth) < 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsValidMonthKey(string? monthKey)
    {
        if (string.IsNullOrWhiteSpace(monthKey))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            $"{monthKey}-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static bool TryResolveMonthKey(string? monthKey, string? dateText, out string resolvedMonthKey)
    {
        if (IsValidMonthKey(monthKey))
        {
            resolvedMonthKey = monthKey!.Trim();
            return true;
        }

        if (DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateFromInvariant))
        {
            resolvedMonthKey = dateFromInvariant.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return true;
        }

        if (DateOnly.TryParse(dateText, out var dateFromCulture))
        {
            resolvedMonthKey = dateFromCulture.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return true;
        }

        resolvedMonthKey = string.Empty;
        return false;
    }

    private static bool TryParseDate(string? rawDate, out DateOnly date)
    {
        if (DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateOnly.TryParse(rawDate, out date);
    }

    private static ExpenseItem? MapExpense(ExpenseDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.UserId))
        {
            return null;
        }

        if (!DateOnly.TryParse(doc.Date, out var date))
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var createdAt = ParseDateTimeOffset(doc.CreatedAt, DateTimeOffset.UtcNow);
        var updatedAt = ParseDateTimeOffset(doc.UpdatedAt, createdAt);

        return new ExpenseItem(
            doc.Id,
            date,
            doc.Description ?? string.Empty,
            doc.Amount,
            doc.MovementType ?? string.Empty,
            doc.PaymentMethod ?? string.Empty,
            createdAt,
            updatedAt);
    }

    private static RecurringExpenseItem? MapRecurringExpense(RecurringExpenseDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.Id) || string.IsNullOrWhiteSpace(doc.UserId))
        {
            return null;
        }

        var createdAt = ParseDateTimeOffset(doc.CreatedAt, DateTimeOffset.UtcNow);
        var updatedAt = ParseDateTimeOffset(doc.UpdatedAt, createdAt);

        return new RecurringExpenseItem(
            doc.Id,
            doc.Description ?? string.Empty,
            doc.Amount,
            doc.MovementType ?? string.Empty,
            doc.PaymentMethod ?? string.Empty,
            doc.DayOfMonth,
            doc.StartMonth ?? string.Empty,
            doc.EndMonth,
            doc.IsActive,
            createdAt,
            updatedAt);
    }

    private static BudgetItem? MapBudget(BudgetDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.MovementType))
        {
            return null;
        }

        var updatedAt = ParseDateTimeOffset(doc.UpdatedAt, DateTimeOffset.UtcNow);
        return new BudgetItem(doc.MovementType, doc.Amount, updatedAt);
    }

    private static IReadOnlyList<MovementTypeConfig> MapConfigs(MovementTypeConfigDoc[]? docs)
    {
        if (docs is null || docs.Length == 0)
        {
            return [];
        }

        return docs
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new MovementTypeConfig(d.Name.Trim(), d.Icon ?? "📋", d.Color ?? "#D8F3DC"))
            .ToList();
    }

    private static DateTimeOffset ParseDateTimeOffset(string? raw, DateTimeOffset fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return fallback;
        }

        return parsed.ToUniversalTime();
    }

    private sealed class ExpenseDocument
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string MonthKey { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string MovementType { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string? SourceRecurringId { get; set; }
        public string? SourceRecurringMonth { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class BudgetDocument
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string MovementType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class CatalogDocument
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string[] MovementTypes { get; set; } = [];
        public string[] PaymentMethods { get; set; } = [];
        public MovementTypeConfigDoc[] MovementTypeConfigs { get; set; } = [];
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class MovementTypeConfigDoc
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    private sealed class RecurringExpenseDocument
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string MovementType { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public int DayOfMonth { get; set; }
        public string StartMonth { get; set; } = string.Empty;
        public string? EndMonth { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class ExpenseMonthProjection
    {
        public string? MonthKey { get; set; }
        public string? Date { get; set; }
    }

    private sealed class ExpenseIdentity
    {
        public string? Id { get; set; }
    }

    private sealed class BudgetIdentity
    {
        public string? Id { get; set; }
        public string? MovementType { get; set; }
    }

    private sealed class IdentityPartitionRow
    {
        public string? Id { get; set; }
        public string? PartitionKey { get; set; }
    }
}
