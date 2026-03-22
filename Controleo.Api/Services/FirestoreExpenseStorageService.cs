using Controleo.Api.Models;
using Controleo.Api.Options;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace Controleo.Api.Services;

public sealed class FirestoreExpenseStorageService : IExpenseStorageService
{
    private readonly FirebaseStorageOptions _options;
    private readonly FirestoreDb _firestoreDb;

    public FirestoreExpenseStorageService(IOptions<FirebaseStorageOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ProjectId))
        {
            throw new InvalidOperationException("Debes configurar FirebaseStorage:ProjectId en appsettings.");
        }

        var builder = new FirestoreDbBuilder
        {
            ProjectId = _options.ProjectId
        };

        if (!string.IsNullOrWhiteSpace(_options.CredentialsFilePath))
        {
            builder.CredentialsPath = _options.CredentialsFilePath;
        }

        _firestoreDb = builder.Build();
    }

    public async Task<SaveExpenseResult> SaveAsync(ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogsAsync(cancellationToken);
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
            var collection = _firestoreDb.Collection(_options.CollectionName);
            var now = Timestamp.GetCurrentTimestamp();
            var payload = new Dictionary<string, object>
            {
                ["date"] = request.Date.ToString("yyyy-MM-dd"),
                ["description"] = request.Description.Trim(),
                ["amount"] = Convert.ToDouble(request.Amount),
                ["movementType"] = request.MovementType.Trim(),
                ["paymentMethod"] = request.PaymentMethod.Trim(),
                ["createdAt"] = now,
                ["updatedAt"] = now
            };

            var documentReference = await collection.AddAsync(payload, cancellationToken);

            return new SaveExpenseResult(true, $"Gasto guardado en Firebase (id: {documentReference.Id}).", 0);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"No se pudo guardar en Firebase: {ex.Message}", 0);
        }
    }

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(CancellationToken cancellationToken)
    {
        var query = _firestoreDb
            .Collection(_options.CollectionName)
            .OrderByDescending("createdAt")
            .Limit(300);

        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents
            .Select(MapExpense)
            .Where(item => item is not null)
            .Cast<ExpenseItem>()
            .ToArray();
    }

    public async Task<OperationResult> UpdateExpenseAsync(string expenseId, ExpenseEntryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expenseId))
        {
            return new OperationResult(false, "El id del gasto es requerido.");
        }

        var catalog = await GetCatalogsAsync(cancellationToken);
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
            var doc = _firestoreDb.Collection(_options.CollectionName).Document(expenseId);
            var existing = await doc.GetSnapshotAsync(cancellationToken);
            if (!existing.Exists)
            {
                return new OperationResult(false, "No existe el gasto indicado.");
            }

            var updates = new Dictionary<string, object>
            {
                ["date"] = request.Date.ToString("yyyy-MM-dd"),
                ["description"] = request.Description.Trim(),
                ["amount"] = Convert.ToDouble(request.Amount),
                ["movementType"] = request.MovementType.Trim(),
                ["paymentMethod"] = request.PaymentMethod.Trim(),
                ["updatedAt"] = Timestamp.GetCurrentTimestamp()
            };

            await doc.SetAsync(updates, SetOptions.MergeAll, cancellationToken);
            return new OperationResult(true, "Gasto actualizado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo actualizar el gasto: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteExpenseAsync(string expenseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expenseId))
        {
            return new OperationResult(false, "El id del gasto es requerido.");
        }

        try
        {
            var doc = _firestoreDb.Collection(_options.CollectionName).Document(expenseId);
            var existing = await doc.GetSnapshotAsync(cancellationToken);
            if (!existing.Exists)
            {
                return new OperationResult(false, "No existe el gasto indicado.");
            }

            await doc.DeleteAsync();
            return new OperationResult(true, "Gasto eliminado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo eliminar el gasto: {ex.Message}");
        }
    }

    public async Task<ExpenseCatalog> GetCatalogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var doc = _firestoreDb
                .Collection(_options.SettingsCollectionName)
                .Document(_options.CatalogDocumentId);

            var snapshot = await doc.GetSnapshotAsync(cancellationToken);
            if (!snapshot.Exists)
            {
                return new ExpenseCatalog(_options.MovementTypes, _options.PaymentMethods);
            }

            var movementTypes = snapshot.TryGetValue("movementTypes", out List<string>? movementRaw)
                ? NormalizeCatalog(movementRaw)
                : NormalizeCatalog(_options.MovementTypes);

            var paymentMethods = snapshot.TryGetValue("paymentMethods", out List<string>? paymentRaw)
                ? NormalizeCatalog(paymentRaw)
                : NormalizeCatalog(_options.PaymentMethods);

            return new ExpenseCatalog(movementTypes, paymentMethods);
        }
        catch
        {
            return new ExpenseCatalog(NormalizeCatalog(_options.MovementTypes), NormalizeCatalog(_options.PaymentMethods));
        }
    }

    public async Task<OperationResult> UpdateCatalogsAsync(UpdateCatalogsRequest request, CancellationToken cancellationToken)
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
            var doc = _firestoreDb
                .Collection(_options.SettingsCollectionName)
                .Document(_options.CatalogDocumentId);

            await doc.SetAsync(new Dictionary<string, object>
            {
                ["movementTypes"] = movementTypes,
                ["paymentMethods"] = paymentMethods,
                ["updatedAt"] = Timestamp.GetCurrentTimestamp()
            }, cancellationToken: cancellationToken);

            return new OperationResult(true, "Catálogos actualizados correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudieron actualizar los catálogos: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(CancellationToken cancellationToken)
    {
        var query = _firestoreDb
            .Collection(_options.BudgetsCollectionName)
            .OrderBy("movementType");

        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents
            .Select(MapBudget)
            .Where(item => item is not null)
            .Cast<BudgetItem>()
            .ToArray();
    }

    public async Task<OperationResult> UpsertBudgetAsync(string movementType, BudgetUpsertRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return new OperationResult(false, "La sección es requerida.");
        }

        if (request.Amount < 0)
        {
            return new OperationResult(false, "El presupuesto no puede ser negativo.");
        }

        var catalog = await GetCatalogsAsync(cancellationToken);
        if (!IsAllowedValue(movementType, catalog.MovementTypes))
        {
            return new OperationResult(false, "La sección no existe en configuración.");
        }

        try
        {
            var key = BuildBudgetKey(movementType);
            var doc = _firestoreDb.Collection(_options.BudgetsCollectionName).Document(key);
            await doc.SetAsync(new Dictionary<string, object>
            {
                ["movementType"] = movementType.Trim(),
                ["amount"] = Convert.ToDouble(request.Amount),
                ["updatedAt"] = Timestamp.GetCurrentTimestamp()
            }, cancellationToken: cancellationToken);

            return new OperationResult(true, "Presupuesto guardado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo guardar el presupuesto: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteBudgetAsync(string movementType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return new OperationResult(false, "La sección es requerida.");
        }

        try
        {
            var key = BuildBudgetKey(movementType);
            await _firestoreDb.Collection(_options.BudgetsCollectionName).Document(key).DeleteAsync();
            return new OperationResult(true, "Presupuesto eliminado correctamente.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"No se pudo eliminar el presupuesto: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<DashboardCategoryItem>> GetDashboardByCategoryAsync(CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogsAsync(cancellationToken);
        var budgets = await GetBudgetsAsync(cancellationToken);
        var expenses = await GetExpensesAsync(cancellationToken);

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

    private static string BuildBudgetKey(string movementType)
    {
        return movementType.Trim().Replace("/", "_", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static ExpenseItem? MapExpense(DocumentSnapshot doc)
    {
        if (!doc.Exists)
        {
            return null;
        }

        var dateRaw = doc.TryGetValue("date", out string? dateText) ? dateText : null;
        if (!DateOnly.TryParse(dateRaw, out var date))
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var description = doc.TryGetValue("description", out string? descText) ? descText ?? string.Empty : string.Empty;
        var movementType = doc.TryGetValue("movementType", out string? movementText) ? movementText ?? string.Empty : string.Empty;
        var paymentMethod = doc.TryGetValue("paymentMethod", out string? paymentText) ? paymentText ?? string.Empty : string.Empty;
        var amount = ReadDecimal(doc, "amount");

        var createdAt = doc.TryGetValue("createdAt", out Timestamp createdStamp)
            ? new DateTimeOffset(createdStamp.ToDateTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        var updatedAt = doc.TryGetValue("updatedAt", out Timestamp updatedStamp)
            ? new DateTimeOffset(updatedStamp.ToDateTime(), TimeSpan.Zero)
            : createdAt;

        return new ExpenseItem(
            doc.Id,
            date,
            description,
            amount,
            movementType,
            paymentMethod,
            createdAt,
            updatedAt);
    }

    private static BudgetItem? MapBudget(DocumentSnapshot doc)
    {
        if (!doc.Exists)
        {
            return null;
        }

        var movementType = doc.TryGetValue("movementType", out string? movementText) ? movementText ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(movementType))
        {
            return null;
        }

        var amount = ReadDecimal(doc, "amount");
        var updatedAt = doc.TryGetValue("updatedAt", out Timestamp updatedStamp)
            ? new DateTimeOffset(updatedStamp.ToDateTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        return new BudgetItem(movementType, amount, updatedAt);
    }

    private static decimal ReadDecimal(DocumentSnapshot doc, string fieldName)
    {
        if (!doc.TryGetValue(fieldName, out object? raw) || raw is null)
        {
            return 0m;
        }

        return raw switch
        {
            double d => Convert.ToDecimal(d),
            long l => l,
            int i => i,
            decimal m => m,
            _ => decimal.TryParse(raw.ToString(), out var parsed) ? parsed : 0m
        };
    }
}
