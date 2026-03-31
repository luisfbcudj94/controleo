using System.Collections.ObjectModel;
using Controleo.Mobile.Models;
using Controleo.Mobile.Services;

namespace Controleo.Mobile;

public partial class DashboardPage : ContentPage
{
    private readonly ExpenseApiClient _apiClient;
    private readonly MonthContextService _monthContext;
    private readonly ObservableCollection<DashboardCategoryViewItem> _items = [];
    private readonly ObservableCollection<DonutLegendItem> _donutLegendItems = [];
    private bool _isRefreshing;
    private bool _isOpeningDetail;
    private bool _isMonthPickerSyncing;
    private bool _isActive;
    private readonly BudgetRingDrawable _budgetRingDrawable = new();
    private readonly DistributionDonutDrawable _distributionDonutDrawable = new();

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
        DonutLegendCollection.ItemsSource = _donutLegendItems;
        BudgetRingView.Drawable = _budgetRingDrawable;
        DistributionDonutView.Drawable = _distributionDonutDrawable;
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

                var balanceText = hasBudget
                    ? $"Presupuesto: ${row.BudgetTotal:N0} · Saldo: ${row.Balance:N0}"
                    : "Sin presupuesto asignado";

                _items.Add(new DashboardCategoryViewItem(
                    row.MovementType,
                    row.ExpenseTotal,
                    progress,
                    hasBudget ? ProgressColorForType(row.MovementType) : Color.FromArgb("#E5534B"),
                    balanceText,
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
            _budgetRingDrawable.TrackColor = Color.FromArgb("#DCE5DF");
            _budgetRingDrawable.ProgressColor = Color.FromArgb("#2D6A4F");
            BudgetRingView.Invalidate();

            BudgetSummaryLabel.Text = $"{FormatCompactCurrency(spentFromBudget)} / {FormatCompactCurrency(totalBudget)}";
            DistributionSummaryLabel.Text = "100% de gastos";
            DistributionSummaryLabel.TextColor = Color.FromArgb("#1A2E23");
            AvailableSummaryLabel.Text = $"${availableBudget:N0}";
            TotalBudgetSummaryLabel.Text = $"${totalBudget:N0}";
            SpentSummaryLabel.Text = $"${spentFromBudget:N0}";

            var distributionByExpense = data
                .Where(item => item.ExpenseTotal > 0)
                .OrderByDescending(item => item.ExpenseTotal)
                .ToList();
            var totalForDistribution = distributionByExpense.Sum(item => item.ExpenseTotal);

            _distributionDonutDrawable.Segments = distributionByExpense
                .Select(item => new DistributionDonutDrawable.Segment(
                    ProgressColorForType(item.MovementType),
                    totalForDistribution <= 0 ? 0 : (double)(item.ExpenseTotal / totalForDistribution)))
                .ToList();
            DistributionDonutView.Invalidate();

            _donutLegendItems.Clear();
            if (totalForDistribution <= 0)
            {
                DistributionSummaryLabel.Text = "Añade gastos para ver la distribución";
                DistributionSummaryLabel.TextColor = Color.FromArgb("#E5534B");
            }
            else
            {
                foreach (var item in distributionByExpense)
                {
                    var pct = Math.Round((item.ExpenseTotal / totalForDistribution) * 100m);
                    _donutLegendItems.Add(new DonutLegendItem(
                        $"{item.MovementType} {pct:0}%",
                        ProgressColorForType(item.MovementType)));
                }
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

    private static string FormatCompactCurrency(decimal value)
    {
        if (value >= 1_000_000m)
        {
            return $"${value / 1_000_000m:0.#}M";
        }

        if (value >= 1_000m)
        {
            return $"${value / 1_000m:0.#}K";
        }

        return $"${value:0}";
    }

    private static Color ProgressColorForType(string movementType)
    {
        return movementType.ToLowerInvariant() switch
        {
            "basicos para vivir" => Color.FromArgb("#2D6A4F"),
            "hogar" => Color.FromArgb("#3A8FBF"),
            "salidas" => Color.FromArgb("#D4765A"),
            "imprevistos" => Color.FromArgb("#7B5EA7"),
            "suscripciones" => Color.FromArgb("#C5A200"),
            "bienestar" => Color.FromArgb("#C0608F"),
            "viajes" => Color.FromArgb("#3A9E8E"),
            "deudas" => Color.FromArgb("#5A7FA0"),
            "prestamo" => Color.FromArgb("#B08A40"),
            "obra" => Color.FromArgb("#4A8B72"),
            _ => Color.FromArgb("#2D6A4F")
        };
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
        double ExpenseRatio,
        Color ProgressColor,
        string BudgetAndBalanceLabel,
        Color CardColor,
        string Icon);

    private sealed record DonutLegendItem(string Label, Color DotColor);

    private sealed class BudgetRingDrawable : IDrawable
    {
        public double Progress { get; set; }
        public Color TrackColor { get; set; } = Colors.LightGray;
        public Color ProgressColor { get; set; } = Colors.MediumSlateBlue;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = 22f;
            var padding = stroke / 2 + 4;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - (padding * 2);
            if (size <= 0)
            {
                return;
            }

            var x = (dirtyRect.Width - size) / 2;
            var y = (dirtyRect.Height - size) / 2;

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Butt;

            canvas.StrokeColor = TrackColor;
            canvas.DrawArc(x, y, size, size, 0, 360, true, false);

            if (Progress <= 0)
            {
                return;
            }

            var sweep = (float)(360 * Math.Clamp(Progress, 0, 1));
            var start = -90f;
            var end = start - sweep;
            canvas.StrokeColor = ProgressColor;
            canvas.DrawArc(x, y, size, size, start, end, true, false);
        }
    }

    private sealed class DistributionDonutDrawable : IDrawable
    {
        public sealed record Segment(Color Color, double Ratio);
        public List<Segment> Segments { get; set; } = [];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = 34f;
            var padding = stroke / 2 + 4;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - (padding * 2);
            if (size <= 0)
            {
                return;
            }

            var x = (dirtyRect.Width - size) / 2;
            var y = (dirtyRect.Height - size) / 2;

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Butt;

            var start = -90f;
            var normalizedSegments = Segments
                .Where(segment => segment.Ratio > 0)
                .ToList();
            var total = normalizedSegments.Sum(segment => segment.Ratio);
            if (total <= 0)
            {
                canvas.StrokeColor = Color.FromArgb("#DCE5DF");
                canvas.DrawArc(x, y, size, size, 0, 360, true, false);
                return;
            }

            float consumedSweep = 0f;
            for (var i = 0; i < normalizedSegments.Count; i++)
            {
                var segment = normalizedSegments[i];
                var isLast = i == normalizedSegments.Count - 1;
                var sweep = isLast
                    ? 360f - consumedSweep
                    : (float)(360d * (segment.Ratio / total));

                if (sweep <= 0.01f)
                {
                    continue;
                }

                var end = start - sweep;
                canvas.StrokeColor = segment.Color;
                canvas.DrawArc(x, y, size, size, start, end, true, false);

                start = end;
                consumedSweep += sweep;
            }
        }
    }
}
