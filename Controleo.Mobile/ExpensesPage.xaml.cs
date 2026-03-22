using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class ExpensesPage : ContentPage
{
    private static readonly int[] AllowedPageSizes = [5, 10, 20];

    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<ExpenseItem> _expenses = [];
    private ExpenseCatalog _catalog = new([], []);
    private ExpenseItem? _selectedExpense;
    private bool _isRefreshing;
    private bool _isMonthPickerSyncing;
    private bool _isPageSizeSyncing;
    private bool _isActive;
    private int _pageNumber = 1;
    private int _pageSize = 5;

    public ExpensesPage(ExpenseApiClient apiClient, MonthContextService monthContext)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        RefreshMonthPickerItems();
        _monthContext.MonthChanged += OnMonthChanged;
        _monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
        SyncMonthSelection();
        ExpensesCollection.ItemsSource = _expenses;
        PageSizePicker.ItemsSource = AllowedPageSizes.Select(item => item.ToString()).ToList();
        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = _pageSize.ToString();
        _isPageSizeSyncing = false;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
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
        StatusLabel.Text = "Cargando información...";
        try
        {
            _catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
            EditMovementPicker.ItemsSource = _catalog.MovementTypes.ToList();
            EditPaymentPicker.ItemsSource = _catalog.PaymentMethods.ToList();

            var page = await _apiClient.GetExpensesPageAsync(
                _monthContext.SelectedMonthKey,
                _pageNumber,
                _pageSize,
                movementType: null,
                CancellationToken.None);

            _pageNumber = page.PageNumber;
            _expenses.Clear();
            foreach (var item in page.Items)
            {
                _expenses.Add(item);
            }

            PaginationStatusLabel.Text = $"Página {page.PageNumber}/{page.TotalPages} · {page.TotalCount} registros";
            PrevPageButton.IsEnabled = page.HasPreviousPage;
            NextPageButton.IsEnabled = page.HasNextPage;

            StatusLabel.Text = string.Empty;
        }
        finally
        {
            _isRefreshing = false;
            ExpensesRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private async void OnDeleteExpenseClicked(object? sender, EventArgs e)
    {
        if ((sender as ImageButton)?.CommandParameter is not ExpenseItem expense)
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
            EditPanel.IsVisible = false;
            _selectedExpense = null;
            await LoadDataAsync();
        }
    }

    private void OnEditExpenseClicked(object? sender, EventArgs e)
    {
        if ((sender as ImageButton)?.CommandParameter is not ExpenseItem expense)
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

        if (string.IsNullOrWhiteSpace(EditDescriptionEntry.Text) || amount == 0 ||
            EditMovementPicker.SelectedItem is null || EditPaymentPicker.SelectedItem is null)
        {
            StatusLabel.Text = "Completa todos los campos y usa un valor diferente de cero.";
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
            _pageNumber = 1;
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

    private async void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (_isPageSizeSyncing || PageSizePicker.SelectedItem is not string pageSizeText)
        {
            return;
        }

        if (!int.TryParse(pageSizeText, out var selectedPageSize))
        {
            return;
        }

        _pageSize = AllowedPageSizes.Contains(selectedPageSize) ? selectedPageSize : 5;
        PageSizeSelectorLabel.Text = _pageSize.ToString();
        _pageNumber = 1;
        await LoadDataAsync();
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

    private async void OnPageSizeSelectorTapped(object? sender, EventArgs e)
    {
        var options = AllowedPageSizes.Select(size => size.ToString()).ToList();
        var selected = await StyledSelectorModalPage.PickAsync(this, "Mostrar elementos", options, PageSizeSelectorLabel.Text);
        if (selected is null)
        {
            return;
        }

        if (PageSizePicker.SelectedItem?.ToString() == selected)
        {
            return;
        }

        _isPageSizeSyncing = true;
        PageSizePicker.SelectedItem = selected;
        _isPageSizeSyncing = false;

        if (int.TryParse(selected, out var selectedPageSize) && AllowedPageSizes.Contains(selectedPageSize))
        {
            _pageSize = selectedPageSize;
            PageSizeSelectorLabel.Text = _pageSize.ToString();
            _pageNumber = 1;
            await LoadDataAsync();
        }
    }

    private async void OnPrevPageClicked(object? sender, EventArgs e)
    {
        if (_pageNumber <= 1)
        {
            return;
        }

        _pageNumber--;
        await LoadDataAsync();
    }

    private async void OnNextPageClicked(object? sender, EventArgs e)
    {
        _pageNumber++;
        await LoadDataAsync();
    }
}
