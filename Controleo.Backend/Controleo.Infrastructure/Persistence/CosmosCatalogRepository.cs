using System.Globalization; using System.Net; using Controleo.Domain.Common; using Controleo.Domain.Entities; using Controleo.Domain.Interfaces; using Microsoft.Azure.Cosmos;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosCatalogRepository(CosmosContainerProvider p) : ICatalogRepository
{
    public async Task<ExpenseCatalog> GetCatalogsAsync(string userId, CancellationToken ct)
    {
        var docId = $"{userId.Trim().Replace("/","_").ToLowerInvariant()}-catalogs";
        try { var r = await p.Settings.ReadItemAsync<CatalogDocument>(docId, new PartitionKey(docId), cancellationToken: ct); var mt = CosmosHelper.NormalizeCatalog(r.Resource.MovementTypes); var pm = CosmosHelper.NormalizeCatalog(r.Resource.PaymentMethods); var cfgs = (r.Resource.MovementTypeConfigs ?? []).Where(d => !string.IsNullOrWhiteSpace(d.Name)).Select(d => new MovementTypeConfig(d.Name.Trim(), d.Icon ?? "\ud83d\udccb", d.Color ?? "#D8F3DC")).ToList(); return new ExpenseCatalog(mt.Length == 0 ? CosmosHelper.NormalizeCatalog(p.Options.MovementTypes) : mt, pm.Length == 0 ? CosmosHelper.NormalizeCatalog(p.Options.PaymentMethods) : pm, cfgs); }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return new ExpenseCatalog(CosmosHelper.NormalizeCatalog(p.Options.MovementTypes), CosmosHelper.NormalizeCatalog(p.Options.PaymentMethods)); }
        catch { return new ExpenseCatalog(CosmosHelper.NormalizeCatalog(p.Options.MovementTypes), CosmosHelper.NormalizeCatalog(p.Options.PaymentMethods)); }
    }
    public async Task<OperationResult> UpdateCatalogsAsync(string userId, IReadOnlyList<string> movementTypes, IReadOnlyList<string> paymentMethods, IReadOnlyList<MovementTypeConfig>? configs, CancellationToken ct)
    {
        var mt = CosmosHelper.NormalizeCatalog(movementTypes); var pm = CosmosHelper.NormalizeCatalog(paymentMethods);
        if (mt.Length == 0) return new OperationResult(false, "Debe existir al menos una sección."); if (pm.Length == 0) return new OperationResult(false, "Debe existir al menos un medio de pago.");
        try { var docId = $"{userId.Trim().Replace("/","_").ToLowerInvariant()}-catalogs"; var doc = new CatalogDocument { Id = docId, UserId = userId, MovementTypes = mt, PaymentMethods = pm, MovementTypeConfigs = (configs ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => new MovementTypeConfigDoc { Name = c.Name.Trim(), Icon = string.IsNullOrWhiteSpace(c.Icon) ? "\ud83d\udccb" : c.Icon.Trim(), Color = string.IsNullOrWhiteSpace(c.Color) ? "#D8F3DC" : c.Color.Trim() }).ToArray(), UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) }; await p.Settings.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct); return new OperationResult(true, "Catálogos actualizados."); }
        catch (Exception ex) { return new OperationResult(false, $"Error: {ex.Message}"); }
    }
}
