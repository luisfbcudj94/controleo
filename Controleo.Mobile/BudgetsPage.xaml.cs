using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class BudgetsPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly ObservableCollection<BudgetItem> _budgets = [];
    private bool _loaded;

    public BudgetsPage(ExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        BudgetsCollection.ItemsSource = _budgets;
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
        var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
        MovementTypePicker.ItemsSource = catalog.MovementTypes.ToList();
        if (MovementTypePicker.SelectedItem is null && MovementTypePicker.ItemsSource.Count > 0)
        {
            MovementTypePicker.SelectedIndex = 0;
        }

        var items = await _apiClient.GetBudgetsAsync(CancellationToken.None);
        _budgets.Clear();
        foreach (var item in items.OrderBy(row => row.MovementType))
        {
            _budgets.Add(item);
        }
    }

    private async void OnSaveBudgetClicked(object? sender, EventArgs e)
    {
        if (MovementTypePicker.SelectedItem is null)
        {
            StatusLabel.Text = "Selecciona una sección.";
            return;
        }

        if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) &&
            !decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount))
        {
            StatusLabel.Text = "El presupuesto debe ser numérico.";
            return;
        }

        if (amount < 0)
        {
            StatusLabel.Text = "El presupuesto no puede ser negativo.";
            return;
        }

        SaveBudgetButton.IsEnabled = false;
        var result = await _apiClient.UpsertBudgetAsync(MovementTypePicker.SelectedItem.ToString()!, amount, CancellationToken.None);
        StatusLabel.Text = result.Message;
        SaveBudgetButton.IsEnabled = true;

        if (result.IsSuccess)
        {
            AmountEntry.Text = string.Empty;
            await LoadDataAsync();
        }
    }

    private async void OnDeleteBudgetClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string movementType)
        {
            return;
        }

        var confirm = await DisplayAlert("Eliminar presupuesto", $"¿Eliminar presupuesto de '{movementType}'?", "Sí", "No");
        if (!confirm)
        {
            return;
        }

        var result = await _apiClient.DeleteBudgetAsync(movementType, CancellationToken.None);
        StatusLabel.Text = result.Message;
        if (result.IsSuccess)
        {
            await LoadDataAsync();
        }
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }
}
