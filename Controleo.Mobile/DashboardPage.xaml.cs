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

            var totalBudget = data.Sum(item => item.BudgetTotal);
            var spentFromBudget = data.Where(item => item.BudgetTotal > 0).Sum(item => item.ExpenseTotal);
            var availableBudget = totalBudget - spentFromBudget;
            var others = data
                .Where(item => item.BudgetTotal <= 0 && item.ExpenseTotal != 0)
                .OrderByDescending(item => item.ExpenseTotal)
                .ToList();
            var othersTotal = others.Sum(item => item.ExpenseTotal);
            var totalExpenses = data.Sum(item => item.ExpenseTotal);

            BudgetTotalLabel.Text = $"${totalBudget:N0}";
            BudgetAvailableLabel.Text = $"${availableBudget:N0}";
            var budgetProgress = totalBudget <= 0 ? 0d : (double)Math.Min(1m, spentFromBudget / totalBudget);
            SpentFromBudgetValueLabel.Text = $"${spentFromBudget:N0}";
            TotalSpentLabel.Text = $"${totalExpenses:N0}";
            MiniBudgetLabel.Text = $"${totalBudget:N0}";
            MiniSpentLabel.Text = $"${spentFromBudget:N0}";
            MiniAvailableLabel.Text = $"${availableBudget:N0}";
            RingPercentLabel.Text = $"{budgetProgress * 100:0}%";
            _budgetRingDrawable.Progress = budgetProgress;
            _budgetRingDrawable.TrackColor = Color.FromArgb("#2A2A2A");
            _budgetRingDrawable.ProgressColor = Color.FromArgb("#C8F55A");
            BudgetRingView.Invalidate();

            if (others.Count == 0)
            {
                OthersLabel.Text = "Sin gastos en otras secciones.";
                OthersTotalLabel.Text = "$0";
                OthersPillLeftNameLabel.Text = "Sin datos";
                OthersPillLeftValueLabel.Text = "$0";
                OthersPillRightNameLabel.Text = "Sin datos";
                OthersPillRightValueLabel.Text = "$0";
            }
            else
            {
                var topOthers = others.Take(2).Select(item => $"{item.MovementType}: ${item.ExpenseTotal:N0}");
                var hiddenCount = Math.Max(0, others.Count - 2);
                OthersLabel.Text = string.Join(" · ", topOthers);
                OthersTotalLabel.Text = $"Otros: ${othersTotal:N0}";
                if (hiddenCount > 0)
                {
                    OthersLabel.Text += $" · +{hiddenCount} más";
                }

                var first = others.ElementAtOrDefault(0);
                var second = others.ElementAtOrDefault(1);
                OthersPillLeftNameLabel.Text = first?.MovementType ?? "Sin datos";
                OthersPillLeftValueLabel.Text = first is null ? "$0" : $"${first.ExpenseTotal:N0}";
                OthersPillRightNameLabel.Text = second?.MovementType ?? "Sin datos";
                OthersPillRightValueLabel.Text = second is null ? "$0" : $"${second.ExpenseTotal:N0}";
            }
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
        Color BalanceColor);

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
