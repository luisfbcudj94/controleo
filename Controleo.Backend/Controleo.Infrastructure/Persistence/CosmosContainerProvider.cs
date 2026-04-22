using Controleo.Infrastructure.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
namespace Controleo.Infrastructure.Persistence;
public sealed class CosmosContainerProvider
{
    public Container Expenses { get; }
    public Container Budgets { get; }
    public Container Recurring { get; }
    public Container Settings { get; }
    public Container Goals { get; }
    public CosmosStorageOptions Options { get; }
    public CosmosContainerProvider(IOptions<CosmosStorageOptions> options)
    {
        Options = options.Value;
        var ep = Resolve(Options.Endpoint, "COSMOS_DB_ENDPOINT", "COSMOS_ENDPOINT");
        var key = Resolve(Options.Key, "COSMOS_DB_KEY", "COSMOS_KEY");
        if (string.IsNullOrWhiteSpace(ep) || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Cosmos DB no está configurado.");
        var client = new CosmosClient(ep, key, new CosmosClientOptions { SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase } });
        var db = client.GetDatabase(Options.DatabaseName);
        Expenses = db.GetContainer(Options.ExpensesContainerName);
        Budgets = db.GetContainer(Options.BudgetsContainerName);
        Recurring = db.GetContainer(Options.RecurringExpensesContainerName);
        Settings = db.GetContainer(Options.SettingsContainerName);
        Goals = EnsureContainer(db, Options.GoalsContainerName, "/userId");
    }
    private static Container EnsureContainer(Database db, string name, string partitionKeyPath)
    {
        db.CreateContainerIfNotExistsAsync(name, partitionKeyPath).GetAwaiter().GetResult();
        return db.GetContainer(name);
    }
    private static string Resolve(string configured, params string[] envVars)
    {
        if (!string.IsNullOrWhiteSpace(configured) && !configured.StartsWith("TU_")) return configured.Trim();
        foreach (var n in envVars) { var v = Environment.GetEnvironmentVariable(n); if (!string.IsNullOrWhiteSpace(v)) return v.Trim(); }
        return "";
    }
}
