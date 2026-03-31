namespace Controleo.Infrastructure.Options;
public sealed class CosmosStorageOptions
{
    public const string SectionName = "CosmosStorage";
    public string Endpoint { get; set; } = ""; public string Key { get; set; } = "";
    public string DatabaseName { get; set; } = "controleo";
    public string ExpensesContainerName { get; set; } = "expenses";
    public string BudgetsContainerName { get; set; } = "budgets";
    public string RecurringExpensesContainerName { get; set; } = "recurring_expenses";
    public string SettingsContainerName { get; set; } = "app_settings";
    public string CatalogDocumentId { get; set; } = "catalogs";
    public string[] MovementTypes { get; set; } = [];
    public string[] PaymentMethods { get; set; } = [];
}
