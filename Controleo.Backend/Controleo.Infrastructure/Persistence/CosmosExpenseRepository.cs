using System.Globalization;
using System.Net;
using Controleo.Domain.Common;
using Controleo.Domain.Entities;
using Controleo.Domain.Interfaces;
using Microsoft.Azure.Cosmos;

namespace Controleo.Infrastructure.Persistence;

public sealed class CosmosExpenseRepository(CosmosContainerProvider p, ICatalogRepository catalogRepo) : IExpenseRepository
{
    public async Task<SaveExpenseResult> SaveAsync(
        string userId,
        string desc,
        decimal amount,
        DateOnly date,
        string mt,
        string pm,
        bool isCredit,
        int? installments,
        string? clientMutationId,
        CancellationToken ct)
    {
        try
        {
            var normalizedClientMutationId = NormalizeClientMutationId(clientMutationId);
            if (!string.IsNullOrWhiteSpace(normalizedClientMutationId))
            {
                var existing = await CosmosHelper.QueryAsync<ExpenseIdentity>(
                    p.Expenses,
                    new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.userId = @uid AND c.clientMutationId = @cmid")
                        .WithParameter("@uid", userId)
                        .WithParameter("@cmid", normalizedClientMutationId),
                    ct);

                var existingId = existing.FirstOrDefault()?.Id;
                if (!string.IsNullOrWhiteSpace(existingId))
                {
                    return new SaveExpenseResult(true, $"Gasto guardado (id: {existingId}).", 0, existingId);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var doc = new ExpenseDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MonthKey = date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Description = desc.Trim(),
                Amount = amount,
                MovementType = mt.Trim(),
                PaymentMethod = pm.Trim(),
                IsCredit = isCredit,
                Installments = installments,
                ClientMutationId = normalizedClientMutationId,
                CreatedAt = now.ToString("O", CultureInfo.InvariantCulture),
                UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
            };

            await p.Expenses.CreateItemAsync(doc, new PartitionKey(doc.MonthKey), cancellationToken: ct);
            return new SaveExpenseResult(true, $"Gasto guardado (id: {doc.Id}).", 0, doc.Id);
        }
        catch (Exception ex)
        {
            return new SaveExpenseResult(false, $"Error: {ex.Message}", 0);
        }
    }

    public async Task<IReadOnlyList<ExpenseItem>> GetExpensesAsync(string userId, string mk, CancellationToken ct)
    {
        await EnsureRecurringAsync(userId, mk, ct);
        var data = await CosmosHelper.QueryAsync<ExpenseDocument>(
            p.Expenses,
            new QueryDefinition("SELECT * FROM c WHERE c.monthKey = @mk AND c.userId = @uid")
                .WithParameter("@mk", mk)
                .WithParameter("@uid", userId),
            ct);

        return data
            .Select(MapExpense)
            .Where(e => e is not null)
            .Cast<ExpenseItem>()
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.UpdatedAt)
            .ToArray();
    }

    public async Task<PagedExpenseResult> GetExpensesPageAsync(string userId, string mk, int pn, int ps, string? mt, string? pm, string? st, CancellationToken ct)
    {
        var all = await GetExpensesAsync(userId, mk, ct);
        IReadOnlyList<ExpenseItem> data = all;

        if (!string.IsNullOrWhiteSpace(mt))
            data = data.Where(e => string.Equals(e.MovementType, mt.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

        if (!string.IsNullOrWhiteSpace(pm))
            data = data.Where(e => string.Equals(e.PaymentMethod, pm.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

        if (!string.IsNullOrWhiteSpace(st))
            data = data.Where(e => e.Description.Contains(st.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

        var total = data.Count;
        var tp = total == 0 ? 1 : (int)Math.Ceiling(total / (double)ps);
        var sp = Math.Min(Math.Max(pn, 1), tp);
        var items = data.Skip((sp - 1) * ps).Take(ps).ToArray();

        return new PagedExpenseResult(items, sp, ps, total, data.Sum(e => e.Amount), tp, sp > 1, sp < tp);
    }

    public async Task<IReadOnlyList<string>> GetAvailableMonthKeysAsync(string userId, CancellationToken ct)
    {
        var snap = await CosmosHelper.QueryAsync<ExpenseMonthProjection>(
            p.Expenses,
            new QueryDefinition("SELECT c.monthKey, c.date FROM c WHERE c.userId = @uid").WithParameter("@uid", userId),
            ct);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in snap)
        {
            if (CosmosHelper.IsValidMonthKey(r.MonthKey))
            {
                keys.Add(r.MonthKey!);
                continue;
            }

            if (DateOnly.TryParse(r.Date, out var d))
                keys.Add(d.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }

        return keys.OrderBy(k => k).ToArray();
    }

    public async Task<OperationResult> UpdateExpenseAsync(string userId, string id, string desc, decimal amount, DateOnly date, string mt, string pm, bool isCredit, int? installments, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new OperationResult(false, "El id es requerido.");

        try
        {
            var existing = await GetDocAsync(userId, id, ct);
            if (existing is null)
                return new OperationResult(false, "No existe.");

            var targetMonthKey = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var updated = new ExpenseDocument
            {
                Id = existing.Id,
                UserId = userId,
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                MonthKey = targetMonthKey,
                Description = desc.Trim(),
                Amount = amount,
                MovementType = mt.Trim(),
                PaymentMethod = pm.Trim(),
                IsCredit = isCredit,
                Installments = installments,
                ClientMutationId = existing.ClientMutationId,
                SourceRecurringId = existing.SourceRecurringId,
                SourceRecurringMonth = existing.SourceRecurringMonth,
                CreatedAt = string.IsNullOrWhiteSpace(existing.CreatedAt)
                    ? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    : existing.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            await p.Expenses.UpsertItemAsync(updated, new PartitionKey(updated.MonthKey), cancellationToken: ct);
            if (!string.Equals(existing.MonthKey, updated.MonthKey, StringComparison.Ordinal))
                await DelDocAsync(existing.Id, existing.MonthKey, ct);

            return new OperationResult(true, "Actualizado.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<OperationResult> DeleteExpenseAsync(string userId, string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new OperationResult(false, "El id es requerido.");

        try
        {
            var existing = await GetDocAsync(userId, id, ct);
            if (existing is null)
                return new OperationResult(false, "No existe.");

            await DelDocAsync(existing.Id, existing.MonthKey, ct);
            return new OperationResult(true, "Eliminado.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    public async Task<int> CountExpensesByMovementTypeAsync(string userId, string mt, CancellationToken ct)
    {
        try
        {
            var r = await CosmosHelper.QueryAsync<int>(
                p.Expenses,
                new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.userId = @uid AND c.movementType = @mt")
                    .WithParameter("@uid", userId)
                    .WithParameter("@mt", mt),
                ct);

            return r.FirstOrDefault();
        }
        catch
        {
            return 0;
        }
    }

    public async Task<OperationResult> DeleteAllByMovementTypeAsync(string userId, string mt, CancellationToken ct)
    {
        try
        {
            var expenses = await CosmosHelper.QueryAsync<ExpenseDocument>(
                p.Expenses,
                new QueryDefinition("SELECT c.id, c.monthKey FROM c WHERE c.userId = @uid AND c.movementType = @mt")
                    .WithParameter("@uid", userId)
                    .WithParameter("@mt", mt),
                ct);

            foreach (var expense in expenses)
            {
                try
                {
                    await p.Expenses.DeleteItemAsync<ExpenseDocument>(expense.Id, new PartitionKey(expense.MonthKey), cancellationToken: ct);
                }
                catch
                {
                }
            }

            var recurring = await CosmosHelper.QueryAsync<RecurringExpenseDocument>(
                p.Recurring,
                new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @uid AND c.movementType = @mt")
                    .WithParameter("@uid", userId)
                    .WithParameter("@mt", mt),
                ct);

            foreach (var item in recurring)
            {
                try
                {
                    await p.Recurring.DeleteItemAsync<RecurringExpenseDocument>(item.Id, new PartitionKey(item.Id), cancellationToken: ct);
                }
                catch
                {
                }
            }

            return new OperationResult(true, $"Eliminados {expenses.Count} gastos y {recurring.Count} recurrentes.");
        }
        catch (Exception ex)
        {
            return new OperationResult(false, $"Error: {ex.Message}");
        }
    }

    private async Task EnsureRecurringAsync(string userId, string mk, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact($"{mk}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthStart))
            return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (string.CompareOrdinal(mk, today.ToString("yyyy-MM", CultureInfo.InvariantCulture)) > 0)
            return;

        var items = await CosmosHelper.QueryAsync<RecurringExpenseDocument>(
            p.Recurring,
            new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid AND c.isActive = true").WithParameter("@uid", userId),
            ct);

        if (items.Count == 0)
            return;

        var catalog = await catalogRepo.GetCatalogsAsync(userId, ct);
        foreach (var rec in items)
        {
            if (string.IsNullOrWhiteSpace(rec.Id)
                || !CosmosHelper.IsValidMonthKey(rec.StartMonth)
                || string.CompareOrdinal(mk, rec.StartMonth) < 0)
                continue;

            var dayOfMonth = Math.Min(Math.Max(rec.DayOfMonth, 1), DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
            var dueDate = new DateOnly(monthStart.Year, monthStart.Month, dayOfMonth);
            if (mk == today.ToString("yyyy-MM", CultureInfo.InvariantCulture) && dueDate > today)
                continue;

            if (!CosmosHelper.IsAllowedValue(rec.MovementType ?? string.Empty, catalog.MovementTypes)
                || !CosmosHelper.IsAllowedValue(rec.PaymentMethod ?? string.Empty, catalog.PaymentMethods))
                continue;

            var existing = await CosmosHelper.QueryAsync<ExpenseIdentity>(
                p.Expenses,
                new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.monthKey = @mk AND c.userId = @uid AND c.sourceRecurringId = @sid AND c.sourceRecurringMonth = @smk")
                    .WithParameter("@mk", mk)
                    .WithParameter("@uid", userId)
                    .WithParameter("@sid", rec.Id)
                    .WithParameter("@smk", mk),
                ct);

            if (existing.Count > 0)
                continue;

            var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            await p.Expenses.CreateItemAsync(
                new ExpenseDocument
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    Date = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    MonthKey = mk,
                    Description = rec.Description?.Trim() ?? string.Empty,
                    Amount = rec.Amount,
                    MovementType = rec.MovementType?.Trim() ?? string.Empty,
                    PaymentMethod = rec.PaymentMethod?.Trim() ?? string.Empty,
                    SourceRecurringId = rec.Id,
                    SourceRecurringMonth = mk,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new PartitionKey(mk),
                cancellationToken: ct);
        }
    }

    private async Task<ExpenseDocument?> GetDocAsync(string uid, string id, CancellationToken ct)
    {
        return (await CosmosHelper.QueryAsync<ExpenseDocument>(
            p.Expenses,
            new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id AND c.userId = @uid")
                .WithParameter("@id", id)
                .WithParameter("@uid", uid),
            ct)).FirstOrDefault();
    }

    private async Task DelDocAsync(string id, string? mk, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mk))
            return;

        try
        {
            await p.Expenses.DeleteItemAsync<ExpenseDocument>(id, new PartitionKey(mk), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private static ExpenseItem? MapExpense(ExpenseDocument d)
    {
        if (string.IsNullOrWhiteSpace(d.Id))
            return null;

        if (!DateOnly.TryParse(d.Date, out var date))
            date = DateOnly.FromDateTime(DateTime.UtcNow);

        var createdAt = CosmosHelper.ParseDateTimeOffset(d.CreatedAt, DateTimeOffset.UtcNow);
        return new ExpenseItem(
            d.Id,
            date,
            d.Description ?? string.Empty,
            d.Amount,
            d.MovementType ?? string.Empty,
            d.PaymentMethod ?? string.Empty,
            createdAt,
            CosmosHelper.ParseDateTimeOffset(d.UpdatedAt, createdAt),
            d.IsCredit,
            d.Installments);
    }

    private static string? NormalizeClientMutationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }
}
