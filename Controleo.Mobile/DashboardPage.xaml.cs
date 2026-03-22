using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class DashboardPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<DashboardCategoryViewItem> _items = [];
    private bool _isRefreshing;
    private bool _isOpeningDetail;
    private bool _isMonthPickerSyncing;
    private bool _isActive;

    public DashboardPage(ExpenseApiClient apiClient, MonthContextService monthContext)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        MonthPicker.ItemsSource = _monthContext.MonthOptions.ToList();
        _monthContext.MonthChanged += OnMonthChanged;
        SyncMonthSelection();
        ChartCollection.ItemsSource = _items;
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
            var data = await _apiClient.GetDashboardByCategoryAsync(_monthContext.SelectedMonthKey, CancellationToken.None);

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
                    Math.Min(1d, (double)(row.ExpenseTotal / maxExpense)),
                    row.Balance < 0 ? Colors.IndianRed : Colors.ForestGreen));
            }

            var totalExpenses = data.Sum(item => item.ExpenseTotal);
            var totalBudget = data.Sum(item => item.BudgetTotal);
            TotalsLabel.Text = $"Gasto total: ${totalExpenses:N0}  |  Presupuesto total: ${totalBudget:N0}";
        }
        finally
        {
            _isRefreshing = false;
            DashboardRefreshView.IsRefreshing = false;
            SetLoading(false);
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadDataAsync();
    }

    private async void OnSectionTapped(object? sender, TappedEventArgs e)
    {
        if (_isOpeningDetail)
        {
            return;
        }

        if (e.Parameter is not string movementType || string.IsNullOrWhiteSpace(movementType))
        {
            return;
        }

        try
        {
            _isOpeningDetail = true;
            SetLoading(true);
            var expenses = await _apiClient.GetExpensesAsync(_monthContext.SelectedMonthKey, CancellationToken.None);
            var filtered = expenses
                .Where(item => string.Equals(item.MovementType, movementType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Date)
                .ThenByDescending(item => item.UpdatedAt)
                .ToList();

            await Navigation.PushModalAsync(new NavigationPage(new SectionExpensesModalPage(movementType, filtered)));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"No se pudo abrir el detalle: {ex.Message}", "OK");
        }
        finally
        {
            _isOpeningDetail = false;
            SetLoading(false);
        }
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

    private sealed record DashboardCategoryViewItem(
        string MovementType,
        decimal ExpenseTotal,
        decimal BudgetTotal,
        decimal Balance,
        double ExpenseRatio,
        Color BalanceColor);
}
