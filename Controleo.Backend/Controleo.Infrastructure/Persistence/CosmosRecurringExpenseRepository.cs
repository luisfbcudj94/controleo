using System.Globalization; using System.Net; using Controleo.Domain.Common; using Controleo.Domain.Entities; using Controleo.Domain.Interfaces; using Microsoft.Azure.Cosmos;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosRecurringExpenseRepository(CosmosContainerProvider p) : IRecurringExpenseRepository
{
    public async Task<IReadOnlyList<RecurringExpenseItem>> GetRecurringExpensesAsync(string userId, CancellationToken ct)
    {
        var data = await CosmosHelper.QueryAsync<RecurringExpenseDocument>(p.Recurring, new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid").WithParameter("@uid", userId), ct);
        return data.Where(d => !string.IsNullOrWhiteSpace(d.Id)).Select(d => new RecurringExpenseItem(d.Id, d.Description ?? "", d.Amount, d.MovementType ?? "", d.PaymentMethod ?? "", d.DayOfMonth, ResolveStartDate(d.StartMonth, d.DayOfMonth), d.StartMonth ?? "", d.EndMonth, d.IsActive, CosmosHelper.ParseDateTimeOffset(d.CreatedAt, DateTimeOffset.UtcNow), CosmosHelper.ParseDateTimeOffset(d.UpdatedAt, DateTimeOffset.UtcNow))).OrderBy(r => r.DayOfMonth).ThenBy(r => r.Description).ToArray();
    }

    public async Task<PagedRecurringExpenseResult> GetRecurringExpensesPageAsync(string userId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var normalizedPageSize = pageSize is 10 or 20 ? pageSize : 5;
        var all = await GetRecurringExpensesAsync(userId, ct);
        var totalCount = all.Count;
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);
        var safePageNumber = Math.Min(Math.Max(pageNumber, 1), totalPages);
        var items = all.Skip((safePageNumber - 1) * normalizedPageSize).Take(normalizedPageSize).ToArray();

        return new PagedRecurringExpenseResult(
            items,
            safePageNumber,
            normalizedPageSize,
            totalCount,
            totalPages,
            safePageNumber > 1,
            safePageNumber < totalPages);
    }

    public async Task<OperationResult> UpsertRecurringExpenseAsync(string userId, string? id, string desc, decimal amount, string mt, string pm, int day, string startMonth, bool active, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow; var docId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        try { var existing = (await CosmosHelper.QueryAsync<RecurringExpenseDocument>(p.Recurring, new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id AND c.userId = @uid").WithParameter("@id", docId).WithParameter("@uid", userId), ct)).FirstOrDefault(); var doc = new RecurringExpenseDocument { Id = docId, UserId = userId, Description = desc.Trim(), Amount = amount, MovementType = mt.Trim(), PaymentMethod = pm.Trim(), DayOfMonth = day, StartMonth = startMonth, IsActive = active, CreatedAt = existing?.CreatedAt ?? now.ToString("O", CultureInfo.InvariantCulture), UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture) }; await p.Recurring.UpsertItemAsync(doc, new PartitionKey(doc.UserId), cancellationToken: ct); return new OperationResult(true, "Gasto recurrente guardado."); }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }
    public async Task<OperationResult> DeleteRecurringExpenseAsync(string userId, string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return new OperationResult(false, "El id es requerido.");
        try { await p.Recurring.DeleteItemAsync<RecurringExpenseDocument>(id.Trim(), new PartitionKey(userId), cancellationToken: ct); return new OperationResult(true, "Eliminado."); }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return new OperationResult(false, "No existe."); }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }

    private static DateOnly ResolveStartDate(string? startMonth, int dayOfMonth)
    {
        if (!string.IsNullOrWhiteSpace(startMonth) && DateOnly.TryParseExact($"{startMonth.Trim()}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthStart))
        {
            var safeDay = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
            return new DateOnly(monthStart.Year, monthStart.Month, safeDay);
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var fallbackDay = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(today.Year, today.Month));
        return new DateOnly(today.Year, today.Month, fallbackDay);
    }
}
