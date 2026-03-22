using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class SettingsPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<string> _movementTypes = [];
    private readonly ObservableCollection<string> _paymentMethods = [];
    private string? _selectedMovement;
    private string? _selectedPayment;
    private bool _isEditingMovement;
    private string? _editingOriginalValue;
    private bool _isRefreshing;
    private bool _isMonthPickerSyncing;
    private bool _isActive;

    public SettingsPage(ExpenseApiClient apiClient, MonthContextService monthContext)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        RefreshMonthPickerItems();
        _monthContext.MonthChanged += OnMonthChanged;
        _monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
        SyncMonthSelection();
        MovementCollection.ItemsSource = _movementTypes;
        PaymentCollection.ItemsSource = _paymentMethods;
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
            var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);

            _movementTypes.Clear();
            foreach (var item in catalog.MovementTypes)
            {
                _movementTypes.Add(item);
            }

            _paymentMethods.Clear();
            foreach (var item in catalog.PaymentMethods)
            {
                _paymentMethods.Add(item);
            }

            _selectedMovement = null;
            _selectedPayment = null;
            StatusLabel.Text = string.Empty;
        }
        finally
        {
            _isRefreshing = false;
            SettingsRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private async void OnAddMovementClicked(object? sender, EventArgs e)
    {
        if (AddItem(_movementTypes, NewMovementEntry, "sección"))
        {
            await SaveCatalogsAsync();
        }
    }

    private async void OnAddPaymentClicked(object? sender, EventArgs e)
    {
        if (AddItem(_paymentMethods, NewPaymentEntry, "medio de pago"))
        {
            await SaveCatalogsAsync();
        }
    }

    private void OnEditMovementClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedMovement))
        {
            StatusLabel.Text = "Selecciona una sección para editar.";
            return;
        }

        _isEditingMovement = true;
        _editingOriginalValue = _selectedMovement;
        EditModalTitleLabel.Text = "Editar sección";
        EditModalEntry.Text = _selectedMovement;
        EditOverlay.IsVisible = true;
    }

    private void OnEditPaymentClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedPayment))
        {
            StatusLabel.Text = "Selecciona un medio de pago para editar.";
            return;
        }

        _isEditingMovement = false;
        _editingOriginalValue = _selectedPayment;
        EditModalTitleLabel.Text = "Editar medio de pago";
        EditModalEntry.Text = _selectedPayment;
        EditOverlay.IsVisible = true;
    }

    private async void OnDeleteMovementClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedMovement))
        {
            _movementTypes.Remove(_selectedMovement);
            _selectedMovement = null;
            await SaveCatalogsAsync();
        }
    }

    private async void OnDeletePaymentClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedPayment))
        {
            _paymentMethods.Remove(_selectedPayment);
            _selectedPayment = null;
            await SaveCatalogsAsync();
        }
    }

    private async Task SaveCatalogsAsync()
    {
        if (_movementTypes.Count == 0 || _paymentMethods.Count == 0)
        {
            StatusLabel.Text = "Debe existir al menos una sección y un medio de pago.";
            return;
        }

        SetLoading(true);

        var request = new UpdateCatalogsRequest(_movementTypes.ToList(), _paymentMethods.ToList());
        var result = await _apiClient.UpdateCatalogsAsync(request, CancellationToken.None);

        StatusLabel.Text = result.Message;
        SetLoading(false);
    }

    private void OnMovementSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedMovement = e.CurrentSelection.FirstOrDefault()?.ToString();
    }

    private void OnPaymentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedPayment = e.CurrentSelection.FirstOrDefault()?.ToString();
    }

    private bool AddItem(ObservableCollection<string> source, Entry entry, string label)
    {
        var value = entry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusLabel.Text = $"Ingresa un nombre de {label}.";
            return false;
        }

        if (source.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLabel.Text = $"Ese {label} ya existe.";
            return false;
        }

        source.Add(value);
        entry.Text = string.Empty;
        StatusLabel.Text = string.Empty;
        return true;
    }

    private static void ReplaceItem(ObservableCollection<string> source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue);
        if (index >= 0)
        {
            source[index] = newValue;
        }
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
        if (!_isActive)
        {
            MainThread.BeginInvokeOnMainThread(SyncMonthSelection);
            return;
        }

        MainThread.BeginInvokeOnMainThread(SyncMonthSelection);
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

    private void OnCancelEditModalClicked(object? sender, EventArgs e)
    {
        EditOverlay.IsVisible = false;
        _editingOriginalValue = null;
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

    private async void OnConfirmEditModalClicked(object? sender, EventArgs e)
    {
        var edited = EditModalEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(edited) || string.IsNullOrWhiteSpace(_editingOriginalValue))
        {
            return;
        }

        if (_isEditingMovement)
        {
            if (_movementTypes.Any(item => !string.Equals(item, _editingOriginalValue, StringComparison.OrdinalIgnoreCase) && string.Equals(item, edited, StringComparison.OrdinalIgnoreCase)))
            {
                StatusLabel.Text = "Esa sección ya existe.";
                return;
            }

            ReplaceItem(_movementTypes, _editingOriginalValue, edited);
            _selectedMovement = edited;
        }
        else
        {
            if (_paymentMethods.Any(item => !string.Equals(item, _editingOriginalValue, StringComparison.OrdinalIgnoreCase) && string.Equals(item, edited, StringComparison.OrdinalIgnoreCase)))
            {
                StatusLabel.Text = "Ese medio de pago ya existe.";
                return;
            }

            ReplaceItem(_paymentMethods, _editingOriginalValue, edited);
            _selectedPayment = edited;
        }

        EditOverlay.IsVisible = false;
        _editingOriginalValue = null;
        await SaveCatalogsAsync();
    }
}
