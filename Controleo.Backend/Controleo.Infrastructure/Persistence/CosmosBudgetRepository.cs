using System.Globalization; using System.Net; using Controleo.Domain.Common; using Controleo.Domain.Entities; using Controleo.Domain.Interfaces; using Microsoft.Azure.Cosmos;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosBudgetRepository(CosmosContainerProvider p) : IBudgetRepository
{
    public async Task<IReadOnlyList<BudgetItem>> GetBudgetsAsync(string userId, CancellationToken ct)
    {
        var data = await CosmosHelper.QueryAsync<BudgetDocument>(p.Budgets, new QueryDefinition("SELECT * FROM c WHERE c.userId = @uid").WithParameter("@uid", userId), ct);
        return data.Where(d => !string.IsNullOrWhiteSpace(d.MovementType)).Select(d => new BudgetItem(d.MovementType, d.Amount, CosmosHelper.ParseDateTimeOffset(d.UpdatedAt, DateTimeOffset.UtcNow))).GroupBy(b => b.MovementType, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(b => b.UpdatedAt).First()).OrderBy(b => b.MovementType).ToArray();
    }
    public async Task<OperationResult> UpsertBudgetAsync(string userId, string mt, decimal amount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mt)) return new OperationResult(false, "La sección es requerida.");
        try { var n = mt.Trim(); var doc = new BudgetDocument { Id = BuildKey(userId, n), UserId = userId, MovementType = n, Amount = amount, UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) }; await p.Budgets.UpsertItemAsync(doc, new PartitionKey(doc.MovementType), cancellationToken: ct); return new OperationResult(true, "Presupuesto guardado."); }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }
    public async Task<OperationResult> DeleteBudgetAsync(string userId, string mt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mt)) return new OperationResult(false, "La sección es requerida.");
        try { var n = mt.Trim(); var k = BuildKey(userId, n); try { await p.Budgets.DeleteItemAsync<BudgetDocument>(k, new PartitionKey(n), cancellationToken: ct); } catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { } var legacy = await CosmosHelper.QueryAsync<BudgetIdentity>(p.Budgets, new QueryDefinition("SELECT c.id, c.movementType FROM c WHERE c.movementType = @mt AND c.userId = @uid").WithParameter("@mt", n).WithParameter("@uid", userId), ct); foreach (var i in legacy.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.Equals(i.Id, k))) await p.Budgets.DeleteItemAsync<BudgetDocument>(i.Id!, new PartitionKey(i.MovementType!), cancellationToken: ct); return new OperationResult(true, "Presupuesto eliminado."); }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }
    private static string BuildKey(string uid, string mt) => $"{uid.Trim().Replace("/","_").ToLowerInvariant()}-budget-{mt.Trim().Replace("/","_").ToLowerInvariant()}";
}
