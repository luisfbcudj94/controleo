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
    private readonly ObservableCollection<ExpenseViewItem> _expenses = [];
    private ExpenseCatalog _catalog = new([], []);
    private ExpenseItem? _selectedExpense;
    private bool _isRefreshing;
    private bool _isMonthPickerSyncing;
    private bool _isPageSizeSyncing;
    private bool _isActive;
    private int _pageNumber = 1;
    private int _pageSize = 5;
    private string _searchTerm = string.Empty;
    private DateOnly _editSelectedDate = DateOnly.FromDateTime(DateTime.Today);

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
        MoneyFormatHelper.Attach(EditAmountEntry);
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
            PastelColorHelper.SetConfigs(_catalog.MovementTypeConfigs);
            EditMovementPicker.ItemsSource = _catalog.MovementTypes.ToList();
            EditPaymentPicker.ItemsSource = _catalog.PaymentMethods.ToList();

            var page = await _apiClient.GetExpensesPageAsync(
                _monthContext.SelectedMonthKey,
                _pageNumber,
                _pageSize,
                movementType: null,
                searchTerm: _searchTerm,
                CancellationToken.None);

            _pageNumber = page.PageNumber;
            _expenses.Clear();
            foreach (var item in page.Items)
            {
                _expenses.Add(new ExpenseViewItem(item, Services.PastelColorHelper.ForMovementType(item.MovementType), Services.PastelColorHelper.IconForMovementType(item.MovementType)));
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

    private async void OnDeleteExpenseTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExpenseViewItem view)
        {
            return;
        }

        var expense = view.Item;

        var confirm = await StyledConfirmModalPage.ConfirmAsync(
            this,
            "Eliminar gasto",
            $"¿Deseas eliminar '{expense.Description}'?");
        if (!confirm)
        {
            return;
        }

        var result = await _apiClient.DeleteExpenseAsync(expense.Id, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Gasto eliminado" : "No se pudo eliminar",
            result.IsSuccess ? "El gasto se eliminó correctamente." : result.Message);
        if (result.IsSuccess)
        {
            EditPanel.IsVisible = false;
            _selectedExpense = null;
            await LoadDataAsync();
        }
    }

    private void OnEditExpenseTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ExpenseViewItem view)
        {
            return;
        }

        var expense = view.Item;

        _selectedExpense = expense;
        _editSelectedDate = expense.Date;
        UpdateEditDateSelectorLabel();
        EditDescriptionEntry.Text = expense.Description;
        EditAmountEntry.Text = MoneyFormatHelper.FormatWithDots(((long)expense.Amount).ToString());
        EditMovementPicker.SelectedItem = expense.MovementType;
        EditPaymentPicker.SelectedItem = expense.PaymentMethod;
        EditPanel.IsVisible = true;
        StatusLabel.Text = string.Empty;
    }

    private async void OnSaveEditClicked(object? sender, EventArgs e)
    {
        if (_selectedExpense is null)
        {
            ResetEditInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "Selecciona un gasto para editar.");
            return;
        }

        if (!MoneyFormatHelper.TryParse(EditAmountEntry.Text, out var amount))
        {
            ResetEditInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "El valor debe ser numérico.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EditDescriptionEntry.Text) || amount == 0 ||
            EditMovementPicker.SelectedItem is null || EditPaymentPicker.SelectedItem is null)
        {
            ResetEditInputs();
            await StyledResultModalPage.ShowAsync(this, false, "No se pudo editar", "Completa todos los campos y usa un valor diferente de cero.");
            return;
        }

        var request = new ExpenseEntryRequest(
            _editSelectedDate,
            EditDescriptionEntry.Text.Trim(),
            amount,
            EditMovementPicker.SelectedItem.ToString()!,
            EditPaymentPicker.SelectedItem.ToString()!);

        var result = await _apiClient.UpdateExpenseAsync(_selectedExpense.Id, request, CancellationToken.None);
        await StyledResultModalPage.ShowAsync(
            this,
            result.IsSuccess,
            result.IsSuccess ? "Gasto actualizado" : "No se pudo actualizar",
            result.IsSuccess ? "Los cambios se guardaron correctamente." : result.Message);

        if (result.IsSuccess)
        {
            await LoadDataAsync();
            EditPanel.IsVisible = false;
            _selectedExpense = null;
        }
        else
        {
            ResetEditInputs();
        }
    }

    private void OnCancelEditClicked(object? sender, EventArgs e)
    {
        EditPanel.IsVisible = false;
        _selectedExpense = null;
        StatusLabel.Text = string.Empty;
    }

    private void ResetEditInputs()
    {
        EditDescriptionEntry.Text = "0";
        EditAmountEntry.Text = "0";
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

    private async void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchTerm = e.NewTextValue?.Trim() ?? string.Empty;
        _pageNumber = 1;
        await LoadDataAsync();
    }

    private async void OnEditDateSelectorTapped(object? sender, TappedEventArgs e)
    {
        var selected = await CalendarDateModalPage.PickAsync(
            this,
            "Fecha del gasto",
            _editSelectedDate,
            DateOnly.FromDateTime(DateTime.Today.AddYears(-10)),
            DateOnly.FromDateTime(DateTime.Today.AddYears(10)));

        if (selected is null)
        {
            return;
        }

        _editSelectedDate = selected.Value;
        UpdateEditDateSelectorLabel();
    }
    private void UpdateEditDateSelectorLabel()
    {
        EditDateSelectorLabel.Text = _editSelectedDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
    }

    internal sealed record ExpenseViewItem(ExpenseItem Item, Color CardColor, string Icon)
    {
        public string Id => Item.Id;
        public DateOnly Date => Item.Date;
        public string Description => Item.Description;
        public decimal Amount => Item.Amount;
        public string MovementType => Item.MovementType;
        public string PaymentMethod => Item.PaymentMethod;
    }
}
