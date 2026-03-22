using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class DashboardPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly ObservableCollection<DashboardCategoryViewItem> _items = [];
    private bool _loaded;

    public DashboardPage(ExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ChartCollection.ItemsSource = _items;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        await LoadDataAsync();
        _loaded = true;
    }

    private async Task LoadDataAsync()
    {
        var data = await _apiClient.GetDashboardByCategoryAsync(CancellationToken.None);

        var maxExpense = data.Count == 0 ? 0m : data.Max(item => item.ExpenseTotal);
        if (maxExpense <= 0)
        {
            maxExpense = 1;
        }

        _items.Clear();
        foreach (var row in data)
        {
            _items.Add(new DashboardCategoryViewItem(
                row.MovementType,
                row.ExpenseTotal,
                row.BudgetTotal,
                row.Balance,
                Math.Min(1d, (double)(row.ExpenseTotal / maxExpense))));
        }

        var totalExpenses = data.Sum(item => item.ExpenseTotal);
        var totalBudget = data.Sum(item => item.BudgetTotal);
        TotalsLabel.Text = $"Gasto total: ${totalExpenses:N0}  |  Presupuesto total: ${totalBudget:N0}";
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private sealed record DashboardCategoryViewItem(
        string MovementType,
        decimal ExpenseTotal,
        decimal BudgetTotal,
        decimal Balance,
        double ExpenseRatio);
}
