using System.Collections.ObjectModel;
using System.Globalization;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class BudgetsPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<BudgetItem> _budgets = [];
    private bool _isRefreshing;
    private bool _isMonthPickerSyncing;
    private bool _isActive;

    public BudgetsPage(ExpenseApiClient apiClient, MonthContextService monthContext)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        MonthPicker.ItemsSource = _monthContext.MonthOptions.ToList();
        _monthContext.MonthChanged += OnMonthChanged;
        SyncMonthSelection();
        BudgetsCollection.ItemsSource = _budgets;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isActive = true;
        await LoadDataAsync();
    }

    protected override void OnDisappearing()
    {
        _isActive = false;
        base.OnDisappearing();
    }

    private async Task LoadDataAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        SetLoading(true);
        try
        {
            var selectedMovement = MovementTypePicker.SelectedItem?.ToString();
            var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
            var movementTypes = catalog.MovementTypes.ToList();
            MovementTypePicker.ItemsSource = movementTypes;

            if (!string.IsNullOrWhiteSpace(selectedMovement) && movementTypes.Contains(selectedMovement))
            {
                MovementTypePicker.SelectedItem = selectedMovement;
            }
            else if (movementTypes.Count > 0)
            {
                MovementTypePicker.SelectedIndex = 0;
            }

            var items = await _apiClient.GetBudgetsAsync(_monthContext.SelectedMonthKey, CancellationToken.None);
            _budgets.Clear();
            foreach (var item in items.OrderBy(row => row.MovementType))
            {
                _budgets.Add(item);
            }
        }
        finally
        {
            _isRefreshing = false;
            BudgetsRefreshView.IsRefreshing = false;
            SetLoading(false);
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
        SetLoading(true);
        var result = await _apiClient.UpsertBudgetAsync(MovementTypePicker.SelectedItem.ToString()!, amount, _monthContext.SelectedMonthKey, CancellationToken.None);
        StatusLabel.Text = result.Message;
        SaveBudgetButton.IsEnabled = true;
        SetLoading(false);

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

        SetLoading(true);
        var result = await _apiClient.DeleteBudgetAsync(movementType, _monthContext.SelectedMonthKey, CancellationToken.None);
        StatusLabel.Text = result.Message;
        if (result.IsSuccess)
        {
            await LoadDataAsync();
        }
        SetLoading(false);
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private void OnPrevMonthClicked(object? sender, EventArgs e)
    {
        _monthContext.MoveMonths(-1);
    }

    private void OnNextMonthClicked(object? sender, EventArgs e)
    {
        _monthContext.MoveMonths(1);
    }

    private void OnMonthPickerChanged(object? sender, EventArgs e)
    {
        if (_isMonthPickerSyncing || MonthPicker.SelectedItem is not MonthContextService.MonthOption option)
        {
            return;
        }

        _monthContext.SetMonth(option.Value);
    }

    private void OnMonthChanged(object? sender, DateOnly month)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            SyncMonthSelection();
            if (!_isActive)
            {
                return;
            }

            await LoadDataAsync();
        });
    }

    private void SyncMonthSelection()
    {
        var selected = _monthContext.MonthOptions.FirstOrDefault(item => item.Value == _monthContext.SelectedMonth);
        if (selected is null)
        {
            return;
        }

        _isMonthPickerSyncing = true;
        MonthPicker.SelectedItem = selected;
        _isMonthPickerSyncing = false;
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }
}
