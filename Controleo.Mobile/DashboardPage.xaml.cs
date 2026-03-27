using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class DashboardPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<DashboardCategoryViewItem> _items = [];
    private readonly ObservableCollection<DashboardSummaryCard> _summaryCards = [];
    private bool _isRefreshing;
    private bool _isOpeningDetail;
    private bool _isMonthPickerSyncing;
    private bool _isActive;
    private readonly BudgetRingDrawable _budgetRingDrawable = new();

    public DashboardPage(ExpenseApiClient apiClient, MonthContextService monthContext)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _monthContext = monthContext;
        RefreshMonthPickerItems();
        _monthContext.MonthChanged += OnMonthChanged;
        _monthContext.MonthOptionsChanged += OnMonthOptionsChanged;
        SyncMonthSelection();
        ChartCollection.ItemsSource = _items;
        SummaryCarousel.ItemsSource = _summaryCards;
        BudgetRingView.Drawable = _budgetRingDrawable;
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
            // Ensure we have latest configs for colors/icons
            var catalog = await _apiClient.GetCatalogsAsync(CancellationToken.None);
            Services.PastelColorHelper.SetConfigs(catalog.MovementTypeConfigs);

            var data = await _apiClient.GetDashboardByCategoryAsync(_monthContext.SelectedMonthKey, CancellationToken.None);

            var orderedData = data
                .OrderBy(item => item.BudgetTotal <= 0 ? 1 : 0)
                .ThenByDescending(item => item.BudgetTotal)
                .ThenByDescending(item => item.ExpenseTotal)
                .ToList();

            _items.Clear();
            foreach (var row in orderedData)
            {
                var hasBudget = row.BudgetTotal > 0;
                var progress = hasBudget
                    ? (double)Math.Clamp(row.ExpenseTotal / row.BudgetTotal, 0m, 1m)
                    : (row.ExpenseTotal > 0 ? 1d : 0d);

                _items.Add(new DashboardCategoryViewItem(
                    row.MovementType,
                    row.ExpenseTotal,
                    row.BudgetTotal,
                    row.Balance,
                    progress,
                    row.Balance < 0 ? Color.FromArgb("#E5534B") : Color.FromArgb("#40916C"),
                    hasBudget ? Color.FromArgb("#40916C") : Color.FromArgb("#E5534B"),
                    hasBudget
                        ? $"Presupuesto: ${row.BudgetTotal:N0}"
                        : "Sin presupuesto asignado",
                    Services.PastelColorHelper.ForMovementType(row.MovementType),
                    Services.PastelColorHelper.IconForMovementType(row.MovementType)));
            }

            var totalBudget = data.Sum(item => item.BudgetTotal);
            var spentFromBudget = data.Where(item => item.BudgetTotal > 0).Sum(item => item.ExpenseTotal);
            var availableBudget = totalBudget - spentFromBudget;
            var totalExpenses = data.Sum(item => item.ExpenseTotal);
            var budgetProgress = totalBudget <= 0 ? 0d : (double)Math.Min(1m, spentFromBudget / totalBudget);
            RingPercentLabel.Text = $"{budgetProgress * 100:0}%";
            _budgetRingDrawable.Progress = budgetProgress;
            _budgetRingDrawable.TrackColor = Color.FromArgb("#D8F3DC");
            _budgetRingDrawable.ProgressColor = Color.FromArgb("#2D6A4F");
            BudgetRingView.Invalidate();

            _summaryCards.Clear();
            _summaryCards.Add(new DashboardSummaryCard("Disponible", $"${availableBudget:N0}", Color.FromArgb("#40916C")));
            _summaryCards.Add(new DashboardSummaryCard("Presupuesto", $"${totalBudget:N0}", Color.FromArgb("#2D6A4F")));
            _summaryCards.Add(new DashboardSummaryCard("Gastado", $"${spentFromBudget:N0}", Color.FromArgb("#E5534B")));
            _summaryCards.Add(new DashboardSummaryCard("Total gastado", $"${totalExpenses:N0}", Color.FromArgb("#7B61C4")));
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
            await Navigation.PushModalAsync(new NavigationPage(new SectionExpensesModalPage(_apiClient, _monthContext.SelectedMonthKey, movementType)));
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

    private sealed record DashboardCategoryViewItem(
        string MovementType,
        decimal ExpenseTotal,
        decimal BudgetTotal,
        decimal Balance,
        double ExpenseRatio,
        Color BalanceColor,
        Color ProgressColor,
        string BudgetLabel,
        Color CardColor,
        string Icon);

    private sealed record DashboardSummaryCard(string Title, string Value, Color AccentColor);

    private sealed class BudgetRingDrawable : IDrawable
    {
        public double Progress { get; set; }
        public Color TrackColor { get; set; } = Colors.LightGray;
        public Color ProgressColor { get; set; } = Colors.MediumSlateBlue;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = 12f;
            var padding = stroke / 2 + 4;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - (padding * 2);
            if (size <= 0)
            {
                return;
            }

            var x = (dirtyRect.Width - size) / 2;
            var y = (dirtyRect.Height - size) / 2;

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;

            canvas.StrokeColor = TrackColor;
            canvas.DrawArc(x, y, size, size, -90, 270, false, false);

            if (Progress <= 0)
            {
                return;
            }

            var end = -90 + (float)(360 * Math.Clamp(Progress, 0, 1));
            canvas.StrokeColor = ProgressColor;
            canvas.DrawArc(x, y, size, size, -90, end, false, false);
        }
    }
}
