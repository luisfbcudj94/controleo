using Microsoft.Azure.Cosmos;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: <email> <category>");
    return 1;
}

var email = args[0].Trim().ToLowerInvariant();
var category = args[1].Trim();
if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(category))
{
    Console.Error.WriteLine("Email and category are required.");
    return 1;
}

var endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")?.Trim() ?? string.Empty;
var key = Environment.GetEnvironmentVariable("COSMOS_KEY")?.Trim() ?? string.Empty;
var dbName = Environment.GetEnvironmentVariable("COSMOS_DB")?.Trim();
var settingsContainerName = Environment.GetEnvironmentVariable("COSMOS_SETTINGS_CONTAINER")?.Trim();
var budgetsContainerName = Environment.GetEnvironmentVariable("COSMOS_BUDGETS_CONTAINER")?.Trim();

if (string.IsNullOrWhiteSpace(dbName)) dbName = "controleo";
if (string.IsNullOrWhiteSpace(settingsContainerName)) settingsContainerName = "app_settings";
if (string.IsNullOrWhiteSpace(budgetsContainerName)) budgetsContainerName = "budgets";

if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("COSMOS_ENDPOINT and COSMOS_KEY are required in env vars.");
    return 1;
}

var options = new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
};

using var client = new CosmosClient(endpoint, key, options);
var db = client.GetDatabase(dbName);
var settings = db.GetContainer(settingsContainerName);
var budgets = db.GetContainer(budgetsContainerName);

var user = await QuerySingleAsync<UserCredentialRow>(
    settings,
    new QueryDefinition("SELECT TOP 1 c.userId, c.id, c.email FROM c WHERE c.type = @type AND LOWER(c.email) = @email")
        .WithParameter("@type", "user-credential")
        .WithParameter("@email", email));

if (user is null || string.IsNullOrWhiteSpace(user.UserId))
{
    Console.Error.WriteLine($"User not found for email: {email}");
    return 2;
}

var userId = user.UserId.Trim();
var docId = $"{userId.Replace("/", "_").ToLowerInvariant()}-catalogs";
var categoryLower = category.ToLowerInvariant();

var catalogRemoved = false;
var configRemoved = false;
var catalogUpdated = false;
var movementTypeCountBefore = 0;
var movementTypeCountAfter = 0;

try
{
    var response = await settings.ReadItemAsync<CatalogDocument>(docId, new PartitionKey(docId));
    var doc = response.Resource;

    var movementTypes = (doc.MovementTypes ?? Array.Empty<string>()).ToList();
    movementTypeCountBefore = movementTypes.Count;

    var removedMt = movementTypes.RemoveAll(mt => string.Equals((mt ?? string.Empty).Trim(), category, StringComparison.OrdinalIgnoreCase));
    catalogRemoved = removedMt > 0;

    var movementTypeConfigs = (doc.MovementTypeConfigs ?? Array.Empty<MovementTypeConfigDoc>()).ToList();
    var removedCfg = movementTypeConfigs.RemoveAll(cfg => string.Equals((cfg?.Name ?? string.Empty).Trim(), category, StringComparison.OrdinalIgnoreCase));
    configRemoved = removedCfg > 0;

    movementTypeCountAfter = movementTypes.Count;

    if (catalogRemoved || configRemoved)
    {
        doc.MovementTypes = movementTypes.ToArray();
        doc.MovementTypeConfigs = movementTypeConfigs.ToArray();
        doc.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
        await settings.UpsertItemAsync(doc, new PartitionKey(doc.Id));
        catalogUpdated = true;
    }
}
catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
{
    // No catalog doc for this user.
}

var budgetsDeleted = 0;
var budgetRows = await QueryManyAsync<BudgetRow>(
    budgets,
    new QueryDefinition("SELECT c.id, c.movementType FROM c WHERE c.userId = @uid AND LOWER(c.movementType) = @movementType")
        .WithParameter("@uid", userId)
        .WithParameter("@movementType", categoryLower));

foreach (var row in budgetRows)
{
    if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.MovementType))
    {
        continue;
    }

    try
    {
        await budgets.DeleteItemAsync<BudgetRow>(row.Id, new PartitionKey(row.MovementType));
        budgetsDeleted++;
    }
    catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
    }
}

Console.WriteLine($"UserId={userId}");
Console.WriteLine($"CatalogUpdated={catalogUpdated}");
Console.WriteLine($"CategoryRemovedFromCatalog={catalogRemoved}");
Console.WriteLine($"CategoryConfigRemoved={configRemoved}");
Console.WriteLine($"MovementTypeCountBefore={movementTypeCountBefore}");
Console.WriteLine($"MovementTypeCountAfter={movementTypeCountAfter}");
Console.WriteLine($"BudgetsDeleted={budgetsDeleted}");

return 0;

static async Task<T?> QuerySingleAsync<T>(Container container, QueryDefinition query)
{
    using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
    while (iterator.HasMoreResults)
    {
        var page = await iterator.ReadNextAsync();
        foreach (var item in page)
        {
            return item;
        }
    }

    return default;
}

static async Task<List<T>> QueryManyAsync<T>(Container container, QueryDefinition query)
{
    var result = new List<T>();
    using var iterator = container.GetItemQueryIterator<T>(query);
    while (iterator.HasMoreResults)
    {
        var page = await iterator.ReadNextAsync();
        result.AddRange(page);
    }

    return result;
}

sealed class UserCredentialRow
{
    public string? UserId { get; set; }
    public string? Id { get; set; }
    public string? Email { get; set; }
}

sealed class CatalogDocument
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string[] MovementTypes { get; set; } = Array.Empty<string>();
    public MovementTypeConfigDoc[] MovementTypeConfigs { get; set; } = Array.Empty<MovementTypeConfigDoc>();
    public string UpdatedAt { get; set; } = string.Empty;
}

sealed class MovementTypeConfigDoc
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

sealed class BudgetRow
{
    public string Id { get; set; } = string.Empty;
    public string MovementType { get; set; } = string.Empty;
}
