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
        RefreshMonthPickerItems();
        _monthContext.MonthChanged += OnMonthChanged;
        _monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
        SyncMonthSelection();
        BudgetsCollection.ItemsSource = _budgets;
        MovementTypeSelectorLabel.Text = "Seleccionar sección";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isActive = true;
        await RefreshMonthOptionsAsync();
        await LoadDataAsync();
    }

    private async Task RefreshMonthOptionsAsync()
    {
        var monthKeys = await _apiClient.GetAvailableMonthsAsync(CancellationToken.None);
        _monthContext.SetAvailableMonths(monthKeys);
        RefreshMonthPickerItems();
        SyncMonthSelection();
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

            MovementTypeSelectorLabel.Text = MovementTypePicker.SelectedItem?.ToString() ?? "Seleccionar sección";

            var items = await _apiClient.GetBudgetsAsync(CancellationToken.None);
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
            ResetAmountInput();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", "Selecciona una sección.");
            return;
        }

        if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) &&
            !decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out amount))
        {
            ResetAmountInput();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", "El presupuesto debe ser numérico.");
            return;
        }

        if (amount < 0)
        {
            ResetAmountInput();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo guardar", "El presupuesto no puede ser negativo.");
            return;
        }

        SaveBudgetButton.IsEnabled = false;
        SetLoading(true);
        var result = await _apiClient.UpsertBudgetAsync(MovementTypePicker.SelectedItem.ToString()!, amount, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Presupuesto guardado" : "No se pudo guardar",
            result.IsSuccess ? "El presupuesto se guardó correctamente." : result.Message);
        SaveBudgetButton.IsEnabled = true;
        SetLoading(false);

        if (result.IsSuccess)
        {
            AmountEntry.Text = string.Empty;
            await LoadDataAsync();
        }
        else
        {
            ResetAmountInput();
        }
    }

    private async void OnDeleteBudgetClicked(object? sender, EventArgs e)
    {
        if ((sender as ImageButton)?.CommandParameter is not string movementType)
        {
            return;
        }

        var confirm = await StyledConfirmModalPage.ConfirmAsync(
            this,
            "Eliminar presupuesto",
            $"¿Eliminar presupuesto de '{movementType}'?");
        if (!confirm)
        {
            return;
        }

        SetLoading(true);
        var result = await _apiClient.DeleteBudgetAsync(movementType, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Presupuesto eliminado" : "No se pudo eliminar",
            result.IsSuccess ? "El presupuesto se eliminó correctamente." : result.Message);
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

    private void OnMonthOptionsChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshMonthPickerItems();
            SyncMonthSelection();
        });
    }

    private void RefreshMonthPickerItems()
    {
        MonthPicker.ItemsSource = _monthContext.MonthOptions.ToList();
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
        MonthSelectorLabel.Text = selected.Label;
    }

    private void SetLoading(bool isLoading)
    {
        LoadingOverlay.IsVisible = isLoading;
    }

    private async void OnMonthSelectorTapped(object? sender, EventArgs e)
    {
        var monthOptions = _monthContext.MonthOptions.ToList();
        if (monthOptions.Count == 0)
        {
            return;
        }

        var options = monthOptions.Select(option => option.Label).ToList();
        var selected = await StyledSelectorModalPage.PickAsync(this, "Seleccionar mes", options, MonthSelectorLabel.Text);
        if (selected is null)
        {
            return;
        }

        var selectedMonth = monthOptions.FirstOrDefault(option => option.Label == selected);
        if (selectedMonth is not null)
        {
            _monthContext.SetMonth(selectedMonth.Value);
        }
    }

    private async void OnMovementTypeSelectorTapped(object? sender, EventArgs e)
    {
        var movementTypes = MovementTypePicker.ItemsSource?.Cast<string>().ToList() ?? [];
        if (movementTypes.Count == 0)
        {
            return;
        }

        var selected = await StyledSelectorModalPage.PickAsync(this, "Seleccionar sección", movementTypes, MovementTypeSelectorLabel.Text);
        if (selected is null)
        {
            return;
        }

        if (MovementTypePicker.SelectedItem?.ToString() == selected)
        {
            return;
        }

        MovementTypePicker.SelectedItem = selected;
        MovementTypeSelectorLabel.Text = selected;
    }

    private void ResetAmountInput()
    {
        AmountEntry.Text = "0";
    }
}
