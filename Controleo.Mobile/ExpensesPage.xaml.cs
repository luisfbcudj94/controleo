using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class ExpensesPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly ObservableCollection<ExpenseItem> _expenses = [];
    private ExpenseCatalog _catalog = new([], []);
    private ExpenseItem? _selectedExpense;
    private bool _loaded;

    public ExpensesPage(ExpenseApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ExpensesCollection.ItemsSource = _expenses;
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
        StatusLabel.Text = "Cargando información...";
        _catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
        EditMovementPicker.ItemsSource = _catalog.MovementTypes.ToList();
        EditPaymentPicker.ItemsSource = _catalog.PaymentMethods.ToList();

        var items = await _apiClient.GetExpensesAsync(CancellationToken.None);
        _expenses.Clear();
        foreach (var item in items)
        {
            _expenses.Add(item);
        }

        StatusLabel.Text = string.Empty;
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private async void OnDeleteExpenseClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not ExpenseItem expense)
        {
            return;
        }

        var confirm = await DisplayAlert("Eliminar gasto", $"¿Deseas eliminar '{expense.Description}'?", "Sí", "No");
        if (!confirm)
        {
            return;
        }

        var result = await _apiClient.DeleteExpenseAsync(expense.Id, CancellationToken.None);
        StatusLabel.Text = result.Message;
        if (result.IsSuccess)
        {
            _expenses.Remove(expense);
            if (_selectedExpense?.Id == expense.Id)
            {
                EditPanel.IsVisible = false;
                _selectedExpense = null;
            }
        }
    }

    private void OnEditExpenseClicked(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not ExpenseItem expense)
        {
            return;
        }

        _selectedExpense = expense;
        EditDatePicker.Date = expense.Date.ToDateTime(TimeOnly.MinValue);
        EditDescriptionEntry.Text = expense.Description;
        EditAmountEntry.Text = expense.Amount.ToString(CultureInfo.InvariantCulture);
        EditMovementPicker.SelectedItem = expense.MovementType;
        EditPaymentPicker.SelectedItem = expense.PaymentMethod;
        EditPanel.IsVisible = true;
        StatusLabel.Text = string.Empty;
    }

    private async void OnSaveEditClicked(object? sender, EventArgs e)
    {
        if (_selectedExpense is null)
        {
            StatusLabel.Text = "Selecciona un gasto para editar.";
            return;
        }

        if (!decimal.TryParse(EditAmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) &&
            !decimal.TryParse(EditAmountEntry.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount))
        {
            StatusLabel.Text = "El valor debe ser numérico.";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditDescriptionEntry.Text) || amount <= 0 ||
            EditMovementPicker.SelectedItem is null || EditPaymentPicker.SelectedItem is null)
        {
            StatusLabel.Text = "Completa todos los campos correctamente.";
            return;
        }

        SaveEditButton.IsEnabled = false;

        var request = new ExpenseEntryRequest(
            DateOnly.FromDateTime(EditDatePicker.Date),
            EditDescriptionEntry.Text.Trim(),
            amount,
            EditMovementPicker.SelectedItem.ToString()!,
            EditPaymentPicker.SelectedItem.ToString()!);

        var result = await _apiClient.UpdateExpenseAsync(_selectedExpense.Id, request, CancellationToken.None);
        StatusLabel.Text = result.Message;

        if (result.IsSuccess)
        {
            await LoadDataAsync();
            EditPanel.IsVisible = false;
            _selectedExpense = null;
        }

        SaveEditButton.IsEnabled = true;
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        EditPanel.IsVisible = false;
        _selectedExpense = null;
        StatusLabel.Text = string.Empty;
    }

    private void OnGoToRegisterClicked(object? sender, EventArgs e)
    {
        var rootPage = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (rootPage is TabbedPage tabbedPage && tabbedPage.Children.Count > 0)
        {
            tabbedPage.CurrentPage = tabbedPage.Children[0];
        }
    }
}
